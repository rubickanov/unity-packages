using System.Collections.Generic;

namespace Rubickanov.Report
{
    /// <summary>
    /// Lets each kind of error through once a session and no more than <see cref="Cap"/> kinds in all, so an error in
    /// Update does not spend the month's quota; feedback has a cap of its own. Safe from any thread.
    /// </summary>
    public sealed class OnceASession
    {
        public enum Verdict
        {
            New,
            Seen,
            OverCap,
        }

        private readonly object _lock = new();
        private readonly HashSet<string> _seen = new();
        private int _feedbacks;

        public OnceASession(int cap, int feedbackCap)
        {
            Cap = cap;
            FeedbackCap = feedbackCap;
        }

        public int Cap { get; }

        public int FeedbackCap { get; }

        /// <summary>Kinds let through so far.</summary>
        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _seen.Count;
                }
            }
        }

        /// <summary>Whether an error of this fingerprint goes; a <see cref="Verdict.New"/> one is counted.</summary>
        public Verdict Check(string fingerprint)
        {
            lock (_lock)
            {
                if (_seen.Contains(fingerprint))
                {
                    return Verdict.Seen;
                }

                if (_seen.Count >= Cap)
                {
                    return Verdict.OverCap;
                }

                _seen.Add(fingerprint);
                return Verdict.New;
            }
        }

        /// <summary>Whether one more feedback may go; counts it if so.</summary>
        public bool TakeFeedback()
        {
            lock (_lock)
            {
                if (_feedbacks >= FeedbackCap)
                {
                    return false;
                }

                _feedbacks++;
                return true;
            }
        }
    }
}
