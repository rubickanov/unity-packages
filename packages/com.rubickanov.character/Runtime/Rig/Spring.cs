using System;
using UnityEngine;

namespace Rubickanov.Rig
{
    /// <summary>How a spring follows its target: how fast, and whether it overshoots.</summary>
    [Serializable]
    public struct SpringSettings
    {
        [Tooltip("Oscillations per second. Higher follows faster. 0 follows instantly.")]
        [Min(0)] public float Frequency;
        [Tooltip("1 settles without overshoot, below 1 overshoots and wobbles, above 1 is sluggish.")]
        [Min(0)] public float Damping;

        public SpringSettings(float frequency, float damping)
        {
            Frequency = frequency;
            Damping = damping;
        }
    }

    /// <summary>
    /// Damped harmonic oscillator solved exactly over the step, so it stays stable at any frame time.
    /// One step is a linear map of (offset from target, velocity), shared by every axis of a vector.
    /// </summary>
    public readonly struct SpringStep
    {
        private readonly float _xx, _xv, _vx, _vv;
        private readonly bool _snap;

        public SpringStep(SpringSettings settings, float deltaTime)
        {
            _xx = _xv = _vx = _vv = 0f;
            float omega = 2f * Mathf.PI * settings.Frequency;
            _snap = omega <= 0f;
            if (_snap || deltaTime <= 0f)
            {
                _xx = _vv = 1f;
                return;
            }

            float zeta = settings.Damping;
            if (Mathf.Abs(zeta - 1f) < 1e-4f)
            {
                // Critical: x(t) = e^(-wt) (x0 + (v0 + w x0) t).
                float e = Mathf.Exp(-omega * deltaTime);
                _xx = e * (1f + omega * deltaTime);
                _xv = e * deltaTime;
                _vx = -e * omega * omega * deltaTime;
                _vv = e * (1f - omega * deltaTime);
            }
            else if (zeta < 1f)
            {
                float omegaD = omega * Mathf.Sqrt(1f - zeta * zeta);
                float e = Mathf.Exp(-zeta * omega * deltaTime);
                float cos = Mathf.Cos(omegaD * deltaTime);
                float sin = Mathf.Sin(omegaD * deltaTime);
                _xx = e * (cos + zeta * omega / omegaD * sin);
                _xv = e * sin / omegaD;
                _vx = -e * omega * omega / omegaD * sin;
                _vv = e * (cos - zeta * omega / omegaD * sin);
            }
            else
            {
                float root = omega * Mathf.Sqrt(zeta * zeta - 1f);
                float r1 = -zeta * omega + root;
                float r2 = -zeta * omega - root;
                float e1 = Mathf.Exp(r1 * deltaTime);
                float e2 = Mathf.Exp(r2 * deltaTime);
                float inv = 1f / (r2 - r1);
                _xx = (r2 * e1 - r1 * e2) * inv;
                _xv = (e2 - e1) * inv;
                _vx = r1 * r2 * (e1 - e2) * inv;
                _vv = (r2 * e2 - r1 * e1) * inv;
            }
        }

        public void Apply(ref float value, ref float velocity, float target)
        {
            if (_snap)
            {
                value = target;
                velocity = 0f;
                return;
            }

            float x = value - target;
            float v = velocity;
            value = target + _xx * x + _xv * v;
            velocity = _vx * x + _vv * v;
        }

        public void Apply(ref Vector3 value, ref Vector3 velocity, Vector3 target)
        {
            Apply(ref value.x, ref velocity.x, target.x);
            Apply(ref value.y, ref velocity.y, target.y);
            Apply(ref value.z, ref velocity.z, target.z);
        }
    }

    /// <summary>A value that chases its target through a spring.</summary>
    public struct FloatSpring
    {
        public float Value;
        public float Velocity;

        public float Step(float target, SpringSettings settings, float deltaTime)
        {
            new SpringStep(settings, deltaTime).Apply(ref Value, ref Velocity, target);
            return Value;
        }

        /// <summary>Chases an angle in degrees the short way round, never spinning through 360.</summary>
        public float StepAngle(float target, SpringSettings settings, float deltaTime) =>
            Step(Value + Mathf.DeltaAngle(Value, target), settings, deltaTime);

        public void Reset(float value)
        {
            Value = value;
            Velocity = 0f;
        }
    }

    public struct Vector3Spring
    {
        public Vector3 Value;
        public Vector3 Velocity;

        public Vector3 Step(Vector3 target, SpringSettings settings, float deltaTime)
        {
            new SpringStep(settings, deltaTime).Apply(ref Value, ref Velocity, target);
            return Value;
        }

        public void Reset(Vector3 value)
        {
            Value = value;
            Velocity = Vector3.zero;
        }
    }
}
