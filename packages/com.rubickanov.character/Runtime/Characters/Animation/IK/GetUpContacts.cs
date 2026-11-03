using System.Collections.Generic;
using Rubickanov.Rig;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Hands and feet on the ground while a character gets up. A clip made for another body puts them into the
    /// ground and slides them along it; here each one stays above the ground under it and stays where it
    /// touched down, stepping after the animation only when dragged too far. Lets go by stepping or sliding
    /// back to the animation once the get-up is over.
    /// </summary>
    public sealed class GetUpContacts
    {
        private sealed class Limb
        {
            public Transform Upper, Middle, End;
            // Null on rigs without toe bones.
            public Transform Toes;
            public bool IsLeg;
            // How far above the ground a foot's ankle sits when it stands; a hand's comes from the config.
            public float SoleHeight;
            public Vector3 AnimatedEnd;
            public Vector3 AnimatedToes;
            public Quaternion AnimatedEndRotation;
            public Vector3 Bend;
            public LimbPin Pin;
            public bool OnToes;
        }

        private readonly Transform _body;
        private readonly Limb[] _limbs;
        private readonly RaycastHit[] _hits = new RaycastHit[8];
        private FloatSpring _weight;
        private bool _active;

        public bool IsValid => _limbs.Length > 0;

        /// <summary>Ground besides what physics hits, taken where it is higher (like <see cref="FootPlacement.Ground"/>); null: physics alone.</summary>
        public IFootGround Ground { get; set; }

        /// <summary>Layers never taken for ground, on top of the config's.</summary>
        public int IgnoredLayers { get; set; }

        public GetUpContacts(Animator animator, Transform body)
        {
            _body = body;
            var limbs = new List<Limb>(4);
            AddLeg(limbs, animator, HumanBodyBones.LeftUpperLeg, HumanBodyBones.LeftLowerLeg, HumanBodyBones.LeftFoot,
                HumanBodyBones.LeftToes, animator.leftFeetBottomHeight);
            AddLeg(limbs, animator, HumanBodyBones.RightUpperLeg, HumanBodyBones.RightLowerLeg,
                HumanBodyBones.RightFoot, HumanBodyBones.RightToes, animator.rightFeetBottomHeight);
            AddArm(limbs, animator, HumanBodyBones.LeftUpperArm, HumanBodyBones.LeftLowerArm, HumanBodyBones.LeftHand);
            AddArm(limbs, animator, HumanBodyBones.RightUpperArm, HumanBodyBones.RightLowerArm,
                HumanBodyBones.RightHand);
            _limbs = limbs.ToArray();
        }

        public void Reset()
        {
            _weight.Reset(0f);
            _active = false;
            foreach (Limb limb in _limbs)
            {
                limb.Pin.Reset();
            }
        }

        /// <summary>Remembers the animated pose before other passes move the body over it.</summary>
        public void Capture()
        {
            foreach (Limb limb in _limbs)
            {
                limb.AnimatedEnd = limb.End.position;
                limb.AnimatedToes = limb.Toes != null ? limb.Toes.position : limb.AnimatedEnd;
                limb.AnimatedEndRotation = limb.End.rotation;
                Vector3 root = limb.Upper.position;
                Vector3 line = limb.AnimatedEnd - root;
                limb.Bend = line.sqrMagnitude > 1e-8f
                    ? Vector3.ProjectOnPlane(limb.Middle.position - root, line.normalized)
                    : limb.Upper.rotation * Vector3.forward;
            }
        }

        /// <summary>Holds the contacts while active; once it stops, lets them go and fades out.</summary>
        public void Apply(CharacterRigConfig config, bool active, float weight, float deltaTime)
        {
            if (active && !_active)
            {
                foreach (Limb limb in _limbs)
                {
                    limb.Pin.Reset();
                }
            }

            Vector3 up = _body.up;
            if (!active && _active)
            {
                foreach (Limb limb in _limbs)
                {
                    limb.Pin.Release(Settings(config, limb), Contact(limb), up);
                }
            }

            _active = active;
            bool settling = false;
            foreach (Limb limb in _limbs)
            {
                settling |= limb.Pin.IsStepping || limb.Pin.Shift.sqrMagnitude > 1e-6f;
            }

            float target = config.GetUpContacts && (active || settling) ? 1f : 0f;
            weight *= Mathf.Clamp01(_weight.Step(target, config.ContactWeightSpring, deltaTime));
            if (weight <= 1e-3f)
            {
                return;
            }

            foreach (Limb limb in _limbs)
            {
                Place(limb, config, up, weight, deltaTime);
            }
        }

        private void Place(Limb limb, CharacterRigConfig config, Vector3 up, float weight, float deltaTime)
        {
            // Out of the ground: whichever of the end bone and the toes goes deeper decides how far it rises.
            float endLift = Lift(limb.AnimatedEnd, limb.IsLeg ? limb.SoleHeight : config.HandClearance, config, up);
            float lift = endLift;
            bool onToes = false;
            if (limb.Toes != null)
            {
                float toesLift = Lift(limb.AnimatedToes, config.ToesClearance, config, up);
                onToes = toesLift < endLift;
                lift = Mathf.Min(endLift, toesLift);
            }

            Vector3 rise = up * Mathf.Max(0f, -lift);
            if (onToes != limb.OnToes)
            {
                // The foot rolls onto its toes or back onto its heel: hold the new point where it is shown.
                Vector3 from = Contact(limb);
                limb.OnToes = onToes;
                limb.Pin.Rebase(from, Contact(limb), up);
            }

            Vector3 shift = limb.Pin.Update(Contact(limb), Mathf.Max(0f, lift), up, Settings(config, limb),
                deltaTime);
            Vector3 target = limb.AnimatedEnd + rise + shift;
            Vector3 hint = limb.Middle.position + limb.Bend;
            TwoBoneIK.Solve(limb.Upper, limb.Middle, limb.End, target, hint, weight);
            limb.End.rotation = Quaternion.Slerp(limb.End.rotation, limb.AnimatedEndRotation, weight);
        }

        private static Vector3 Contact(Limb limb) => limb.OnToes ? limb.AnimatedToes : limb.AnimatedEnd;

        private static LimbPinSettings Settings(CharacterRigConfig config, Limb limb) =>
            limb.IsLeg ? config.FootPin : config.HandPin;

        // How far point sits above where it would rest on the ground under it; negative inside the ground.
        // Without ground under it, the ground is taken to be level with the body. The ground provider, if any,
        // counts within the same span as the ray, where it is above what the ray hit.
        private float Lift(Vector3 point, float clearance, CharacterRigConfig config, Vector3 up)
        {
            float ground = Vector3.Dot(_body.position, up);
            Vector3 origin = point + up * config.ProbeHeight;
            float length = config.ProbeHeight + config.MaxStepDown;
            int count = Physics.RaycastNonAlloc(origin, -up, _hits, length, config.GroundLayers & ~IgnoredLayers,
                QueryTriggerInteraction.Ignore);
            IFootGround provider = Ground;
            float nearest = float.PositiveInfinity;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _hits[i];
                if (!hit.collider.transform.IsChildOf(_body) && hit.distance < nearest
                    && (provider == null || !provider.Ignores(hit.collider, point)))
                {
                    nearest = hit.distance;
                    ground = Vector3.Dot(hit.point, up);
                }
            }

            if (provider != null && provider.TryGetGround(point, out float height, out _))
            {
                var provided = new Vector3(point.x, height, point.z);
                float along = Vector3.Dot(origin - provided, up);
                if (along >= 0f && along <= length && along < nearest)
                {
                    ground = Vector3.Dot(provided, up);
                }
            }

            return Vector3.Dot(point, up) - ground - clearance;
        }

        private static void AddLeg(List<Limb> limbs, Animator animator,
            HumanBodyBones upper, HumanBodyBones middle, HumanBodyBones end, HumanBodyBones toes, float soleHeight)
        {
            Add(limbs, animator, upper, middle, end, true, soleHeight, animator.GetBoneTransform(toes));
        }

        private static void AddArm(List<Limb> limbs, Animator animator,
            HumanBodyBones upper, HumanBodyBones middle, HumanBodyBones end)
        {
            Add(limbs, animator, upper, middle, end, false, 0f, null);
        }

        private static void Add(List<Limb> limbs, Animator animator, HumanBodyBones upper,
            HumanBodyBones middle, HumanBodyBones end, bool isLeg, float soleHeight, Transform toes)
        {
            var limb = new Limb
            {
                Upper = animator.GetBoneTransform(upper),
                Middle = animator.GetBoneTransform(middle),
                End = animator.GetBoneTransform(end),
                Toes = toes,
                IsLeg = isLeg,
                SoleHeight = soleHeight,
            };
            if (limb.Upper != null && limb.Middle != null && limb.End != null)
            {
                limbs.Add(limb);
            }
        }
    }
}
