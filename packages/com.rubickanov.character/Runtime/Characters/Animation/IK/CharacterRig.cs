using Rubickanov.Log;
using UnityEngine;

namespace Rubickanov.Characters
{
    /// <summary>
    /// Procedural touches on top of the animated pose: leaning into turns, looking where the player
    /// looks, feet on the ground, and hands and feet held where they touch down while getting up. Reads the same motion as the CharacterAnimator, so remote
    /// characters get it too without any bones sent over the network. While the character rides
    /// (<see cref="CharacterMotor.Mount"/>) only the look stays, and the hands and feet go to the seat's holds.
    /// </summary>
    // After KinematicCharacterSystem interpolates characters (-100), before the camera follows them (0).
    [DefaultExecutionOrder(-50)]
    [DisallowMultipleComponent]
    public sealed class CharacterRig : MonoBehaviour
    {
        private static readonly LogChannel Log = LogChannel.Get("Character");

        [Tooltip("Provides the motion and the animated body.")]
        public CharacterAnimator Animator;
        public CharacterRigConfig Config;
        [Range(0, 1)] public float Weight = 1f;

        private Transform _body;
        // Turned from the body while the character turns in place; looking and leaning follow it.
        private Transform _model;
        private Animator _animator;
        private FootPlacement _feet;
        private GetUpContacts _contacts;
        private HeadLook _look;
        private TurnLean _lean;
        private MountLimbs _limbs;
        // The seat the limbs reach for; changing it starts the reach over, leaving it resets what it held back.
        private ICharacterMount _mount;
        private float _lastTurnOffset;
        private IFootGround _footGround;
        private IFootGround _bodyGround;
        private IFootprints _footprints;
        private int _ignoredGround;

        /// <summary>Told each time a foot is set down; null: no one.</summary>
        public IFootprints Footprints
        {
            get => _footprints;
            set
            {
                _footprints = value;
                if (_feet != null)
                {
                    _feet.Prints = value;
                }
            }
        }

        /// <summary>Ground for the feet besides what physics hits; null: physics alone.</summary>
        public IFootGround FootGround
        {
            get => _footGround;
            set
            {
                _footGround = value;
                if (_feet != null)
                {
                    _feet.Ground = value;
                }
            }
        }

        /// <summary>
        /// Layers the feet and the get-up contacts never take for ground, on top of the config's: e.g. the layer
        /// of ragdoll shells, whose bones stand around the legs.
        /// </summary>
        public int IgnoredGroundLayers
        {
            get => _ignoredGround;
            set
            {
                _ignoredGround = value;
                if (_feet != null)
                {
                    _feet.IgnoredLayers = value;
                    _contacts.IgnoredLayers = value;
                }
            }
        }

        /// <summary>
        /// Ground for a lying body and for the hands and feet while getting up besides what physics hits
        /// (<see cref="CharacterRagdoll"/>, <see cref="GetUpContacts"/>); null: physics alone.
        /// </summary>
        public IFootGround BodyGround
        {
            get => _bodyGround;
            set
            {
                _bodyGround = value;
                if (_contacts != null)
                {
                    _contacts.Ground = value;
                }
            }
        }

        private void OnEnable()
        {
            if (Animator == null || Animator.Character == null || Animator.Animancer == null || Config == null)
            {
                Log.Error("CharacterRig requires a character animator and a rig config.", this);
                enabled = false;
                return;
            }

            _animator = Animator.Animancer.Animator;
            if (_animator == null || !_animator.isHuman)
            {
                Log.Error("CharacterRig requires a humanoid Animator.", this);
                enabled = false;
                return;
            }

            if (_animator.cullingMode != AnimatorCullingMode.AlwaysAnimate)
            {
                // A culled Animator stops rewriting the bones, and the rig's rotations would pile up on them.
                Log.Warn("CharacterRig expects the Animator to always animate.", this);
            }

            _body = Animator.Character.transform;
            _model = _animator.transform;
            _feet = new FootPlacement(_animator, _body)
                { Ground = _footGround, Prints = _footprints, IgnoredLayers = _ignoredGround };
            _contacts = new GetUpContacts(_animator, _body) { Ground = _bodyGround, IgnoredLayers = _ignoredGround };
            _look = new HeadLook(_animator, _model, Config.LookBones);
            _lean = new TurnLean(_animator, _model, Config.LeanBones);
            _limbs = new MountLimbs(_animator);

            CharacterMotion motion = Animator.Source?.Motion ?? default;
            _feet.Reset(motion.IsGrounded);
            _contacts.Reset();
            _look.Reset();
            _lean.Reset(motion.PlanarSpeed);
            _limbs.Reset();
            _mount = Animator.Character.Mount;
            _lastTurnOffset = Animator.TurnOffset;
        }

        private void LateUpdate() => UpdateRig(Time.deltaTime);

        private void UpdateRig(float deltaTime)
        {
            ICharacterMotionSource source = Animator.Source;
            if (source == null || !_animator.isActiveAndEnabled)
            {
                return;
            }

            CharacterMotion motion = source.Motion;
            ICharacterMount mount = Animator.Character.Mount;
            if (!ReferenceEquals(mount, _mount))
            {
                if (mount == null)
                {
                    // Off the seat: start the feet, lean and contacts from the pose shown now, as after a get-up,
                    // not from where they were when the character sat down.
                    _feet.Reset(motion.IsGrounded);
                    _contacts.Reset();
                    _lean.Reset(motion.PlanarSpeed);
                }

                _limbs.Reset();
                _mount = mount;
            }

            bool mounted = mount != null;
            // Feet go last so they land where the animation put them, whatever the passes before did to the body.
            if (_feet.IsValid)
            {
                _feet.Capture();
            }

            if (_contacts.IsValid)
            {
                _contacts.Capture();
            }

            if (_lean.IsValid && !mounted)
            {
                _lean.Apply(Config, motion.PlanarSpeed, motion.IsGrounded, Weight, deltaTime);
            }

            float modelTurn = Mathf.DeltaAngle(_lastTurnOffset, Animator.TurnOffset);
            _lastTurnOffset = Animator.TurnOffset;
            if (_look.IsValid)
            {
                // No look while getting up lets the head follow the clip, easing out and back in.
                Vector3 aim = Animator.IsGettingUp ? Vector3.zero : ModelAim(source.Aim);
                // A model that faces the look turns with it: nothing to hold the upper body against.
                _look.Apply(Config, aim, motion.FacesLook ? 0f : modelTurn, Weight, deltaTime);
            }

            if (mounted)
            {
                // The seat holds the body: no ground under the feet, no lean, no get-up.
                // A body down or getting up lets go of the holds.
                if (_limbs.IsValid && !Animator.IsGettingUp && !Animator.IsDown)
                {
                    _limbs.Apply(Config, mount, Weight, deltaTime);
                }

                return;
            }

            if (_feet.IsValid)
            {
                _feet.Apply(Config, motion.IsGrounded, Weight, deltaTime);
            }

            if (_contacts.IsValid)
            {
                _contacts.Apply(Config, Animator.IsGettingUp, Weight, deltaTime);
            }
        }

        // The look in the model's space; the model may be turned from the body.
        private Vector3 ModelAim(Vector3 aim) => Quaternion.Inverse(_model.rotation) * aim;

        private void OnDrawGizmosSelected()
        {
            if (!Application.isPlaying || !isActiveAndEnabled || Animator.Source == null)
            {
                return;
            }

            if (_feet.IsValid)
            {
                _feet.DrawGizmos();
            }

            if (_look.IsValid)
            {
                _look.DrawGizmos(ModelAim(Animator.Source.Aim));
            }
        }
    }
}
