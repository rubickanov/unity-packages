using System;

namespace Rubickanov.UI
{
    /// <summary>How a stat's value reads against its limits: plain without limits, good, worth a look, or bad.</summary>
    public enum StatLevel
    {
        Plain,
        Good,
        Warn,
        Bad,
    }

    /// <summary>
    /// Where a stat's value turns from good to worth a look and to bad. Which way is better is the stat's: fewer
    /// milliseconds of ping, more frames a second. A value on a limit is already past it. The default has no limits and
    /// reads every value as <see cref="StatLevel.Plain"/>.
    /// </summary>
    public readonly struct StatLimits
    {
        private readonly double _warn;
        private readonly double _bad;
        private readonly bool _lowerIsBetter;

        private StatLimits(double warn, double bad, bool lowerIsBetter)
        {
            _warn = warn;
            _bad = bad;
            _lowerIsBetter = lowerIsBetter;
            IsSet = true;
        }

        public static StatLimits None => default;

        public bool IsSet { get; }

        /// <summary>Good below <paramref name="warn"/>, bad from <paramref name="bad"/> up: ping, frame time, loss.</summary>
        public static StatLimits LowerIsBetter(double warn, double bad)
        {
            if (!(warn <= bad)) throw new ArgumentException($"Warn {warn} must not be above bad {bad} when lower is better.");
            return new StatLimits(warn, bad, true);
        }

        /// <summary>Good above <paramref name="warn"/>, bad from <paramref name="bad"/> down: frames a second.</summary>
        public static StatLimits HigherIsBetter(double warn, double bad)
        {
            if (!(warn >= bad)) throw new ArgumentException($"Warn {warn} must not be below bad {bad} when higher is better.");
            return new StatLimits(warn, bad, false);
        }

        /// <summary>The level of <paramref name="value"/>; plain without limits or for NaN, a value not known yet.</summary>
        public StatLevel Judge(double value)
        {
            if (!IsSet || double.IsNaN(value))
            {
                return StatLevel.Plain;
            }

            if (_lowerIsBetter)
            {
                return value >= _bad ? StatLevel.Bad : value >= _warn ? StatLevel.Warn : StatLevel.Good;
            }

            return value <= _bad ? StatLevel.Bad : value <= _warn ? StatLevel.Warn : StatLevel.Good;
        }
    }
}
