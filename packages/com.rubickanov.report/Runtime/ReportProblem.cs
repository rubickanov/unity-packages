using Sentry;

namespace Rubickanov.Report
{
    /// <summary>What kind of thing broke.</summary>
    public enum ProblemKind
    {
        Exception,
        Error,
        Hang,
    }

    /// <summary>One problem that went to Sentry: its event, its kind, a short title.</summary>
    public readonly struct ReportProblem
    {
        public ReportProblem(SentryId id, ProblemKind kind, string title)
        {
            Id = id;
            Kind = kind;
            Title = title;
        }

        public SentryId Id { get; }

        public ProblemKind Kind { get; }

        public string Title { get; }

        public override string ToString() => $"{Kind} {Id}: {Title}";
    }

    /// <summary>What became of a feedback.</summary>
    public enum SendResult
    {
        /// <summary>Delivered.</summary>
        Sent,

        /// <summary>In the SDK's cache on disk: it goes on a later start.</summary>
        Queued,

        /// <summary>Reports are off.</summary>
        Off,

        /// <summary>The SDK did not take it, or the session's cap is reached.</summary>
        Refused,
    }
}
