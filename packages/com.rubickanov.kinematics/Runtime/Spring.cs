using System;
using Unity.Mathematics;

namespace Rubickanov.Kinematics
{
    /// <summary>How a spring follows its target: how fast, and whether it overshoots.</summary>
    [Serializable]
    public struct SpringSettings
    {
        /// <summary>Oscillations per second. Higher follows faster; 0 follows at once.</summary>
        public double Frequency;

        /// <summary>1 settles without overshoot, below 1 overshoots and wobbles, above 1 is sluggish.</summary>
        public double Damping;

        public SpringSettings(double frequency, double damping)
        {
            Frequency = frequency;
            Damping = damping;
        }
    }

    /// <summary>
    /// A damped harmonic oscillator solved exactly over the step, so it stays stable at any step length. One step is
    /// a linear map of (offset from target, velocity), the same for every axis of a vector.
    /// </summary>
    public readonly struct SpringStep
    {
        private readonly double _xx, _xv, _vx, _vv;
        private readonly bool _snap;

        public SpringStep(SpringSettings settings, double deltaTime)
        {
            _xx = _xv = _vx = _vv = 0.0;
            double omega = 2.0 * DetMath.Pi * settings.Frequency;
            _snap = !(omega > 0.0);
            if (_snap || !(deltaTime > 0.0))
            {
                _xx = _vv = 1.0;
                return;
            }

            double zeta = settings.Damping;
            if (math.abs(zeta - 1.0) < 1e-4)
            {
                // Critical: x(t) = e^(−ωt) (x0 + (v0 + ω x0) t).
                double e = DetMath.Exp(-omega * deltaTime);
                _xx = e * (1.0 + omega * deltaTime);
                _xv = e * deltaTime;
                _vx = -e * omega * omega * deltaTime;
                _vv = e * (1.0 - omega * deltaTime);
            }
            else if (zeta < 1.0)
            {
                double omegaD = omega * math.sqrt(1.0 - zeta * zeta);
                double e = DetMath.Exp(-zeta * omega * deltaTime);
                DetMath.SinCos(omegaD * deltaTime, out double sin, out double cos);
                _xx = e * (cos + zeta * omega / omegaD * sin);
                _xv = e * sin / omegaD;
                _vx = -e * omega * omega / omegaD * sin;
                _vv = e * (cos - zeta * omega / omegaD * sin);
            }
            else
            {
                double root = omega * math.sqrt(zeta * zeta - 1.0);
                double r1 = -zeta * omega + root;
                double r2 = -zeta * omega - root;
                double e1 = DetMath.Exp(r1 * deltaTime);
                double e2 = DetMath.Exp(r2 * deltaTime);
                double inv = 1.0 / (r2 - r1);
                _xx = (r2 * e1 - r1 * e2) * inv;
                _xv = (e2 - e1) * inv;
                _vx = r1 * r2 * (e1 - e2) * inv;
                _vv = (r2 * e2 - r1 * e1) * inv;
            }
        }

        public void Apply(ref double value, ref double velocity, double target)
        {
            if (_snap)
            {
                value = target;
                velocity = 0.0;
                return;
            }

            double x = value - target;
            double v = velocity;
            value = target + _xx * x + _xv * v;
            velocity = _vx * x + _vv * v;
        }

        public void Apply(ref double3 value, ref double3 velocity, double3 target)
        {
            Apply(ref value.x, ref velocity.x, target.x);
            Apply(ref value.y, ref velocity.y, target.y);
            Apply(ref value.z, ref velocity.z, target.z);
        }
    }

    /// <summary>A number that chases its target through a spring.</summary>
    public struct ScalarSpring
    {
        public double Value;
        public double Velocity;

        public double Step(double target, SpringSettings settings, double deltaTime)
        {
            new SpringStep(settings, deltaTime).Apply(ref Value, ref Velocity, target);
            return Value;
        }

        /// <summary>Chases an angle in degrees the short way round, never spinning through 360.</summary>
        public double StepAngle(double target, SpringSettings settings, double deltaTime) =>
            Step(Value + DetMath.DeltaAngle(Value, target), settings, deltaTime);

        public void Reset(double value)
        {
            Value = value;
            Velocity = 0.0;
        }
    }

    /// <summary>A point or a direction that chases its target through a spring, each axis alike.</summary>
    public struct VectorSpring
    {
        public double3 Value;
        public double3 Velocity;

        public double3 Step(double3 target, SpringSettings settings, double deltaTime)
        {
            new SpringStep(settings, deltaTime).Apply(ref Value, ref Velocity, target);
            return Value;
        }

        public void Reset(double3 value)
        {
            Value = value;
            Velocity = double3.zero;
        }
    }
}
