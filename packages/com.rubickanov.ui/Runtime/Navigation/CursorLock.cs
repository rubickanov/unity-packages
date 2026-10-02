using System;
using R3;
using UnityEngine;

namespace Rubickanov.UI
{
    /// <summary>
    /// The one writer of the hardware cursor: locked to the view and hidden while something looks with the mouse
    /// (<see cref="Lock"/>) and no UI holds a pointer capture (<see cref="IUIService.PointerCaptured"/>), free and
    /// visible otherwise. A console, a menu of the game's own or a "free the cursor" toggle frees it by taking a pointer
    /// capture. Written again when the window gets its focus back: the OS takes the lock away while it is out of focus.
    /// </summary>
    public sealed class CursorLock : IDisposable
    {
        private readonly Action<bool> _write;
        private readonly IDisposable _capture;
        private int _locks;
        private bool _captured;
        private bool _disposed;

        public CursorLock(IUIService ui) : this(ui, WriteCursor)
        {
        }

        /// <param name="write">Applies the state: true locked and hidden, false free and visible.</param>
        internal CursorLock(IUIService ui, Action<bool> write)
        {
            if (ui == null) throw new ArgumentNullException(nameof(ui));
            _write = write;
            _captured = ui.PointerCaptured.CurrentValue;
            _capture = ui.PointerCaptured.Subscribe(OnCaptured);
            Application.focusChanged += OnFocusChanged;
            // Written at once: what the engine shows at start is not taken on trust.
            Apply();
        }

        /// <summary>Whether the cursor is locked now.</summary>
        public bool IsLocked { get; private set; }

        /// <summary>
        /// Asks for the cursor locked, as a camera that looks with the mouse does, until the handle is disposed. Counted.
        /// </summary>
        public IDisposable Lock()
        {
            if (_disposed) throw new ObjectDisposedException(nameof(CursorLock));

            _locks++;
            Apply();
            return new LockHandle(this);
        }

        public void Dispose()
        {
            if (_disposed) return;

            _disposed = true;
            _capture.Dispose();
            Application.focusChanged -= OnFocusChanged;
            _locks = 0;
            IsLocked = false;
            _write(false);
        }

        private void Release()
        {
            if (_disposed) return;

            _locks--;
            Apply();
        }

        private void OnCaptured(bool captured)
        {
            if (captured == _captured) return;
            _captured = captured;
            Apply();
        }

        private void OnFocusChanged(bool focused)
        {
            if (focused && !_disposed) Apply();
        }

        private void Apply()
        {
            IsLocked = _locks > 0 && !_captured;
            _write(IsLocked);
        }

        // The lock first: on some platforms leaving Locked shows the cursor, so visibility goes after it.
        private static void WriteCursor(bool locked)
        {
            Cursor.lockState = locked ? CursorLockMode.Locked : CursorLockMode.None;
            Cursor.visible = !locked;
        }

        private sealed class LockHandle : IDisposable
        {
            private CursorLock? _owner;

            public LockHandle(CursorLock owner) => _owner = owner;

            public void Dispose()
            {
                var owner = _owner;
                _owner = null;
                owner?.Release();
            }
        }
    }
}
