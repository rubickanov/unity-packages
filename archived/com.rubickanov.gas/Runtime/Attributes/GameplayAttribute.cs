using System;

namespace Rubickanov.GAS
{
    /// <summary>
    /// A single attribute of a target. Holds a <see cref="BaseValue"/> (authoritative) and a
    /// <see cref="CurrentValue"/> (derived by aggregating active modifiers). Mutate base values
    /// through <see cref="AttributeSet.SetBaseValue"/>, not by direct assignment.
    /// </summary>
    public sealed class GameplayAttribute
    {
        private float _baseValue;
        private float _currentValue;

        public float BaseValue
        {
            get => _baseValue;
            internal set => _baseValue = value;
        }

        public float CurrentValue => _currentValue;

        /// <summary>
        /// Fires when <see cref="CurrentValue"/> changes. Arguments are (oldValue, newValue).
        /// </summary>
        public event Action<float, float>? ValueChanged;

        public GameplayAttribute(float baseValue = 0f)
        {
            _baseValue = baseValue;
            _currentValue = baseValue;
        }

        internal void SetCurrentValue(float value)
        {
            if (Math.Abs(_currentValue - value) < float.Epsilon) return;
            float previous = _currentValue;
            _currentValue = value;
            ValueChanged?.Invoke(previous, _currentValue);
        }
    }
}
