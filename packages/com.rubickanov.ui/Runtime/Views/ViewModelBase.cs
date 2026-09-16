using System;
using R3;

namespace Rubickanov.UI
{
    public abstract class ViewModelBase : IDisposable
    {
        private DisposableBag _disposables;
        private bool _disposed;

        public void Dispose()
        {
            if (_disposed) return;
            _disposed = true;
            OnDispose();
            _disposables.Dispose();
        }

        protected virtual void OnDispose() { }

        protected void AddDisposable(IDisposable disposable)
        {
            disposable.AddTo(ref _disposables);
        }

        /// <summary>
        /// Tracks a disposable for cleanup when the ViewModel is disposed.
        /// Public wrapper for use by extension methods in bridge packages.
        /// </summary>
        public void TrackDisposable(IDisposable disposable)
        {
            disposable.AddTo(ref _disposables);
        }

        protected ReactiveProperty<T> CreateProperty<T>(T initialValue = default!)
        {
            var property = new ReactiveProperty<T>(initialValue);
            property.AddTo(ref _disposables);
            return property;
        }

        protected ReactiveCommand CreateCommand(Action? onExecute = null)
        {
            var command = new ReactiveCommand();
            if (onExecute != null)
                command.Subscribe(onExecute, static (_, action) => action()).AddTo(ref _disposables);
            command.AddTo(ref _disposables);
            return command;
        }

        protected ReactiveCommand<T> CreateCommand<T>(Action<T>? onExecute = null)
        {
            var command = new ReactiveCommand<T>();
            if (onExecute != null)
                command.Subscribe(onExecute, static (value, action) => action(value)).AddTo(ref _disposables);
            command.AddTo(ref _disposables);
            return command;
        }

        protected Subject<T> CreateSubject<T>()
        {
            var subject = new Subject<T>();
            subject.AddTo(ref _disposables);
            return subject;
        }
    }
}
