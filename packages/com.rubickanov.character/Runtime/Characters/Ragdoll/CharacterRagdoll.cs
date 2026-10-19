using System;
using System.Collections.Generic;
using Animancer;
using Rubickanov.Rig;
using Rubickanov.Log;
using UnityEngine;

namespace Rubickanov.Characters
{
    public enum BodyState
    {
        /// <summary>The capsule is the body and the skeleton is animated.</summary>
        Upright,

        /// <summary>The ragdoll is the body; the capsule follows its hips.</summary>
        Down,

        /// <summary>The pose blends from where the ragdoll lies into getting up; the capsule stands.</summary>
        GettingUp,
    }

    /// <summary>
    /// Knocks a character down into its ragdoll and gets it back up. Falling hands the body from the capsule to
    /// the ragdoll with the animated pose and its speed; lying, the capsule follows the hips so the camera and
    /// the network stay with the body; getting up stands the capsule where the hips lie, turned so the get-up
    /// clip's first frame lies as the ragdoll does, and blends the pose from the one into the other while the
    /// clip gathers speed, so the body moves off the ground as one motion instead of two laid over each other.
    /// In water the bones float, the chest most (a life vest), and a body at rest in deep water comes back up
    /// swimming instead of standing. On a replicated character nothing is simulated: the pose and the get-up come
    /// from the owner.
    /// </summary>
    // After CharacterRig works on the model (-50), before the camera follows it (0).
    [DefaultExecutionOrder(-40)]
    [DisallowMultipleComponent]
    public sealed partial class CharacterRagdoll : MonoBehaviour
    {
        private static readonly LogChannel Log = LogChannel.Get("Character");

        public CharacterMotor Character;
        public CharacterAnimator Animator;
        [Tooltip("Optional. Switched off while the ragdoll shows the body.")]
        public CharacterRig Rig;
        public RagdollConfig Config;
        [Tooltip("Written by Tools/Characters/Build Ragdoll: the hips first, every bone after its parent.")]
        public RagdollBone[] Bones = { };

        private Ragdoll _ragdoll;
        private float _lyingTime;
        private float _restTime;
        private float _fallTime;
        // Fell in the air from a long fall: the bones fall with the character's gravity until they reach the ground.
        private bool _fallGravity;
        private bool _getUpPending;
        private bool _showPending;
        private CharacterGetUp _shownGetUp;
        private bool _showCaptured;
        private Vector3 _drawnAt;
        private Quaternion _drawnTurn = Quaternion.identity;
        private float _blend;
        private float _blendTime;
        private AnimancerState _getUpClip;
        private Vector3 _followPosition;
        private readonly RaycastHit[] _groundHits = new RaycastHit[8];
        // Seconds since the fall, in the air or not; a get-up with no place to stand waits until _spotAgain.
        private float _downTime;
        private float _spotAgain;
        private bool _hasSpot;
        private Vector3 _spot;
        private readonly List<Collider> _boneColliders = new();
        // The flail clip on the animation and the reflex's count of choices it was played for.
        private FlailClip _flailShown = FlailClip.None;
        private int _flailChoices;
        private int _flailFlights;
        // A replica showing a pose streamed in a frame: kinematic bones placed in LateUpdate, after the root has moved.
        private bool _streamed;
        private bool _streamPending;
        private Vector3 _streamFrame;
        private Quaternion _streamTurn;
        private Vector3 _streamHips;
        private readonly Quaternion[] _streamRotations = new Quaternion[16];
        private bool _replica;
        private bool _getsUpElsewhere;
        private bool _askedGetUp;
        private bool _shell;

        public BodyState State { get; private set; }

        /// <summary>
        /// Whether the pose comes from elsewhere, as on a character replicated over the network or one whose ragdoll
        /// another machine simulates for now. A body down when it becomes a replica holds still where it is, for the
        /// stream to move it; one that stops being a replica while down goes on from <see cref="TakeOver"/>.
        /// </summary>
        public bool IsReplica
        {
            get => _replica;
            set
            {
                if (_replica == value)
                {
                    return;
                }

                _replica = value;
                _getUpPending = false;
                _askedGetUp = false;
                if (value && State == BodyState.Down && _ragdoll != null && _ragdoll.Mode == RagdollMode.Active)
                {
                    _ragdoll.Freeze();
                }
            }
        }

        /// <summary>
        /// Simulated here for another machine that owns the character, e.g. a rider's body on the ride's machine: once it
        /// would get up, it lies on and raises <see cref="WouldGetUp"/> instead, for the owner to take it back.
        /// </summary>
        public bool GetsUpElsewhere
        {
            get => _getsUpElsewhere;
            set
            {
                _getsUpElsewhere = value;
                _askedGetUp = false;
            }
        }

        /// <summary>A body down that <see cref="GetsUpElsewhere"/> came to rest (or lay too long), once until it changes.</summary>
        public event Action WouldGetUp;

        public bool IsDown => State == BodyState.Down;

        /// <summary>
        /// A body down stays down while true: it does not get up when it comes to rest or lies too long, nor asks
        /// <see cref="WouldGetUp"/>; a <see cref="GetUp"/> waits until it is false again (on a body that
        /// <see cref="GetsUpElsewhere"/> it is dropped). False: as before.
        /// </summary>
        public bool StayDown { get; set; }

        /// <summary>
        /// A body down lies limp while true: the flail lets go at once and does not start, the muscles fade to
        /// nothing. False: as before.
        /// </summary>
        public bool Limp { get; set; }

        /// <summary>An upright replica whose bones show a streamed pose (<see cref="ShowStreamed"/>) instead of the animation.</summary>
        public bool ShowsStreamed => _streamed && State == BodyState.Upright;

        /// <summary>
        /// Velocity of whatever carries the body at a point, such as a moving deck it lies on, or null for none: a
        /// body at rest on it rests, and the capsule that follows the hips is carried along rather than left behind.
        /// </summary>
        public Func<Vector3, Vector3> Carrier { get; set; }

        /// <summary>The game's say over the body at the water while it is down, or null to float as configured.</summary>
        public IRagdollWater WaterHook { get; set; }

        /// <summary>The gravity on the body now: the character's while a fall lasts, the physics' after it.</summary>
        public Vector3 BodyGravity => State == BodyState.Down && _fallGravity ? Character.Gravity : Physics.gravity;

        /// <summary>The ragdoll itself, null until the bones are built.</summary>
        public Ragdoll Ragdoll => _ragdoll ??= CreateRagdoll();

        /// <summary>
        /// Whether the body wants a shell: its ragdoll bones kinematic on the animated pose, on
        /// <see cref="RagdollConfig.ShellLayer"/>, for other ragdolls to hit. Shelled while upright with the bones
        /// otherwise off and no stream shown, here or on a replica; a fall goes on from the shell's bones, and the shell
        /// comes back once the body stands again while still wanted. Nothing without a shell layer. Asked for, it starts
        /// at once where it can, so a caller in the physics step can see its bones before the step is simulated.
        /// </summary>
        public bool Shell
        {
            get => _shell;
            set
            {
                _shell = value;
                if (!value && Shelled)
                {
                    _ragdoll.TurnOff();
                }
                else if (value && !Shelled && isActiveAndEnabled)
                {
                    StepShell();
                }
            }
        }

        /// <summary>Whether the body is a shell now (<see cref="Shell"/>).</summary>
        public bool Shelled => _ragdoll != null && _ragdoll.Mode == RagdollMode.Shell;

        /// <summary>A bone of the shell was hit, on whatever machine shows it.</summary>
        public event Action<ShellHit> ShellHit;

        /// <summary>The ragdoll took over. Raised where it is simulated (not on a replica).</summary>
        public event Action FellDown;

        /// <summary>The character starts getting up: lying face up or down, and the nearest-frame get-up's clip and
        /// start (<see cref="CharacterGetUp.Classic"/> for the face up / down one). Raised where it is simulated.</summary>
        public event Action<CharacterGetUp> GettingUp;

        // The bones are left on in the prefab, so they can be seen and edited there; the character starts
        // upright, before the first physics step.
        private void Awake() => Ragdoll?.TurnOff();

        private void OnEnable()
        {
            if (Character == null || Animator == null || Config == null || Bones.Length == 0)
            {
                Log.Error("CharacterRagdoll requires a character, an animator, a config and built bones.", this);
                enabled = false;
                return;
            }

            // Shell bones stand around the legs: the feet would take their own or a neighbour's for ground.
            if (Rig != null && Config.ShellLayer >= 0)
            {
                Rig.IgnoredGroundLayers |= 1 << Config.ShellLayer;
            }

            Everyone.Add(this);
        }

        private void OnDisable()
        {
            Everyone.Remove(this);
            if (Shelled)
            {
                _ragdoll.TurnOff();
            }
            if (State != BodyState.Upright)
            {
                Stand();
            }
        }

        /// <summary>
        /// Knocks the character down, pushed by <paramref name="impulse"/> (N·s) at <paramref name="point"/>.
        /// A body already down is pushed where it lies; one getting up falls again.
        /// </summary>
        public void Knockdown(Vector3 impulse, Vector3 point) => Fall(impulse, point, false);

        // In flight, the ragdoll takes over at the capsule's full speed: a long fall is not slowed in the air.
        private void Fall(Vector3 impulse, Vector3 point, bool inFlight)
        {
            if (!isActiveAndEnabled || IsReplica || Ragdoll == null)
            {
                return;
            }

            if (State == BodyState.Down)
            {
                _restTime = 0f;
                _ragdoll.Push(impulse, point, Config.MaxPushSpeed);
                return;
            }

            // A getting-up body falls from the pose it shows now, which is what SamplePose reads next.
            Vector3 carrier = Character.Motion.Velocity;
            if (State == BodyState.GettingUp)
            {
                _ragdoll.TurnOff();
                _ragdoll.SamplePose(0f);
                carrier = Vector3.zero;
            }

            Character.Suspend();
            if (inFlight)
            {
                _ragdoll.Activate(carrier, carrier.magnitude + Config.MaxActivationSpeed);
            }
            else
            {
                _ragdoll.Activate(carrier);
            }

            _ragdoll.GoLimp();
            _ragdoll.Push(impulse, point, Config.MaxPushSpeed);
            LieDown();
            _fallGravity = inFlight;
            FellDown?.Invoke();
        }

        /// <summary>
        /// Throws a standing character down with its whole body going at <paramref name="velocity"/>, e.g. off a
        /// vehicle: the bones take that velocity rather than the capsule's, and no push is added, so how fast it
        /// flies does not depend on the body's mass. A body down or getting up is left as it is.
        /// </summary>
        public void Throw(Vector3 velocity)
        {
            if (!isActiveAndEnabled || IsReplica || Ragdoll == null || State != BodyState.Upright)
            {
                return;
            }

            Character.Suspend();
            _ragdoll.Activate(velocity, Config.MaxThrowSpeed);
            _ragdoll.GoLimp();
            LieDown();
            FellDown?.Invoke();
        }

        /// <summary>Gets a character that is down up now, whether or not it has come to rest.</summary>
        public void GetUp()
        {
            if (State == BodyState.Down && !IsReplica && GetsUpElsewhere)
            {
                if (!StayDown)
                {
                    WouldGetUp?.Invoke();
                }
            }
            else if (State == BodyState.Down && !IsReplica)
            {
                _getUpPending = true;
            }
        }

        /// <summary>A replicated character went down: its bones follow <see cref="ShowPose"/> from now.</summary>
        public void ShowDown()
        {
            if (!isActiveAndEnabled || !IsReplica || Ragdoll == null || State == BodyState.Down)
            {
                return;
            }

            // Bones already showing a stream, or a shell, go on from where they are.
            if (_ragdoll.Mode != RagdollMode.Off && _ragdoll.Mode != RagdollMode.Shell
                && !(_streamed && _ragdoll.Mode == RagdollMode.Kinematic))
            {
                _ragdoll.TurnOff();
            }

            _ragdoll.BeKinematic();
            LieDown();
        }

        /// <summary>
        /// A replicated character's bones, as <see cref="Characters.Ragdoll.CaptureIn"/> gave them, in a frame that is at
        /// <paramref name="framePosition"/> and <paramref name="frameRotation"/> this frame. Down, the bones lie there;
        /// upright, the bones go kinematic and are shown instead of the animation until <see cref="EndStreamed"/>.
        /// Placed after the character's root has moved this frame.
        /// </summary>
        public void ShowStreamed(Vector3 framePosition, Quaternion frameRotation, Vector3 hipsPosition,
            IReadOnlyList<Quaternion> rotations)
        {
            if (!isActiveAndEnabled || !IsReplica || Ragdoll == null || State == BodyState.GettingUp
                || rotations.Count != _ragdoll.Bones.Count || rotations.Count > _streamRotations.Length)
            {
                return;
            }

            if (State == BodyState.Upright && !_streamed)
            {
                _ragdoll.BeKinematic();
                _streamed = true;
            }

            _streamFrame = framePosition;
            _streamTurn = frameRotation;
            _streamHips = hipsPosition;
            for (int i = 0; i < rotations.Count; i++)
            {
                _streamRotations[i] = rotations[i];
            }

            _streamPending = true;
        }

        /// <summary>An upright replica's stream ended: back to the animated pose.</summary>
        public void EndStreamed()
        {
            _streamPending = false;
            if (!_streamed)
            {
                return;
            }

            _streamed = false;
            if (State == BodyState.Upright && _ragdoll != null && _ragdoll.Mode != RagdollMode.Off)
            {
                _ragdoll.TurnOff();
            }
        }

        /// <summary>A replicated character's bones, as <see cref="Characters.Ragdoll.Capture"/> gave them.</summary>
        public void ShowPose(Vector3 hipsPosition, IReadOnlyList<Quaternion> rotations)
        {
            if (State == BodyState.Down && _ragdoll != null)
            {
                _ragdoll.Place(hipsPosition, rotations);
            }
        }

        /// <summary>
        /// This machine simulates the body from now on, from the state another machine simulated it in
        /// (<see cref="Characters.Ragdoll.CaptureState"/>, world space), or with null from the pose shown here, at rest
        /// on whatever carries it. A body shown upright goes down with it (<see cref="FellDown"/> is raised).
        /// <paramref name="getUp"/> gets it up at once, as a body come to rest does. A body getting up is left as it is.
        /// </summary>
        public void TakeOver(IReadOnlyList<Vector3> positions, IReadOnlyList<Quaternion> rotations,
            IReadOnlyList<Vector3> velocities, IReadOnlyList<Vector3> spins, bool getUp)
        {
            if (!isActiveAndEnabled || Ragdoll == null || State == BodyState.GettingUp)
            {
                IsReplica = false;
                return;
            }

            IsReplica = false;
            bool fell = State == BodyState.Upright;
            if (fell)
            {
                _streamed = false;
                _streamPending = false;
                Character.Suspend();
            }

            if (positions == null || positions.Count != _ragdoll.Bones.Count)
            {
                if (_ragdoll.Mode == RagdollMode.Off || _ragdoll.Mode == RagdollMode.Shell)
                {
                    _ragdoll.Activate(Vector3.zero);
                    _ragdoll.GoLimp();
                }
                else
                {
                    int n = _ragdoll.Bones.Count;
                    var at = new Vector3[n];
                    var turns = new Quaternion[n];
                    var moves = new Vector3[n];
                    var spinsNow = new Vector3[n];
                    _ragdoll.CaptureState(at, turns, moves, spinsNow);
                    for (int i = 0; i < n; i++)
                    {
                        moves[i] = Carrier?.Invoke(at[i]) ?? Vector3.zero;
                        spinsNow[i] = Vector3.zero;
                    }

                    _ragdoll.Seed(at, turns, moves, spinsNow);
                }
            }
            else
            {
                _ragdoll.Seed(positions, rotations, velocities, spins);
            }

            if (fell)
            {
                LieDown();
                FellDown?.Invoke();
            }

            _getUpPending = getUp;
        }

        /// <summary>A replicated character gets up; its capsule is already where the owner stood it.</summary>
        public void ShowGetUp(CharacterGetUp getUp)
        {
            if (State != BodyState.Down || !IsReplica)
            {
                return;
            }

            if (getUp.Clip != CharacterGetUp.Classic && NearestReady() && getUp.Clip < Animator.Animations.GetUps.Length)
            {
                Log.Info($"Shows get-up {Animator.Animations.GetUpPoses.Entries[getUp.Clip].Name} from {getUp.Time:F2} s", this);
                // Switched in LateUpdate, as the owner does: played now, the animation update would move the clip on
                // by this frame before the inertia shows the ragdoll's pose. The pose it starts from is the one shown
                // until now, taken before the stream moves the lying body onto the new capsule.
                _shownGetUp = getUp;
                _showPending = true;
                _showCaptured = CaptureShown();
                if (_showCaptured)
                {
                    // The stream already set the capsule where the owner stood it, carrying the lying bones along: the
                    // pose shown is the one at the capsule as drawn last frame.
                    Transform body = Character.transform;
                    Quaternion back = _drawnTurn * Quaternion.Inverse(body.rotation);
                    _shownHips = _drawnAt + back * (_shownHips - body.position);
                    _shown[0] = back * _shown[0];
                }
                return;
            }

            BeginGetUp(getUp.FaceUp, false);
        }

        private void FixedUpdate()
        {
            float dt = Time.fixedDeltaTime;
            StepShell();
            if (IsReplica)
            {
                // The owner of a body simulated elsewhere: its capsule follows the hips shown, for its camera and the network.
                if (State == BodyState.Down && Character.isActiveAndEnabled && Character.IsSuspended
                    && _ragdoll.Mode != RagdollMode.Off)
                {
                    Vector3 shown = _ragdoll.Hips.transform.position;
                    TryGroundUnder(shown, out Vector3 under, out _);
                    bool afloat = InWater(shown, out float top, out _) && IsDeep(shown);
                    FollowHips(shown, under, afloat, top, dt);
                }

                return;
            }

            if (State == BodyState.Upright)
            {
                WatchFall(dt);
                return;
            }

            if (State != BodyState.Down)
            {
                return;
            }

            _downTime += dt;
            _ragdoll.Limp = Limp;
            _ragdoll.Drive(dt);
            RagdollWaterStep step = WaterHook?.Step(this, dt) ?? RagdollWaterStep.Float;
            _ragdoll.SetWet(step == RagdollWaterStep.Float ? Float() : step == RagdollWaterStep.Pushed);
            ShowFlail();
            // Only a body lying on the ground or in water rests: hips slow at the top of a flight or hung on
            // something do not. Water of any depth counts: a body floating in water too shallow to swim in has its
            // hips higher over the bottom than LyingHeight, yet it lies there.
            Vector3 hips = _ragdoll.Hips.transform.position;
            bool onGround = TryGroundUnder(hips, out Vector3 ground, out float height)
                            && height <= Config.LyingHeight;
            bool inWater = InWater(hips, out float surface, out Vector3 water);
            bool floating = inWater && IsDeep(hips) && step == RagdollWaterStep.Float;
            bool lying = onGround || inWater;
            // The time down counts lying or floating only, so a long flight does not use it up before the landing.
            if (lying)
            {
                _lyingTime += dt;
            }

            // The physics' gravity is weaker than the character's: without the rest the body would float down.
            if (_fallGravity && (lying || step != RagdollWaterStep.Float))
            {
                _fallGravity = false;
            }
            else if (_fallGravity)
            {
                _ragdoll.Accelerate(Character.Gravity - Physics.gravity);
            }

            Vector3 carried = Carrier?.Invoke(hips) ?? Vector3.zero;
            bool slow = inWater
                ? (_ragdoll.Hips.Body.linearVelocity - water).magnitude < Config.WaterRestSpeed
                : (_ragdoll.Hips.Body.linearVelocity - carried).magnitude < Config.RestSpeed;
            _restTime = lying && slow ? _restTime + dt : 0f;
            bool rested = _lyingTime >= Config.MinDownTime && _restTime >= Config.RestTime;
            bool tooLong = Config.MaxDownTime > 0f && _lyingTime >= Config.MaxDownTime && lying;
            if (StayDown)
            {
                // Lies on: nothing asked, so it gets up (or asks) once let go.
            }
            else if ((rested || tooLong) && GetsUpElsewhere)
            {
                // Its owner gets it up: asked once, lying on meanwhile.
                if (!_askedGetUp)
                {
                    _askedGetUp = true;
                    WouldGetUp?.Invoke();
                }
            }
            else if (rested || tooLong)
            {
                _getUpPending = true;
            }

            FollowHips(hips, ground, floating, surface, dt);
        }

        // A wanted shell starts on an upright body whose bones are off and follows the pose each step; it ends when
        // unwanted or once a stream shows.
        private void StepShell()
        {
            if (_ragdoll == null || State != BodyState.Upright)
            {
                return;
            }

            bool wanted = _shell && !_streamed && Config.ShellLayer >= 0;
            if (!wanted)
            {
                if (_ragdoll.Mode == RagdollMode.Shell)
                {
                    _ragdoll.TurnOff();
                }

                return;
            }

            if (_ragdoll.Mode == RagdollMode.Off)
            {
                _ragdoll.Shell(Config.ShellLayer, RaiseShellHit);
            }

            if (_ragdoll.Mode != RagdollMode.Shell)
            {
                return;
            }

            _ragdoll.FollowPose();
        }

        private void RaiseShellHit(ShellHit hit) => ShellHit?.Invoke(hit);

        // The capsule stands where the hips lie, so the camera and the network stay with the body; in deep water it
        // floats where a swimmer would. Thrown off a tilted seat, it turns upright as a standing character would, not
        // at once, so the camera does not jerk.
        private void FollowHips(Vector3 hips, Vector3 ground, bool floating, float surface, float dt)
        {
            if (floating)
            {
                ground = new Vector3(hips.x, surface - Character.Settings.FloatDepth, hips.z);
            }

            _followPosition += (Carrier?.Invoke(_followPosition) ?? Vector3.zero) * dt;
            _followPosition = Vector3.Lerp(_followPosition, ground, 1f - Mathf.Exp(-dt / Config.FollowTime));
            Quaternion rotation = Character.Motor.TransientRotation;
            rotation = Quaternion.Slerp(rotation, Upright(rotation),
                1f - Mathf.Exp(-Character.Settings.UpOrientationSharpness * dt));
            Character.Follow(_followPosition, rotation);
        }

        private void LateUpdate()
        {
            if (Ragdoll == null)
            {
                return;
            }

            if (_streamPending)
            {
                _streamPending = false;
                if (State == BodyState.Down || _streamed)
                {
                    _ragdoll.Place(_streamFrame, _streamTurn, _streamHips, _streamRotations);
                }
            }

            switch (State)
            {
                case BodyState.Upright:
                    // What the ragdoll takes over from: the pose as drawn, and how fast it moves.
                    if (!IsReplica || _ragdoll.Mode == RagdollMode.Shell)
                    {
                        _ragdoll.SamplePose(Time.deltaTime);
                    }
                    else if (_streamed)
                    {
                        _ragdoll.Map(1f);
                    }

                    break;
                case BodyState.Down:
                    if (_showPending)
                    {
                        _showPending = false;
                        BeginNearest(_shownGetUp.Clip, _shownGetUp.Time, false, _showCaptured);
                        break;
                    }

                    if (_getUpPending && !StayDown)
                    {
                        _getUpPending = false;
                        if (UsesNearest())
                        {
                            if (ChooseNearest(out int clip, out float time))
                            {
                                _ragdoll.Freeze();
                                BeginNearest(clip, time, true);
                            }
                            else
                            {
                                _ragdoll.Map(1f);
                            }

                            break;
                        }

                        if (!PlaceToStand())
                        {
                            _ragdoll.Map(1f);
                            break;
                        }

                        _ragdoll.Freeze();
                        BeginGetUp(LiesFaceUp(), true);
                        break;
                    }

                    // The muscles reach for the flail clip as the animation played it this frame, before it is hidden.
                    if (_ragdoll.ReadsFlailPose)
                    {
                        _ragdoll.SamplePose(Time.deltaTime);
                    }

                    _ragdoll.Map(1f);
                    Character.transform.GetPositionAndRotation(out _drawnAt, out _drawnTurn);
                    break;
                case BodyState.GettingUp:
                    StepGetUp();
                    break;
            }
        }

        // A character falling longer than the config's FallTime, counted from the top of its flight, goes down in the
        // air. Standing, seated or simulated by something else, it is not falling.
        private void WatchFall(float dt)
        {
            Vector3 gravity = Character.Gravity;
            bool falling = Config.FallTime > 0f && Character.isActiveAndEnabled && !Character.IsSuspended &&
                           Character.Mount == null && !Character.Motion.IsGrounded &&
                           Vector3.Dot(Character.Motion.Velocity, gravity) > 0f;
            _fallTime = falling ? _fallTime + dt : 0f;
            // falling first: with FallTime 0 ("never") a zero _fallTime would otherwise pass the check every step.
            if (falling && _fallTime >= Config.FallTime)
            {
                Fall(Vector3.zero, _ragdoll.Hips.transform.position, true);
            }
        }

        private void LieDown()
        {
            State = BodyState.Down;
            _streamed = false;
            _lyingTime = _restTime = _fallTime = _downTime = _spotAgain = 0f;
            _hasSpot = false;
            _searchAgain = 0f;
            _risingByClip = false;
            _flailShown = FlailClip.None;
            _fallGravity = false;
            _getUpPending = false;
            _showPending = false;
            _followPosition = Character.transform.position;
            if (IsReplica || !Character.isActiveAndEnabled)
            {
                // The owner's capsule collides with nothing while down; one this machine does not move must not block others.
                Character.Motor.Capsule.enabled = false;
            }

            Animator.EnterDown();
            if (Rig != null)
            {
                Rig.enabled = false;
            }

            _ragdoll.Map(1f);
        }

        // The pose stops being the ragdoll's, the capsule stands where the hips lie, and the get-up starts
        // from its first frame. The owner places the capsule; a replica's comes from the owner's snapshots.
        private void BeginGetUp(bool faceUp, bool placeBody)
        {
            Transform hips = _ragdoll.Hips.transform;
            Transform head = _ragdoll.Head.transform;
            if (InDeepWater(hips.position, out float surface, out _) && Animator.PlaySwimUp())
            {
                BeginSwimUp(hips.position, head.position, surface, placeBody);
                return;
            }

            _getUpClip = Animator.PlayGetUp(faceUp, placeBody);
            if (_getUpClip != null)
            {
                _getUpClip.Speed = 0f;
            }

            if (placeBody)
            {
                PlaceBody(faceUp, hips.position, head.position);
            }

            State = BodyState.GettingUp;
            _blend = 1f;
            _blendTime = _getUpClip != null ? Config.GetUpBlendTime : Config.StandInBlendTime;
            _ragdoll.Map(1f);
            // The rig holds hands and feet on the ground from here; the ragdoll's pose hides it until it fades.
            if (Rig != null)
            {
                Rig.enabled = true;
            }

            if (placeBody)
            {
                GettingUp?.Invoke(new CharacterGetUp(faceUp));
            }
        }

        // Swimming takes over where the body floats, facing where its head points; the pose blends from the
        // ragdoll into the swimming blend, which StepGetUp ends like a get-up without a clip.
        private void BeginSwimUp(Vector3 hips, Vector3 head, float surface, bool placeBody)
        {
            _getUpClip = null;
            if (placeBody)
            {
                Transform body = Character.transform;
                Vector3 heading = Vector3.ProjectOnPlane(head - hips, body.up);
                Quaternion rotation = heading.sqrMagnitude > 1e-6f
                    ? Quaternion.LookRotation(heading, body.up)
                    : body.rotation;
                Character.Teleport(new Vector3(hips.x, surface - Character.Settings.FloatDepth, hips.z), rotation);
                Animator.AlignModel();
                Character.StartSwimming();
            }

            State = BodyState.GettingUp;
            _blend = 1f;
            _blendTime = Config.SwimUpBlendTime;
            _ragdoll.Map(1f);
            if (Rig != null)
            {
                Rig.enabled = true;
            }

            if (placeBody)
            {
                GettingUp?.Invoke(new CharacterGetUp(LiesFaceUp()));
            }
        }

        // The flail clip the reflex follows goes on the animation: a new flight's from its start at once, a new
        // choice fading in; once the reflex lets go the pose is held again.
        private void ShowFlail()
        {
            FlailReflex flail = _ragdoll.Flail;
            if (flail == null || (flail.Clip == _flailShown && flail.Choices == _flailChoices))
            {
                return;
            }

            bool start = flail.Flights != _flailFlights;
            _flailShown = flail.Clip;
            _flailChoices = flail.Choices;
            _flailFlights = flail.Flights;
            if (flail.Clip == FlailClip.None)
            {
                Animator.StopFlail();
                return;
            }

            Animator.PlayFlail((int)flail.Clip, start ? 0f : Config.FlailBlendTime);
            if (start)
            {
                _ragdoll.SamplePose(0f);
            }
        }

        // Lifts each bone by how deep it is under water and drags it through the water; the vest on the chest
        // lifts in front of it, so a body face down rolls over. True when any bone is wet.
        private bool Float()
        {
            ICharacterWater water = Character.Water;
            if (water == null)
            {
                return false;
            }

            bool anyWet = false;

            Vector3 lift = -Physics.gravity;
            IReadOnlyList<RagdollBone> bones = _ragdoll.Bones;
            for (int i = 0; i < bones.Count; i++)
            {
                Rigidbody body = bones[i].Body;
                Vector3 center = body.worldCenterOfMass;
                if (!water.TrySample(center, out float surface, out Vector3 flow))
                {
                    continue;
                }

                float wet = Mathf.Clamp01((surface - center.y) / Config.ImmersionDepth + 0.5f);
                if (wet <= 0f)
                {
                    continue;
                }

                anyWet = true;

                if (i == Ragdoll.ChestIndex)
                {
                    Vector3 front = center + _ragdoll.Facing(i) * Vector3.forward * Config.VestForward;
                    body.AddForceAtPosition(lift * (Config.VestBuoyancy * wet * body.mass), front);
                }
                else
                {
                    body.AddForce(lift * (Config.Buoyancy * wet), ForceMode.Acceleration);
                }

                body.AddForce((flow - body.linearVelocity) * (Config.WaterDrag * wet), ForceMode.Acceleration);
                body.AddTorque(-body.angularVelocity * (Config.WaterAngularDrag * wet), ForceMode.Acceleration);
            }

            return anyWet;
        }

        // Hips at or under the surface of water too deep to stand in: a body there swims rather than stands.
        private bool InDeepWater(Vector3 hips, out float surface, out Vector3 flow) =>
            InWater(hips, out surface, out flow) && IsDeep(hips);

        // Hips at or under the surface of water of any depth.
        private bool InWater(Vector3 hips, out float surface, out Vector3 flow)
        {
            flow = Vector3.zero;
            surface = 0f;
            ICharacterWater water = Character.Water;
            return water != null && water.TrySample(hips, out surface, out flow) && hips.y <= surface + 0.1f;
        }

        // The water at rest over the bottom under the hips is too deep to stand in. Only with a Water.
        private bool IsDeep(Vector3 hips)
        {
            float bottom = TryGroundUnder(hips, out Vector3 ground, out _) ? ground.y : float.NegativeInfinity;
            return Character.Water.Level(hips) - bottom > Character.Settings.SwimEnterDepth;
        }

        // Turned so the clip's first frame lies along the ragdoll, hips over hips. The clip's pose is on the
        // bones now, read in the model's space before the body moves.
        private void PlaceBody(bool faceUp, Vector3 hips, Vector3 head)
        {
            Transform body = Character.transform;
            Vector3 up = Up;
            Transform model = Animator.Animancer.Animator.transform;
            Transform clipHips = _ragdoll.Hips.Visible;
            Transform clipHead = _ragdoll.Head.Visible;
            Vector3 hipsToHead = head - hips;

            // Without a clip the character stands facing where its head lay, or its feet when face up.
            Vector3 heading = Vector3.ProjectOnPlane(faceUp ? -hipsToHead : hipsToHead, up);
            Quaternion bodyRotation = heading.sqrMagnitude > 1e-6f
                ? Quaternion.LookRotation(heading, up)
                : body.rotation;
            Vector3 ground = GroundUnder(hips);
            Vector3 bodyPosition = ground;
            if (_getUpClip != null)
            {
                Quaternion toModel = Quaternion.Inverse(model.rotation);
                Vector3 clipHipsToHead = toModel * (clipHead.position - clipHips.position);
                Vector3 clipHipsOffset = toModel * (clipHips.position - model.position);
                Quaternion modelInBody = Quaternion.Inverse(body.rotation) * model.rotation;
                Quaternion modelRotation = RagdollMath.GetUpRotation(clipHipsToHead, hipsToHead, up,
                    bodyRotation * modelInBody);
                bodyRotation = modelRotation * Quaternion.Inverse(modelInBody);
                Vector3 modelPosition = ground - Vector3.ProjectOnPlane(modelRotation * clipHipsOffset, up);
                bodyPosition = modelPosition - bodyRotation * body.InverseTransformVector(model.position - body.position);
            }

            // Where the clip puts it, or the nearest place round it that stands; else where the hips' place stood.
            if (_hasSpot)
            {
                bodyPosition = FindSpot(bodyPosition, bodyRotation, head, out Vector3 spot) ? spot : _spot;
                _hasSpot = false;
            }

            Character.Teleport(bodyPosition, bodyRotation);
            Animator.AlignModel();
        }

        // Whether a body about to get up has a place to stand round the ground under its hips: with none it stays
        // down and looks again every SpotRetry. Down SpotGiveUp from the fall it waits no longer: it gets up at the
        // place if there is one, else where it lies. A body in deep water swims up wherever it floats.
        private bool PlaceToStand()
        {
            _hasSpot = false;
            Vector3 hips = _ragdoll.Hips.transform.position;
            if (!Config.SafeSpot || InDeepWater(hips, out _, out _))
            {
                return true;
            }

            bool gaveUp = _downTime >= Config.SpotGiveUp;
            if (!gaveUp && _downTime < _spotAgain)
            {
                return false;
            }

            _hasSpot = FindSpot(GroundUnder(hips), Upright(Character.Motor.TransientRotation),
                _ragdoll.Head.transform.position, out _spot);
            if (!_hasSpot)
            {
                _spotAgain = _downTime + Config.SpotRetry;
            }

            return _hasSpot || gaveUp;
        }

        // GetUpSpot with the body's own bones out of the motor's sight (frozen, they are kinematic, which the motor
        // would stand on) and the suspended motor's ground solving on for the search.
        private bool FindSpot(Vector3 feet, Quaternion heading, Vector3 head, out Vector3 spot)
        {
            List<Collider> ignored = Character.IgnoredColliders;
            int count = ignored.Count;
            _boneColliders.Clear();
            _ragdoll.GetColliders(_boneColliders);
            ignored.AddRange(_boneColliders);
            Character.Motor.SetGroundSolvingActivation(true);
            try
            {
                var settings = new GetUpSpotSettings
                {
                    Step = Config.SpotStep,
                    Rings = Config.SpotRings,
                    FreeTries = Config.SpotFreeTries,
                    PieceSlop = Config.PieceSlop,
                    HeadRadius = _ragdoll.HeadRadius,
                };
                return GetUpSpot.Find(Character.Motor, feet, heading, head, settings, out spot);
            }
            finally
            {
                ignored.RemoveRange(count, ignored.Count - count);
                Character.Motor.SetGroundSolvingActivation(!Character.IsSuspended);
            }
        }

        private void StepGetUp()
        {
            if (_blend > 0f)
            {
                _blend = _blendTime > 0f ? Mathf.Max(0f, _blend - Time.deltaTime / _blendTime) : 0f;
                // Smoothed at both ends so the pose neither jerks off the ragdoll nor snaps onto the clip.
                float weight = _blend * _blend * (3f - 2f * _blend);
                if (_getUpClip != null)
                {
                    _getUpClip.Speed = 1f - weight;
                }

                if (weight > 0f)
                {
                    _ragdoll.Map(weight);
                    return;
                }

                _ragdoll.TurnOff();
                if (_getUpClip == null)
                {
                    Animator.EndGetUp();
                }
            }

            if (!Animator.IsGettingUp)
            {
                Stand();
            }
        }

        private void Stand()
        {
            _streamed = false;
            _streamPending = false;
            if (_ragdoll != null && _ragdoll.Mode != RagdollMode.Off)
            {
                _ragdoll.TurnOff();
            }

            State = BodyState.Upright;
            _risingByClip = false;
            _getUpPending = false;
            _showPending = false;
            _getUpClip = null;
            Animator.EndGetUp();
            Character.Resume();
            if (IsReplica || !Character.isActiveAndEnabled)
            {
                Character.Motor.Capsule.enabled = true;
            }

            if (Rig != null)
            {
                Rig.enabled = true;
            }
        }

        // Against gravity, not along the capsule: a body thrown off a seat lies down with the seat's tilt.
        private Vector3 Up => Character.Gravity.sqrMagnitude > 0f ? -Character.Gravity.normalized : Vector3.up;

        private Quaternion Upright(Quaternion rotation)
        {
            Vector3 up = Up;
            Vector3 forward = Vector3.ProjectOnPlane(rotation * Vector3.forward, up);
            if (forward.sqrMagnitude < 1e-6f)
            {
                // Facing straight up or down: its up shows which way its front was.
                forward = Vector3.ProjectOnPlane(rotation * Vector3.up, up);
            }

            return Quaternion.LookRotation(forward, up);
        }

        private bool LiesFaceUp() =>
            RagdollMath.LiesFaceUp(_ragdoll.Facing(0) * Vector3.forward, Up);

        // The ground under a point, or the point itself where nothing is under it.
        private Vector3 GroundUnder(Vector3 point)
        {
            TryGroundUnder(point, out Vector3 ground, out _);
            return ground;
        }

        // The ground under a point and how far below it that is; the point itself where nothing is under it.
        // Looked for from the point down, not from above it: a table or a ledge over the hips is not ground.
        // The rig's body ground, if any, counts below the point within the probe, where it is above what the ray hit.
        private bool TryGroundUnder(Vector3 point, out Vector3 ground, out float height)
        {
            Vector3 down = -Up;
            IFootGround provider = Rig != null ? Rig.BodyGround : null;
            if (provider == null)
            {
                if (Physics.Raycast(point, down, out RaycastHit hit, Config.GroundProbe, Config.GroundLayers,
                        QueryTriggerInteraction.Ignore))
                {
                    ground = hit.point;
                    height = hit.distance;
                    return true;
                }

                ground = point;
                height = float.PositiveInfinity;
                return false;
            }

            ground = point;
            height = float.PositiveInfinity;
            int count = Physics.RaycastNonAlloc(point, down, _groundHits, Config.GroundProbe, Config.GroundLayers,
                QueryTriggerInteraction.Ignore);
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _groundHits[i];
                if (hit.distance < height && !provider.Ignores(hit.collider, point))
                {
                    ground = hit.point;
                    height = hit.distance;
                }
            }

            if (provider.TryGetGround(point, out float top, out _))
            {
                var provided = new Vector3(point.x, top, point.z);
                float along = Vector3.Dot(provided - point, down);
                if (along >= 0f && along <= Config.GroundProbe && along < height)
                {
                    ground = provided;
                    height = along;
                }
            }

            return !float.IsPositiveInfinity(height);
        }

        private Ragdoll CreateRagdoll()
        {
            if (Bones.Length == 0 || Config == null)
            {
                return null;
            }

            var own = new List<Collider>();
            if (Character != null)
            {
                Character.GetComponents(own);
            }

            return new Ragdoll(Config, Character.transform, Bones, own,
                Config.Flail && Animator != null && Animator.HasFlails);
        }
    }
}
