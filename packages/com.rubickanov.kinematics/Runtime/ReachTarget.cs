using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>How a hand reaches for a target: how fast it travels, how fast the reach takes over and lets go.</summary>
    public struct ReachSettings
    {
        /// <summary>The hand travels toward the target at most this fast, m/s.</summary>
        public double Speed;

        /// <summary>The reach takes over from the clip's hand, or gives it back, over this long, s.</summary>
        public double Blend;

        /// <summary>The share of the arm's full length the hand is held within (a straight arm locks).</summary>
        public double Stretch;

        public static ReachSettings Default => new() { Speed = 3.0, Blend = 0.12, Stretch = 0.97 };
    }

    /// <summary>A reach between ticks: where the reaching hand is and how much of the pose it holds.</summary>
    public struct ReachState
    {
        public double3 Hand;
        public double Weight;
        public bool Started;
    }

    /// <summary>
    /// A hand to a target at a limited speed (a palm into an opponent's chest): the hand starts where the clip has it,
    /// travels toward the target no faster than <see cref="ReachSettings.Speed"/>, held within the arm's reach from
    /// the shoulder; the reach fades in and out over <see cref="ReachSettings.Blend"/>. The arm itself is bent by
    /// <see cref="TwoBoneIK"/> onto the hand this returns.
    /// </summary>
    public static class ReachTarget
    {
        /// <summary>One tick: where the hand goes, given the clip's hand, the shoulder, the arm's full length and the
        /// target (<paramref name="reaching"/> false lets the hand go back to the clip).</summary>
        public static double3 Step(ref ReachState state, in ReachSettings settings, double3 clipHand, double3 shoulder,
            double armLength, double3 target, bool reaching, double deltaTime)
        {
            double fade = settings.Blend > 0.0 ? deltaTime / settings.Blend : 1.0;
            state.Weight = DetMath.Clamp01(state.Weight + (reaching ? fade : -fade));
            if (state.Weight <= 0.0)
            {
                state.Started = false;
                return clipHand;
            }

            if (!state.Started)
            {
                state.Hand = clipHand;
                state.Started = true;
            }

            // Where the hand would go: the target, or the clip's hand when letting go, within the arm's reach.
            double3 goal = reaching ? target : clipHand;
            double3 reach = goal - shoulder;
            double most = armLength * settings.Stretch;
            double length = DetMath.Length(reach);
            if (length > most)
            {
                goal = shoulder + reach * (most / length);
            }

            double3 step = goal - state.Hand;
            double far = DetMath.Length(step);
            double travel = settings.Speed * deltaTime;
            state.Hand = far <= travel ? goal : state.Hand + step * (travel / far);
            return clipHand + (state.Hand - clipHand) * state.Weight;
        }
    }
}
