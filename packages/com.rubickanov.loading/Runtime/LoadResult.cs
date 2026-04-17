using System;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Outcome of a <see cref="ILoadingService.Load"/> call.
    /// </summary>
    public enum LoadStatus
    {
        /// <summary>All operations completed and deferred activations ran.</summary>
        Ok,

        /// <summary>An operation threw. See <see cref="LoadResult.Error"/>.</summary>
        Failed,

        /// <summary>Caller cancelled via the <c>CancellationToken</c> passed to <see cref="ILoadingService.Load"/>.</summary>
        Cancelled,
    }

    /// <summary>
    /// Result of a loading pipeline execution.
    /// </summary>
    public readonly struct LoadResult
    {
        public LoadStatus Status { get; }
        public Exception? Error { get; }

        public bool Success => Status == LoadStatus.Ok;
        public bool Cancelled => Status == LoadStatus.Cancelled;

        private LoadResult(LoadStatus status, Exception? error = null)
        {
            Status = status;
            Error = error;
        }

        public static LoadResult Ok => new(LoadStatus.Ok);
        public static LoadResult Cancel => new(LoadStatus.Cancelled);
        public static LoadResult Fail(Exception ex) => new(LoadStatus.Failed, ex);

        public override string ToString() => Status switch
        {
            LoadStatus.Ok => "LoadResult(Ok)",
            LoadStatus.Cancelled => "LoadResult(Cancelled)",
            LoadStatus.Failed => $"LoadResult(Failed: {Error?.GetType().Name}: {Error?.Message})",
            _ => $"LoadResult({Status})",
        };
    }
}
