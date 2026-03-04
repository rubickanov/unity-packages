using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// CapsuleCast-based kinematic body. Tracks velocity internally and resolves
    /// collisions via iterative CapsuleCast sweeps. Deterministic — suitable for
    /// multiplayer with prediction and reconciliation.
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

        // Interpolation
        private Vector3 _previousPosition;
        private Vector3 _simulatedPosition;

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

            _previousPosition = _transform.position;
            _simulatedPosition = _transform.position;
        }

        public Transform Transform => _transform;
        public Vector3 Position => _transform.position;
        public Quaternion Rotation => _transform.rotation;
        public Vector3 Velocity => _velocity;
        public float CapsuleHeight => _capsule.height;

        public void BeginFrame(MotorState state, float deltaTime)
        {
            // Restore exact simulated position for physics (undo visual interpolation)
            _transform.position = _simulatedPosition;
            _previousPosition = _simulatedPosition;

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

        public void Interpolate(float alpha)
        {
            _transform.position = Vector3.Lerp(_previousPosition, _simulatedPosition, alpha);
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
            _transform.position = position;
            _previousPosition = position;
            _simulatedPosition = position;
        }

        public void Rotate(Vector3 axis, float angle, Space relativeTo)
        {
            _transform.Rotate(axis, angle, relativeTo);
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

        public BodySnapshot SaveState()
        {
            return new BodySnapshot(
                _transform.position,
                _velocity,
                _transform.rotation,
                _capsule.height
            );
        }

        public void RestoreState(BodySnapshot snapshot)
        {
            _transform.position = snapshot.Position;
            _previousPosition = snapshot.Position;
            _simulatedPosition = snapshot.Position;
            _velocity = snapshot.Velocity;
            _transform.rotation = snapshot.Rotation;
            _capsule.height = snapshot.CapsuleHeight;
            _capsule.center = Vector3.up * (snapshot.CapsuleHeight * 0.5f);
            Physics.SyncTransforms();
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
