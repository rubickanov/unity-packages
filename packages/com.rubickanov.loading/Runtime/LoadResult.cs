using System;

namespace Rubickanov.Loading
{
    /// <summary>
    /// Result of a loading pipeline execution.
    /// </summary>
    public readonly struct LoadResult
    {
        public bool Success { get; }
        public Exception? Error { get; }

        private LoadResult(bool success, Exception? error = null)
        {
            Success = success;
            Error = error;
        }

        public static LoadResult Ok => new(true);
        public static LoadResult Fail(Exception ex) => new(false, ex);
    }
}
