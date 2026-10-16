using System;
using System.Collections.Generic;
using KinematicCharacterController;
using Rubickanov.Log;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>Basic locomotion based on KCC's ExampleCharacterController, without input or presentation.</summary>
    [DisallowMultipleComponent]
    [RequireComponent(typeof(KinematicCharacterMotor))]
    public sealed class CharacterMotor : MonoBehaviour, ICharacterController, ICharacterMotionSource
    {
        private static readonly LogChannel Log = LogChannel.Get("Character");
        private static readonly List<CharacterMotor> Registry = new();

        /// <summary>Every character motor that exists, enabled or not, in the order they woke up.</summary>
        public static IReadOnlyList<CharacterMotor> All => Registry;

        public MovementConfig Settings;
        public List<Collider> IgnoredColliders = new();

        public KinematicCharacterMotor Motor { get; private set; }
        public CharacterMotion Motion { get; private set; }

        public Vector3 Aim => _aim;
        /// <summary>Optional per-character gravity. Does not modify shared settings.</summary>
        public Vector3? GravityOverride { get; set; }
        public Vector3 Gravity => GravityOverride ?? Settings.Gravity;
        /// <summary>Scales the top speed on the ground, for whatever slows the character down. 1 is no change.</summary>
        public float SpeedMultiplier { get; set; } = 1f;
        /// <summary>
        /// The velocity the input asks for along the ground, world space, at the top speed it allows; zero without input
        /// or while suspended. What the character leans into something with: a ball it walks into is pushed toward it.
        /// </summary>
        public Vector3 WantedVelocity { get; private set; }
        /// <summary>
        /// Asked at the moment of a jump for the share of <see cref="MovementConfig.JumpUpSpeed"/> the ground gives (0 or more: above 1 is a jump
        /// stronger than the full one, a climb out of a deep hole): what the character pushes off decides how hard it can push. Null: the full jump.
        /// </summary>
        public Func<float> JumpScale { get; set; }
        public event Action Jumped;
        public event Action Landed;
        public event Action LeftGround;
        public event Action<CharacterMotion> MotionUpdated;

        /// <summary>
        /// Something else is the body, such as a ragdoll: the capsule collides with nothing, ignores input and
        /// only follows <see cref="Follow"/>. Motion keeps updating, so the character still replicates.
        /// </summary>
        public bool IsSuspended => _suspended;

        /// <summary>
        /// What the character rides in or on, such as a seat of a sled, or null. Set on every machine by whatever
        /// seats the character, which also suspends the motor; the body, its animation and its rig follow it.
        /// Raises <see cref="MountChanged"/> when it changes.
        /// </summary>
        public ICharacterMount Mount
        {
            get => _mount;
            set
            {
                if (ReferenceEquals(_mount, value))
                {
                    return;
                }

                _mount = value;
                MountChanged?.Invoke();
            }
        }

        public event Action MountChanged;

        /// <summary>
        /// The input <see cref="InputSource"/> gave this frame, read while suspended too, for whatever holds the body
        /// then, such as a seat a rider jumps off.
        /// </summary>
        public CharacterInput SourceInput { get; private set; }

        /// <summary>Water the character swims in, or null where there is none. Set by the game.</summary>
        public ICharacterWater Water { get; set; }

        /// <summary>The character swims at the surface, held at <see cref="MovementConfig.FloatDepth"/> under it.</summary>
        public bool IsSwimming => _swimming;

        /// <summary>
        /// Asked when a swimmer presses jump: true if something took the press, such as climbing out onto a ride,
        /// and the swimmer must not lunge. Null: every press lunges.
        /// </summary>
        public Func<bool> SwimJump { get; set; }

        /// <summary>
        /// Who controls the character, read every frame into <see cref="SetInput"/>. Null: nothing does, input comes
        /// only from direct <see cref="SetInput"/> calls. Changing it clears the input.
        /// </summary>
        public ICharacterInputSource InputSource
        {
            get => _inputSource;
            set
            {
                if (ReferenceEquals(_inputSource, value))
                {
                    return;
                }

                _inputSource = value;
                ClearInput();
            }
        }

        private Vector3 _move;
        private Vector3 _look;
        private Vector3 _aim;
        private const float FacingCatch = 2f;
        private bool _faceLook;
        // Heading locked onto the look: set once the body has turned to it, so it snaps only from there on.
        private bool _facing;
        private bool _sprint;
        private Vector3 _addedVelocity;
        private bool _pendingJump;
        private bool _jumpRequested;
        private bool _jumpConsumed;
        private bool _jumped;
        private float _jumpAge;
        private float _timeSinceCanJump = float.PositiveInfinity;
        private bool _suspended;
        private Vector3 _followPosition;
        private Quaternion _followRotation;
        private ICharacterMount _mount;
        private ICharacterInputSource _inputSource;
        private bool _swimming;
        private float _swimJumpTimer;
        private readonly RaycastHit[] _bottomHits = new RaycastHit[8];

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            Registry.Clear();
        }

        private void Awake()
        {
            Motor = GetComponent<KinematicCharacterMotor>();
            Motor.CharacterController = this;
            // Dynamic bodies with ICharacterSolid stop the capsule, which gives them nothing (their own rules push them);
            // every other dynamic body is let through by IsColliderValidForCollisions, as the kinematic mode did.
            Motor.RigidbodyInteractionType = RigidbodyInteractionType.SimulatedDynamic;
            Motor.SimulatedCharacterMass = 0f;
            Registry.Add(this);
        }

        private void OnDestroy()
        {
            Registry.Remove(this);
        }

        private void OnEnable()
        {
            if (Settings == null)
            {
                Log.Error("CharacterMotor requires movement settings.", this);
                enabled = false;
                return;
            }
            Motor.enabled = true;
        }

        private void OnDisable()
        {
            ClearInput();
            Resume();
            _addedVelocity = Vector3.zero;
            WantedVelocity = Vector3.zero;
            _jumpConsumed = false;
            _timeSinceCanJump = float.PositiveInfinity;
            _swimming = false;
            if (Motor != null)
            {
                Motor.enabled = false;
            }

            Motion = default;
        }

        private void Update()
        {
            if (_inputSource != null)
            {
                SourceInput = _inputSource.ReadInput();
                SetInput(SourceInput);
            }
        }

        /// <summary>Move and Look persist. A false JumpPressed never erases an unprocessed press.</summary>
        public void SetInput(CharacterInput input)
        {
            if (!isActiveAndEnabled || _suspended)
            {
                return;
            }

            _move = Vector3.ClampMagnitude(input.Move, 1f);
            _look = input.Look;
            _aim = input.Aim;
            _faceLook = input.FirstPerson && Settings.FirstPersonTurn == BodyTurn.AtOnce;
            _sprint = input.Sprint;
            _pendingJump |= input.JumpPressed;
        }

        /// <summary>
        /// Where the character looks, as <see cref="CharacterInput.Aim"/>, taken while suspended too: a seated rider
        /// still turns its head, and Motion carries the aim to the other machines.
        /// </summary>
        public void SetAim(Vector3 aim)
        {
            if (isActiveAndEnabled)
            {
                _aim = aim;
            }
        }

        /// <summary>Call when control is revoked (menus, focus loss, changing input source).</summary>
        public void ClearInput()
        {
            _move = _look = _aim = Vector3.zero;
            _sprint = _faceLook = _facing = false;
            _pendingJump = _jumpRequested = false;
        }

        /// <summary>Queue a world-space velocity change for the next simulation step.</summary>
        public void AddVelocity(Vector3 velocity)
        {
            if (isActiveAndEnabled)
            {
                _addedVelocity += velocity;
            }
        }

        /// <summary>
        /// Moving at <paramref name="velocity"/> in the world from now on, as the body was carried. Landing on a
        /// moving body, the character system takes that body's own motion out, so a character put on a deck going
        /// as fast as it stays where it was put.
        /// </summary>
        public void SetVelocity(Vector3 velocity)
        {
            if (isActiveAndEnabled && Motor != null)
            {
                _addedVelocity = Vector3.zero;
                Motor.BaseVelocity = velocity;
            }
        }

        /// <summary>Hands the body over to something else; see <see cref="IsSuspended"/>.</summary>
        public void Suspend()
        {
            if (_suspended || !isActiveAndEnabled)
            {
                return;
            }

            ClearInput();
            _suspended = true;
            _swimming = false;
            _addedVelocity = Vector3.zero;
            _followPosition = Motor.TransientPosition;
            _followRotation = Motor.TransientRotation;
            Motor.BaseVelocity = Vector3.zero;
            Motor.SetMovementCollisionsSolvingActivation(false);
            Motor.SetGroundSolvingActivation(false);
            Motor.Capsule.enabled = false;
        }

        /// <summary>
        /// The character swims from now, such as a ragdoll recovering in deep water. It stays swimming until the
        /// water gets shallow; nothing happens where there is no water.
        /// </summary>
        public void StartSwimming()
        {
            if (Water == null || !Water.TrySample(Motor.TransientPosition, out _, out _))
            {
                return;
            }

            _swimming = true;
        }

        /// <summary>Where a suspended capsule goes on the next step.</summary>
        public void Follow(Vector3 position, Quaternion rotation)
        {
            _followPosition = position;
            _followRotation = rotation;
        }

        /// <summary>Puts a suspended capsule there at once, without interpolating to it.</summary>
        public void Teleport(Vector3 position, Quaternion rotation)
        {
            Motor.SetPositionAndRotation(position, rotation);
            Follow(position, rotation);
        }

        /// <summary>The capsule is the body again, at rest where it was left.</summary>
        public void Resume()
        {
            if (!_suspended)
            {
                return;
            }

            _suspended = false;
            if (Motor != null)
            {
                Motor.BaseVelocity = Vector3.zero;
                Motor.Capsule.enabled = true;
                Motor.SetMovementCollisionsSolvingActivation(true);
                Motor.SetGroundSolvingActivation(true);
            }
        }

        public void BeforeCharacterUpdate(float deltaTime)
        {
            _jumped = false;
            if (_pendingJump)
            {
                _jumpRequested = true;
                _jumpAge = 0f;
                _pendingJump = false;
            }
        }

        public void UpdateRotation(ref Quaternion currentRotation, float deltaTime)
        {
            if (_suspended)
            {
                currentRotation = _followRotation;
                _facing = false;
                return;
            }

            Vector3 look = Vector3.ProjectOnPlane(_look, Motor.CharacterUp).normalized;
            _facing = _faceLook && look.sqrMagnitude > 0f
                      && (_facing || Vector3.Angle(Motor.CharacterForward, look) <= FacingCatch);
            if (look.sqrMagnitude > 0f && (_facing || Settings.OrientationSharpness > 0f))
            {
                Vector3 direction = _facing
                    ? look
                    : Vector3.Slerp(Motor.CharacterForward, look,
                        1f - Mathf.Exp(-Settings.OrientationSharpness * deltaTime)).normalized;
                currentRotation = Quaternion.LookRotation(direction, Motor.CharacterUp);
            }

            Vector3 currentUp = currentRotation * Vector3.up;
            Vector3 targetUp = Vector3.up;
            if (Settings.UpMode != CharacterUpMode.World)
            {
                targetUp = Gravity.sqrMagnitude > 0f ? -Gravity.normalized : currentUp;
            }

            bool alignToGround = Settings.UpMode == CharacterUpMode.GroundAndGravity
                                 && Motor.GroundingStatus.IsStableOnGround;
            if (alignToGround)
            {
                targetUp = Motor.GroundingStatus.GroundNormal;
            }

            Vector3 smoothedUp = Vector3.Slerp(currentUp, targetUp,
                1f - Mathf.Exp(-Settings.UpOrientationSharpness * deltaTime));
            currentRotation = Quaternion.FromToRotation(currentUp, smoothedUp) * currentRotation;
            if (alignToGround)
            {
                // Rotate around the bottom hemisphere, as in the KCC example.
                Vector3 center = Motor.TransientPosition + currentUp * Motor.Capsule.radius;
                Motor.SetTransientPosition(center - currentRotation * Vector3.up * Motor.Capsule.radius);
            }
        }

        public void UpdateVelocity(ref Vector3 currentVelocity, float deltaTime)
        {
            if (_suspended)
            {
                _addedVelocity = Vector3.zero;
                WantedVelocity = Vector3.zero;
                currentVelocity = deltaTime > 0f ? (_followPosition - Motor.TransientPosition) / deltaTime : Vector3.zero;
                return;
            }

            Vector3 move = Vector3.ClampMagnitude(Vector3.ProjectOnPlane(_move, Motor.CharacterUp), 1f);
            float maxSpeed = (_sprint ? Settings.MaxSprintSpeed : Settings.MaxStableMoveSpeed) * SpeedMultiplier;
            WantedVelocity = move * maxSpeed;

            if (UpdateSwimming(deltaTime, out float surface, out Vector3 water))
            {
                Swim(ref currentVelocity, move, surface, water, deltaTime);
                currentVelocity += _addedVelocity;
                _addedVelocity = Vector3.zero;
                return;
            }

            if (Motor.GroundingStatus.IsStableOnGround)
            {
                Vector3 normal = Motor.GroundingStatus.GroundNormal;
                currentVelocity = Motor.GetDirectionTangentToSurface(currentVelocity, normal) * currentVelocity.magnitude;
                Vector3 right = Vector3.Cross(move, Motor.CharacterUp);
                Vector3 target = Vector3.Cross(normal, right).normalized * (move.magnitude * maxSpeed);
                float sharpness = target.sqrMagnitude < currentVelocity.sqrMagnitude
                    ? Settings.StableDecelerationSharpness : Settings.StableAccelerationSharpness;
                currentVelocity = Vector3.Lerp(currentVelocity, target, 1f - Mathf.Exp(-sharpness * deltaTime));
            }
            else
            {
                Vector3 added = move * Settings.AirAccelerationSpeed * deltaTime;
                Vector3 planarVelocity = Vector3.ProjectOnPlane(currentVelocity, Motor.CharacterUp);
                if (planarVelocity.magnitude < Settings.MaxAirMoveSpeed)
                {
                    added = Vector3.ClampMagnitude(planarVelocity + added, Settings.MaxAirMoveSpeed) - planarVelocity;
                }
                else if (Vector3.Dot(planarVelocity, added) > 0f)
                {
                    added = Vector3.ProjectOnPlane(added, planarVelocity.normalized);
                }

                // Prevent input from pushing the character up unstable slopes.
                if (Motor.GroundingStatus.FoundAnyGround && Vector3.Dot(currentVelocity + added, added) > 0f)
                {
                    Vector3 obstruction = Vector3.Cross(Vector3.Cross(Motor.CharacterUp,
                        Motor.GroundingStatus.GroundNormal), Motor.CharacterUp).normalized;
                    added = Vector3.ProjectOnPlane(added, obstruction);
                }
                currentVelocity += added + Gravity * deltaTime;
                currentVelocity /= 1f + Settings.Drag * deltaTime;
            }

            bool canJump = CanJumpFromGround();
            if (canJump)
            {
                _timeSinceCanJump = 0f;
                _jumpConsumed = false;
            }
            if (_jumpRequested && !_jumpConsumed &&
                (canJump || _timeSinceCanJump <= Settings.JumpPostGroundingGraceTime))
            {
                Vector3 direction = Motor.CharacterUp;
                if (Motor.GroundingStatus.FoundAnyGround && !Motor.GroundingStatus.IsStableOnGround)
                {
                    direction = Motor.GroundingStatus.GroundNormal;
                }

                Motor.ForceUnground();
                float jumpScale = Mathf.Max(0f, JumpScale?.Invoke() ?? 1f);
                currentVelocity += direction * (Settings.JumpUpSpeed * jumpScale) - Vector3.Project(currentVelocity, Motor.CharacterUp);
                currentVelocity += move * Settings.JumpScalableForwardSpeed;
                _jumpRequested = false;
                _jumpConsumed = _jumped = true;
            }
            currentVelocity += _addedVelocity;
            _addedVelocity = Vector3.zero;
        }

        public void AfterCharacterUpdate(float deltaTime)
        {
            if (_jumpRequested)
            {
                _jumpAge += deltaTime;
                if (_jumpAge > Settings.JumpPreGroundingGraceTime)
                {
                    _jumpRequested = false;
                }
            }
            if (!CanJumpFromGround())
            {
                _timeSinceCanJump += deltaTime;
            }

            bool grounded = Motor.GroundingStatus.IsStableOnGround && !_jumped && !_suspended && !_swimming;
            bool wasGrounded = Motion.IsGrounded;
            Motion = new CharacterMotion(Motor.Velocity, Motor.BaseVelocity, _aim, Motor.TransientRotation,
                grounded, Motor.GroundingStatus.FoundAnyGround && !_jumped && !_swimming, _swimming, _facing);
            if (_jumped)
            {
                Jumped?.Invoke();
            }

            if (grounded && !wasGrounded)
            {
                Landed?.Invoke();
            }

            if (!grounded && wasGrounded)
            {
                LeftGround?.Invoke();
            }

            MotionUpdated?.Invoke(Motion);
        }

        // Starts and stops swimming by the depth of the water at rest over the bottom, so waves do not flip it: a
        // walker swims once the bottom drops away under the float line, a swimmer stands once it comes up near the
        // surface. True while swimming, with the surface height and the water's velocity.
        private bool UpdateSwimming(float deltaTime, out float surface, out Vector3 water)
        {
            Vector3 position = Motor.TransientPosition;
            if (Water == null || !Water.TrySample(position, out surface, out water))
            {
                surface = 0f;
                water = Vector3.zero;
                _swimming = false;
                return false;
            }

            float depth = DepthOverBottom(position, Water.Level(position));
            if (_swimming)
            {
                _swimming = depth >= Settings.SwimExitDepth;
            }
            else
            {
                // The soles reach the float line: wading in, or falling in from above.
                _swimming = depth > Settings.SwimEnterDepth && surface - position.y > Settings.FloatDepth - 0.1f;
            }

            _swimJumpTimer = Mathf.Max(0f, _swimJumpTimer - deltaTime);
            return _swimming;
        }

        // Height of the water level over the bottom under the character, at most the reach of the probe.
        private float DepthOverBottom(Vector3 position, float level)
        {
            Vector3 up = Motor.CharacterUp;
            float reach = Settings.FloatDepth + Settings.SwimEnterDepth;
            float lift = Motor.Capsule.radius;
            int count = Physics.RaycastNonAlloc(position + up * lift, -up, _bottomHits, lift + reach,
                Motor.CollidableLayers, QueryTriggerInteraction.Ignore);
            float bottom = position.y - reach;
            for (int i = 0; i < count; i++)
            {
                RaycastHit hit = _bottomHits[i];
                // Any collider but our own counts as bottom, dynamic bodies too (a tube, a hull): not the solid rule above.
                if (hit.collider != Motor.Capsule && !IgnoredColliders.Contains(hit.collider))
                {
                    bottom = Mathf.Max(bottom, hit.point.y);
                }
            }

            return level - bottom;
        }

        // Moves toward the input at swimming speed, carried a little by the water, while a spring holds the body at
        // the float line against the water's rise and fall. A jump lunges up out of the water.
        private void Swim(ref Vector3 velocity, Vector3 move, float surface, Vector3 water, float deltaTime)
        {
            Motor.ForceUnground();
            Vector3 up = Motor.CharacterUp;
            float rise = Vector3.Dot(velocity, up);
            Vector3 planar = velocity - up * rise;

            float offset = surface - Settings.FloatDepth - Vector3.Dot(Motor.TransientPosition, up);
            float relativeRise = rise - Vector3.Dot(water, up);
            rise += (Settings.FloatSpring * offset - Settings.FloatDamping * relativeRise) * deltaTime;

            float speed = _sprint ? Settings.SwimSprintSpeed : Settings.SwimSpeed;
            Vector3 target = move * (speed * SpeedMultiplier) + Vector3.ProjectOnPlane(water, up) * Settings.SwimDrift;
            float sharpness = target.sqrMagnitude < planar.sqrMagnitude
                ? Settings.SwimDecelerationSharpness : Settings.SwimAccelerationSharpness;
            planar = Vector3.Lerp(planar, target, 1f - Mathf.Exp(-sharpness * deltaTime));

            if (_jumpRequested)
            {
                _jumpRequested = false;
                bool taken = SwimJump?.Invoke() ?? false;
                if (!taken && _swimJumpTimer <= 0f && offset > -0.2f)
                {
                    rise = Mathf.Max(rise, Settings.SwimJumpSpeed);
                    _swimJumpTimer = Settings.SwimJumpCooldown;
                }
            }

            velocity = planar + up * rise;
        }

        private bool CanJumpFromGround() => Settings.AllowJumpingWhenSliding
            ? Motor.GroundingStatus.FoundAnyGround : Motor.GroundingStatus.IsStableOnGround;

        public bool IsColliderValidForCollisions(Collider coll)
        {
            if (IgnoredColliders.Contains(coll))
                return false;

            Rigidbody body = coll.attachedRigidbody;
            if (body == null || body.isKinematic)
                return true;

            if (body.TryGetComponent(out ICharacterSolid solid) && solid.StopsCharacters)
                return true;

            body.WakeUp();
            return false;
        }
        public void PostGroundingUpdate(float deltaTime) { }
        public void OnGroundHit(Collider collider, Vector3 normal, Vector3 point, ref HitStabilityReport report) { }
        public void OnMovementHit(Collider collider, Vector3 normal, Vector3 point, ref HitStabilityReport report) { }
        // A solid dynamic body (a ball) is no ground: walking into one leans on it rather than climbs it.
        public void ProcessHitStabilityReport(Collider collider, Vector3 normal, Vector3 point,
            Vector3 position, Quaternion rotation, ref HitStabilityReport report)
        {
            Rigidbody body = collider.attachedRigidbody;
            if (report.IsStable && body != null && !body.isKinematic && body.TryGetComponent(out ICharacterSolid _))
                report.IsStable = false;
        }
        public void OnDiscreteCollisionDetected(Collider collider) { }
    }
}
