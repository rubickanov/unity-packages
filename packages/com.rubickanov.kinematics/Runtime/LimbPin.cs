using System;
using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    [Serializable]
    public struct LimbPinSettings
    {
        /// <summary>A contact this close to the ground, metres, plants and stays where it touched down.</summary>
        public double PlantHeight;

        /// <summary>How much higher than <see cref="PlantHeight"/> the animation must lift it before it lets go,
        /// metres.</summary>
        public double LiftMargin;

        /// <summary>Farthest the animation may drag a planted contact away before it steps after it, metres.</summary>
        public double MaxSlip;

        /// <summary>Seconds a step takes.</summary>
        public double StepTime;

        /// <summary>How high a step lifts the contact at its middle, metres.</summary>
        public double StepHeight;

        /// <summary>Seconds a contact that let go takes to catch up with the animation in the air.</summary>
        public double CatchUpTime;

        public LimbPinSettings(double plantHeight, double liftMargin, double maxSlip, double stepTime,
            double stepHeight, double catchUpTime)
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
    /// Keeps a hand or a foot where it touched the ground while the animation around it slides it along. It lets go
    /// when the animation lifts it, and steps after the animation when it is dragged too far, so a foot that scrapes
    /// along the ground in the clip takes a step instead. Planar: the height is the animation's.
    /// </summary>
    public struct LimbPin
    {
        private enum State : byte
        {
            Free,
            Planted,
            Stepping
        }

        private State _state;
        private double3 _anchor;
        private double3 _stepFrom;
        private double _stepTime;
        private bool _released;

        /// <summary>What to add to the animated contact to put it where it is held.</summary>
        public double3 Shift { get; private set; }

        public readonly bool IsPlanted => _state == State.Planted;
        public readonly bool IsStepping => _state == State.Stepping;

        public void Reset()
        {
            _state = State.Free;
            _stepTime = 0.0;
            _released = false;
            Shift = double3.zero;
        }

        /// <summary>
        /// The held point moves from one bone of the limb to another, as a foot rolls onto its toes: the anchor and a
        /// step under way move with it, so the limb stays where it is shown.
        /// </summary>
        public void Rebase(double3 from, double3 to, double3 up)
        {
            double3 moved = DetMath.ProjectOnPlane(to - from, up);
            _anchor += moved;
            _stepFrom += moved;
        }

        /// <summary>
        /// Stops planting. A contact held far from the animation steps back to it, one held close slides the short
        /// way; either way nothing plants again until <see cref="Reset"/>.
        /// </summary>
        public void Release(in LimbPinSettings settings, double3 contact, double3 up)
        {
            _released = true;
            if (_state == State.Planted)
            {
                double slip = DetMath.Length(DetMath.ProjectOnPlane(_anchor - contact, up));
                if (slip > settings.MaxSlip * 0.25)
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
        /// Contact is the animated point that touches the ground, lift how far above the ground it is. Returns
        /// <see cref="Shift"/>.
        /// </summary>
        public double3 Update(double3 contact, double lift, double3 up, in LimbPinSettings settings, double deltaTime)
        {
            if (_state == State.Planted && lift > settings.PlantHeight + settings.LiftMargin)
            {
                _state = State.Free;
            }

            switch (_state)
            {
                case State.Free:
                    if (!_released && lift <= settings.PlantHeight)
                    {
                        // Planted where it is shown, which is the animation plus what is still left to catch up.
                        _state = State.Planted;
                        _anchor = contact + DetMath.ProjectOnPlane(Shift, up);
                        break;
                    }

                    double decay = 1.0 - DetMath.Exp(-deltaTime / settings.CatchUpTime);
                    Shift *= 1.0 - DetMath.Clamp01(decay);
                    return Shift;
                case State.Planted:
                    if (DetMath.Length(DetMath.ProjectOnPlane(_anchor - contact, up)) > settings.MaxSlip)
                    {
                        BeginStep();
                    }

                    break;
            }

            if (_state == State.Stepping)
            {
                _stepTime += deltaTime;
                double t = DetMath.Clamp01(_stepTime / settings.StepTime);
                // Lands on the animation, which may still be moving, rather than where it was when the step began.
                double3 from = DetMath.ProjectOnPlane(_stepFrom - contact, up);
                double eased = t * t * (3.0 - 2.0 * t);
                Shift = from * (1.0 - eased) + up * (settings.StepHeight * DetMath.Sin(t * DetMath.Pi));
                if (t >= 1.0)
                {
                    Shift = double3.zero;
                    _state = State.Free;
                    if (!_released && lift <= settings.PlantHeight)
                    {
                        _state = State.Planted;
                        _anchor = contact;
                    }
                }

                return Shift;
            }

            Shift = DetMath.ProjectOnPlane(_anchor - contact, up);
            return Shift;
        }

        private void BeginStep()
        {
            _state = State.Stepping;
            _stepFrom = _anchor;
            _stepTime = 0.0;
        }
    }
}
