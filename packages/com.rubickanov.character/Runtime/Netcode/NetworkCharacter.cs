using System;
using System.Collections.Generic;
using Rubickanov.Log;
using Unity.Netcode;
using UnityEngine;

namespace Rubickanov.Characters.Netcode
{
    /// <summary>
    /// Replicates a character, a player's or a bot's. The owner simulates it and sends snapshots of each step;
    /// everyone else plays them back, so movement feels instant for the owner and animations match what others
    /// actually see. Animations themselves are never sent. A downed character's ragdoll is sent bone by bone, since
    /// physics does not come out the same twice (a live one too while the game streams it, <see cref="IRagdollNetFrame"/>);
    /// its get-up is replayed from an event. The game's data rides in the same
    /// snapshots through <see cref="ICharacterNetPart"/>s, and its glue (camera, input, spawn point) is an
    /// <see cref="INetworkCharacterSide"/>, both components anywhere on the character. Ownership never changes.
    /// </summary>
    [DisallowMultipleComponent]
    public sealed class NetworkCharacter : NetworkBehaviour
    {
        private static readonly LogChannel Log = LogChannel.Get("Character");

        public CharacterMotor Character;
        public CharacterAnimator Animator;

        [Tooltip("How far behind its owner a remote character is shown, in seconds. " +
                 "Must cover a few send intervals so a late or lost snapshot does not stall it.")]
        [Min(0)] public float InterpolationDelay = 0.1f;
        [Tooltip("A remote character that moves further than this between snapshots jumps instead of sliding.")]
        [Min(0)] public float TeleportDistance = 5f;

        private readonly List<ICharacterNetPart> _parts = new();
        private readonly List<INetworkCharacterSide> _sides = new();
        private ICharacterNetFrame _frame;
        private IRagdollNetFrame _ragdollFrame;
        private RemoteCharacterMotion _remote;
        private CharacterRagdoll _ragdoll;
        private RemoteRagdoll _remoteRagdoll;
        private readonly Quaternion[] _captured = new Quaternion[RagdollSnapshot.BoneCount];
        private double _nextSendTime;
        private bool _sentGrounded;
        private bool _sentSwimming;
        private bool _sentSeated;
        private byte _sentJumps;
        private byte _jumps;
        private byte _sentFalls;
        private byte _falls;
        private byte _sentGetUps;
        private byte _getUps;
        private bool _faceUp;
        private byte _getUpClip = CharacterGetUp.Classic;
        private ushort _getUpTime;
        private bool _warnedOverflow;

        /// <summary>A copy's playback of its owner's snapshots; null on the owner.</summary>
        public RemoteCharacterMotion Remote => _remote;

        /// <summary>The character's ragdoll, found at spawn; null without one.</summary>
        public CharacterRagdoll Ragdoll => _ragdoll;

        /// <summary>
        /// A replica was just shown: raised after every <see cref="RemoteCharacterMotion.Update"/>, also before its first
        /// snapshot, and before the ragdoll's.
        /// </summary>
        public event Action Shown;

        public override void OnNetworkSpawn()
        {
            _ragdoll = Character.TryGetComponent(out CharacterRagdoll ragdoll) ? ragdoll : null;
            GetComponentsInChildren(true, _parts);
            GetComponentsInChildren(true, _sides);
            _frame = null;
            _ragdollFrame = GetComponentInChildren<IRagdollNetFrame>(true);
            foreach (ICharacterNetPart part in _parts)
            {
                if (part is ICharacterNetFrame frame)
                {
                    if (_frame != null)
                    {
                        Log.Warn($"{name}: more than one frame part, {frame} ignored", this);
                        continue;
                    }

                    _frame = frame;
                }
            }

            if (IsOwner)
            {
                BecomeLocal();
            }
            else
            {
                BecomeRemote();
            }
        }

        public override void OnNetworkDespawn()
        {
            foreach (INetworkCharacterSide side in _sides)
            {
                if (IsOwner)
                {
                    side.OnLocalDespawn(this);
                }
                else
                {
                    side.OnReplicaDespawn(this);
                }
            }

            if (_remote != null)
            {
                Animator.Source = null;
                if (_ragdoll != null)
                {
                    _remote.FellDown -= _ragdoll.ShowDown;
                    _remote.GettingUp -= _ragdoll.ShowGetUp;
                }

                _remote = null;
                _remoteRagdoll = null;
            }

            if (!IsOwner)
            {
                return;
            }

            if (_ragdoll != null)
            {
                _ragdoll.FellDown -= OnFellDown;
                _ragdoll.GettingUp -= OnGettingUp;
            }

            Character.Jumped -= OnJumped;
            Character.MotionUpdated -= OnStep;
        }

        private void BecomeLocal()
        {
            // The game's first: a player starts at its spawn point, and its camera looks where the body faces then.
            foreach (INetworkCharacterSide side in _sides)
            {
                side.OnLocal(this);
            }

            Character.Jumped += OnJumped;
            Character.MotionUpdated += OnStep;
            if (_ragdoll != null)
            {
                _ragdoll.FellDown += OnFellDown;
                _ragdoll.GettingUp += OnGettingUp;
            }
        }

        private void BecomeRemote()
        {
            // The owner simulates; here the capsule only follows, still blocking other characters.
            Character.enabled = false;
            _remote = new RemoteCharacterMotion(Character.transform, InterpolationDelay, TeleportDistance, _frame, _parts);
            Animator.Source = _remote;
            if (_ragdoll != null)
            {
                _ragdoll.IsReplica = true;
                _remoteRagdoll = new RemoteRagdoll(_ragdoll, _ragdollFrame);
                _remote.FellDown += _ragdoll.ShowDown;
                _remote.GettingUp += _ragdoll.ShowGetUp;
            }

            // The game's then, still before its components' Start: input sources off, camera gone…
            foreach (INetworkCharacterSide side in _sides)
            {
                side.OnReplica(this);
            }
        }

        /// <summary>
        /// Knocks the character down with <paramref name="impulse"/> (N·s) at <paramref name="point"/>, from any
        /// machine: the owner simulates the fall and everyone else sees it replicated.
        /// </summary>
        public void Knockdown(Vector3 impulse, Vector3 point)
        {
            if (!IsSpawned)
            {
                return;
            }

            if (!IsOwner)
            {
                KnockdownRpc(impulse, point);
            }
            else if (_ragdoll != null)
            {
                _ragdoll.Knockdown(impulse, point);
            }
        }

        private void Update()
        {
            if (_remote == null)
            {
                return;
            }

            _remote.Update(Time.deltaTime);
            Shown?.Invoke();
            _remoteRagdoll?.Update(_remote.Playback);
        }

        private void OnJumped() => _jumps++;

        private void OnFellDown() => _falls++;

        private void OnGettingUp(CharacterGetUp getUp)
        {
            _getUps++;
            _faceUp = getUp.FaceUp;
            _getUpClip = getUp.Clip;
            _getUpTime = (ushort)Mathf.Clamp(Mathf.RoundToInt(getUp.Time * 1000f), 0, ushort.MaxValue);
        }

        // Runs in the simulation step, right after the motor moved.
        private void OnStep(CharacterMotion motion)
        {
            double time = Time.fixedTimeAsDouble;
            // Send at the tick rate, but a jump, landing, knockdown, get-up, sitting down, standing up, going in or out
            // of the water, or a part's change goes out on its own step so its timing is exact. Every part is asked.
            bool seated = Character.Mount != null;
            bool partsNow = false;
            for (int i = 0; i < _parts.Count; i++)
            {
                partsNow |= _parts[i].SendNow;
            }

            bool changed = partsNow || motion.IsGrounded != _sentGrounded || motion.IsSwimming != _sentSwimming
                || seated != _sentSeated || _jumps != _sentJumps
                || _falls != _sentFalls || _getUps != _sentGetUps;
            if (!changed && time < _nextSendTime)
            {
                return;
            }

            double interval = 1.0 / NetworkManager.NetworkConfig.TickRate;
            _nextSendTime = time < _nextSendTime + interval ? _nextSendTime + interval : time + interval;
            _sentGrounded = motion.IsGrounded;
            _sentSwimming = motion.IsSwimming;
            _sentSeated = seated;
            _sentJumps = _jumps;
            _sentFalls = _falls;
            _sentGetUps = _getUps;
            bool down = _ragdoll != null && _ragdoll.IsDown;

            Vector3 position = Character.Motor.TransientPosition;
            Quaternion rotation = Character.Motor.TransientRotation;
            _frame?.ToFrame(ref position, ref rotation);

            var snapshot = new CharacterNetSnapshot
            {
                Time = time,
                Position = position,
                Rotation = rotation,
                Aim = motion.Aim,
                Grounded = motion.IsGrounded,
                Swimming = motion.IsSwimming,
                FacesLook = motion.FacesLook,
                Jumps = _jumps,
                Falls = _falls,
                GetUps = _getUps,
                FaceUp = _faceUp,
                GetUpClip = _getUpClip,
                GetUpTime = _getUpTime,
                Down = down,
                Seated = seated,
            };
            for (int i = 0; i < _parts.Count; i++)
            {
                var writer = new CharacterNetPartWriter();
                _parts[i].Write(ref writer);
                if ((writer.Length > 0 || writer.Overflowed) && !snapshot.TryAddPart(_parts[i].PartId, writer)
                    && !_warnedOverflow)
                {
                    _warnedOverflow = true;
                    Log.Warn($"{name}: part {_parts[i].PartId} ({writer.Length} B) does not fit the snapshot, left out", this);
                }
            }

            SnapshotRpc(snapshot);

            // Down, or upright while the game streams a live body (in a frame of its own, never frame 0).
            bool streams = !down && _ragdollFrame != null && _ragdollFrame.Streams;
            if ((down || streams && _ragdoll.Ragdoll.Mode != RagdollMode.Off)
                && _ragdoll.Ragdoll.Bones.Count == RagdollSnapshot.BoneCount)
            {
                var pose = new RagdollSnapshot { Time = time };
                Vector3 framePosition = default;
                Quaternion frameRotation = Quaternion.identity;
                double frameTime = 0.0;
                uint frame = _ragdollFrame != null
                    ? _ragdollFrame.FrameOf(out framePosition, out frameRotation, out frameTime)
                    : 0;
                if (frame != 0)
                {
                    pose.Frame = frame;
                    pose.Time = frameTime;
                    _ragdoll.Ragdoll.CaptureIn(framePosition, frameRotation, out pose.Hips, _captured);
                }
                else if (streams)
                {
                    return;
                }
                else
                {
                    _ragdoll.Ragdoll.Capture(out pose.Hips, _captured);
                }

                for (int i = 0; i < _captured.Length; i++)
                {
                    pose.SetRotation(i, _captured[i]);
                }

                RagdollRpc(pose);
            }
        }

        // Unreliable: a lost pose is covered by interpolation, and the next one supersedes it anyway.
        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        private void SnapshotRpc(CharacterNetSnapshot snapshot) => _remote?.Add(snapshot);

        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        private void RagdollRpc(RagdollSnapshot snapshot) => _remoteRagdoll?.Add(snapshot);

        // Reliable: a hit must not be lost. The owner simulates it wherever its character is by now.
        [Rpc(SendTo.Owner)]
        private void KnockdownRpc(Vector3 impulse, Vector3 point)
        {
            if (_ragdoll != null)
            {
                _ragdoll.Knockdown(impulse, point);
            }
        }
    }
}
