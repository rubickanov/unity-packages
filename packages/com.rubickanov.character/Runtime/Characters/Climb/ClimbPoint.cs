using System;
using Animancer;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// A place on the rim of something afloat where a swimmer climbs out of the water onto it, such as the side of a
    /// tube or the stern of a boat: this transform sits on top of the rim, its forward pointing onto what is climbed.
    /// The climber ends with its soles on <see cref="Top"/>. Whatever handles <see cref="Climbed"/> takes the climber
    /// from there, such as a seat; with no handler the point is a seat itself: the climber sits on <see cref="Top"/>,
    /// facing its forward, until it jumps back into the water.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class ClimbPoint : MonoBehaviour, ICharacterMount, IClimbable
    {
        [Tooltip("Where the climber's soles end up, facing its forward.")]
        public Transform Top;
        [Tooltip("Whether a swimmer may climb here now, such as while the seat behind it is free.")]
        public bool Open = true;
        [Tooltip("Optional. Held while sitting on the point itself.")]
        public ClipTransition SeatPose = new();

        private Rigidbody _body;

        /// <summary>
        /// A climber reached the top, on the machine that simulates it, standing and in control again. Without a
        /// handler the climber sits on the point instead.
        /// </summary>
        public event Action<CharacterMotor> Climbed;

        /// <summary>Whether something takes climbers from the top, rather than the point seating them.</summary>
        public bool HasTaker => Climbed != null;

        /// <summary>How fast a point on the rim moves, from the physics of what carries it where there is a body.</summary>
        public Vector3 VelocityAt(Vector3 point) => _body != null ? _body.GetPointVelocity(point) : Vector3.zero;

        public Transform Anchor => Top != null ? Top : transform;
        public ClipTransition Pose => SeatPose;
        public Transform LeftHand => null;
        public Transform RightHand => null;
        public Transform LeftFoot => null;
        public Transform RightFoot => null;
        public bool HandsFree => true;
        public float CameraDistance => 0f;
        public Vector3 Velocity => VelocityAt(Anchor.position);

        private void OnEnable()
        {
            _body = GetComponentInParent<Rigidbody>();
            Climbables.Add(this);
        }

        private void OnDisable() => Climbables.Remove(this);

        public bool TryEdge(in ClimbQuery query, out ClimbEdge edge)
        {
            edge = default;
            Transform rim = transform;
            float distance = Vector3.Distance(query.Grip, rim.position);
            Vector3 onto = Vector3.ProjectOnPlane(rim.forward, Vector3.up).normalized;
            if (!Open || distance >= query.Reach || Vector3.Dot(query.Facing, onto) < query.FacingCos)
            {
                return false;
            }

            edge = new ClimbEdge
            {
                Target = this,
                Space = rim,
                LocalRim = Vector3.zero,
                LocalTop = rim.InverseTransformPoint(Anchor.position),
                LocalOnto = rim.InverseTransformDirection(onto),
                Height = rim.position.y - query.Grip.y,
                Distance = distance,
            };
            return true;
        }

        void IClimbable.Climbed(CharacterMotor climber) => Climbed?.Invoke(climber);

        private void OnDrawGizmos()
        {
            Gizmos.color = Open ? Color.green : Color.red;
            Gizmos.DrawWireSphere(transform.position, 0.1f);
            Gizmos.DrawLine(transform.position, Anchor.position);
        }
    }
}
