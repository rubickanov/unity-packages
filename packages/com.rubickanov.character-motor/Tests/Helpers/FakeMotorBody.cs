using System;
using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Motor.Tests
{
    /// <summary>
    /// In-memory <see cref="IMotorBody"/> used by module tests in place of
    /// <c>KinematicMotorBody</c>/<c>RigidbodyMotorBody</c>. All mutator calls
    /// are recorded and all cast queries return whatever the test arranged.
    /// Owns a throwaway <see cref="GameObject"/> with a <see cref="Rigidbody"/>
    /// so modules that call <c>Body.Transform.GetComponentInParent&lt;Rigidbody&gt;()</c>
    /// (<see cref="Modules.CrouchModule"/>, <see cref="Modules.SlideModule"/>) find one.
    /// Call <see cref="Dispose"/> from <c>[TearDown]</c>.
    /// </summary>
    internal sealed class FakeMotorBody : IMotorBody, IDisposable
    {
        private readonly GameObject _go;
        private readonly Rigidbody _rigidbody;

        public Transform Transform => _go.transform;
        public Vector3 Position { get; set; }
        public Quaternion Rotation { get; set; } = Quaternion.identity;
        public Vector3 SimulatedPosition => Position;
        public Quaternion SimulatedRotation => Rotation;
        public Vector3 Velocity { get; set; }
        public float CapsuleHeight { get; set; } = 2f;

        // ---------- Recorded calls ----------
        public readonly List<(Vector3 force, ForceMode mode)> ForcesAdded = new();
        public readonly List<Vector3> MoveCalls = new();
        public readonly List<(Vector3 axis, float angle, Space space)> RotateCalls = new();
        public readonly List<float> HeightCalls = new();

        /// <summary>
        /// Unified timeline of lifecycle events ("begin"/"end"). Assign a list shared
        /// with <see cref="RecordingModule"/> to verify call ordering across both.
        /// </summary>
        public List<string> LifecycleLog = new();

        public int BeginFrameCalls;
        public int EndFrameCalls;

        // ---------- Configured cast responses ----------
        /// <summary>Default SphereCast response; used when no direction handler matches.</summary>
        public RaycastHit? NextSphereCastHit;

        /// <summary>Default Raycast response; used when no direction handler matches.</summary>
        public RaycastHit? NextRaycastHit;

        /// <summary>
        /// Programmatic SphereCast response. Returning a non-null hit counts as a hit.
        /// Arguments: (origin, radius, direction, distance).
        /// </summary>
        public Func<Vector3, float, Vector3, float, RaycastHit?>? SphereCastHandler;

        /// <summary>
        /// Programmatic Raycast response. Arguments: (origin, direction, distance).
        /// </summary>
        public Func<Vector3, Vector3, float, RaycastHit?>? RaycastHandler;

        /// <summary>Results copied into caller-supplied buffer on <see cref="SphereCastNonAlloc"/>.</summary>
        public RaycastHit[] SphereCastNonAllocResults = Array.Empty<RaycastHit>();

        /// <summary>Number of results to copy. Defaults to the length of the results array.</summary>
        public int SphereCastNonAllocCount = -1;

        public FakeMotorBody()
        {
            _go = new GameObject("FakeMotorBody");
            _rigidbody = _go.AddComponent<Rigidbody>();
        }

        public Rigidbody OwnRigidbody => _rigidbody;

        public void Dispose()
        {
            if (_go != null)
                UnityEngine.Object.DestroyImmediate(_go);
        }

        public void BeginFrame(MotorState state, float deltaTime)
        {
            BeginFrameCalls++;
            LifecycleLog.Add("begin");
        }

        public void EndFrame(MotorState state, float deltaTime)
        {
            EndFrameCalls++;
            LifecycleLog.Add("end");
        }

        public void AddForce(Vector3 force, ForceMode mode)
        {
            ForcesAdded.Add((force, mode));
        }

        public void SetCapsuleHeight(float height)
        {
            CapsuleHeight = height;
            HeightCalls.Add(height);
        }

        public void MovePosition(Vector3 position)
        {
            Position = position;
            MoveCalls.Add(position);
        }

        public void Rotate(Vector3 axis, float angle, Space relativeTo)
        {
            RotateCalls.Add((axis, angle, relativeTo));
        }

        public void Teleport(Vector3 position, Quaternion rotation, Vector3 velocity)
        {
            Position = position;
            Rotation = rotation;
            Velocity = velocity;
        }

        public bool SphereCast(Vector3 origin, float radius, Vector3 direction, float distance, out RaycastHit hit)
        {
            if (SphereCastHandler != null)
            {
                var handlerHit = SphereCastHandler(origin, radius, direction, distance);
                if (handlerHit.HasValue)
                {
                    hit = handlerHit.Value;
                    return true;
                }
            }

            if (NextSphereCastHit.HasValue)
            {
                hit = NextSphereCastHit.Value;
                return true;
            }

            hit = default;
            return false;
        }

        public bool Raycast(Vector3 origin, Vector3 direction, float distance, out RaycastHit hit)
        {
            if (RaycastHandler != null)
            {
                var handlerHit = RaycastHandler(origin, direction, distance);
                if (handlerHit.HasValue)
                {
                    hit = handlerHit.Value;
                    return true;
                }
            }

            if (NextRaycastHit.HasValue)
            {
                hit = NextRaycastHit.Value;
                return true;
            }

            hit = default;
            return false;
        }

        public int SphereCastNonAlloc(Vector3 origin, float radius, Vector3 direction, RaycastHit[] results, float distance)
        {
            int limit = SphereCastNonAllocCount < 0 ? SphereCastNonAllocResults.Length : SphereCastNonAllocCount;
            int count = Mathf.Min(limit, results.Length);
            for (int i = 0; i < count; i++)
                results[i] = SphereCastNonAllocResults[i];
            return count;
        }

        public BodySnapshot SaveState() => new BodySnapshot(Position, Velocity, Rotation, CapsuleHeight);

        public void RestoreState(BodySnapshot snapshot)
        {
            Position = snapshot.Position;
            Velocity = snapshot.Velocity;
            Rotation = snapshot.Rotation;
            CapsuleHeight = snapshot.CapsuleHeight;
        }

        /// <summary>
        /// Integrates recorded VelocityChange forces into <see cref="Velocity"/> and clears
        /// the recorded list. Tests that run multiple <c>Simulate</c> ticks in a row use this
        /// to emulate a physics step between ticks.
        /// </summary>
        public void ApplyVelocityChanges()
        {
            for (int i = 0; i < ForcesAdded.Count; i++)
            {
                var (force, mode) = ForcesAdded[i];
                if (mode == ForceMode.VelocityChange)
                    Velocity += force;
            }
            ForcesAdded.Clear();
        }
    }
}
