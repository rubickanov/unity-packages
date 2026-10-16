using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Climbs a swimmer out of the water onto a <see cref="IClimbable"/> when it jumps facing an edge it can reach: the
    /// body leaves the motor and moves up over the rim and onto the top in the climbable's own space, so a ride that
    /// bobs or moves carries the climber along. A ride jerked away under the hands, or a knockdown, lets go back into the
    /// water. Without an edge in reach the jump lunges as usual. On top, a <see cref="ClimbPoint"/> without a taker
    /// seats the climber on itself; a jump from there goes back into the water.
    /// </summary>
    // After the motor reads its input this frame.
    [DefaultExecutionOrder(10)]
    [DisallowMultipleComponent]
    public sealed class CharacterClimb : MonoBehaviour
    {
        public CharacterMotor Character;
        [Tooltip("Optional. A knockdown ends the climb.")]
        public CharacterRagdoll Ragdoll;
        [Tooltip("How far from the shoulders a single climb point (a tube's rim) can be and still be reached, metres.")]
        [Min(0)] public float Reach = 1.3f;
        [Tooltip("Widest angle between where the swimmer faces and a climb point's way onto the ride, degrees.")]
        [Range(0, 180)] public float FacingAngle = 75f;
        [Tooltip("Seconds from the water to standing on top.")]
        [Min(0.05f)] public float Duration = 0.8f;
        [Tooltip("Share of the climb spent pulling up over the rim; the rest steps onto the top.")]
        [Range(0.1f, 0.9f)] public float PullShare = 0.6f;
        [Tooltip("How high the soles clear the rim, metres.")]
        [Min(0)] public float Clearance = 0.15f;
        [Tooltip("The hands let go when the rim speeds up harder than this, m/s².")]
        [Min(0)] public float MaxGripAcceleration = 25f;
        [Tooltip("Speed of a jump off a seat back into the water: back from the rim and up, m/s.")]
        public Vector2 JumpOff = new(2f, 3f);

        private ClimbEdge _edge;
        private bool _climbing;
        // The point the character sits on, having climbed it.
        private ClimbPoint _seat;
        private float _progress;
        // Start and the point over the rim, in the edge's space.
        private Vector3 _from;
        private Vector3 _over;
        private Vector3 _rimVelocity;

        /// <summary>The climber is on its way out of the water.</summary>
        public bool IsClimbing => _climbing;

        /// <summary>The character sits on the climb point it climbed.</summary>
        public bool IsSeated => _seat != null;

        /// <summary>A jump would climb now: swimming, not yet climbing, an edge in reach ahead.</summary>
        public bool InReach => TryReach(out _);

        /// <summary>The edge a jump would climb now, if any.</summary>
        public bool TryReach(out ClimbEdge edge)
        {
            edge = default;
            return Character != null && Character.IsSwimming && !Character.IsSuspended && !_climbing && Nearest(out edge);
        }

        private void OnEnable()
        {
            if (Character != null)
            {
                Character.SwimJump = TryClimb;
            }
        }

        private void OnDisable()
        {
            if (Character != null && Character.SwimJump == TryClimb)
            {
                Character.SwimJump = null;
            }

            _climbing = false;
            Unseat();
        }

        private void Update()
        {
            if (_seat != null && Character.SourceInput.JumpPressed && Character.isActiveAndEnabled)
            {
                ClimbPoint seat = _seat;
                Vector3 back = Vector3.ProjectOnPlane(-seat.transform.forward, Vector3.up).normalized;
                Unseat();
                Character.Resume();
                Character.AddVelocity(seat.Velocity + back * JumpOff.x + Vector3.up * JumpOff.y);
            }
        }

        // Asked by the motor when the swimmer jumps.
        private bool TryClimb()
        {
            if (!Nearest(out ClimbEdge edge))
            {
                return false;
            }

            Character.Suspend();
            // Still in the water until over the rim: shows and replicates as swimming.
            Character.StartSwimming();
            _edge = edge;
            _climbing = true;
            _progress = 0f;
            _from = edge.Space.InverseTransformPoint(Character.Motor.TransientPosition);
            _over = edge.LocalRim + Vector3.up * Clearance;
            _rimVelocity = edge.Target.VelocityAt(edge.Space.TransformPoint(edge.LocalRim));
            return true;
        }

        private bool Nearest(out ClimbEdge best)
        {
            best = default;
            Transform body = Character.transform;
            var capsule = Character.Motor.Capsule;
            var query = new ClimbQuery
            {
                Grip = body.position + body.up * Character.Settings.FloatDepth,
                Facing = Vector3.ProjectOnPlane(body.forward, Vector3.up).normalized,
                ReachUp = Character.Settings.ClimbReachUp,
                Reach = Reach,
                FacingCos = Mathf.Cos(FacingAngle * Mathf.Deg2Rad),
                Radius = capsule.radius,
                Height = capsule.height,
                Layers = Character.Motor.CollidableLayers,
                Self = Character.transform,
            };
            bool found = false;
            IReadOnlyList<IClimbable> all = Climbables.All;
            for (int i = 0; i < all.Count; i++)
            {
                if (all[i].TryEdge(query, out ClimbEdge edge) && (!found || edge.Distance < best.Distance))
                {
                    best = edge;
                    found = true;
                }
            }

            return found;
        }

        private void FixedUpdate()
        {
            if (_seat != null)
            {
                Ride();
                return;
            }

            if (!_climbing)
            {
                return;
            }

            float dt = Time.fixedDeltaTime;
            Transform space = _edge.Space;
            if (space == null || _edge.Target is Behaviour { isActiveAndEnabled: false } || !Character.IsSuspended
                || (Ragdoll != null && Ragdoll.State != BodyState.Upright))
            {
                _climbing = false;
                return;
            }

            Vector3 velocity = _edge.Target.VelocityAt(space.TransformPoint(_edge.LocalRim));
            if ((velocity - _rimVelocity).magnitude / dt > MaxGripAcceleration)
            {
                LetGo();
                return;
            }

            _rimVelocity = velocity;
            _progress = Mathf.Min(1f, _progress + dt / Duration);
            Vector3 local = _progress < PullShare
                ? Vector3.Lerp(_from, _over, Smooth(_progress / PullShare))
                : Vector3.Lerp(_over, _edge.LocalTop, Smooth((_progress - PullShare) / (1f - PullShare)));
            Vector3 onto = Vector3.ProjectOnPlane(space.TransformDirection(_edge.LocalOnto), Vector3.up);
            Quaternion rotation = onto.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(onto, Vector3.up)
                : Character.transform.rotation;
            Character.Follow(space.TransformPoint(local), rotation);
            if (_progress >= 1f)
            {
                _climbing = false;
                if (_edge.Target is not ClimbPoint { HasTaker: false } point)
                {
                    Character.Resume();
                    Character.SetVelocity(velocity);
                    _edge.Target.Climbed(Character);
                    return;
                }

                // A point without a taker is a seat itself.
                _seat = point;
                point.Open = false;
                Character.Mount = point;
            }
        }

        // Held on the seat as it moves; a knockdown throws the rider off, and the ragdoll has the body from there.
        private void Ride()
        {
            if (!_seat.isActiveAndEnabled || (Ragdoll != null && Ragdoll.State != BodyState.Upright))
            {
                Unseat();
                return;
            }

            Transform anchor = _seat.Anchor;
            Character.Follow(anchor.position, anchor.rotation);
        }

        private void Unseat()
        {
            if (_seat == null)
            {
                return;
            }

            _seat.Open = true;
            if (Character != null && ReferenceEquals(Character.Mount, _seat))
            {
                Character.Mount = null;
            }

            _seat = null;
        }

        // Back into the water, moving as the climber was.
        private void LetGo()
        {
            _climbing = false;
            Character.Resume();
            Character.AddVelocity(_rimVelocity);
        }

        private static float Smooth(float t) => t * t * (3f - 2f * t);
    }
}
