using System.Linq;
using Animancer;
using Rubickanov.Rig;
using Rubickanov.Log;
using Unity.Profiling;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Presents a character through Animancer. Only reads the simulation, never drives it,
    /// so the motor stays authoritative. Root motion only turns the model in place: the model lags
    /// behind the body with its feet planted, and a turn animation catches it up. While the character rides
    /// (<see cref="CharacterMotor.Mount"/>) it holds the seat's pose and nothing else plays. A swimmer plays the
    /// swimming blend, and a lunge out of the water once it rises fast.
    /// </summary>
    // After KinematicCharacterSystem interpolates the body (-100), before CharacterRig works on the model (-50).
    [DefaultExecutionOrder(-75)]
    [DisallowMultipleComponent]
    public sealed class CharacterAnimator : MonoBehaviour
    {
        private static readonly LogChannel Log = LogChannel.Get("Character");

        [Tooltip("Provides movement settings, and the motion unless Source is replaced.")]
        public CharacterMotor Character;
        public AnimancerComponent Animancer;
        public CharacterAnimationsConfig Animations;
        [Tooltip("Optional. Inertialization over the clips (the config's Inertialization switches); without it " +
                 "every switch cross-fades and get-ups blend from a held clip.")]
        public PoseInertia Inertia;

        private enum Phase { Grounded, Turning, Jumping, Falling, Landing, Down, GettingUp, Mounted, Swimming }

        // Inertialization per switch kind, from the config, changed at run time by SetInertia.
        private readonly bool[] _inertial = new bool[5];
        private readonly float[] _inertiaLength = new float[5];

        private const float MinSlopeSpeed = 0.1f;
        // A larger turn of the body between two frames is a teleport or a snap; the model goes with it.
        private const float MaxBodyTurnPerFrame = 90f;
        // Rising faster than this, m/s, a swimmer lunges out of the water; waves never lift one that fast.
        private const float LungeRiseSpeed = 2f;
        // A blend parameter this close to its target holds still: a mixer recalculates its weights whenever its
        // parameter is set, so one left alone costs nothing. A thousandth of the range does not show in the pose.
        private const float Settled = 1e-3f;
        private Phase _phase;
        // Blends downhill (-1), flat (0) and uphill (1) locomotion by the grade along the movement.
        private LinearMixerState _locomotion;
        private MixerState<Vector2> _flat;
        private MixerState<float> _uphill;
        private MixerState<float> _downhill;
        private MixerState<Vector2> _swim;
        private bool _lunging;
        private Vector2 _moveParameter;
        private float _slope;
        private float _airTime;
        // Fastest downward speed since leaving the ground; picks the standing landing.
        private float _fallSpeed;
        // Smoothed planar speed when leaving the ground; the airborne speed includes the jump's forward boost.
        private float _takeoffSpeed;
        private bool _largeFall;
        private bool _movingLand;
        private AnimancerState _land;
        // The hold pose over each arm, on its own masked layer; null without the clip or the mask.
        private AnimancerLayer _holdRight;
        private AnimancerLayer _holdLeft;
        private bool _holdingRight;
        private bool _holdingLeft;
        private Transform _rightShoulder;
        private Transform _leftShoulder;
        private float _landAuthoredSpeed;
        private ICharacterMotionSource _source;
        private Transform _model;
        // Model rotation in the body's space when it is not turned.
        private Quaternion _modelRest;
        private bool _rootMotion;
        private bool _getUpMovesBody;
        private TurnOffset _turn;
        private Vector3 _lastBodyForward;
        // Where the character looks, in degrees from the body's forward, positive to the right.
        private float _aimYaw;
        private bool _facing;
        private const float FacingCatch = 2f;
        // How long the look has been past TurnAngle.
        private float _turnPending;
        // Stretches the clip's root rotation to the turn actually needed.
        private float _turnScale;
        // Stride sync: the shown pose's reach (left foot minus right along the model's forward, m) and how fast it
        // changes; a falling reach is the left foot's stance.
        private static readonly ProfilerMarker StrideMarker = new("CharacterAnimator.StrideSync");
        private Transform _leftFoot;
        private Transform _rightFoot;
        private float _shownReach;
        private float _reachRate;
        private bool _reachKnown;
        private bool _strideStaleWarned;
        // Culling: the renderers whose sight keeps the bones written, and bones as last drawn, read before the Animator
        // runs, to tell whether it wrote them this frame.
        private Renderer[] _renderers;
        private Transform[] _watched;
        private Vector3 _watchedHips;
        private readonly Quaternion[] _watchedTurns = new Quaternion[4];

        /// <summary>
        /// Degrees the model is turned from the body while it turns in place, positive to the right.
        /// Changes when a turn animation or settling turns the model, not when the body turns under it.
        /// </summary>
        public float TurnOffset => _turn.Value;

        /// <summary>
        /// The Animator may skip writing the bones while none of the character's renderers is drawn (shadows count):
        /// time, events and root motion go on. The game turns it off for whatever reads the drawn pose; never while
        /// lying or getting up. Off by default.
        /// </summary>
        public bool MayCull { get; set; }

        /// <summary>
        /// The Animator left the bones as last drawn this frame: whatever poses on top of the clip skips the frame,
        /// or its offsets would pile up.
        /// </summary>
        public bool Culled { get; private set; }

        /// <summary>The ragdoll is the body: nothing here plays until a get-up.</summary>
        public bool IsDown => _phase == Phase.Down;

        /// <summary>A get-up clip is playing; the character stands once it ends.</summary>
        public bool IsGettingUp => _phase == Phase.GettingUp;

        /// <summary>The character rides in or on something and holds the seat's pose.</summary>
        public bool IsMounted => _phase == Phase.Mounted;

        /// <summary>How far the right or left arm is in the hold pose now, 0 (the clip's) to 1.</summary>
        public float ArmHold(bool right)
        {
            AnimancerLayer layer = right ? _holdRight : _holdLeft;
            return layer != null ? layer.Weight : 0f;
        }

        /// <summary>
        /// Puts each arm in the hold pose (<see cref="CharacterAnimationsConfig.HoldArms"/>) in place of the clip's,
        /// or gives it back to the clip, fading over <see cref="CharacterAnimationsConfig.HoldFade"/>. Lying down or
        /// getting up lets go at once.
        /// </summary>
        public void HoldArms(bool right, bool left)
        {
            FadeHold(_holdRight, ref _holdingRight, right);
            FadeHold(_holdLeft, ref _holdingLeft, left);
        }

        private void FadeHold(AnimancerLayer layer, ref bool holding, bool hold)
        {
            if (layer == null || holding == hold)
            {
                return;
            }

            holding = hold;
            layer.StartFade(hold ? 1f : 0f, Animations.HoldFade);
        }

        // Mecanim leaves a clavicle to the base layer unless both arms are masked over: a held arm's goes where the
        // hold pose has it here.
        private void HoldShoulders()
        {
            HoldShoulder(_holdRight, _rightShoulder, Animations.HoldShoulderRight);
            HoldShoulder(_holdLeft, _leftShoulder, Animations.HoldShoulderLeft);
        }

        private static void HoldShoulder(AnimancerLayer layer, Transform shoulder, Quaternion held)
        {
            float weight = layer != null ? layer.Weight : 0f;
            if (weight > 0f && shoulder != null)
            {
                shoulder.localRotation = Quaternion.Slerp(shoulder.localRotation, held, weight);
            }
        }

        private void DropHold()
        {
            foreach (AnimancerLayer layer in new[] { _holdRight, _holdLeft })
            {
                if (layer != null)
                {
                    layer.Weight = 0f;
                }
            }

            _holdingRight = false;
            _holdingLeft = false;
        }

        // Above layer 0, which the clips play on.
        private AnimancerLayer CreateHold(int index, AvatarMask mask, string name)
        {
            if (Animations.HoldArms == null || mask == null)
            {
                return null;
            }

            AnimancerLayer layer = Animancer.Layers[index];
            layer.SetDebugName(name);
            layer.Mask = mask;
            layer.SetLayerWeightOnPlay = false;
            layer.Play(Animations.HoldArms);
            layer.Weight = 0f;
            return layer;
        }

        /// <summary>Motion being presented: Character by default, or e.g. a character replicated over the network.</summary>
        public ICharacterMotionSource Source
        {
            get => _source ?? (Character != null ? Character : null);
            set
            {
                bool wasEnabled = enabled;
                enabled = false;
                _source = value;
                enabled = wasEnabled;
            }
        }

        private void OnEnable()
        {
            if (Character == null || Animancer == null || Animations == null)
            {
                Log.Error("CharacterAnimator requires a character, an AnimancerComponent and an animation set.", this);
                enabled = false;
                return;
            }
            // OnAnimatorMove only receives root motion from an Animator on this GameObject, and then takes it
            // over from Unity. Anywhere else Unity would move the model itself, so leave it off.
            _model = Animancer.Animator.transform;
            _rootMotion = _model.gameObject == gameObject && _model != Body;
            Animancer.Animator.applyRootMotion = _rootMotion;
            if (!_rootMotion && Animations.Turns.Length > 0)
            {
                Log.Warn("CharacterAnimator turns in place only on the Animator's GameObject, below the body.", this);
            }

            _modelRest = Quaternion.Inverse(Body.rotation) * _model.rotation;
            _inertial[(int)InertiaKind.GetUp] = Animations.InertialGetUp;
            _inertial[(int)InertiaKind.Jump] = Animations.InertialJump;
            _inertial[(int)InertiaKind.Land] = Animations.InertialLand;
            _inertial[(int)InertiaKind.Turn] = Animations.InertialTurn;
            _inertial[(int)InertiaKind.WaterEntry] = Animations.InertialWaterEntry;
            _inertiaLength[(int)InertiaKind.GetUp] = Animations.GetUpInertia;
            for (int i = 1; i < _inertiaLength.Length; i++)
            {
                _inertiaLength[i] = Animations.SwitchInertia;
            }

            TrackInertia();
            _lastBodyForward = PlanarForward(Body);
            _turn.Reset();
            _aimYaw = 0f;
            _turnPending = 0f;
            ICharacterMotionSource source = Source;
            source.Jumped += OnJumped;
            source.Landed += OnLanded;
            source.MotionUpdated += OnMotionUpdated;
            Character.MountChanged += OnMountChanged;

            _locomotion ??= CreateLocomotion();
            Animator animator = Animancer.Animator;
            _renderers = animator.GetComponentsInChildren<Renderer>(true)
                .OrderBy(r => r is SkinnedMeshRenderer ? 0 : 1).ToArray();
            _watched = new[]
            {
                animator.GetBoneTransform(HumanBodyBones.Hips), animator.GetBoneTransform(HumanBodyBones.Head),
                animator.GetBoneTransform(HumanBodyBones.LeftLowerLeg), animator.GetBoneTransform(HumanBodyBones.RightLowerArm),
            };
            Culled = false;
            _rightShoulder = Animancer.Animator.GetBoneTransform(HumanBodyBones.RightShoulder);
            _leftShoulder = Animancer.Animator.GetBoneTransform(HumanBodyBones.LeftShoulder);
            _holdRight ??= CreateHold(1, Animations.RightArmMask, "Hold Right");
            _holdLeft ??= CreateHold(2, Animations.LeftArmMask, "Hold Left");
            CharacterMotion motion = source.Motion;
            _moveParameter = PlanarVelocity(motion);
            _slope = 0f;
            _airTime = 0f;
            _fallSpeed = 0f;
            // Enabled again while lying, it would stand frozen.
            Animancer.Graph.UnpauseGraph();
            if (Character.Mount != null)
            {
                PlayMounted();
            }
            else if (motion.IsGrounded || motion.IsSwimming)
            {
                PlayLocomotion();
            }
            else
            {
                PlayFall();
            }
        }

        private void OnDisable()
        {
            Culled = false;
            if (Animancer != null && Animancer.Animator != null)
            {
                Animancer.Animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }

            if (_model != null && _rootMotion)
            {
                Animancer.Animator.applyRootMotion = false;
                _model.rotation = Body.rotation * _modelRest;
            }

            if (Character != null)
            {
                Character.MountChanged -= OnMountChanged;
            }

            ICharacterMotionSource source = Source;
            if (source == null)
            {
                return;
            }

            source.Jumped -= OnJumped;
            source.Landed -= OnLanded;
            source.MotionUpdated -= OnMotionUpdated;
        }

        private void Update()
        {
            // The motor steps at a fixed rate; smoothing here keeps the blend from stepping with it.
            CharacterMotion motion = Source.Motion;
            if (StrideSyncs)
            {
                TrackReach();
            }

            Vector2 target = PlanarVelocity(motion);
            if ((target - _moveParameter).sqrMagnitude > Settled * Settled)
            {
                _moveParameter = Vector2.Lerp(_moveParameter, target, Smoothing(Animations.ParameterSharpness));
            }

            float slope = SlopeAlongMovement(motion);
            if (Mathf.Abs(slope - _slope) > Settled)
            {
                _slope = Mathf.Lerp(_slope, slope, Smoothing(Animations.SlopeSharpness));
            }

            if (!IsHeld)
            {
                bool swimming = motion.IsSwimming && Animations.Swim.IsValid;
                if (swimming && _phase != Phase.Swimming)
                {
                    PlaySwim(SwitchFade(InertiaKind.WaterEntry, Animations.Swim.FadeDuration));
                }
                else if (!swimming && _phase == Phase.Swimming)
                {
                    // Out on the bottom; a swimmer that left the water some other way falls from here.
                    PlayLocomotion();
                }
            }

            switch (_phase)
            {
                case Phase.Swimming:
                    UpdateSwim(motion);
                    break;
                case Phase.Grounded:
                    UpdateLocomotion();
                    break;
                case Phase.Turning:
                    if (target.magnitude > Animations.TurnMaxSpeed)
                    {
                        PlayLocomotion();
                    }

                    break;
                case Phase.Landing:
                    // Standing landings give way when the character sets off, moving ones when it stops.
                    if ((target.magnitude > Animations.LandCancelSpeed) != _movingLand)
                    {
                        PlayLocomotion();
                    }
                    else if (_landAuthoredSpeed > 0f)
                    {
                        _land.Speed = Mathf.Max(1f, target.magnitude / _landAuthoredSpeed);
                    }

                    break;
            }

            ChooseCulling();
        }

        // Before the Animator runs: may it skip the bones this frame, and how they stand as last drawn.
        private void ChooseCulling()
        {
            bool cull = MayCull && !IsLyingOrRising && _watched[0] != null;
            Animator animator = Animancer.Animator;
            AnimatorCullingMode mode = cull ? AnimatorCullingMode.CullUpdateTransforms : AnimatorCullingMode.AlwaysAnimate;
            if (animator.cullingMode != mode)
            {
                animator.cullingMode = mode;
            }

            if (!cull)
            {
                return;
            }

            _watchedHips = _watched[0].localPosition;
            for (int i = 0; i < _watched.Length; i++)
            {
                _watchedTurns[i] = _watched[i] != null ? _watched[i].localRotation : Quaternion.identity;
            }
        }

        /// <summary>
        /// Writes the pose the clips are at now onto bones left as last drawn (<see cref="Culled"/>), for whatever is
        /// about to take the pose over (a fall). Nothing happens when the bones are written already.
        /// </summary>
        public void Redraw()
        {
            if (!Culled)
            {
                return;
            }

            AnimateAlways();
            Animancer.Evaluate();
            Culled = false;
        }

        // A manual evaluation writes the bones whether the character is seen or not.
        private void AnimateAlways()
        {
            Animator animator = Animancer.Animator;
            if (animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                animator.cullingMode = AnimatorCullingMode.AlwaysAnimate;
            }
        }

        // After the Animator: culled when it may cull, nothing of the character was drawn last frame and the bones are
        // still exactly as drawn. The two together, so a still clip on a seen character never counts as culled.
        private bool CheckCulled()
        {
            if (Animancer.Animator.cullingMode != AnimatorCullingMode.CullUpdateTransforms
                || _watched[0].localPosition != _watchedHips)
            {
                return false;
            }

            for (int i = 0; i < _watched.Length; i++)
            {
                if (_watched[i] != null && _watched[i].localRotation != _watchedTurns[i])
                {
                    return false;
                }
            }

            foreach (Renderer r in _renderers)
            {
                if (r != null && r.isVisible)
                {
                    return false;
                }
            }

            return true;
        }

        private void LateUpdate()
        {
            Culled = CheckCulled();
            if (Inertia != null)
            {
                Inertia.Paused = Culled;
            }

            if (!Culled)
            {
                HoldShoulders();
            }

            if (!_rootMotion)
            {
                return;
            }

            CharacterMotion motion = Source.Motion;
            Transform body = Body;
            Vector3 forward = PlanarForward(body);
            float bodyTurn = Vector3.SignedAngle(_lastBodyForward, forward, body.up);
            _lastBodyForward = forward;
            if (Mathf.Abs(bodyTurn) > MaxBodyTurnPerFrame)
            {
                bodyTurn = 0f;
            }

            // Looking nearly straight up or down keeps the last yaw rather than spinning on noise.
            Vector3 aim = Quaternion.Inverse(body.rotation) * Source.Aim;
            if (aim.x * aim.x + aim.z * aim.z > 1e-4f)
            {
                _aimYaw = Mathf.Atan2(aim.x, aim.z) * Mathf.Rad2Deg;
            }
            else if (aim.sqrMagnitude < 1e-8f)
            {
                _aimYaw = 0f;
            }

            bool still = motion.PlanarSpeed <= Animations.TurnMaxSpeed;
            bool canTurn = still && motion.IsGrounded && Animations.Turns.Length > 0 && !motion.FacesLook;
            _turn.Counter(bodyTurn, _aimYaw, Animations.MaxTurnLag);
            // Only moving lines the model up with the body: in third person the body keeps facing its last
            // move while turns in place face the model elsewhere, and a jump on the spot must not undo them.
            // A swimmer has no feet planted and turns with the body.
            if (!still || Animations.Turns.Length == 0 || _phase == Phase.Swimming)
            {
                _turn.Settle(Smoothing(Animations.TurnSettleSharpness));
            }

            // First person: the model faces the look this frame, whatever the simulated body's step has reached; coming
            // into it (a get-up, a seat left) it turns there first.
            _facing = motion.FacesLook && (_facing || Mathf.Abs(_turn.ErrorTo(_aimYaw)) <= FacingCatch);
            if (_facing)
            {
                _turn.Face(_aimYaw);
            }
            else if (motion.FacesLook)
            {
                _turn.Toward(_aimYaw, Smoothing(Animations.TurnSettleSharpness));
            }

            // A rider faces the way the seat does, however the vehicle turns under it.
            if (IsLyingOrRising || IsMounted)
            {
                _turn.Reset();
            }

            if (canTurn && _phase == Phase.Grounded)
            {
                float error = _turn.ErrorTo(_aimYaw);
                _turnPending = Mathf.Abs(error) >= Animations.TurnAngle ? _turnPending + Time.deltaTime : 0f;
                if (_turnPending >= Animations.TurnDelay)
                {
                    PlayTurn(error);
                }
            }
            else
            {
                _turnPending = 0f;
            }

            _model.rotation = Quaternion.AngleAxis(_turn.Value, body.up) * body.rotation * _modelRest;
        }

        // Called in place of Unity applying root motion. A turn's rotation turns the model; a get-up's travel moves
        // the body, so it ends standing over the capsule. Otherwise the motor does the moving.
        private void OnAnimatorMove()
        {
            if (_phase == Phase.GettingUp && _getUpMovesBody)
            {
                Transform body = Body;
                Vector3 travel = Vector3.ProjectOnPlane(Animancer.Animator.deltaPosition, body.up);
                if (travel.sqrMagnitude > 0f)
                {
                    Character.Teleport(body.position + travel, body.rotation);
                }

                return;
            }

            if (_phase != Phase.Turning)
            {
                return;
            }

            Vector3 turned = Animancer.Animator.deltaRotation * Vector3.forward;
            _turn.Turn(Mathf.Atan2(turned.x, turned.z) * Mathf.Rad2Deg * _turnScale);
        }

        private void UpdateLocomotion()
        {
            if (_flat.Parameter.Equals(_moveParameter) && _locomotion.Parameter.Equals(_slope))
            {
                return;
            }

            float speed = _moveParameter.magnitude;
            _flat.Parameter = _moveParameter;
            if (_uphill != null)
            {
                _uphill.Parameter = speed;
            }

            if (_downhill != null)
            {
                _downhill.Parameter = speed;
            }

            _locomotion.Parameter = _slope;
            _locomotion.Speed = LocomotionSpeed();
        }

        // Raised per simulation step or per frame depending on the source; Time.deltaTime is that interval.
        private void OnMotionUpdated(CharacterMotion motion)
        {
            if (motion.IsSwimming)
            {
                _airTime = 0f;
                _fallSpeed = 0f;
                return;
            }

            if (motion.IsGrounded || IsHeld)
            {
                return;
            }

            if (_airTime == 0f)
            {
                _takeoffSpeed = _moveParameter.magnitude;
            }

            _airTime += Time.deltaTime;
            _fallSpeed = Mathf.Max(_fallSpeed, -motion.VerticalSpeed);
            if (_phase is Phase.Grounded or Phase.Turning or Phase.Landing
                && _airTime >= Animations.FallDelay)
            {
                PlayFall();
            }
            else if (_phase == Phase.Falling && !_largeFall && Animations.FallLarge.IsValid && IsLargeFall(motion))
            {
                PlayFall();
            }
        }

        private void OnJumped()
        {
            if (IsHeld || _phase == Phase.Swimming)
            {
                return;
            }

            _airTime = 0f;
            _fallSpeed = 0f;
            // Motion already includes the jump's forward boost, so pick by the smoothed speed from before it.
            // Update has not run since the jump, so it is still the ground speed.
            ClipTransition jump = CharacterAnimationsConfig.Pick(Animations.Jumps, _moveParameter.magnitude).Animation;
            if (jump == null)
            {
                PlayFall();
                return;
            }
            _phase = Phase.Jumping;
            AnimancerState state = Animancer.Play(jump, SwitchFade(InertiaKind.Jump, jump.FadeDuration));
            state.Events(this).OnEnd ??= OnJumpEnd;
        }

        private void OnLanded()
        {
            float fallSpeed = _fallSpeed;
            _airTime = 0f;
            _fallSpeed = 0f;
            if (_phase is Phase.Grounded or Phase.Turning or Phase.Swimming || IsHeld)
            {
                return;
            }

            float speed = PlanarVelocity(Source.Motion).magnitude;
            _movingLand = speed > Animations.LandCancelSpeed;
            ThresholdClip land = _movingLand
                ? CharacterAnimationsConfig.Pick(Animations.MovingLands, _takeoffSpeed)
                : CharacterAnimationsConfig.Pick(Animations.StandingLands, fallSpeed);
            if (fallSpeed < Animations.MinLandFallSpeed || land.Animation == null)
            {
                PlayLocomotion();
                return;
            }
            _phase = Phase.Landing;
            _landAuthoredSpeed = land.AuthoredSpeed;
            _land = Animancer.Play(land.Animation, SwitchFade(InertiaKind.Land, land.Animation.FadeDuration));
            _land.Events(this).OnEnd ??= OnLandEnd;
        }

        // End events also fire on a state that is fading out, so check it is still the current phase.
        private void OnJumpEnd()
        {
            if (_phase == Phase.Jumping)
            {
                PlayFall();
            }
        }

        private void OnLandEnd()
        {
            if (_phase == Phase.Landing)
            {
                PlayLocomotion(Animations.LandBlendOut);
            }
        }

        private void OnGetUpEnd()
        {
            if (_phase == Phase.GettingUp)
            {
                PlayLocomotion(Animations.GetUpBlendOut);
            }
        }

        private void OnTurnEnd()
        {
            if (_phase == Phase.Turning)
            {
                PlayLocomotion();
            }
        }

        private void PlayTurn(float degrees)
        {
            ClipTransition turn = CharacterAnimationsConfig.PickTurn(Animations.Turns, degrees, out float angle);
            _turnPending = 0f;
            if (turn == null)
            {
                return;
            }

            _phase = Phase.Turning;
            // Lands the turn on the look; stretching the clip's rotation a little barely shows in the feet.
            _turnScale = Mathf.Abs(degrees) / angle;
            // From the start even when a turn to the same side is still fading out, or it would end at once.
            AnimancerState state = Animancer.Play(turn, SwitchFade(InertiaKind.Turn, turn.FadeDuration),
                FadeMode.FromStart);
            state.Events(this).OnEnd ??= OnTurnEnd;
        }

        /// <summary>
        /// The ragdoll takes over: nothing plays or turns until <see cref="PlayGetUp"/>. The model lines up with
        /// the body so a get-up starts from it.
        /// </summary>
        public void EnterDown()
        {
            _phase = Phase.Down;
            _airTime = 0f;
            _fallSpeed = 0f;
            AlignModel();
            DropHold();
            // The ragdoll moves only the bones it has; the feet and hands hang on them in the animated pose, which
            // a clip left playing would keep walking or flailing. Held, they keep the pose the body fell in.
            Animancer.Graph.PauseGraph();
        }

        /// <summary>Whether the config has the three flail clips a flying ragdoll reaches for.</summary>
        public bool HasFlails
        {
            get
            {
                ClipTransition[] flails = Animations != null ? Animations.Flails : null;
                if (flails == null || flails.Length < 3)
                {
                    return false;
                }

                for (int i = 0; i < 3; i++)
                {
                    if (flails[i] == null || !flails[i].IsValid)
                    {
                        return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// While down, plays flail clip <paramref name="kind"/> (head first, along the fall, feet first) from its
        /// start, fading in over <paramref name="fade"/> seconds, and evaluates it so its pose is on the bones now.
        /// </summary>
        public void PlayFlail(int kind, float fade)
        {
            if (_phase != Phase.Down || !HasFlails)
            {
                return;
            }

            Animancer.Graph.UnpauseGraph();
            Animancer.Play(Animations.Flails[kind], fade, FadeMode.FromStart);
            AnimateAlways();
            Animancer.Evaluate();
        }

        /// <summary>Holds the pose again, as <see cref="EnterDown"/> does, once the body stops flailing.</summary>
        public void StopFlail()
        {
            if (_phase == Phase.Down)
            {
                Animancer.Graph.PauseGraph();
            }
        }

        /// <summary>
        /// Plays the get-up for a body lying face up or down, its first frame already on the bones so the caller
        /// can read it. Null without that clip: the character goes straight back to locomotion. The clip's travel
        /// over the ground moves the body when <paramref name="moveBody"/>; a replica's body comes from its owner.
        /// </summary>
        public AnimancerState PlayGetUp(bool faceUp, bool moveBody)
        {
            Animancer.Graph.UnpauseGraph();
            AlignModel();
            DropHold();
            _getUpMovesBody = moveBody && _rootMotion;
            ClipTransition getUp = faceUp ? Animations.GetUpFaceUp : Animations.GetUpFaceDown;
            AnimancerState state = null;
            if (getUp != null && getUp.IsValid)
            {
                state = Animancer.Play(getUp, 0f, FadeMode.FromStart);
                state.Events(this).OnEnd ??= OnGetUpEnd;
            }
            else
            {
                // Standing still, held here until EndGetUp.
                UpdateLocomotion();
                Animancer.Play(_locomotion, 0f);
            }

            _phase = Phase.GettingUp;
            AnimateAlways();
            Animancer.Evaluate();
            return state;
        }

        /// <summary>The model's rotation in the body's space when it is not turned.</summary>
        public Quaternion ModelRest => _modelRest;

        /// <summary>Whether the config has nearest-frame get-ups (<see cref="CharacterAnimationsConfig.GetUps"/>).</summary>
        public bool HasGetUps => Animations.GetUps != null && Animations.GetUps.Length > 0 && Animations.GetUpPoses != null;

        /// <summary>
        /// Plays nearest-frame get-up <paramref name="index"/> of the config from <paramref name="time"/> s, its pose
        /// on the bones now. With an inertia, the clip is first evaluated a step later so it knows the new pose's
        /// velocity. The clip's travel moves the body when <paramref name="moveBody"/>. It hands back to locomotion
        /// at <paramref name="standTime"/> s, where the body stands, not at the clip's end.
        /// </summary>
        public AnimancerState PlayGetUpAt(int index, float time, float standTime, bool moveBody, PoseInertia inertia)
        {
            Animancer.Graph.UnpauseGraph();
            AlignModel();
            DropHold();
            // Setting the time is no travel: nothing moves the body while the start is found.
            _getUpMovesBody = false;
            AnimancerState state = Animancer.Play(Animations.GetUps[index], 0f, FadeMode.FromStart);
            AnimancerEvent.Sequence events = state.Events(this);
            events.OnEnd ??= OnGetUpEnd;
            events.NormalizedEndTime = state.Length > 0f ? Mathf.Clamp01(standTime / state.Length) : 1f;
            _phase = Phase.GettingUp;
            if (inertia != null)
            {
                const float step = 1f / 60f;
                state.Time = time + step;
                AnimateAlways();
            Animancer.Evaluate();
                inertia.TargetBefore(-step);
            }

            state.Time = time;
            AnimateAlways();
            Animancer.Evaluate();
            _getUpMovesBody = moveBody && _rootMotion;
            return state;
        }

        /// <summary>Whether a switch of <paramref name="kind"/> is inertialized now, and over how long.</summary>
        public bool InertiaOf(InertiaKind kind, out float seconds)
        {
            seconds = _inertiaLength[(int)kind];
            return _inertial[(int)kind] && Inertia != null && seconds > 0f;
        }

        /// <summary>Turns inertialization of <paramref name="kind"/> on or off for this character (not saved).</summary>
        public void SetInertia(InertiaKind kind, bool on)
        {
            _inertial[(int)kind] = on;
            TrackInertia();
        }

        /// <summary>Sets how long the old pose of <paramref name="kind"/> takes to die away (not saved).</summary>
        public void SetInertiaLength(InertiaKind kind, float seconds) => _inertiaLength[(int)kind] = Mathf.Max(0f, seconds);

        // A switch inertialized from the last frame's pose: no cross-fade, the inertia begins once Animancer evaluated.
        private float SwitchFade(InertiaKind kind, float fade)
        {
            if (!InertiaOf(kind, out float seconds))
            {
                return fade;
            }

            Inertia.Request(seconds);
            return 0f;
        }

        // The inertia reads every frame only while a switch begun from the last frame is on.
        private void TrackInertia()
        {
            if (Inertia != null)
            {
                Inertia.Tracking = _inertial[(int)InertiaKind.Jump] || _inertial[(int)InertiaKind.Land] ||
                                   _inertial[(int)InertiaKind.Turn] || _inertial[(int)InertiaKind.WaterEntry];
            }
        }

        /// <summary>
        /// Starts swimming as a get-up without a clip, from a body floating in deep water: the swimming blend is on
        /// the bones until <see cref="EndGetUp"/>. False without swimming clips: the caller gets up as on land.
        /// </summary>
        public bool PlaySwimUp()
        {
            if (!Animations.Swim.IsValid)
            {
                return false;
            }

            Animancer.Graph.UnpauseGraph();
            AlignModel();
            DropHold();
            _getUpMovesBody = false;
            _lunging = false;
            _swim = (MixerState<Vector2>)Animancer.Play(Animations.Swim, 0f);
            _swim.Parameter = Vector2.zero;
            _phase = Phase.GettingUp;
            AnimateAlways();
            Animancer.Evaluate();
            return true;
        }

        /// <summary>
        /// Ends a get-up early, or one without a clip, back into locomotion; a body taken off the ragdoll while it
        /// still lies goes back too.
        /// </summary>
        public void EndGetUp()
        {
            if (_phase is Phase.GettingUp or Phase.Down)
            {
                Animancer.Graph.UnpauseGraph();
                PlayLocomotion(Animations.GetUpBlendOut);
            }
        }

        /// <summary>Puts the model back on the body, unturned, for a body that was just teleported.</summary>
        public void AlignModel()
        {
            _turn.Reset();
            _turnPending = 0f;
            _lastBodyForward = PlanarForward(Body);
            if (_model != null)
            {
                _model.rotation = Body.rotation * _modelRest;
            }
        }

        private bool IsLyingOrRising => _phase is Phase.Down or Phase.GettingUp;

        // Something other than the motion decides the pose: the ragdoll, a get-up or a seat.
        private bool IsHeld => IsLyingOrRising || IsMounted;

        // Sat down or stood up. A body that lies is the ragdoll's until it gets up, which brings locomotion back.
        private void OnMountChanged()
        {
            if (IsLyingOrRising)
            {
                return;
            }

            if (Character.Mount != null)
            {
                PlayMounted();
            }
            else if (IsMounted)
            {
                PlayLocomotion();
            }
        }

        // The seat's own pose, else the default seated one; without either the character idles in place.
        private void PlayMounted()
        {
            _phase = Phase.Mounted;
            _airTime = 0f;
            _fallSpeed = 0f;
            _turnPending = 0f;
            ClipTransition pose = Character.Mount.Pose;
            if (pose == null || !pose.IsValid)
            {
                pose = Animations.Seated;
            }

            if (pose != null && pose.IsValid)
            {
                Animancer.Play(pose);
                return;
            }

            Log.Warn($"{name} rides without a pose: neither the seat nor the animation set has a seated clip; idling.", this);
            // The vehicle's speed must not walk the legs.
            _moveParameter = Vector2.zero;
            _slope = 0f;
            UpdateLocomotion();
            Animancer.Play(_locomotion, Animations.Locomotion.FadeDuration);
        }

        private void PlayLocomotion() => PlayLocomotion(Animations.Locomotion.FadeDuration);

        private void PlayLocomotion(float fadeDuration)
        {
            if (Source.Motion.IsSwimming && Animations.Swim.IsValid)
            {
                PlaySwim(fadeDuration);
                return;
            }

            _phase = Phase.Grounded;
            // Parameters were left alone while airborne or landing; bring them up to date before the fade shows them.
            UpdateLocomotion();
            // Animancer stops a state whose fade-out ends, and stopping rewinds it: the loops come back from their
            // start unless stride sync picks the stance the shown pose is in.
            bool restarts = _locomotion.Weight <= 0f;
            Animancer.Play(_locomotion, fadeDuration);
            if (restarts && StrideSyncs)
            {
                SyncStride();
            }
        }

        private bool StrideSyncs => Animations.StrideSync && Animations.StrideMarkers != null;

        // Bones still hold the pose shown last frame (after IK) when Update runs.
        private void TrackReach()
        {
            if (_leftFoot == null || _rightFoot == null)
            {
                _leftFoot = Animancer.Animator.GetBoneTransform(HumanBodyBones.LeftFoot);
                _rightFoot = Animancer.Animator.GetBoneTransform(HumanBodyBones.RightFoot);
                _reachKnown = false;
                if (_leftFoot == null || _rightFoot == null)
                {
                    return;
                }
            }

            float reach = (Quaternion.Inverse(_model.rotation) * (_leftFoot.position - _rightFoot.position)).z;
            _reachRate = _reachKnown && Time.deltaTime > 0f ? (reach - _shownReach) / Time.deltaTime : 0f;
            _shownReach = reach;
            _reachKnown = true;
        }

        // On the stride the shown pose is on (Sinew ActorController.EndAction/SetPhase: the loops carry on the
        // stride the clip was on), here judged by the feet: the dominant loop's frame in the bearing foot's stance
        // with the shown reach. The mixer keeps its children in step by normalized time, so one time sets them all.
        private void SyncStride()
        {
            using ProfilerMarker.AutoScope scope = StrideMarker.Auto();
            AnimationClip clip = DominantClip();
            if (clip == null || !_reachKnown)
            {
                return;
            }

            StrideMarkers markers = Animations.StrideMarkers;
            if (!markers.TryGet(clip, out StrideMarkers.Entry entry, out bool stale))
            {
                if (stale && !_strideStaleWarned)
                {
                    _strideStaleWarned = true;
                    Log.Warn($"Stride markers {markers.name} do not match {clip.name}: rebuild them. That loop " +
                             "starts from its first frame meanwhile.", this);
                }

                return;
            }

            float time = markers.StanceTime(entry, _shownReach, _reachRate < 0f);
            if (time >= 0f)
            {
                _locomotion.NormalizedTime = time;
            }
        }

        // The clip that weighs most in the locomotion blend, through the slope mixers.
        private AnimationClip DominantClip()
        {
            _locomotion.RecalculateWeights();
            AnimancerState slope = Heaviest(_locomotion);
            if (slope is MixerState<Vector2> flat)
            {
                flat.RecalculateWeights();
            }
            else if (slope is MixerState<float> linear)
            {
                linear.RecalculateWeights();
            }

            AnimancerState leaf = slope is ManualMixerState mixer ? Heaviest(mixer) : slope;
            return leaf is ClipState clipState ? clipState.Clip : null;
        }

        private static AnimancerState Heaviest(ManualMixerState mixer)
        {
            AnimancerState heaviest = null;
            for (int i = 0; i < mixer.ChildCount; i++)
            {
                AnimancerState child = mixer.GetChild(i);
                if (heaviest == null || child.Weight > heaviest.Weight)
                {
                    heaviest = child;
                }
            }

            return heaviest;
        }

        private void PlaySwim(float fadeDuration)
        {
            _phase = Phase.Swimming;
            _lunging = false;
            _airTime = 0f;
            _fallSpeed = 0f;
            _turnPending = 0f;
            _swim = (MixerState<Vector2>)Animancer.Play(Animations.Swim, fadeDuration);
            UpdateSwim(Source.Motion);
        }

        private void UpdateSwim(CharacterMotion motion)
        {
            if (!_lunging && motion.VerticalSpeed > LungeRiseSpeed && Animations.SwimLunge.IsValid)
            {
                _lunging = true;
                AnimancerState lunge = Animancer.Play(Animations.SwimLunge, Animations.SwimLunge.FadeDuration,
                    FadeMode.FromStart);
                lunge.Events(this).OnEnd ??= OnLungeEnd;
                return;
            }

            if (_lunging)
            {
                return;
            }

            if (_swim.Parameter.Equals(_moveParameter))
            {
                return;
            }

            _swim.Parameter = _moveParameter;
            _swim.Speed = MixerSpeed(_swim);
        }

        private void OnLungeEnd()
        {
            if (_phase == Phase.Swimming && _lunging)
            {
                PlaySwim(Animations.Swim.FadeDuration);
            }
        }

        private void PlayFall()
        {
            _phase = Phase.Falling;
            _largeFall = Animations.FallLarge.IsValid && IsLargeFall(Source.Motion);
            ClipTransition fall = _largeFall ? Animations.FallLarge : Animations.Fall;
            if (fall.IsValid)
            {
                Animancer.Play(fall);
            }
        }

        private bool IsLargeFall(CharacterMotion motion) => -motion.VerticalSpeed >= Animations.LargeFallSpeed;

        private LinearMixerState CreateLocomotion()
        {
            // Speed is scaled on the whole blend so that flat and slope clips stay in step.
            var mixer = new LinearMixerState { ExtrapolateSpeed = false };
            mixer.SetGraph(Animancer.Graph);
            if (Animations.Downhill.IsValid)
            {
                _downhill = (MixerState<float>)mixer.Add(Animations.Downhill, -1f);
            }

            _flat = (MixerState<Vector2>)mixer.Add(Animations.Locomotion, 0f);
            if (Animations.Uphill.IsValid)
            {
                _uphill = (MixerState<float>)mixer.Add(Animations.Uphill, 1f);
            }

            return mixer;
        }

        // Grounded velocity follows the surface, so its rise over run is the grade along the movement.
        private float SlopeAlongMovement(CharacterMotion motion)
        {
            float planar = motion.PlanarSpeed;
            if (!motion.IsGrounded || planar < MinSlopeSpeed)
            {
                return 0f;
            }

            float angle = Mathf.Atan2(motion.VerticalSpeed, planar) * Mathf.Rad2Deg;
            // Slope clips only move forward; fade them out when strafing or backing up.
            float forward = Mathf.Clamp01(motion.LocalVelocity.z / planar);
            return Mathf.Clamp(angle / Animations.SlopeClipAngle, -1f, 1f) * forward;
        }

        // Beyond the outermost thresholds the feet would slide, so play faster to match the actual speed.
        private float LocomotionSpeed() => MixerSpeed(_flat);

        private float MixerSpeed(MixerState<Vector2> mixer)
        {
            mixer.RecalculateWeights();
            float animated = 0f;
            for (int i = 0; i < mixer.ChildCount; i++)
            {
                animated += mixer.GetChild(i).Weight * mixer.GetThreshold(i).magnitude;
            }

            float actual = _moveParameter.magnitude;
            return animated > 0.01f && actual > animated ? actual / animated : 1f;
        }

        private Transform Body => Character.transform;

        private static Vector3 PlanarForward(Transform transform)
        {
            Vector3 forward = Vector3.ProjectOnPlane(transform.forward, transform.up);
            return forward.sqrMagnitude > 1e-6f ? forward.normalized : transform.forward;
        }

        private static float Smoothing(float sharpness) =>
            sharpness > 0f ? 1f - Mathf.Exp(-sharpness * Time.deltaTime) : 1f;

        private static Vector2 PlanarVelocity(CharacterMotion motion) =>
            new(motion.LocalVelocity.x, motion.LocalVelocity.z);
    }
}
