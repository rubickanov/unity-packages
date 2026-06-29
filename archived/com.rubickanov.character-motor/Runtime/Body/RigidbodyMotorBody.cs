using UnityEngine;

namespace Rubickanov.Motor
{
    /// <summary>
    /// Rigidbody-based physics body. Forces are applied via <see cref="Rigidbody.AddForce"/>
    /// and resolved by Unity's physics engine. Best for singleplayer prototypes.
    /// Not suitable for deterministic replay (multiplayer reconciliation).
    ///
    /// <para>
    /// <see cref="SimulatedPosition"/>/<see cref="SimulatedRotation"/> are latched
    /// at <see cref="BeginFrame"/> and reflect the <em>post-physics state of the
    /// previous tick</em>. PhysX resolves forces after all <c>FixedUpdate</c>
    /// scripts return, so during a single <c>Simulate</c> call
    /// <see cref="Rigidbody.position"/> has not yet advanced. Consumers reading
    /// <see cref="SimulatedPosition"/> between ticks get a consistent,
    /// one-tick-delayed snapshot — the inherent price of rigidbody mode.
    /// </para>
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

        private Vector3 _simulatedPosition;
        private Quaternion _simulatedRotation;

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

            _simulatedPosition = _rb.position;
            _simulatedRotation = _rb.rotation;
        }

        public Transform Transform => _rb.transform;
        public Vector3 Position => _simulatedPosition;
        public Quaternion Rotation => _simulatedRotation;
        public Vector3 SimulatedPosition => _simulatedPosition;
        public Quaternion SimulatedRotation => _simulatedRotation;
        public Vector3 Velocity => _rb.linearVelocity;
        public float CapsuleHeight => _capsule.height;

        public void BeginFrame(MotorState state, float deltaTime)
        {
            // Latch the current physics state. transform ↔ _rb.{position,rotation}
            // is kept in sync by PhysX, so no explicit transform write is needed.
            _simulatedPosition = _rb.position;
            _simulatedRotation = _rb.rotation;
            state.CurrentVelocity = _rb.linearVelocity;
        }

        public void EndFrame(MotorState state, float deltaTime)
        {
            // No-op: Unity physics engine resolves forces applied during the frame,
            // but only after all FixedUpdates return — re-reading _rb.position here
            // would give the same stale value as BeginFrame.
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
            // Physics-aware move; resolves during the next PhysX step. Note: unlike
            // KinematicMotorBody.MovePosition, the collider is NOT moved immediately
            // in the current tick — modules that need an instant collider shift must
            // use Kinematic body.
            _rb.MovePosition(position);
            _simulatedPosition = position;
        }

        public void Rotate(Vector3 axis, float angle, Space relativeTo)
        {
            var delta = Quaternion.AngleAxis(angle, axis);
            var newRotation = relativeTo == Space.World
                ? delta * _rb.rotation
                : _rb.rotation * delta;
            _rb.MoveRotation(newRotation);
            _simulatedRotation = newRotation;
        }

        public void Teleport(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            // Direct writes to _rb.{position,rotation} are teleports — unlike
            // MovePosition/MoveRotation, they do not interpolate across the
            // physics step.
            _rb.position = position;
            _rb.rotation = rotation;
            _rb.linearVelocity = velocity;
            _simulatedPosition = position;
            _simulatedRotation = rotation;
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
            _rb.linearVelocity = snapshot.Velocity;
            _rb.rotation = snapshot.Rotation;
            _simulatedPosition = snapshot.Position;
            _simulatedRotation = snapshot.Rotation;
            _capsule.height = snapshot.CapsuleHeight;
            _capsule.center = Vector3.up * (snapshot.CapsuleHeight * 0.5f);
        }
    }
}
