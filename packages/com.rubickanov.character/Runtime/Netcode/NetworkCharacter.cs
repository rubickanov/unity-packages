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
    /// <see cref="INetworkCharacterSide"/>, both components anywhere on the character. Ownership never changes; the
    /// ragdoll alone can be simulated by another machine for a while (<see cref="Lend"/>), e.g. the one that simulates
    /// the ride the character is held on: there it is simulated and sent from, and everywhere else, the owner
    /// included, shown.
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
        [Tooltip("How long a machine given the ragdoll waits for its state before it goes on from the pose it shows, s.")]
        [Min(0)] public float StateWait = 0.5f;

        // The simulator of the ragdoll when it is not the owner's, and how many hand-overs there were.
        private struct RagdollLend : INetworkSerializeByMemcpy
        {
            public ulong Simulator;
            public ushort Count;
        }

        private const ulong ByOwner = ulong.MaxValue;
        private readonly NetworkVariable<RagdollLend> _lend = new(new RagdollLend { Simulator = ByOwner });

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
        private IRagdollNetState _ragdollState;
        private double _nextRagdollTime;
        // A hand-over this machine takes: the state it waits for, since when.
        private int _taking = -1;
        private float _takingSince;
        private RagdollState? _handed;
        // This machine simulates the body for its owner and it came to rest: the owner takes it back.
        private bool _returning;
        // Simulated here and given up (HandUp): shown here, its state with the host, until the lend moves on.
        private bool _handedUp;
        // Host: a state handed up, for the next machine the body is lent to, and who handed it up.
        private RagdollState? _parked;
        private ulong _parkedFrom;
        // The machine given it found the last one gone: the pose shown is all there is.
        private bool _lastGone;
        private readonly Vector3[] _statePositions = new Vector3[RagdollSnapshot.BoneCount];
        private readonly Quaternion[] _stateRotations = new Quaternion[RagdollSnapshot.BoneCount];
        private readonly Vector3[] _stateVelocities = new Vector3[RagdollSnapshot.BoneCount];
        private readonly Vector3[] _stateSpins = new Vector3[RagdollSnapshot.BoneCount];

        /// <summary>A copy's playback of its owner's snapshots; null on the owner.</summary>
        public RemoteCharacterMotion Remote => _remote;

        /// <summary>The character's ragdoll, found at spawn; null without one.</summary>
        public CharacterRagdoll Ragdoll => _ragdoll;

        /// <summary>The client whose machine simulates the ragdoll: the owner's, unless the host lent it (<see cref="Lend"/>).</summary>
        public ulong RagdollSimulator => IsSpawned && _lend.Value.Simulator != ByOwner ? _lend.Value.Simulator : OwnerClientId;

        /// <summary>This machine simulates the ragdoll (true without a session).</summary>
        public bool SimulatesRagdoll => !IsSpawned || RagdollSimulator == NetworkManager.LocalClientId;

        /// <summary>The ragdoll is simulated by a machine other than its owner's.</summary>
        public bool RagdollLent => IsSpawned && _lend.Value.Simulator != ByOwner;

        /// <summary>Hand-overs of the ragdoll so far.</summary>
        public int Lends => _lend.Value.Count;

        /// <summary>Host: the lent ragdoll went down on its simulator since it was lent (thrown off what held it).</summary>
        public bool LentDown { get; private set; }

        /// <summary>
        /// Host: <paramref name="client"/>'s machine simulates the ragdoll from now (the owner's to give it back). The
        /// machine that simulated it hands its state on and shows it from then; the new one goes on from that state,
        /// or from the pose it shows should none come within <see cref="StateWait"/>. A lent body that comes to rest
        /// goes back to its owner, which gets it up.
        /// </summary>
        /// <param name="anew">
        /// A hand-over even to the machine that simulates it now, once that machine handed the body up
        /// (<see cref="HandUp"/>), e.g. given back what it just let go of.
        /// </param>
        public void Lend(ulong client, bool anew = false)
        {
            if (!IsServer || !IsSpawned || _ragdoll == null)
            {
                return;
            }

            ulong value = client == OwnerClientId ? ByOwner : client;
            if (value == _lend.Value.Simulator && !(anew && _parked.HasValue && _parkedFrom == client))
            {
                return;
            }

            LentDown = false;
            Log.Info($"{name}: ragdoll simulated by client {client} (owner {OwnerClientId})");
            _lend.Value = new RagdollLend { Simulator = value, Count = (ushort)(_lend.Value.Count + 1) };
            if (_parked.HasValue)
            {
                RagdollState parked = _parked.Value;
                _parked = null;
                Forward(parked);
            }
        }

        /// <summary>
        /// The machine that simulates the ragdoll gives it up now, e.g. as it lets go of what the body is held on: the
        /// bodies' state goes to the host for whichever machine it is lent to next (<see cref="Lend"/>), and until that
        /// one's poses come the body is shown here holding its last pose. Nothing while down (a body in flight stays).
        /// </summary>
        public void HandUp()
        {
            if (!IsSpawned || _ragdoll == null || !SimulatesRagdoll || _handedUp || _ragdoll.State != BodyState.Upright
                || _ragdoll.Ragdoll == null || !_ragdoll.Ragdoll.ShowsBody)
            {
                return;
            }

            RagdollState state = CaptureState(_lend.Value.Count);
            _remoteRagdoll ??= new RemoteRagdoll(_ragdoll, _ragdollFrame);
            if (TryPose(false, true, Time.fixedTimeAsDouble, out RagdollSnapshot last))
            {
                _remoteRagdoll.Add(last);
            }

            _handedUp = true;
            _taking = -1;
            _returning = false;
            _ragdoll.GetsUpElsewhere = false;
            _ragdoll.IsReplica = true;
            Log.Info($"{name}: ragdoll handed up (lend {state.Lend}), shown here");
            if (IsServer)
            {
                Park(state, NetworkManager.LocalClientId);
            }
            else
            {
                HandUpRpc(state);
            }
        }

        // Host: keeps a handed-up state for the next lend, or passes it on to a lend that moved on already.
        private void Park(RagdollState state, ulong from)
        {
            if (state.Lend != _lend.Value.Count)
            {
                if (RagdollSimulator != from)
                {
                    Forward(state);
                }

                return;
            }

            _parked = state;
            _parkedFrom = from;
        }

        // Host: a handed-up state to the machine the body is lent to now, as part of this hand-over.
        private void Forward(RagdollState state)
        {
            state.Lend = _lend.Value.Count;
            if (RagdollSimulator == NetworkManager.LocalClientId)
            {
                _handed = state;
                TakeOverWhenHanded();
            }
            else
            {
                StateRpc(state, RpcTarget.Single(RagdollSimulator, RpcTargetUse.Temp));
            }
        }

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
            _ragdollState = GetComponentInChildren<IRagdollNetState>(true);
            _taking = -1;
            _handed = null;
            _returning = false;
            _handedUp = false;
            _parked = null;
            _lastGone = false;
            LentDown = false;
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

            if (_ragdoll != null)
            {
                _remoteRagdoll ??= new RemoteRagdoll(_ragdoll, _ragdollFrame);
                _ragdoll.FellDown += OnFellDown;
                _ragdoll.WouldGetUp += OnWouldGetUp;
                _lend.OnValueChanged += OnLend;
                TakeRole();
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
            }

            _remoteRagdoll = null;
            if (_ragdoll != null)
            {
                _ragdoll.FellDown -= OnFellDown;
                _ragdoll.WouldGetUp -= OnWouldGetUp;
                _lend.OnValueChanged -= OnLend;
                _ragdoll.GetsUpElsewhere = false;
            }

            if (!IsOwner)
            {
                return;
            }

            if (_ragdoll != null)
            {
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
        /// machine: the ragdoll's simulator simulates the fall and everyone else sees it replicated.
        /// </summary>
        public void Knockdown(Vector3 impulse, Vector3 point) => Knockdown(impulse, point, NoStriker);

        /// <summary>
        /// As <see cref="Knockdown(Vector3, Vector3)"/>, naming the network object that struck it: the machine that
        /// simulates the fall raises <see cref="KnockedBy"/> with it, so the two can pass through each other there.
        /// </summary>
        public void Knockdown(Vector3 impulse, Vector3 point, ulong striker)
        {
            if (!IsSpawned)
            {
                return;
            }

            if (!SimulatesRagdoll)
            {
                KnockdownRpc(impulse, point, striker, RpcTarget.Single(RagdollSimulator, RpcTargetUse.Temp));
            }
            else if (_ragdoll != null)
            {
                Knock(impulse, point, striker);
            }
        }

        /// <summary>No striker named with a knockdown.</summary>
        public const ulong NoStriker = ulong.MaxValue;

        /// <summary>
        /// The ragdoll was just knocked down here, on its simulator, by a knockdown that named its striker (a
        /// <see cref="NetworkObject.NetworkObjectId"/>); raised after the fall has started.
        /// </summary>
        public event Action<ulong> KnockedBy;

        private void Knock(Vector3 impulse, Vector3 point, ulong striker)
        {
            bool wasDown = _ragdoll.IsDown;
            _ragdoll.Knockdown(impulse, point);
            if (striker != NoStriker && !wasDown && _ragdoll.IsDown)
            {
                KnockedBy?.Invoke(striker);
            }
        }

        private void Update()
        {
            if (_remote != null)
            {
                _remote.Update(Time.deltaTime);
                Shown?.Invoke();
                if (!SimulatesRagdoll || _handedUp)
                {
                    _remoteRagdoll?.Update(_remote.Playback);
                }
            }
            else if (_remoteRagdoll != null && (!SimulatesRagdoll || _handedUp))
            {
                // The owner of a lent body shows it as everyone else does: its poses are on the game's frames' clocks.
                _remoteRagdoll.Update(NetworkManager.ServerTime.Time - InterpolationDelay);
            }

            TakeOverWhenHanded();
        }

        private void OnJumped() => _jumps++;

        private void OnFellDown()
        {
            if (IsOwner)
            {
                _falls++;
            }

            if (!IsSpawned || IsOwner || !SimulatesRagdoll)
            {
                return;
            }

            // Simulated here for its owner: the owner and everyone else hear of the fall from here.
            if (IsServer)
            {
                LentDown = true;
            }

            DownRpc(_lend.Value.Count);
        }

        // ---- Who simulates the ragdoll ----------------------------------------------------------------------------

        // Every machine, at spawn and on each hand-over: a replica unless the ragdoll is simulated here.
        private void TakeRole()
        {
            bool here = SimulatesRagdoll;
            if (!here)
            {
                _ragdoll.IsReplica = true;
            }

            _ragdoll.GetsUpElsewhere = here && !IsOwner;
            if (here && _ragdoll.IsReplica && _taking < 0)
            {
                // Simulated here from the start (this machine's own character, or lent here before it joined).
                _ragdoll.IsReplica = false;
            }
        }

        private void OnLend(RagdollLend previous, RagdollLend current)
        {
            ulong local = NetworkManager.LocalClientId;
            ulong was = previous.Simulator == ByOwner ? OwnerClientId : previous.Simulator;
            ulong now = current.Simulator == ByOwner ? OwnerClientId : current.Simulator;
            bool again = was == now && now == local && _handedUp;
            if (was == now && !again || _ragdoll == null)
            {
                return;
            }

            Log.Info($"{name}: ragdoll from client {was} to {now} (lend {current.Count})");
            bool handedUp = _handedUp;
            if (was == local)
            {
                _handedUp = false;
                if (!handedUp)
                {
                    // Handed on: its state to the next machine, shown here from now.
                    RagdollState state = CaptureState(current.Count);
                    _returning = false;
                    _ragdoll.GetsUpElsewhere = false;
                    _ragdoll.IsReplica = true;
                    StateRpc(state, RpcTarget.Single(now, RpcTargetUse.Temp));
                }
            }

            if (now == local)
            {
                // From a machine that is gone no state comes: on at once from the pose shown.
                _lastGone = IsServer && was != NetworkManager.ServerClientId
                    && !NetworkManager.ConnectedClients.ContainsKey(was);
                _taking = current.Count;
                _takingSince = _lastGone ? float.NegativeInfinity : Time.unscaledTime;
            }
            else
            {
                _taking = -1;
            }

            if (IsOwner && now != local && _remote == null && !handedUp)
            {
                // The owner shows its own body from now: the stream starts anew.
                _remoteRagdoll = new RemoteRagdoll(_ragdoll, _ragdollFrame);
            }

            TakeRole();
            TakeOverWhenHanded();
        }

        private RagdollState CaptureState(ushort lend)
        {
            var state = new RagdollState { Lend = lend, Down = _ragdoll.IsDown, GetUp = _returning };
            Ragdoll ragdoll = _ragdoll.Ragdoll;
            if (ragdoll != null && ragdoll.ShowsBody && ragdoll.Bones.Count == RagdollSnapshot.BoneCount)
            {
                int n = ragdoll.Bones.Count;
                state.HasBody = true;
                state.Positions = new Vector3[n];
                state.Rotations = new Quaternion[n];
                state.Velocities = new Vector3[n];
                state.Spins = new Vector3[n];
                ragdoll.CaptureState(state.Positions, state.Rotations, state.Velocities, state.Spins);
                Vector3 framePosition = default;
                Quaternion frameRotation = Quaternion.identity;
                double frameTime = 0.0;
                uint frame = _ragdollFrame != null ? _ragdollFrame.FrameOf(out framePosition, out frameRotation, out frameTime) : 0;
                if (frame != 0)
                {
                    state.Frame = frame;
                    state.Time = frameTime;
                    Quaternion back = Quaternion.Inverse(frameRotation);
                    for (int i = 0; i < n; i++)
                    {
                        state.Positions[i] = back * (state.Positions[i] - framePosition);
                        state.Rotations[i] = back * state.Rotations[i];
                    }
                }
            }

            if (_ragdollState != null)
            {
                var writer = new CharacterNetPartWriter();
                _ragdollState.WriteState(ref writer);
                if (writer.Overflowed || writer.Length > RagdollState.MaxGameBytes)
                {
                    Log.Warn($"{name}: the game's ragdoll state ({writer.Length} B) is over {RagdollState.MaxGameBytes} B, left out", this);
                }
                else
                {
                    state.Game = writer.Bytes;
                }
            }

            return state;
        }

        // The machine given the ragdoll goes on once its state is in, or from the pose it shows after StateWait.
        private void TakeOverWhenHanded()
        {
            if (_taking < 0 || _ragdoll == null)
            {
                return;
            }

            bool handed = _handed.HasValue && _handed.Value.Lend == (ushort)_taking;
            bool waited = Time.unscaledTime - _takingSince >= StateWait;
            if (!handed && !waited || handed && !waited && _ragdollState != null && !_ragdollState.CanTake(_handed.Value))
            {
                return;
            }

            _taking = -1;
            _ragdoll.GetsUpElsewhere = !IsOwner;
            if (!handed)
            {
                if (_lastGone)
                {
                    Log.Info($"{name}: its last simulator is gone, going on from the pose shown");
                }
                else
                {
                    Log.Warn($"{name}: no ragdoll state handed within {StateWait:F2} s, going on from the pose shown", this);
                }

                _lastGone = false;
                if (_ragdoll.IsDown)
                {
                    _ragdoll.TakeOver(null, null, null, null, false);
                }
                else
                {
                    // A live body shown from the stream goes on from that pose, if the game takes it.
                    bool shown = _ragdoll.ShowsStreamed && _ragdollState != null;
                    RagdollState pose = shown ? ShownState() : default;
                    EndShown();
                    if (shown)
                    {
                        _ragdollState.TakeUpright(pose);
                    }
                }

                return;
            }

            RagdollState state = _handed.Value;
            _handed = null;
            if (_ragdollState != null && state.Game.Length > 0)
            {
                _ragdollState.ReadState(new CharacterNetPartReader(state.Game, 0, state.Game.Length));
            }

            if (!state.HasBody || !state.Down && !_ragdoll.IsDown)
            {
                EndShown();
                if (state.HasBody && _ragdollState != null)
                {
                    _ragdollState.TakeUpright(state);
                }

                return;
            }

            if (state.GetUp && _ragdoll.IsDown && _ragdoll.Ragdoll.Mode == RagdollMode.Kinematic)
            {
                // At rest: it gets up from where it is shown here, so this machine sees no jump; the others blend from
                // what they show into the get-up anyway.
                Log.Info($"{name}: takes the ragdoll over at rest, gets up from the pose shown");
                _ragdoll.TakeOver(null, null, null, null, true);
                return;
            }

            int n = state.Positions.Length;
            ICharacterSpace space = null;
            if (state.Frame != 0 && (_ragdollFrame == null || !_ragdollFrame.Resolve(state.Frame, out space, out _)))
            {
                Log.Warn($"{name}: the ragdoll state's frame {state.Frame} is not here, going on from the pose shown", this);
                _ragdoll.TakeOver(null, null, null, null, state.GetUp);
                return;
            }

            for (int i = 0; i < n && i < _statePositions.Length; i++)
            {
                _statePositions[i] = space != null ? space.ToWorld(state.Positions[i]) : state.Positions[i];
                _stateRotations[i] = space != null ? space.ToWorld(state.Rotations[i]) : state.Rotations[i];
                _stateVelocities[i] = state.Velocities[i];
                _stateSpins[i] = state.Spins[i];
            }

            Log.Info($"{name}: takes the ragdoll over ({(state.GetUp ? "rested, gets up" : state.Down ? "down" : "live")})");
            _ragdoll.TakeOver(_statePositions, _stateRotations, _stateVelocities, _stateSpins, state.GetUp);
        }

        // The pose shown here as a state in the world: the bones where they are drawn, at rest.
        private RagdollState ShownState()
        {
            IReadOnlyList<RagdollBone> bones = _ragdoll.Ragdoll.Bones;
            int n = bones.Count;
            var state = new RagdollState
            {
                Lend = _lend.Value.Count, HasBody = true, Positions = new Vector3[n], Rotations = new Quaternion[n],
                Velocities = new Vector3[n], Spins = new Vector3[n],
            };
            for (int i = 0; i < n; i++)
            {
                bones[i].transform.GetPositionAndRotation(out state.Positions[i], out state.Rotations[i]);
            }

            return state;
        }

        // Upright, simulated here from now: whatever stream it showed goes, and the game's own components take it on.
        private void EndShown()
        {
            if (_ragdoll.ShowsStreamed)
            {
                _ragdoll.EndStreamed();
            }

            _ragdoll.IsReplica = false;
        }

        // Simulated here for its owner and come to rest: the owner takes it back and gets it up.
        private void OnWouldGetUp()
        {
            if (!IsSpawned || IsOwner || !SimulatesRagdoll || _returning)
            {
                return;
            }

            _returning = true;
            Log.Info($"{name}: lent ragdoll at rest, back to its owner");
            if (IsServer)
            {
                Lend(OwnerClientId);
            }
            else
            {
                ReturnRpc(_lend.Value.Count);
            }
        }

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
                Strides = motion.Strides,
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
            if (_ragdoll != null && SimulatesRagdoll && !_handedUp)
            {
                SendRagdoll(time, down, false);
            }
        }

        // A lent ragdoll is sent from the machine that simulates it, at the tick rate, in the game's frames only.
        private void FixedUpdate()
        {
            if (!IsSpawned || IsOwner || _ragdoll == null || !RagdollLent || !SimulatesRagdoll || _handedUp)
            {
                return;
            }

            double time = Time.fixedTimeAsDouble;
            if (time < _nextRagdollTime)
            {
                return;
            }

            double interval = 1.0 / NetworkManager.NetworkConfig.TickRate;
            _nextRagdollTime = time < _nextRagdollTime + interval ? _nextRagdollTime + interval : time + interval;
            SendRagdoll(time, _ragdoll.IsDown, true);
        }

        private void SendRagdoll(double time, bool down, bool framedOnly)
        {
            if (TryPose(down, framedOnly, time, out RagdollSnapshot pose))
            {
                RagdollRpc(pose);
            }
        }

        // The pose to send now: down, or upright while the game streams a live body (in a frame of its own, never 0).
        private bool TryPose(bool down, bool framedOnly, double time, out RagdollSnapshot pose)
        {
            pose = default;
            bool streams = !down && _ragdollFrame != null && _ragdollFrame.Streams;
            if (!(down || streams && _ragdoll.Ragdoll.ShowsBody)
                || _ragdoll.Ragdoll.Bones.Count != RagdollSnapshot.BoneCount)
            {
                return false;
            }

            pose = new RagdollSnapshot { Time = time };
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
            else if (streams || framedOnly)
            {
                // Frame 0 is on the owner's clock: from anywhere else it could not be placed in time.
                return false;
            }
            else
            {
                _ragdoll.Ragdoll.Capture(out pose.Hips, _captured);
            }

            for (int i = 0; i < _captured.Length; i++)
            {
                pose.SetRotation(i, _captured[i]);
            }

            return true;
        }

        // Unreliable: a lost pose is covered by interpolation, and the next one supersedes it anyway.
        [Rpc(SendTo.NotOwner, Delivery = RpcDelivery.Unreliable)]
        private void SnapshotRpc(CharacterNetSnapshot snapshot) => _remote?.Add(snapshot);

        // From the ragdoll's simulator, relayed by the host; a pose from any other machine (one that just handed the
        // body on) is dropped.
        [Rpc(SendTo.NotMe, Delivery = RpcDelivery.Unreliable)]
        private void RagdollRpc(RagdollSnapshot snapshot, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == RagdollSimulator && !SimulatesRagdoll)
            {
                _remoteRagdoll?.Add(snapshot);
            }
        }

        // Reliable: a hit must not be lost. The ragdoll's simulator simulates it wherever the character is by now.
        [Rpc(SendTo.SpecifiedInParams)]
        private void KnockdownRpc(Vector3 impulse, Vector3 point, ulong striker, RpcParams rpc)
        {
            if (_ragdoll == null)
            {
                return;
            }

            if (SimulatesRagdoll)
            {
                Knock(impulse, point, striker);
            }
            else if (RagdollSimulator != rpc.Receive.SenderClientId)
            {
                // Handed on meanwhile: on to the machine that simulates it now.
                KnockdownRpc(impulse, point, striker, RpcTarget.Single(RagdollSimulator, RpcTargetUse.Temp));
            }
        }

        // The lent body went down on its simulator.
        [Rpc(SendTo.NotMe)]
        private void DownRpc(ushort lend, RpcParams rpc = default)
        {
            if (_ragdoll == null || rpc.Receive.SenderClientId != RagdollSimulator || lend != _lend.Value.Count)
            {
                return;
            }

            if (IsServer)
            {
                LentDown = true;
            }

            if (_ragdoll.IsReplica && !_ragdoll.IsDown)
            {
                _ragdoll.ShowDown();
                if (IsOwner)
                {
                    // Its snapshots say so too, as for a fall of its own.
                    _falls++;
                }
            }
        }

        [Rpc(SendTo.SpecifiedInParams)]
        private void StateRpc(RagdollState state, RpcParams rpc)
        {
            _handed = state;
            TakeOverWhenHanded();
        }

        [Rpc(SendTo.Server)]
        private void HandUpRpc(RagdollState state, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == RagdollSimulator || state.Lend != _lend.Value.Count)
            {
                Park(state, rpc.Receive.SenderClientId);
            }
        }

        [Rpc(SendTo.Server)]
        private void ReturnRpc(ushort lend, RpcParams rpc = default)
        {
            if (rpc.Receive.SenderClientId == RagdollSimulator && lend == _lend.Value.Count)
            {
                Lend(OwnerClientId);
            }
        }
    }
}
