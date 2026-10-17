using System.Collections.Generic;
using Rubickanov.Rig;
using Unity.Profiling;
using UnityEngine;

namespace Rubickanov.Characters
{
    public enum RagdollMode
    {
        /// <summary>The bones are inactive: nothing to simulate, nothing to collide with.</summary>
        Off,

        /// <summary>The bones are kinematic, held where they were put: a frozen or a replicated pose.</summary>
        Kinematic,

        /// <summary>The bones are simulated, their muscles fading from the pose the character fell in to limp.</summary>
        Active,
    }

    /// <summary>
    /// A character's ragdoll: bodies that are the character only while it is down. Upright, the capsule is
    /// the body and the skeleton is animated; the ragdoll is off, reading the animated pose so it can take
    /// over from it with the same pose and speed. The visible skeleton is shown from it by <see cref="Map"/>.
    /// The bones hang under an object that never moves, apart from the capsule and the model, so that whatever
    /// moves them does not drag the simulated bodies along.
    /// A plain class; CharacterRagdoll owns it and decides the order: <see cref="SamplePose"/> every frame
    /// while upright, <see cref="Drive"/> every physics step while active, <see cref="Map"/> after animation.
    /// </summary>
    public sealed class Ragdoll
    {
        /// <summary>The head's place in <see cref="Bones"/>, in the order RagdollBuilder builds them.</summary>
        public const int HeadIndex = 3;

        /// <summary>The chest's place in <see cref="Bones"/>, in the order RagdollBuilder builds them.</summary>
        public const int ChestIndex = 2;

        private static readonly ProfilerMarker DriveMarker = new("Ragdoll.Drive");

        private readonly RagdollConfig _config;
        private readonly Transform _root;
        private readonly RagdollBone[] _bones;
        private readonly int[] _parents;
        private readonly Collider[][] _colliders;
        private readonly Collider[] _own;
        private readonly float[] _masses;
        private readonly float _mass;

        // A push's spread, rebuilt by Push: each bone's joints from the bone hit and its change of speed.
        private readonly int[] _pushHops;
        private readonly float[] _pushSpeeds;

        // The animated pose in the root's space, in the ragdoll bones' axes, as last read and the read before it.
        private readonly Vector3[] _position;
        private readonly Quaternion[] _rotation;
        private readonly Vector3[] _lastPosition;
        private readonly Quaternion[] _lastRotation;
        private float _sampleDt;
        private int _samples;

        // A replicated pose in the world, rebuilt by Place.
        private readonly Vector3[] _placePosition;
        private readonly Quaternion[] _placeRotation;

        private float _appliedSpring = -1f;
        private float _appliedDamper = -1f;

        // The joints' targets for the pose the body fell in, set on Activate: what the limp muscles hold.
        private readonly Quaternion[] _holdTargets;

        // Only with the flail on: the reflex and the bones' touch.
        private readonly FlailReflex _flail;
        private readonly RagdollTouch[] _touch;
        private bool _wet;

        /// <param name="flail">Whether the muscles flail in the air (<see cref="FlailReflex"/>): the caller has the
        /// clips for it and the config turns it on.</param>
        public Ragdoll(RagdollConfig config, Transform root, IReadOnlyList<RagdollBone> bones,
            IReadOnlyList<Collider> own, bool flail = false)
        {
            _config = config;
            _root = root;
            _bones = new RagdollBone[bones.Count];
            _parents = new int[bones.Count];
            _colliders = new Collider[bones.Count][];
            _masses = new float[bones.Count];
            for (int i = 0; i < bones.Count; i++)
            {
                _bones[i] = bones[i];
                _parents[i] = -1;
                for (int p = 0; p < i; p++)
                {
                    if (bones[p] == bones[i].Parent)
                    {
                        _parents[i] = p;
                    }
                }

                _colliders[i] = bones[i].GetComponentsInChildren<Collider>(true);
                _masses[i] = bones[i].Body.mass;
                _mass += _masses[i];
            }

            _own = new Collider[own.Count];
            for (int i = 0; i < own.Count; i++)
            {
                _own[i] = own[i];
            }

            _position = new Vector3[_bones.Length];
            _rotation = new Quaternion[_bones.Length];
            _lastPosition = new Vector3[_bones.Length];
            _lastRotation = new Quaternion[_bones.Length];
            _placePosition = new Vector3[_bones.Length];
            _placeRotation = new Quaternion[_bones.Length];
            _pushHops = new int[_bones.Length];
            _pushSpeeds = new float[_bones.Length];
            _holdTargets = new Quaternion[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                _holdTargets[i] = Quaternion.identity;
            }

            if (!flail)
            {
                return;
            }

            _flail = new FlailReflex(this, config, _bones, _parents, _mass);
            var ownColliders = new HashSet<Collider>(_own);
            foreach (Collider[] colliders in _colliders)
            {
                ownColliders.UnionWith(colliders);
            }

            _touch = new RagdollTouch[_bones.Length];
            for (int i = 0; i < _bones.Length; i++)
            {
                RagdollBone bone = _bones[i];
                _touch[i] = bone.TryGetComponent(out RagdollTouch touch) ? touch : bone.gameObject.AddComponent<RagdollTouch>();
                _touch[i].Construct(ownColliders);
            }
        }

        /// <summary>The muscles' reflex in the air, null when the flail is off.</summary>
        public FlailReflex Flail => _flail;

        /// <summary>
        /// The force the bones pressed on anything not the character's own in the last driven step, N; 0 when the
        /// flail is off.
        /// </summary>
        public float Touch { get; private set; }

        /// <summary>Whether the pose should be read every frame: the muscles reach for the flail clip playing.</summary>
        public bool ReadsFlailPose => _flail != null && _flail.State == FlailState.Flailing;

        /// <summary>
        /// Whether a bone is in water, set every physics step: the flail lets go at once and does not start again
        /// while it stays wet (floating touches nothing, so it would count as flight).
        /// </summary>
        public void SetWet(bool wet)
        {
            _wet = wet;
            if (wet && _flail != null && _flail.HoldsMuscles)
            {
                _flail.Reset();
                ReleaseMuscles();
            }
        }

        public bool HasJoint(int index) => _bones[index].Joint != null && _parents[index] >= 0;

        /// <summary>Joint <paramref name="index"/>'s target for the pose last read by <see cref="SamplePose"/>.</summary>
        public Quaternion PoseTarget(int index)
        {
            RagdollBone bone = _bones[index];
            Quaternion local = Quaternion.Inverse(_rotation[_parents[index]]) * _rotation[index];
            return RagdollMath.TargetRotation(bone.JointSpace, bone.RestLocal, local);
        }

        /// <summary>Joint <paramref name="index"/>'s target for the pose the body fell in, which limp muscles hold.</summary>
        public Quaternion HoldTarget(int index) => _holdTargets[index];

        /// <summary>Sets joint <paramref name="index"/>'s slerp drive, for a reflex that holds the muscles.</summary>
        public void SetMuscle(int index, float spring, float damper, float maxTorque, Quaternion target)
        {
            ConfigurableJoint joint = _bones[index].Joint;
            joint.slerpDrive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce = maxTorque,
            };
            joint.targetRotation = target;
            _appliedSpring = _appliedDamper = -1f;
        }

        // The reflex let go: the muscles are limp at once, holding the pose the body fell in, and Drive writes them.
        private void ReleaseMuscles()
        {
            MuscleWeight = _config.LimpMuscleWeight;
            for (int i = 1; i < _bones.Length; i++)
            {
                if (HasJoint(i))
                {
                    _bones[i].Joint.targetRotation = _holdTargets[i];
                }
            }

            _appliedSpring = _appliedDamper = -1f;
            DriveLimp(0f);
        }

        // PhysX sleeps a jointed island as one and sends no collision stay for sleeping bodies, so a body lying
        // still reports no touch; asleep means at rest on something.
        private bool Asleep()
        {
            foreach (RagdollBone bone in _bones)
            {
                if (bone.Body.IsSleeping())
                {
                    return true;
                }
            }

            return false;
        }

        private float TakeTouch()
        {
            float force = 0f;
            foreach (RagdollTouch touch in _touch)
            {
                force += touch.Take();
            }

            return force;
        }

        public RagdollMode Mode { get; private set; }

        /// <summary>Whether the ragdoll is limp: its limits and its own collisions on, muscles fading.</summary>
        public bool IsLimp { get; private set; }

        /// <summary>The drive's weight, 1 when the ragdoll takes over and fading to the limp weight.</summary>
        public float MuscleWeight { get; private set; } = 1f;

        /// <summary>The bones, the hips first and every bone after the one it is connected to.</summary>
        public IReadOnlyList<RagdollBone> Bones => _bones;

        /// <summary>Adds every bone's colliders to <paramref name="into"/>.</summary>
        public void GetColliders(List<Collider> into)
        {
            foreach (Collider[] colliders in _colliders)
            {
                into.AddRange(colliders);
            }
        }

        /// <summary>Radius of the head's collider, metres; 0 for none.</summary>
        public float HeadRadius
        {
            get
            {
                float radius = 0f;
                foreach (Collider collider in _colliders[HeadIndex])
                {
                    Vector3 scale = collider.transform.lossyScale;
                    float size = Mathf.Max(Mathf.Abs(scale.x), Mathf.Abs(scale.y), Mathf.Abs(scale.z));
                    radius = Mathf.Max(radius, collider switch
                    {
                        SphereCollider sphere => sphere.radius * size,
                        CapsuleCollider capsule => capsule.radius * size,
                        BoxCollider box => Mathf.Min(box.size.x, box.size.y, box.size.z) * 0.5f * size,
                        _ => 0f,
                    });
                }

                return radius;
            }
        }

        public RagdollBone Hips => _bones[0];
        public RagdollBone Chest => _bones[ChestIndex];
        public RagdollBone Head => _bones[HeadIndex];
        public float Mass => _mass;

        /// <summary>The place in <see cref="Bones"/> of the bone <paramref name="index"/> hangs from, -1 for the hips.</summary>
        public int ParentOf(int index) => _parents[index];

        /// <summary>
        /// The rotation the character's root would have with bone <paramref name="index"/> where it is and the
        /// body in its rest pose: which way that bone faces. The hips' is the body's, the head's the eyes'.
        /// </summary>
        public Quaternion Facing(int index)
        {
            RagdollBone bone = _bones[index];
            return bone.transform.rotation * Quaternion.Inverse(bone.RestRotation);
        }

        /// <summary>
        /// Reads the visible skeleton's pose in the root's space, keeping the read before it for the pose's
        /// velocity. <paramref name="dt"/> is the time since that read.
        /// </summary>
        public void SamplePose(float dt)
        {
            _root.GetPositionAndRotation(out Vector3 rootPosition, out Quaternion rootRotation);
            Quaternion toRoot = Quaternion.Inverse(rootRotation);
            for (int i = 0; i < _bones.Length; i++)
            {
                RagdollBone bone = _bones[i];
                _lastPosition[i] = _position[i];
                _lastRotation[i] = _rotation[i];
                _position[i] = toRoot * (bone.Visible.position - rootPosition);
                _rotation[i] = toRoot * (bone.Visible.rotation * bone.Offset);
            }

            // The first read has nothing before it: the pose stands still.
            if (_samples == 0)
            {
                System.Array.Copy(_position, _lastPosition, _bones.Length);
                System.Array.Copy(_rotation, _lastRotation, _bones.Length);
            }

            _sampleDt = dt;
            _samples++;
        }

        /// <summary>Forgets the reads, for when the pose jumps: a teleport, getting up.</summary>
        public void ClearSamples() => _samples = 0;

        /// <summary>
        /// Has the bones that are on ignore <paramref name="other"/>, or collide with it again, e.g. the vehicle a body
        /// fell out of: its bones start inside it.
        /// </summary>
        public void IgnoreCollision(Collider other, bool ignore)
        {
            if (other == null || !other.enabled || !other.gameObject.activeInHierarchy)
            {
                return;
            }

            foreach (Collider[] colliders in _colliders)
            {
                foreach (Collider collider in colliders)
                {
                    if (collider.enabled && collider.gameObject.activeInHierarchy)
                    {
                        Physics.IgnoreCollision(collider, other, ignore);
                    }
                }
            }
        }

        /// <summary>Whether the bounds of a bone that is on overlap <paramref name="bounds"/>.</summary>
        public bool Overlaps(Bounds bounds)
        {
            foreach (Collider[] colliders in _colliders)
            {
                foreach (Collider collider in colliders)
                {
                    if (collider.enabled && collider.gameObject.activeInHierarchy && collider.bounds.Intersects(bounds))
                    {
                        return true;
                    }
                }
            }

            return false;
        }

        /// <summary>Switches the bones off: inactive, nothing simulated.</summary>
        public void TurnOff()
        {
            for (int i = _bones.Length - 1; i >= 0; i--)
            {
                _bones[i].gameObject.SetActive(false);
            }

            _samples = 0;
            IsLimp = false;
            _flail?.Reset();
            Mode = RagdollMode.Off;
        }

        /// <summary>
        /// Makes the ragdoll the body: the bones placed on the animated pose, simulated and woken, each sent off
        /// with the pose's own velocity plus <paramref name="carrierVelocity"/> (the capsule's), no faster than
        /// the config allows, so a running character does not stop dead. The muscles start at full, holding the
        /// pose it fell in; <see cref="GoLimp"/> lets them fade.
        /// </summary>
        public void Activate(Vector3 carrierVelocity) => Activate(carrierVelocity, _config.MaxActivationSpeed);

        /// <summary>As <see cref="Activate(Vector3)"/>, no bone faster than <paramref name="maxSpeed"/>, m/s.</summary>
        public void Activate(Vector3 carrierVelocity, float maxSpeed)
        {
            if (_samples == 0)
            {
                SamplePose(0f);
            }

            SwitchOn();
            Quaternion rootRotation = _root.rotation;
            PlaceOnPose();
            for (int i = 0; i < _bones.Length; i++)
            {
                Rigidbody body = _bones[i].Body;
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                (Vector3 linear, Vector3 angular) = PoseVelocity(i);
                body.linearVelocity = Vector3.ClampMagnitude(rootRotation * linear + carrierVelocity, maxSpeed);
                body.angularVelocity = rootRotation * angular;
                body.WakeUp();
            }

            SetTargets();
            IsLimp = false;
            MuscleWeight = 1f;
            _appliedSpring = _appliedDamper = -1f;
            if (_flail != null)
            {
                _flail.Bind();
                _flail.Reset();
                TakeTouch();
                Touch = 0f;
                _wet = false;
            }

            SetLimits(ConfigurableJointMotion.Free);
            IgnoreOwnPairs(true);
            SetMaterial(_config.PinnedMaterial);
            Mode = RagdollMode.Active;
        }

        /// <summary>Pulls every bone by <paramref name="acceleration"/> (m/s²) on top of the physics' gravity, for one physics step.</summary>
        public void Accelerate(Vector3 acceleration)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                _bones[i].Body.AddForce(acceleration, ForceMode.Acceleration);
            }
        }

        /// <summary>
        /// Falls limp: the muscles fade to the config's limp weight as <see cref="Drive"/> is stepped, the joints
        /// damped more, their limits and the bones' collisions with each other on, and the material with friction.
        /// </summary>
        public void GoLimp()
        {
            if (Mode != RagdollMode.Active || IsLimp)
            {
                return;
            }

            IsLimp = true;
            SetLimits(ConfigurableJointMotion.Limited);
            IgnoreOwnPairs(false);
            SetMaterial(_config.LimpMaterial);
        }

        /// <summary>
        /// Steps the muscles. The slerp drive is written only when its values change: it is a struct, and
        /// writing one wakes the joint.
        /// </summary>
        public void Drive(float dt)
        {
            if (Mode != RagdollMode.Active)
            {
                return;
            }

            using ProfilerMarker.AutoScope scope = DriveMarker.Auto();
            // The reflex holds the drives while it flails or eases; the limp fade waits, and is complete once it lets go.
            if (_flail != null)
            {
                float touch = TakeTouch();
                Touch = touch;
                if (!_wet)
                {
                    if (_flail.Step(touch, Asleep(), dt, out bool ended))
                    {
                        return;
                    }

                    if (ended)
                    {
                        ReleaseMuscles();
                        return;
                    }
                }
            }

            DriveLimp(dt);
        }

        // The muscles fade from full to the limp weight while limp, written only when they change.
        private void DriveLimp(float dt)
        {
            if (IsLimp)
            {
                MuscleWeight = _config.LimpTime > 0f
                    ? Mathf.MoveTowards(MuscleWeight, _config.LimpMuscleWeight,
                        (1f - _config.LimpMuscleWeight) / _config.LimpTime * dt)
                    : _config.LimpMuscleWeight;
            }

            float spring = _config.Spring * MuscleWeight;
            float damper = _config.Damper * MuscleWeight + (IsLimp ? _config.LimpDamper : 0f);
            if (Mathf.Approximately(spring, _appliedSpring) && Mathf.Approximately(damper, _appliedDamper))
            {
                return;
            }

            var drive = new JointDrive
            {
                positionSpring = spring,
                positionDamper = damper,
                maximumForce = float.MaxValue,
            };
            foreach (RagdollBone bone in _bones)
            {
                if (bone.Joint != null)
                {
                    bone.Joint.slerpDrive = drive;
                }
            }

            _appliedSpring = spring;
            _appliedDamper = damper;
        }

        /// <summary>
        /// Pushes an active ragdoll with <paramref name="impulse"/> (N·s) at <paramref name="point"/>: half on the
        /// whole body, so the push carries it, and half where it lands, on the bone nearest the point and the
        /// bones a few joints from it, so the body folds there. The bone hit goes no faster than the config
        /// allows over the rest of the body, so a light limb is not torn off its joint; what that leaves goes to
        /// the whole body. Capped so the body as a whole goes no faster than <paramref name="maxSpeed"/>.
        /// </summary>
        public void Push(Vector3 impulse, Vector3 point, float maxSpeed)
        {
            if (Mode != RagdollMode.Active || impulse.sqrMagnitude <= 0f)
            {
                return;
            }

            impulse = Vector3.ClampMagnitude(impulse, maxSpeed * _mass);
            float magnitude = impulse.magnitude;
            Vector3 direction = impulse / magnitude;
            int hit = Nearest(point, out Vector3 surface);
            RagdollMath.Hops(_parents, hit, _config.PushReach, _pushHops);
            float left = RagdollMath.SpreadPush(_masses, _pushHops, _config.PushFalloff, magnitude * 0.5f,
                _config.MaxHitBoneSpeed, _pushSpeeds);
            Vector3 shared = direction * ((magnitude * 0.5f + left) / _mass);
            for (int i = 0; i < _bones.Length; i++)
            {
                Rigidbody body = _bones[i].Body;
                body.AddForce(shared, ForceMode.VelocityChange);
                if (i == hit)
                {
                    // On the surface where it lands, so the bone hit turns with the blow as well.
                    body.AddForceAtPosition(direction * (_pushSpeeds[i] * _masses[i]), surface, ForceMode.Impulse);
                }
                else if (_pushSpeeds[i] > 0f)
                {
                    body.AddForce(direction * _pushSpeeds[i], ForceMode.VelocityChange);
                }
            }
        }

        // The bone whose colliders come nearest point, and the nearest point on them.
        private int Nearest(Vector3 point, out Vector3 surface)
        {
            int nearest = 0;
            surface = point;
            float best = float.PositiveInfinity;
            for (int i = 0; i < _bones.Length; i++)
            {
                foreach (Collider collider in _colliders[i])
                {
                    Vector3 closest = collider.ClosestPoint(point);
                    float distance = (closest - point).sqrMagnitude;
                    if (distance < best)
                    {
                        best = distance;
                        nearest = i;
                        surface = closest;
                    }
                }
            }

            return nearest;
        }

        /// <summary>
        /// Holds the bones where they are as kinematic bodies, for the pose to blend out of into getting up.
        /// Nothing pushes them from here on.
        /// </summary>
        public void Freeze()
        {
            if (Mode == RagdollMode.Off)
            {
                return;
            }

            foreach (RagdollBone bone in _bones)
            {
                bone.Body.interpolation = RigidbodyInterpolation.None;
                bone.Body.isKinematic = true;
            }

            _flail?.Reset();
            Mode = RagdollMode.Kinematic;
        }

        /// <summary>Switches the bones on as kinematic bodies, for a pose replicated from elsewhere.</summary>
        public void BeKinematic()
        {
            SwitchOn();
            foreach (RagdollBone bone in _bones)
            {
                bone.Body.interpolation = RigidbodyInterpolation.None;
                bone.Body.isKinematic = true;
            }

            IsLimp = true;
            Mode = RagdollMode.Kinematic;
        }

        /// <summary>
        /// The bones as they go on the wire: the hips' position and rotation in the root's space, and every
        /// other bone's rotation relative to the one it hangs from. Only rotations: each joint's two anchors are
        /// fixed, one in each body, so the chain closes without a position per bone.
        /// </summary>
        public void Capture(out Vector3 hipsPosition, IList<Quaternion> rotations)
        {
            _root.GetPositionAndRotation(out Vector3 rootPosition, out Quaternion rootRotation);
            Quaternion toRoot = Quaternion.Inverse(rootRotation);
            Transform hips = _bones[0].transform;
            hipsPosition = toRoot * (hips.position - rootPosition);
            rotations[0] = toRoot * hips.rotation;
            for (int i = 1; i < _bones.Length; i++)
            {
                rotations[i] = Quaternion.Inverse(_bones[_parents[i]].transform.rotation) * _bones[i].transform.rotation;
            }
        }

        /// <summary>
        /// As <see cref="Capture"/>, the hips in the space of a frame at <paramref name="framePosition"/> and
        /// <paramref name="frameRotation"/>, from the bodies rather than the transforms: in the physics step an
        /// interpolated bone's transform is where it was drawn, not where its body is.
        /// </summary>
        public void CaptureIn(Vector3 framePosition, Quaternion frameRotation, out Vector3 hipsPosition,
            IList<Quaternion> rotations)
        {
            Quaternion toFrame = Quaternion.Inverse(frameRotation);
            Rigidbody hips = _bones[0].Body;
            hipsPosition = toFrame * (hips.position - framePosition);
            rotations[0] = toFrame * hips.rotation;
            for (int i = 1; i < _bones.Length; i++)
            {
                rotations[i] = Quaternion.Inverse(_bones[_parents[i]].Body.rotation) * _bones[i].Body.rotation;
            }
        }

        /// <summary>Places kinematic bones on a pose from <see cref="Capture"/>, relative to the root as it is now.</summary>
        public void Place(Vector3 hipsPosition, IReadOnlyList<Quaternion> rotations)
        {
            _root.GetPositionAndRotation(out Vector3 rootPosition, out Quaternion rootRotation);
            Place(rootPosition, rootRotation, hipsPosition, rotations);
        }

        /// <summary>Places kinematic bones on a pose from <see cref="CaptureIn"/>, in a frame where it is now.</summary>
        public void Place(Vector3 rootPosition, Quaternion rootRotation, Vector3 hipsPosition,
            IReadOnlyList<Quaternion> rotations)
        {
            if (Mode != RagdollMode.Kinematic)
            {
                return;
            }

            Vector3[] positions = _placePosition;
            Quaternion[] worldRotations = _placeRotation;
            positions[0] = rootPosition + rootRotation * hipsPosition;
            worldRotations[0] = rootRotation * rotations[0];
            for (int i = 1; i < _bones.Length; i++)
            {
                int parent = _parents[i];
                worldRotations[i] = worldRotations[parent] * rotations[i];
                ConfigurableJoint joint = _bones[i].Joint;
                positions[i] = joint != null
                    ? positions[parent] + worldRotations[parent] * joint.connectedAnchor - worldRotations[i] * joint.anchor
                    : positions[parent];
            }

            for (int i = 0; i < _bones.Length; i++)
            {
                RagdollBone bone = _bones[i];
                bone.transform.SetPositionAndRotation(positions[i], worldRotations[i]);
                bone.Body.position = positions[i];
                bone.Body.rotation = worldRotations[i];
            }
        }

        /// <summary>
        /// The bodies as another machine needs them to simulate on (<see cref="Seed"/>): each bone's position, rotation,
        /// velocity and spin in the world, from the bodies rather than the transforms.
        /// </summary>
        public void CaptureState(IList<Vector3> positions, IList<Quaternion> rotations, IList<Vector3> velocities,
            IList<Vector3> spins)
        {
            for (int i = 0; i < _bones.Length; i++)
            {
                Rigidbody body = _bones[i].Body;
                positions[i] = body.position;
                rotations[i] = body.rotation;
                velocities[i] = body.linearVelocity;
                spins[i] = body.angularVelocity;
            }
        }

        /// <summary>
        /// Makes the ragdoll the body from a state simulated elsewhere (<see cref="CaptureState"/>): every bone where it
        /// was, at its own velocity, limp, the muscles holding that pose, as on a body that has been down a while.
        /// </summary>
        public void Seed(IReadOnlyList<Vector3> positions, IReadOnlyList<Quaternion> rotations,
            IReadOnlyList<Vector3> velocities, IReadOnlyList<Vector3> spins)
        {
            SwitchOn();
            for (int i = 0; i < _bones.Length; i++)
            {
                RagdollBone bone = _bones[i];
                Rigidbody body = bone.Body;
                bone.transform.SetPositionAndRotation(positions[i], rotations[i]);
                body.isKinematic = false;
                body.interpolation = RigidbodyInterpolation.Interpolate;
                body.position = positions[i];
                body.rotation = rotations[i];
                body.linearVelocity = velocities[i];
                body.angularVelocity = spins[i];
                body.WakeUp();
            }

            for (int i = 1; i < _bones.Length; i++)
            {
                RagdollBone bone = _bones[i];
                int parent = _parents[i];
                if (bone.Joint != null && parent >= 0)
                {
                    Quaternion local = Quaternion.Inverse(rotations[parent]) * rotations[i];
                    _holdTargets[i] = RagdollMath.TargetRotation(bone.JointSpace, bone.RestLocal, local);
                    bone.Joint.targetRotation = _holdTargets[i];
                }
            }

            MuscleWeight = _config.LimpMuscleWeight;
            _appliedSpring = _appliedDamper = -1f;
            if (_flail != null)
            {
                _flail.Bind();
                _flail.Reset();
                TakeTouch();
                Touch = 0f;
                _wet = false;
            }

            Mode = RagdollMode.Active;
            IsLimp = true;
            SetLimits(ConfigurableJointMotion.Limited);
            IgnoreOwnPairs(false);
            SetMaterial(_config.LimpMaterial);
        }

        /// <summary>
        /// Shows the ragdoll on the visible skeleton at <paramref name="weight"/> between the animated pose (0)
        /// and the ragdoll's (1). Run after animation, on the bones as they will be drawn.
        /// </summary>
        public void Map(float weight)
        {
            if (Mode == RagdollMode.Off || weight <= 0f)
            {
                return;
            }

            // Every bone after its parent, so a parent's place is final before its child is placed on it.
            foreach (RagdollBone bone in _bones)
            {
                Transform visible = bone.Visible;
                RagdollBone parent = bone.Parent;
                bone.transform.GetPositionAndRotation(out Vector3 position, out Quaternion rotation);
                (Vector3 mappedPosition, Quaternion mappedRotation) = parent != null
                    ? RagdollMath.MapPose(visible.position, visible.rotation, position, rotation, bone.Offset, true,
                        parent.transform.position, parent.transform.rotation, parent.Visible.position,
                        parent.Visible.rotation, parent.Offset, weight)
                    : RagdollMath.MapPose(visible.position, visible.rotation, position, rotation, bone.Offset, false,
                        default, Quaternion.identity, default, Quaternion.identity, Quaternion.identity, weight);
                visible.SetPositionAndRotation(mappedPosition, mappedRotation);
            }
        }

        // The animated pose's velocity at bone index in the root's space, from the last two reads.
        private (Vector3 Linear, Vector3 Angular) PoseVelocity(int index) =>
            _sampleDt > 0f && _samples > 1
                ? ((_position[index] - _lastPosition[index]) / _sampleDt,
                    RagdollMath.AngularVelocity(_lastRotation[index], _rotation[index], _sampleDt))
                : (Vector3.zero, Vector3.zero);

        // Switches inactive bones on in their rest pose, so each joint is made with the zero its targets are
        // measured from, then sets what a switch loses: the solver iterations, which a Rigidbody does not save,
        // and the ignored collisions, which a collider forgets when it is deactivated.
        private void SwitchOn()
        {
            if (Mode != RagdollMode.Off)
            {
                return;
            }

            foreach (RagdollBone bone in _bones)
            {
                bone.ToRest(_root);
            }

            foreach (RagdollBone bone in _bones)
            {
                bone.gameObject.SetActive(true);
                bone.Body.solverIterations = _config.SolverIterations;
                bone.Body.solverVelocityIterations = _config.SolverVelocityIterations;
            }

            foreach (Collider[] colliders in _colliders)
            {
                foreach (Collider collider in colliders)
                {
                    foreach (Collider own in _own)
                    {
                        if (own != null && own.enabled && own.gameObject.activeInHierarchy)
                        {
                            Physics.IgnoreCollision(collider, own, true);
                        }
                    }
                }
            }

            IgnoreOwnPairs(true);
        }

        private void PlaceOnPose()
        {
            _root.GetPositionAndRotation(out Vector3 rootPosition, out Quaternion rootRotation);
            for (int i = 0; i < _bones.Length; i++)
            {
                Vector3 position = rootPosition + rootRotation * _position[i];
                Quaternion rotation = rootRotation * _rotation[i];
                RagdollBone bone = _bones[i];
                bone.transform.SetPositionAndRotation(position, rotation);
                bone.Body.position = position;
                bone.Body.rotation = rotation;
            }
        }

        // The muscles hold the pose the character fell in: each joint's target is that pose's turn from its rest.
        private void SetTargets()
        {
            for (int i = 1; i < _bones.Length; i++)
            {
                RagdollBone bone = _bones[i];
                int parent = _parents[i];
                if (bone.Joint == null || parent < 0)
                {
                    continue;
                }

                Quaternion local = Quaternion.Inverse(_rotation[parent]) * _rotation[i];
                _holdTargets[i] = RagdollMath.TargetRotation(bone.JointSpace, bone.RestLocal, local);
                bone.Joint.targetRotation = _holdTargets[i];
            }
        }

        private void SetLimits(ConfigurableJointMotion motion)
        {
            foreach (RagdollBone bone in _bones)
            {
                ConfigurableJoint joint = bone.Joint;
                if (joint != null)
                {
                    joint.angularXMotion = motion;
                    joint.angularYMotion = motion;
                    joint.angularZMotion = motion;
                }
            }
        }

        private void SetMaterial(PhysicsMaterial material)
        {
            if (material == null)
            {
                return;
            }

            foreach (Collider[] colliders in _colliders)
            {
                foreach (Collider collider in colliders)
                {
                    collider.sharedMaterial = material;
                }
            }
        }

        // The pairs a joint connects never collide whatever this says: the joints are made with collisions off.
        private void IgnoreOwnPairs(bool ignore)
        {
            for (int i = 0; i < _colliders.Length; i++)
            {
                for (int j = i + 1; j < _colliders.Length; j++)
                {
                    foreach (Collider a in _colliders[i])
                    {
                        foreach (Collider b in _colliders[j])
                        {
                            Physics.IgnoreCollision(a, b, ignore);
                        }
                    }
                }
            }
        }
    }
}
