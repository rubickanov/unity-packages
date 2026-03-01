using System;

namespace Rubickanov.GAS
{
    public sealed class GameplayAttribute
    {
        private float _baseValue;
        private float _currentValue;

        public float BaseValue
        {
            get => _baseValue;
            set
            {
                _baseValue = value;
                // CurrentValue will be recalculated by EffectController
            }
        }

        public float CurrentValue => _currentValue;

        public event Action<float>? ValueChanged;

        public GameplayAttribute(float baseValue = 0f)
        {
            _baseValue = baseValue;
            _currentValue = baseValue;
        }

        internal void SetCurrentValue(float value)
        {
            if (Math.Abs(_currentValue - value) < float.Epsilon) return;
            _currentValue = value;
            ValueChanged?.Invoke(_currentValue);
        }
    }
}
