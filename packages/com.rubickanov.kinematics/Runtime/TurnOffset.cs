using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>
    /// How far the visible body is turned from the simulated one, in degrees positive to the right. Lets the feet
    /// stay planted while the simulated body turns under them, until a turn animation catches the visible body up.
    /// </summary>
    public struct TurnOffset
    {
        public double Value { get; private set; }

        public void Reset() => Value = 0.0;

        /// <summary>Degrees the visible body has to turn to face target, given in the simulated body's space.</summary>
        public readonly double ErrorTo(double target) => DetMath.DeltaAngle(Value, target);

        /// <summary>
        /// Keeps the visible body still while the simulated one turns by bodyTurn degrees. Past maxLag from target
        /// the visible body is carried along instead, so it never falls further behind.
        /// </summary>
        public void Counter(double bodyTurn, double target, double maxLag)
        {
            double countered = DetMath.DeltaAngle(0.0, Value - bodyTurn);
            double error = math.abs(DetMath.DeltaAngle(countered, target));
            if (error <= maxLag || error <= math.abs(ErrorTo(target)))
            {
                Value = countered;
            }
        }

        /// <summary>Turns the visible body by degrees, e.g. by an animation's root rotation.</summary>
        public void Turn(double degrees) => Value = DetMath.DeltaAngle(0.0, Value + degrees);

        /// <summary>Eases the visible body back in line with the simulated one.</summary>
        public void Settle(double amount) => Value = DetMath.LerpAngle(Value, 0.0, amount);
    }
}
