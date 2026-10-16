using System;
using UnityEngine;

namespace Rubickanov.Rig
{
    [Serializable]
    public struct LimbPinSettings
    {
        [Tooltip("A contact this close to the ground, metres, plants and stays where it touched down.")]
        [Min(0)] public float PlantHeight;
        [Tooltip("How much higher than Plant Height the animation must lift it before it lets go, metres.")]
        [Min(0)] public float LiftMargin;
        [Tooltip("Farthest the animation may drag a planted contact away before it steps after it, metres.")]
        [Min(0.01f)] public float MaxSlip;
        [Tooltip("Seconds a step takes.")]
        [Min(0.01f)] public float StepTime;
        [Tooltip("How high a step lifts the contact at its middle, metres.")]
        [Min(0)] public float StepHeight;
        [Tooltip("Seconds a contact that let go takes to catch up with the animation in the air.")]
        [Min(0.01f)] public float CatchUpTime;

        public LimbPinSettings(float plantHeight, float liftMargin, float maxSlip, float stepTime, float stepHeight,
            float catchUpTime)
        {
            PlantHeight = plantHeight;
            LiftMargin = liftMargin;
            MaxSlip = maxSlip;
            StepTime = stepTime;
            StepHeight = stepHeight;
            CatchUpTime = catchUpTime;
        }
    }

    /// <summary>
    /// Keeps a hand or a foot where it touched the ground while the animation around it slides it along. It lets
    /// go when the animation lifts it, and steps after the animation when it is dragged too far, so a foot that
    /// scrapes along the ground in the clip takes a step instead. Planar: the height is the animation's.
    /// </summary>
    public struct LimbPin
    {
        private enum State { Free, Planted, Stepping }

        private State _state;
        private Vector3 _anchor;
        private Vector3 _stepFrom;
        private float _stepTime;
        private bool _released;

        /// <summary>What to add to the animated contact to put it where it is held.</summary>
        public Vector3 Shift { get; private set; }

        public bool IsPlanted => _state == State.Planted;
        public bool IsStepping => _state == State.Stepping;

        public void Reset()
        {
            _state = State.Free;
            _stepTime = 0f;
            _released = false;
            Shift = Vector3.zero;
        }

        /// <summary>
        /// The held point moves from one bone of the limb to another, as a foot rolls onto its toes: the anchor
        /// and a step under way move with it, so the limb stays where it is shown.
        /// </summary>
        public void Rebase(Vector3 from, Vector3 to, Vector3 up)
        {
            Vector3 moved = Vector3.ProjectOnPlane(to - from, up);
            _anchor += moved;
            _stepFrom += moved;
        }

        /// <summary>
        /// Stops planting. A contact held far from the animation steps back to it, one held close slides the
        /// short way; either way nothing plants again until <see cref="Reset"/>.
        /// </summary>
        public void Release(in LimbPinSettings settings, Vector3 contact, Vector3 up)
        {
            _released = true;
            if (_state == State.Planted)
            {
                float slip = Vector3.ProjectOnPlane(_anchor - contact, up).magnitude;
                if (slip > settings.MaxSlip * 0.25f)
                {
                    BeginStep();
                }
                else
                {
                    _state = State.Free;
                }
            }
        }

        /// <summary>
        /// Contact is the animated point that touches the ground, lift how far above the ground it is.
        /// Returns <see cref="Shift"/>.
        /// </summary>
        public Vector3 Update(Vector3 contact, float lift, Vector3 up, in LimbPinSettings settings, float deltaTime)
        {
            switch (_state)
            {
                case State.Free:
                    if (!_released && lift <= settings.PlantHeight)
                    {
                        // Planted where it is shown, which is the animation plus what is still left to catch up.
                        _state = State.Planted;
                        _anchor = contact + Vector3.ProjectOnPlane(Shift, up);
                        break;
                    }

                    float decay = 1f - Mathf.Exp(-deltaTime / settings.CatchUpTime);
                    Shift = Vector3.Lerp(Shift, Vector3.zero, decay);
                    return Shift;
                case State.Planted:
                    if (lift > settings.PlantHeight + settings.LiftMargin)
                    {
                        _state = State.Free;
                        return Update(contact, lift, up, settings, deltaTime);
                    }

                    if (Vector3.ProjectOnPlane(_anchor - contact, up).magnitude > settings.MaxSlip)
                    {
                        BeginStep();
                    }

                    break;
            }

            if (_state == State.Stepping)
            {
                _stepTime += deltaTime;
                float t = Mathf.Clamp01(_stepTime / settings.StepTime);
                // Lands on the animation, which may still be moving, rather than where it was when the step began.
                Vector3 from = Vector3.ProjectOnPlane(_stepFrom - contact, up);
                float eased = t * t * (3f - 2f * t);
                Shift = from * (1f - eased) + up * (settings.StepHeight * Mathf.Sin(t * Mathf.PI));
                if (t >= 1f)
                {
                    Shift = Vector3.zero;
                    _state = State.Free;
                    if (!_released && lift <= settings.PlantHeight)
                    {
                        _state = State.Planted;
                        _anchor = contact;
                    }
                }

                return Shift;
            }

            Shift = Vector3.ProjectOnPlane(_anchor - contact, up);
            return Shift;
        }

        private void BeginStep()
        {
            _state = State.Stepping;
            _stepFrom = _anchor;
            _stepTime = 0f;
        }
    }
}
