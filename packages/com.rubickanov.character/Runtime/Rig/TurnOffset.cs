using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>
    /// How far the visible body is turned from the simulated one, in degrees positive to the right.
    /// Lets the feet stay planted while the simulated body turns under them, until a turn animation
    /// catches the visible body up.
    /// </summary>
    public struct TurnOffset
    {
        public float Value { get; private set; }

        public void Reset() => Value = 0f;

        /// <summary>Degrees the visible body has to turn to face target, given in the simulated body's space.</summary>
        public readonly float ErrorTo(float target) => Mathf.DeltaAngle(Value, target);

        /// <summary>
        /// Keeps the visible body still while the simulated one turns by bodyTurn degrees. Past maxLag from
        /// target the visible body is carried along instead, so it never falls further behind.
        /// </summary>
        public void Counter(float bodyTurn, float target, float maxLag)
        {
            float countered = Mathf.DeltaAngle(0f, Value - bodyTurn);
            float error = Mathf.Abs(Mathf.DeltaAngle(countered, target));
            if (error <= maxLag || error <= Mathf.Abs(ErrorTo(target)))
            {
                Value = countered;
            }
        }

        /// <summary>Turns the visible body by degrees, e.g. by an animation's root rotation.</summary>
        public void Turn(float degrees) => Value = Mathf.DeltaAngle(0f, Value + degrees);

        /// <summary>Turns the visible body to face target at once.</summary>
        public void Face(float target) => Value = Mathf.DeltaAngle(0f, target);

        /// <summary>Turns the visible body part of the way to face target.</summary>
        public void Toward(float target, float amount) =>
            Value = Mathf.DeltaAngle(0f, Mathf.LerpAngle(Value, target, Mathf.Clamp01(amount)));

        /// <summary>Eases the visible body back in line with the simulated one.</summary>
        public void Settle(float amount) => Value = Mathf.LerpAngle(Value, 0f, Mathf.Clamp01(amount));
    }
}
