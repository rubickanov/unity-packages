using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// CapsuleCast-based kinematic body. Tracks velocity internally and resolves
    /// collisions via iterative CapsuleCast sweeps. Deterministic — suitable for
    /// multiplayer with prediction and reconciliation.
    ///
    /// <para>
    /// <see cref="SimulatedPosition"/>/<see cref="SimulatedRotation"/> are the
    /// single source of truth. The underlying <see cref="UnityEngine.Transform"/>
    /// is used transiently during <see cref="BeginFrame"/>/<see cref="EndFrame"/>
    /// as a scratchpad for <see cref="Physics.CapsuleCast"/> — it is not an
    /// authoritative output of the motor.
    /// </para>
    /// </summary>
    public class KinematicMotorBody : IMotorBody
    {
        private readonly Transform _transform;
        private readonly Rigidbody _rb;
        private readonly CapsuleCollider _capsule;
        private readonly LayerMask _groundMask;
        private readonly float _skinWidth;
        private readonly int _maxBounces;

        private Vector3 _velocity;
        private float _frameDeltaTime;

        private Vector3 _simulatedPosition;
        private Quaternion _simulatedRotation;

        public KinematicMotorBody(
            Rigidbody rb,
            CapsuleCollider capsule,
            LayerMask groundMask,
            float skinWidth = 0.02f,
            int maxBounces = 3)
        {
            _rb = rb;
            _transform = rb.transform;
            _capsule = capsule;
            _groundMask = groundMask;
            _skinWidth = skinWidth;
            _maxBounces = maxBounces;

            _rb.isKinematic = true;
            _rb.useGravity = false;
            _rb.interpolation = RigidbodyInterpolation.None;

            _simulatedPosition = _transform.position;
            _simulatedRotation = _transform.rotation;
        }

        public Transform Transform => _transform;
        public Vector3 Position => _simulatedPosition;
        public Quaternion Rotation => _simulatedRotation;
        public Vector3 SimulatedPosition => _simulatedPosition;
        public Quaternion SimulatedRotation => _simulatedRotation;
        public Vector3 Velocity => _velocity;
        public float CapsuleHeight => _capsule.height;

        public void BeginFrame(MotorState state, float deltaTime)
        {
            // Prepare the collider for physics queries — sync transform to the
            // authoritative simulated state. Anything a consumer's visual bridge
            // wrote to the transform between ticks is overwritten here.
            _transform.position = _simulatedPosition;
            _transform.rotation = _simulatedRotation;

            _frameDeltaTime = deltaTime;
            state.CurrentVelocity = _velocity;
        }

        public void EndFrame(MotorState state, float deltaTime)
        {
            Vector3 displacement = _velocity * deltaTime;
            ResolveMovement(displacement);
            _simulatedPosition = _transform.position;
            state.CurrentVelocity = _velocity;
        }

        public void AddForce(Vector3 force, ForceMode mode)
        {
            switch (mode)
            {
                case ForceMode.VelocityChange:
                    _velocity += force;
                    break;
                case ForceMode.Acceleration:
                    _velocity += force * _frameDeltaTime;
                    break;
                case ForceMode.Impulse:
                    _velocity += force; // mass = 1
                    break;
                case ForceMode.Force:
                    _velocity += force * _frameDeltaTime; // mass = 1
                    break;
            }
        }

        public void SetCapsuleHeight(float height)
        {
            _capsule.height = height;
            _capsule.center = Vector3.up * (height * 0.5f);
        }

        public void MovePosition(Vector3 position)
        {
            _simulatedPosition = position;
            // Keep the scratchpad in sync so subsequent modules/queries in the
            // same tick see the updated collider position.
            _transform.position = position;
        }

        public void Rotate(Vector3 axis, float angle, Space relativeTo)
        {
            var delta = Quaternion.AngleAxis(angle, axis);
            _simulatedRotation = relativeTo == Space.World
                ? delta * _simulatedRotation
                : _simulatedRotation * delta;
            _transform.rotation = _simulatedRotation;
        }

        public void Teleport(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            _simulatedPosition = position;
            _simulatedRotation = rotation;
            _velocity = velocity;
            // Transform is left alone; next BeginFrame will sync the collider.
        }

        public bool SphereCast(Vector3 origin, float radius, Vector3 direction,
                               float distance, out RaycastHit hit)
        {
            return Physics.SphereCast(origin, radius, direction, out hit,
                                      distance, _groundMask, QueryTriggerInteraction.Ignore);
        }

        public bool Raycast(Vector3 origin, Vector3 direction,
                            float distance, out RaycastHit hit)
        {
            return Physics.Raycast(origin, direction, out hit,
                                   distance, _groundMask, QueryTriggerInteraction.Ignore);
        }

        public int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction,
                                      RaycastHit[] results, float distance)
        {
            return Physics.SphereCastNonAlloc(origin, radius, direction, results,
                                              distance, _groundMask, QueryTriggerInteraction.Ignore);
        }

        public BodySnapshot SaveState()
        {
            return new BodySnapshot(
                _simulatedPosition,
                _velocity,
                _simulatedRotation,
                _capsule.height
            );
        }

        public void RestoreState(BodySnapshot snapshot)
        {
            _simulatedPosition = snapshot.Position;
            _simulatedRotation = snapshot.Rotation;
            _velocity = snapshot.Velocity;
            _capsule.height = snapshot.CapsuleHeight;
            _capsule.center = Vector3.up * (snapshot.CapsuleHeight * 0.5f);
            // No transform write, no Physics.SyncTransforms — next BeginFrame
            // will sync the collider before any physics query runs.
        }

        private void ResolveMovement(Vector3 displacement)
        {
            Vector3 position = _transform.position;
            Vector3 remaining = displacement;

            for (int i = 0; i < _maxBounces && remaining.sqrMagnitude > 0.0001f; i++)
            {
                float distance = remaining.magnitude;
                Vector3 direction = remaining / distance;

                GetCapsulePoints(position, out Vector3 point1, out Vector3 point2);
                float radius = _capsule.radius - _skinWidth;

                if (Physics.CapsuleCast(point1, point2, radius, direction, out RaycastHit hit,
                        distance + _skinWidth, _groundMask, QueryTriggerInteraction.Ignore))
                {
                    float moveDistance = Mathf.Max(0f, hit.distance - _skinWidth);
                    position += direction * moveDistance;

                    remaining -= direction * moveDistance;
                    remaining = Vector3.ProjectOnPlane(remaining, hit.normal);

                    // Cancel velocity component into the wall
                    _velocity = Vector3.ProjectOnPlane(_velocity, hit.normal);
                }
                else
                {
                    position += remaining;
                    break;
                }
            }

            _transform.position = position;
        }

        private void GetCapsulePoints(Vector3 position, out Vector3 point1, out Vector3 point2)
        {
            Vector3 center = position + _capsule.center;
            float halfHeight = Mathf.Max(0f, _capsule.height * 0.5f - _capsule.radius);
            Vector3 offset = Vector3.up * halfHeight;
            point1 = center + offset;
            point2 = center - offset;
        }
    }
}
