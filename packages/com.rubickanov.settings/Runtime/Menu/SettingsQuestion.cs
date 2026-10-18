using System;

namespace Rubickanov.Settings
{
    /// <summary>
    /// A question the menu asks over its rows: left and right pick the answer, Enter gives it, Back says no. With
    /// <see cref="Seconds"/> it says no by itself when they run out, and its text's {0} is the seconds left.
    /// </summary>
    public sealed class SettingsQuestion
    {
        public SettingsQuestion(string textKey, string yesKey, string noKey, Action<bool> answered,
            bool startOnNo = false, float seconds = 0f, params object[] args)
        {
            TextKey = textKey ?? throw new ArgumentNullException(nameof(textKey));
            YesKey = yesKey ?? throw new ArgumentNullException(nameof(yesKey));
            NoKey = noKey ?? throw new ArgumentNullException(nameof(noKey));
            Answered = answered ?? throw new ArgumentNullException(nameof(answered));
            Choice = startOnNo ? 1 : 0;
            Seconds = seconds;
            Args = args ?? Array.Empty<object>();
        }

        public string TextKey { get; }

        public string YesKey { get; }

        public string NoKey { get; }

        public object[] Args { get; }

        public float Seconds { get; }

        /// <summary>The picked answer: 0 yes, 1 no.</summary>
        public int Choice { get; internal set; }

        internal Action<bool> Answered { get; }
    }
}
