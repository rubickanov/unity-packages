using System;

namespace Rubickanov.UI
{
    public sealed class NullSpinnerHost : ISpinnerHost
    {
        public IDisposable Show(string? label = null) => NoOpDisposable.Instance;

        private sealed class NoOpDisposable : IDisposable
        {
            public static readonly NoOpDisposable Instance = new();
            public void Dispose() { }
        }
    }
}
