using System.Collections.Generic;
using Unity.Profiling;
using UnityEngine;

namespace Rubickanov.Characters
{
    public enum FlailState
    {
        Off,
        Flailing,
        Easing,
    }

    /// <summary>The flail clip a flying body follows, by how its pelvis lies against gravity.</summary>
    public enum FlailClip
    {
        None = -1,
        HeadFirst = 0,
        AlongFall = 1,
        FeetFirst = 2,
    }

    /// <summary>
    /// A body in the air after a blow or a fall is not a sack: its muscles reach, with moderate strength, for a flail
    /// clip's pose (feet first, along the fall or head first, as the pelvis lies against gravity, chosen again as it
    /// turns). On the first touch they ease to the limp drive and its targets, the arms last; in water they let go at
    /// once. Only the joints are driven: nothing pushes the body as a whole. The clip plays on the character's
    /// animation, which <see cref="Ragdoll.SamplePose"/> reads; this sets the joints' slerp drives every physics
    /// step. Owned by <see cref="Ragdoll"/>, which creates it only when the config's Flail is on.
    /// </summary>
    public sealed class FlailReflex
    {
        private static readonly ProfilerMarker StepMarker = new("FlailReflex.Step");

        private readonly Ragdoll _ragdoll;
        private readonly RagdollConfig _config;
        private readonly IReadOnlyList<RagdollBone> _bones;
        private readonly int[] _parents;
        private float[] _inertia;
        private readonly float[] _peak;
        private readonly bool[] _arm;

        // Each joint's drive as last set while flailing, what the ease starts from, and the ease's goal.
        private readonly Muscle[] _last;
        private readonly Muscle[] _from;
        private readonly Quaternion[] _goal;

        private float _time;
        private float _sinceChoice;

        public FlailReflex(Ragdoll ragdoll, RagdollConfig config, IReadOnlyList<RagdollBone> bones, int[] parents,
            float mass)
        {
            _ragdoll = ragdoll;
            _config = config;
            _bones = bones;
            _parents = parents;
            int n = bones.Count;
            _peak = new float[n];
            _arm = new bool[n];
            _last = new Muscle[n];
            _from = new Muscle[n];
            _goal = new Quaternion[n];
            float scale = mass / Mathf.Max(1e-3f, config.FlailPeakMass);
            for (int i = 1; i < n; i++)
            {
                _peak[i] = i - 1 < config.FlailPeakTorque.Length ? config.FlailPeakTorque[i - 1] * scale : 0f;
                _arm[i] = IsArm(i, parents);
            }

            Clip = FlailClip.None;
        }

        public FlailState State { get; private set; }

        /// <summary>The flail clip being followed, or None.</summary>
        public FlailClip Clip { get; private set; }

        /// <summary>Counts the flights started and the clips chosen, so the clip can be played again from its start.</summary>
        public int Choices { get; private set; }

        /// <summary>Counts the flights started: each plays its clip from the start with no fade.</summary>
        public int Flights { get; private set; }

        /// <summary>How long the body has been in the air, s (0 while it touches something).</summary>
        public float AirTime { get; private set; }

        /// <summary>The summed force of the last step's contacts with anything not the character's own, N.</summary>
        public float Pressing { get; private set; }

        /// <summary>Each joint's inertia in the air (by bone, 0 for the hips), kg·m²; null until the bones were on.</summary>
        public IReadOnlyList<float> Inertia => _inertia;

        /// <summary>
        /// Works out the joints' inertia once, with the bones on: an inactive body reports no inertia tensor or centre
        /// of mass.
        /// </summary>
        public void Bind() => _inertia ??= BindInertia(_bones, _parents);

        /// <summary>Whether the joints' drives are the reflex's, so the limp fade waits.</summary>
        public bool HoldsMuscles => State != FlailState.Off;

        /// <summary>Back to the passive body, ready to flail on the next flight; the drives are left as they are.</summary>
        public void Reset()
        {
            State = FlailState.Off;
            Clip = FlailClip.None;
            AirTime = 0f;
        }

        /// <summary>
        /// Drives for the next step from what the last step's contacts pressed, N. <paramref name="resting"/>: the
        /// body sleeps, so it lies on something although no contact reports it. True while the reflex holds the
        /// muscles; false once it has let go, <paramref name="ended"/> on the step it let go after easing.
        /// Ported from Sinew Control/FlailReflex.cs Step.
        /// </summary>
        public bool Step(float pressing, bool resting, float dt, out bool ended)
        {
            using ProfilerMarker.AutoScope scope = StepMarker.Auto();
            ended = false;
            Pressing = pressing;
            bool touching = resting || pressing > _config.FlailTouchForce;
            AirTime = touching ? 0f : AirTime + dt;
            switch (State)
            {
                case FlailState.Flailing when touching:
                    BeginEase();
                    break;
                case FlailState.Off when AirTime >= _config.FlailAirTime:
                case FlailState.Easing when AirTime >= _config.FlailAgainTime:
                    BeginFlail();
                    break;
            }

            switch (State)
            {
                case FlailState.Flailing:
                    Flail(dt);
                    return true;
                case FlailState.Easing:
                    if (Ease(dt))
                    {
                        return true;
                    }

                    State = FlailState.Off;
                    Clip = FlailClip.None;
                    ended = true;
                    return false;
                default:
                    return false;
            }
        }

        // The pelvis's up against the world's: feet first above Upright, head first below Inverted, else along the fall.
        private FlailClip Choose()
        {
            float up = (_ragdoll.Facing(0) * Vector3.up).y;
            return up > _config.FlailUpright ? FlailClip.FeetFirst
                : up < _config.FlailInverted ? FlailClip.HeadFirst
                : FlailClip.AlongFall;
        }

        private void BeginFlail()
        {
            State = FlailState.Flailing;
            Clip = Choose();
            Choices++;
            Flights++;
            _time = 0f;
            _sinceChoice = 0f;
        }

        // The clip itself plays and blends on the character's animation; here the joints reach for the pose read last.
        private void Flail(float dt)
        {
            _time += dt;
            _sinceChoice += dt;
            if (_sinceChoice >= _config.FlailChooseTime)
            {
                _sinceChoice = 0f;
                FlailClip chosen = Choose();
                if (chosen != Clip)
                {
                    Clip = chosen;
                    Choices++;
                }
            }

            float ramp = SmoothStep(_time, _config.FlailRampTime);
            for (int i = 1; i < _peak.Length; i++)
            {
                float ceiling = _config.FlailStrength * _peak[i];
                if (ceiling <= 0f || !_ragdoll.HasJoint(i))
                {
                    continue;
                }

                float inertia = _inertia[i];
                float stiffness = Stiffness(inertia, _config.FlailHz, ceiling, _config.FlailSoftestDegrees,
                    _config.FlailStiffestDegrees);
                // Target spin stays 0: the joint space's spin convention is unproven, the damper pulls to rest instead.
                var muscle = new Muscle
                {
                    Target = _ragdoll.PoseTarget(i),
                    Stiffness = ramp * stiffness,
                    Damping = CriticalDamping(stiffness, inertia, ramp),
                    MaxTorque = ramp * ceiling,
                };
                _last[i] = muscle;
                _ragdoll.SetMuscle(i, muscle.Stiffness, muscle.Damping, muscle.MaxTorque, muscle.Target);
            }
        }

        private void BeginEase()
        {
            State = FlailState.Easing;
            _time = 0f;
            for (int i = 1; i < _peak.Length; i++)
            {
                _from[i] = _config.FlailStrength * _peak[i] > 0f
                    ? _last[i]
                    : new Muscle { Target = _ragdoll.HoldTarget(i) };
                _goal[i] = _arm[i] ? _from[i].Target : _ragdoll.HoldTarget(i);
            }
        }

        // From the flail drive to the limp one: Sinew Control/ToneFade.cs, with the limp drive and its targets (the
        // pose the body fell in) for Sinew's tone and bind pose. True while still easing.
        private bool Ease(float dt)
        {
            float t = Mathf.Clamp01(_time / Mathf.Max(1e-6f, _config.FlailEaseTime));
            float s = t * t * (3f - 2f * t);
            float armShare = SmoothStep(_time, _config.FlailArmsTime);
            float tone = _config.Spring * _config.LimpMuscleWeight;
            float toneDamper = _config.Damper * _config.LimpMuscleWeight + _config.LimpDamper;
            for (int i = 1; i < _peak.Length; i++)
            {
                if (!_ragdoll.HasJoint(i))
                {
                    continue;
                }

                Muscle from = _from[i];
                Quaternion goal = _goal[i];
                if (_arm[i] && armShare > 0f)
                {
                    goal = Quaternion.Slerp(goal, _ragdoll.HoldTarget(i), armShare).normalized;
                }

                float stiffness = from.Stiffness > 0f && tone > 0f
                    ? Mathf.Exp(Mathf.Lerp(Mathf.Log(from.Stiffness), Mathf.Log(tone), s))
                    : Mathf.Lerp(from.Stiffness, tone, s);
                // Quaternion.Slerp takes the short way, as the job's sign flip does.
                _ragdoll.SetMuscle(i, stiffness, Mathf.Lerp(from.Damping, toneDamper, s),
                    Mathf.Lerp(from.MaxTorque, _peak[i], s), Quaternion.Slerp(from.Target, goal, s).normalized);
            }

            _time += dt;
            return _time < Mathf.Max(_config.FlailEaseTime, _config.FlailArmsTime);
        }

        // 3u² − 2u³ for u = value / span, clamped to [0, 1]. Sinew Control/ControlMath.cs SmoothStep.
        private static float SmoothStep(float value, float span)
        {
            double u = System.Math.Clamp(value / System.Math.Max(1e-6, span), 0.0, 1.0);
            return (float)(u * u * (3.0 - 2.0 * u));
        }

        /// <summary>
        /// A muscle's stiffness for natural frequency <paramref name="hz"/> on <paramref name="inertia"/>, I·ω², kept
        /// between the ceiling over the softest and over the stiffest angle (degrees). Sinew ControlMath.Stiffness.
        /// </summary>
        public static float Stiffness(float inertia, float hz, float ceiling, float softestDegrees, float stiffestDegrees)
        {
            double omega = 2.0 * System.Math.PI * hz;
            double soft = ceiling / (softestDegrees * System.Math.PI / 180.0);
            double stiff = ceiling / (stiffestDegrees * System.Math.PI / 180.0);
            return (float)System.Math.Clamp(inertia * omega * omega, soft, System.Math.Max(soft, stiff));
        }

        /// <summary>Critical damping 2√(k·I), times <paramref name="scale"/>. Sinew ControlMath.CriticalDamping.</summary>
        public static float CriticalDamping(float stiffness, float inertia, float scale) =>
            (float)(scale * 2.0 * System.Math.Sqrt((double)stiffness * inertia));

        /// <summary>
        /// Per bone, the inertia its joint turns against in the rest pose: each side's bodies about the joint's
        /// anchor (half the trace of each body's inertia plus m·d²), the two sides in series. Sinew
        /// Control/BindInertia.cs InSeries. 0 for the hips.
        /// </summary>
        public static float[] BindInertia(IReadOnlyList<RagdollBone> bones, int[] parents)
        {
            int n = bones.Count;
            var result = new float[n];
            for (int j = 1; j < n; j++)
            {
                ConfigurableJoint joint = bones[j].Joint;
                if (joint == null)
                {
                    continue;
                }

                Vector3 anchor = RestPoint(bones[j], joint.anchor);
                double child = 0.0;
                double parent = 0.0;
                for (int b = 0; b < n; b++)
                {
                    Rigidbody body = bones[b].Body;
                    Vector3 tensor = body.inertiaTensor;
                    double own = 0.5 * ((double)tensor.x + tensor.y + tensor.z);
                    Vector3 d = RestPoint(bones[b], body.centerOfMass) - anchor;
                    double about = own + body.mass * (double)d.sqrMagnitude;
                    if (Descends(b, j, parents))
                    {
                        child += about;
                    }
                    else
                    {
                        parent += about;
                    }
                }

                result[j] = child + parent > 0.0 ? (float)(child * parent / (child + parent)) : 0f;
            }

            return result;
        }

        // A point in the bone's own space, in the root's space as the bone was built.
        private static Vector3 RestPoint(RagdollBone bone, Vector3 local) =>
            bone.RestPosition + bone.RestRotation * Vector3.Scale(bone.transform.lossyScale, local);

        private static bool Descends(int bone, int ancestor, int[] parents)
        {
            for (int at = bone; at >= 0; at = parents[at])
            {
                if (at == ancestor)
                {
                    return true;
                }
            }

            return false;
        }

        // An arm hangs from the chest and is not the head. Sinew BindInertia.IsArm.
        private static bool IsArm(int bone, int[] parents) =>
            bone != Ragdoll.HeadIndex && bone != Ragdoll.ChestIndex && Descends(bone, Ragdoll.ChestIndex, parents);

        private struct Muscle
        {
            public Quaternion Target;
            public float Stiffness;
            public float Damping;
            public float MaxTorque;
        }
    }
}
