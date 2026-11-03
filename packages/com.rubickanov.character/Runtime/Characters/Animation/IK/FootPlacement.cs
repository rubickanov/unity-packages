using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Puts the feet on the ground under them: each foot rises or sinks with the ground, keeping its
    /// animated lift, tilts to the surface while planted, and the pelvis drops so the lower foot can reach.
    /// The ground is probed under the ankle and under the ball, so a foot on a step edge stands level on
    /// the step instead of sinking the toes into it or tipping over the edge.
    /// </summary>
    public sealed class FootPlacement
    {
        // m: the least knee offset a placed foot's leg bends by, so a near-straight leg still has a side to bend to.
        private const float PlacedBend = 0.1f;

        private sealed class Leg
        {
            public Transform Upper, Lower, Foot;
            // Null on rigs without toe bones; the foot is then probed under the ankle alone.
            public Transform Toes;
            public float SoleHeight;
            public Vector3 AnimatedFoot;
            public Vector3 AnimatedToes;
            public Quaternion AnimatedFootRotation;
            public Vector3 KneeBend;
            public FloatSpring Offset;
            public Vector3Spring Normal;
            public bool Grounded;
            public bool AnkleHit, BallHit;
            public Vector3 AnkleGround, BallGround;
            // Set down on the ground; told to the prints once each time.
            public bool Down;
            // Lowest height since it was set down, highest since it was lifted.
            public float Low, High = float.MaxValue;
            // How far ahead of the body it was last frame, along the way the body goes.
            public float Reach;
            // The animated foot as captured, before the game's placed foot; how much of that is laid on.
            public Vector3 Captured;
            public float Placed;
            // Hip to knee to ankle in the bind pose.
            public float Length;
        }

        private readonly Transform _body;
        private readonly Transform _hips;
        private readonly Leg _left;
        private readonly Leg _right;
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private FloatSpring _pelvis;
        private FloatSpring _weight;
        private Vector3 _lastRoot;
        private Vector3 _heading;
        // Prints with no pose to read: the body's place last frame, how far it walked since the last print, which
        // foot is next. A step's length grows with speed (about 0.7 m walking, 1.2 m running).
        private bool _blind;
        private Vector3 _blindLast;
        private float _blindWalked;
        private bool _blindLeft;
        private const float BlindStride = 0.42f;
        private const float BlindStridePerSpeed = 0.2f;
        private const float MaxBlindStride = 1.4f;
        private const float MinBlindApart = 0.05f;
        private const float MaxBlindApart = 0.2f;
        private const float BlindFoot = 0.2f;
        // More than this in a frame is a teleport, not a step.
        private const float MaxBlindFrame = 2f;

        public bool IsValid => _hips != null && _left != null && _right != null;

        /// <summary>Ground besides what the ray hits; null: the ray alone.</summary>
        public IFootGround Ground { get; set; }

        /// <summary>Told each time a foot is set down; null: no one.</summary>
        public IFootprints Prints { get; set; }

        /// <summary>Layers never taken for ground, on top of the config's.</summary>
        public int IgnoredLayers { get; set; }

        /// <summary>The animated ankle as last captured (with the stance spread), before a placed foot.</summary>
        public Vector3 Animated(bool left) => (left ? _left : _right).Captured;

        /// <summary>The ankle as last placed.</summary>
        public Vector3 Ankle(bool left) => (left ? _left : _right).Foot.position;

        /// <summary>The knee as last placed.</summary>
        public Vector3 Knee(bool left) => (left ? _left : _right).Lower.position;

        /// <summary>Hip to knee to ankle, metres.</summary>
        public float LegLength(bool left) => (left ? _left : _right).Length;

        public FootPlacement(Animator animator, Transform body)
        {
            _body = body;
            _hips = animator.GetBoneTransform(HumanBodyBones.Hips);
            _left = CreateLeg(animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg,
                HumanBodyBones.LeftFoot, HumanBodyBones.LeftToes, animator.leftFeetBottomHeight);
            _right = CreateLeg(animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot, HumanBodyBones.RightToes, animator.rightFeetBottomHeight);
        }

        public void Reset(bool grounded)
        {
            _blind = false;
            _blindWalked = 0f;
            _pelvis.Reset(0f);
            _weight.Reset(grounded ? 1f : 0f);
            foreach (Leg leg in new[] { _left, _right })
            {
                leg.Offset.Reset(0f);
                leg.Normal.Reset(_body.up);
            }
        }

        /// <summary>Remembers the animated feet before other passes move the body over them.</summary>
        public void Capture()
        {
            CaptureLeg(_left);
            CaptureLeg(_right);
        }

        /// <summary>As <see cref="Capture"/>, the feet set this far further apart, each to its own side.</summary>
        public void Capture(Vector3 halfSpread)
        {
            Capture();
            Spread(_left, -halfSpread);
            Spread(_right, halfSpread);
        }

        /// <summary>After <see cref="Capture()"/>: lays the game's placed feet over the captured ones.</summary>
        public void Place(RigFoot left, RigFoot right)
        {
            PlaceLeg(_left, left);
            PlaceLeg(_right, right);
        }

        /// <summary>
        /// With the bones as last drawn (a culled Animator) the prints follow the body: a foot set down at each stride
        /// it walks on the ground, the feet by turns, as far apart as the drawn pose stands.
        /// </summary>
        public void StepBlind(CharacterRigConfig config, bool grounded, float deltaTime)
        {
            Vector3 root = _body.position;
            Vector3 up = _body.up;
            Vector3 moved = Vector3.ProjectOnPlane(root - _blindLast, up);
            bool started = _blind;
            _blind = true;
            _blindLast = root;
            float distance = moved.magnitude;
            if (!started || !grounded || Prints == null || deltaTime <= 0f || distance > MaxBlindFrame)
            {
                _blindWalked = 0f;
                return;
            }

            _blindWalked += distance;
            float stride = Mathf.Clamp(BlindStride + BlindStridePerSpeed * distance / deltaTime, BlindStride, MaxBlindStride);
            if (_blindWalked < stride || distance < 1e-5f)
            {
                return;
            }

            _blindWalked = Mathf.Min(_blindWalked - stride, stride);
            Vector3 heading = moved / distance;
            Vector3 right = Vector3.Cross(up, heading);
            Leg leg = _blindLeft ? _left : _right;
            float apart = Mathf.Clamp(Mathf.Abs(Vector3.Dot(leg.Foot.position - root, right)), MinBlindApart, MaxBlindApart);
            Vector3 point = root + right * (_blindLeft ? -apart : apart);
            if (Cast(point, config, root, up, out Vector3 contact, out _) != null)
            {
                Prints.Planted(contact, contact + heading * BlindFoot, _blindLeft);
            }

            _blindLeft = !_blindLeft;
        }

        private void PlaceLeg(Leg leg, RigFoot foot)
        {
            leg.Captured = leg.AnimatedFoot;
            leg.Placed = Mathf.Clamp01(foot.Weight);
            if (leg.Placed <= 0f)
            {
                return;
            }

            // As an animated ankle: the probe sets it on the ground under the point.
            Vector3 delta = (foot.Point - leg.AnimatedFoot) * leg.Placed;
            leg.AnimatedFoot += delta;
            leg.AnimatedToes += delta;
        }

        private static void Spread(Leg leg, Vector3 offset)
        {
            leg.AnimatedFoot += offset;
            leg.AnimatedToes += offset;
        }

        public void Apply(CharacterRigConfig config, bool grounded, float weight, float deltaTime)
        {
            float feetWeight = _weight.Step(config.Feet && grounded ? 1f : 0f, config.FeetWeightSpring, deltaTime);
            weight *= Mathf.Clamp01(feetWeight);
            Vector3 up = _body.up;
            Vector3 root = _body.position;

            float left = Probe(_left, config, root, up, deltaTime);
            float right = Probe(_right, config, root, up, deltaTime);
            Vector3 moved = Vector3.ProjectOnPlane(root - _lastRoot, up);
            _lastRoot = root;
            if (moved.sqrMagnitude > 1e-8f)
            {
                _heading = moved.normalized;
            }

            Step(_left, config, root, up, grounded, true);
            Step(_right, config, root, up, grounded, false);
            float reach = Mathf.Min(Reach(_left, up), Reach(_right, up));
            float pelvis = _pelvis.Step(Mathf.Min(left, right, 0f, reach), config.PelvisSpring, deltaTime);
            // A placed foot is pinned: the hips never lag so far that its leg falls short of it.
            if (reach < 0f)
            {
                pelvis = Mathf.Min(pelvis, reach);
            }

            if (weight <= 0f)
            {
                return;
            }

            _hips.position += up * (pelvis * weight);
            Place(_left, config, root, up, _body.forward, weight);
            Place(_right, config, root, up, _body.forward, weight);
        }

        public void DrawGizmos()
        {
            foreach (Leg leg in new[] { _left, _right })
            {
                Gizmos.color = leg.AnkleHit ? Color.green : Color.red;
                Gizmos.DrawWireSphere(leg.AnkleGround, 0.03f);
                if (leg.Toes != null)
                {
                    Gizmos.color = leg.BallHit ? Color.green : Color.red;
                    Gizmos.DrawWireSphere(leg.BallGround, 0.02f);
                }

                Gizmos.color = leg.Grounded ? Color.cyan : Color.red;
                Gizmos.DrawLine(leg.AnkleGround, leg.AnkleGround + leg.Normal.Value * 0.2f);
            }
        }

        // A placed foot set wide may be out of the leg's reach from the hips: how far they sink so it reaches
        // (negative), by its weight; 0 for an animated foot, so the old pose is untouched.
        private static float Reach(Leg leg, Vector3 up)
        {
            if (leg.Placed <= 0f || leg.Length <= 0f)
            {
                return 0f;
            }

            Vector3 line = leg.AnimatedFoot + up * leg.Offset.Value - leg.Upper.position;
            float below = -Vector3.Dot(line, up);
            float across = Vector3.ProjectOnPlane(line, up).magnitude;
            float length = leg.Length * 0.97f;
            float need = across < length ? Mathf.Sqrt(length * length - across * across) : 0f;
            return Mathf.Clamp(need - below, -0.5f * leg.Length, 0f) * leg.Placed;
        }

        private static Leg CreateLeg(Animator animator, HumanBodyBones upper, HumanBodyBones lower,
            HumanBodyBones foot, HumanBodyBones toes, float soleHeight)
        {
            var leg = new Leg
            {
                Upper = animator.GetBoneTransform(upper),
                Lower = animator.GetBoneTransform(lower),
                Foot = animator.GetBoneTransform(foot),
                Toes = animator.GetBoneTransform(toes),
                SoleHeight = soleHeight,
            };
            if (leg.Upper == null || leg.Lower == null || leg.Foot == null)
            {
                return null;
            }

            leg.Length = Vector3.Distance(leg.Upper.position, leg.Lower.position)
                         + Vector3.Distance(leg.Lower.position, leg.Foot.position);
            return leg;
        }

        private static void CaptureLeg(Leg leg)
        {
            leg.AnimatedFoot = leg.Foot.position;
            leg.AnimatedToes = leg.Toes != null ? leg.Toes.position : leg.AnimatedFoot;
            leg.AnimatedFootRotation = leg.Foot.rotation;
            // Offset of the knee from the hip-to-foot line: which way the animation bends the leg.
            Vector3 hip = leg.Upper.position;
            Vector3 knee = leg.Lower.position;
            Vector3 line = leg.AnimatedFoot - hip;
            leg.KneeBend = line.sqrMagnitude > 1e-8f
                ? Vector3.ProjectOnPlane(knee - hip, line.normalized)
                : leg.Upper.rotation * Vector3.forward;
        }

        // Height of the ground under the foot relative to the character's base, smoothed.
        private float Probe(Leg leg, CharacterRigConfig config, Vector3 root, Vector3 up, float deltaTime)
        {
            float? ankle = Cast(leg.AnimatedFoot, config, root, up, out leg.AnkleGround, out Vector3 ankleNormal);
            leg.AnkleHit = ankle.HasValue;

            Vector3 along = Vector3.ProjectOnPlane(leg.AnimatedToes - leg.AnimatedFoot, up);
            float length = along.magnitude;
            float? ball = null;
            Vector3 ballNormal = up;
            if (leg.Toes != null)
            {
                ball = Cast(leg.AnimatedToes, config, root, up, out leg.BallGround, out ballNormal);
            }

            leg.BallHit = ball.HasValue;
            leg.Grounded = SoleContact.Fit(ankle, ball, length, config.MaxFootAngle, out float target, out float pitch);
            Vector3 normal = up;
            if (leg.Grounded)
            {
                // The side the foot stands on gives the sideways tilt; the two probes give the pitch,
                // which a single normal gets wrong on step edges.
                bool onBall = ball.HasValue && (!ankle.HasValue || ball.Value > ankle.Value);
                normal = onBall ? ballNormal : ankleNormal;
                if (length > 1e-3f)
                {
                    along /= length;
                    Vector3 roll = Vector3.ProjectOnPlane(normal, along);
                    roll = roll.sqrMagnitude > 1e-6f ? roll.normalized : up;
                    normal = Quaternion.AngleAxis(-pitch, Vector3.Cross(up, along)) * roll;
                }
            }

            leg.Normal.Step(normal, config.FootSpring, deltaTime);
            return leg.Offset.Step(target, config.FootSpring, deltaTime);
        }

        // Height of the ground straight under point relative to the character's base, or null if there is none
        // within the step limits. A ray rather than a sphere: a sphere catches step edges early and reports
        // their rounded normals. The ground provider, if any, may hold the foot higher than the ray's hit.
        private float? Cast(Vector3 point, CharacterRigConfig config, Vector3 root, Vector3 up,
            out Vector3 contact, out Vector3 normal)
        {
            float lift = Vector3.Dot(point - root, up);
            Vector3 origin = point + up * (config.ProbeHeight - lift);
            float length = config.ProbeHeight + config.MaxStepDown;
            int count = Physics.RaycastNonAlloc(origin, -up, _hits, length, config.GroundLayers & ~IgnoredLayers,
                QueryTriggerInteraction.Ignore);

            IFootGround provider = Ground;
            float nearest = float.PositiveInfinity;
            RaycastHit ground = default;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (hit.collider.transform.IsChildOf(_body) || hit.distance >= nearest
                    || (provider != null && provider.Ignores(hit.collider, point)))
                {
                    continue;
                }

                nearest = hit.distance;
                ground = hit;
            }

            bool found = !float.IsPositiveInfinity(nearest);
            contact = origin - up * length;
            normal = up;
            if (found)
            {
                contact = ground.point;
                normal = ground.normal;
                Rigidbody mover = ground.rigidbody;
                if (mover != null)
                {
                    // The ray hits the collider where the last physics step left it, while the body stands on the
                    // interpolated transform in between; a moving platform would shift under the feet every tick.
                    Quaternion turn = mover.transform.rotation * Quaternion.Inverse(mover.rotation);
                    contact = mover.transform.position + turn * (contact - mover.position);
                    normal = turn * normal;
                }
            }

            if (provider != null && provider.TryGetGround(point, out float height, out Vector3 providedNormal))
            {
                // Within the same span as the ray, and only where it is above what the ray hit.
                var provided = new Vector3(point.x, height, point.z);
                float along = Vector3.Dot(provided - origin, -up);
                if (along >= 0f && along <= length && (!found || Vector3.Dot(provided - contact, up) > 0f))
                {
                    found = true;
                    contact = provided;
                    normal = providedNormal;
                }
            }

            if (!found)
            {
                return null;
            }

            return Mathf.Clamp(Vector3.Dot(contact - root, up), -config.MaxStepDown, config.MaxStepUp);
        }

        // A foot is set down once it is near the ground, has come down by Lift from its highest point and has
        // stopped swinging ahead (it moves back against the body, as a standing foot does); it is up again once it
        // rises by Lift over its lowest. Relative, so a walk that barely lifts its feet steps as surely as a run,
        // and the print lands where the foot stands, not where it still glides low in its swing.
        private void Step(Leg leg, CharacterRigConfig config, Vector3 root, Vector3 up, bool grounded, bool left)
        {
            float height = Vector3.Dot(leg.AnimatedFoot - root, up) - leg.SoleHeight;
            float lift = config.PlantHeight * 0.25f;
            float reach = Vector3.Dot(leg.AnimatedFoot - root, _heading);
            bool swinging = reach > leg.Reach;
            leg.Reach = reach;
            if (!grounded || !leg.AnkleHit)
            {
                leg.Down = false;
                leg.High = float.MaxValue;
                return;
            }

            if (leg.Down)
            {
                leg.Low = Mathf.Min(leg.Low, height);
                if (height > leg.Low + lift)
                {
                    leg.Down = false;
                    leg.High = height;
                }

                return;
            }

            leg.High = Mathf.Max(leg.High, height);
            if (height > lift || height > leg.High - lift || swinging)
            {
                return;
            }

            leg.Down = true;
            leg.Low = height;
            if (Prints != null)
            {
                Vector3 ball = leg.BallHit ? leg.BallGround : leg.AnkleGround + _body.forward;
                Prints.Planted(leg.AnkleGround, ball, left);
            }
        }

        private static void Place(Leg leg, CharacterRigConfig config, Vector3 root, Vector3 up, Vector3 forward,
            float weight)
        {
            Vector3 target = leg.AnimatedFoot + up * leg.Offset.Value;
            Vector3 hint = leg.Lower.position + leg.KneeBend;
            if (leg.Placed > 0f)
            {
                // A foot the game moved off the animation: the animated knee belongs to another line and can sit
                // behind this one once the foot steps across, behind or apart; the knee bends ahead of the body,
                // from the middle of the new line, handing back to the animated bend only from ahead.
                Vector3 line = target - leg.Upper.position;
                Vector3 across = line.sqrMagnitude > 1e-8f ? line.normalized : up;
                Vector3 ahead = Vector3.ProjectOnPlane(forward, across).normalized;
                float size = Mathf.Max(leg.KneeBend.magnitude, PlacedBend);
                Vector3 bend = Vector3.Lerp(Vector3.ProjectOnPlane(leg.KneeBend, across), ahead * size, leg.Placed);
                if (Vector3.Dot(bend, ahead) < 0.5f * size)
                {
                    bend = Vector3.ProjectOnPlane(bend, ahead) + ahead * (0.5f * size);
                }

                hint = leg.Upper.position + 0.5f * line + bend;
            }

            TwoBoneIK.Solve(leg.Upper, leg.Lower, leg.Foot, target, hint, weight);

            // A planted foot lies on the surface; one in the air keeps its animated angle.
            float height = Vector3.Dot(leg.AnimatedFoot - root, up) - leg.SoleHeight;
            float planted = 1f - Mathf.Clamp01(height / config.PlantHeight);
            Vector3 normal = leg.Normal.Value.sqrMagnitude > 1e-6f ? leg.Normal.Value.normalized : up;
            normal = Vector3.RotateTowards(up, normal, config.MaxFootAngle * Mathf.Deg2Rad, 0f);
            Quaternion tilt = Quaternion.Slerp(Quaternion.identity, Quaternion.FromToRotation(up, normal), planted);
            leg.Foot.rotation = Quaternion.Slerp(leg.Foot.rotation, tilt * leg.AnimatedFootRotation, weight);
        }
    }
}
