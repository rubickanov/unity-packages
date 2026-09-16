using System;
using System.Collections.Generic;
using Cysharp.Threading.Tasks;

namespace Rubickanov.UI
{
    public class ScopedViewRegistration : IDisposable
    {
        private readonly IUIService _ui;
        private readonly List<Action> _cleanupActions = new();
        private bool _disposed;

        public ScopedViewRegistration(IUIService ui) => _ui = ui;

        /// <summary>
        /// Registers the view and unregisters it when the scope is disposed. Disposing the scope while the view is
        /// still loading cancels the registration: this call then throws <see cref="OperationCanceledException"/>.
        /// </summary>
        /// <exception cref="ObjectDisposedException">The scope is already disposed.</exception>
        public async UniTask Register<T>() where T : View
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(ScopedViewRegistration),
                    $"Cannot register view {typeof(T).Name}: the scope is disposed.");

            Action cleanup = () => _ui.Unregister<T>();
            _cleanupActions.Add(cleanup);
            try
            {
                await _ui.Register<T>();
            }
            catch (Exception ex) when (ex is not OperationCanceledException)
            {
                // Not ours to clean up: the type may be registered by someone else.
                _cleanupActions.Remove(cleanup);
                throw;
            }
        }

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;

            List<Exception>? errors = null;
            for (int i = _cleanupActions.Count - 1; i >= 0; i--)
            {
                try { _cleanupActions[i](); }
                catch (Exception ex) { (errors ??= new List<Exception>()).Add(ex); }
            }
            _cleanupActions.Clear();

            if (errors != null)
                throw new AggregateException(errors);
        }
    }
}
