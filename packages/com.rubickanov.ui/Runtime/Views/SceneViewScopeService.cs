using System;

namespace Rubickanov.UI
{
    public class SceneViewScopeService : IDisposable
    {
        private readonly IUIService _ui;
        private ScopedViewRegistration? _current;

        public bool HasActiveScope => _current != null;

        public SceneViewScopeService(IUIService ui) => _ui = ui;

        public ScopedViewRegistration Begin()
        {
            _current?.Dispose();
            _current = new ScopedViewRegistration(_ui);
            return _current;
        }

        public void Dispose()
        {
            _current?.Dispose();
            _current = null;
        }
    }
}
