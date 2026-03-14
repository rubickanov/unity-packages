using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Rigidbody-based physics body. Forces are applied via <see cref="Rigidbody.AddForce"/>
    /// and resolved by Unity's physics engine. Best for singleplayer prototypes.
    /// Not suitable for deterministic replay (multiplayer reconciliation).
    /// </summary>
    public class RigidbodyMotorBody : IMotorBody
    {
        private readonly Rigidbody _rb;
        private readonly CapsuleCollider _capsule;
        private readonly LayerMask _groundMask;

        private static PhysicsMaterial? _zeroFrictionMaterial;

        private static PhysicsMaterial ZeroFrictionMaterial => _zeroFrictionMaterial ??= new PhysicsMaterial
        {
            staticFriction = 0f,
            dynamicFriction = 0f,
            frictionCombine = PhysicsMaterialCombine.Minimum,
            hideFlags = HideFlags.HideAndDontSave
        };

        // Interpolation — track physics positions without touching _rb.position
        private Vector3 _previousPosition;
        private Vector3 _simulatedPosition;
        private bool _isInterpolated;

        public RigidbodyMotorBody(Rigidbody rb, CapsuleCollider capsule, LayerMask groundMask)
        {
            _rb = rb;
            _capsule = capsule;
            _groundMask = groundMask;

            _rb.freezeRotation = true;
            _rb.interpolation = RigidbodyInterpolation.None;
            _rb.collisionDetectionMode = CollisionDetectionMode.Continuous;
            _rb.isKinematic = false;
            _rb.useGravity = false;

            _capsule.material = ZeroFrictionMaterial;

            _previousPosition = _rb.position;
            _simulatedPosition = _rb.position;
        }

        public Transform Transform => _rb.transform;
        public Vector3 Position => _rb.position;
        public Quaternion Rotation => _rb.rotation;
        public Vector3 Velocity => _rb.linearVelocity;
        public float CapsuleHeight => _capsule.height;

        public void BeginFrame(MotorState state, float deltaTime)
        {
            // Undo visual interpolation — restore physics transform for colliders
            if (_isInterpolated)
            {
                _rb.transform.position = _simulatedPosition;
                Physics.SyncTransforms();
                _isInterpolated = false;
            }

            // Shift the interpolation window
            _previousPosition = _simulatedPosition;
            _simulatedPosition = _rb.position;

            state.CurrentVelocity = _rb.linearVelocity;
        }

        public void EndFrame(MotorState state, float deltaTime)
        {
            // No-op: Unity physics engine resolves forces applied during the frame.
        }

        public void Interpolate(float alpha)
        {
            // Capture latest post-PhysX position
            _simulatedPosition = _rb.position;

            // Move ONLY the transform for visual purposes — don't touch _rb.position
            _rb.transform.position = Vector3.Lerp(_previousPosition, _simulatedPosition, alpha);
            _isInterpolated = true;
        }

        public void AddForce(Vector3 force, ForceMode mode)
        {
            _rb.AddForce(force, mode);
        }

        public void SetCapsuleHeight(float height)
        {
            _capsule.height = height;
            _capsule.center = Vector3.up * (height * 0.5f);
        }

        public void MovePosition(Vector3 position)
        {
            _rb.MovePosition(position);
            _previousPosition = position;
            _simulatedPosition = position;
            _isInterpolated = false;
        }

        public void Rotate(Vector3 axis, float angle, Space relativeTo)
        {
            _rb.transform.Rotate(axis, angle, relativeTo);
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
                _rb.position,
                _rb.linearVelocity,
                _rb.rotation,
                _capsule.height
            );
        }

        public void RestoreState(BodySnapshot snapshot)
        {
            _rb.position = snapshot.Position;
            _previousPosition = snapshot.Position;
            _simulatedPosition = snapshot.Position;
            _isInterpolated = false;
            _rb.linearVelocity = snapshot.Velocity;
            _rb.rotation = snapshot.Rotation;
            _capsule.height = snapshot.CapsuleHeight;
            _capsule.center = Vector3.up * (snapshot.CapsuleHeight * 0.5f);
        }
    }
}
