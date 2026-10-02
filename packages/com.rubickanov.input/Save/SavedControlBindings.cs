using System;
using Cysharp.Threading.Tasks;
using R3;
using Rubickanov.Save;
using UnityEngine;

namespace Rubickanov.Input
{
    /// <summary>
    /// Keeps the player's keys (<see cref="ControlBindings"/>) in the save store as <c>controls.json</c>: read at
    /// construction, at once on desktop, and written whole after each change. Text the keys cannot be read from is kept
    /// aside as <c>controls.broken.json</c> and the defaults stay. Resolve it when the container is built, before anything
    /// names a key. One per app.
    /// </summary>
    public sealed class SavedControlBindings : IDisposable
    {
        private readonly ControlBindings _bindings;
        private readonly SaveDocument _file;
        private readonly IDisposable _changed;

        public SavedControlBindings(ControlBindings bindings, ISaveStore store)
        {
            _bindings = bindings ?? throw new ArgumentNullException(nameof(bindings));
            _file = new SaveDocument(store ?? throw new ArgumentNullException(nameof(store)), Key, LogError);
            _file.ReadAsync(Take).Forget();
            _changed = bindings.Changed.Subscribe(_ => _file.WriteAsync(bindings.ToJson).Forget());
        }

        /// <summary><c>controls.json</c>, at the store's root beside the other small documents.</summary>
        public static SaveKey Key { get; } = new(SaveArea.AtRoot("Controls", "controls"), "controls.json");

        /// <summary>Where the keys are kept, for logs and a console.</summary>
        public string Where => _file.Where;

        public void Dispose() => _changed.Dispose();

        private void Take(string text)
        {
            if (text == null)
            {
                return;
            }

            try
            {
                _bindings.LoadJson(text, Where);
            }
            catch (FormatException e)
            {
                LogError($"{Where} could not be read, the keys stay the defaults; the file is kept as " +
                         $"{_file.BrokenWhere}: {e.Message}");
                _file.KeepBrokenAsync(text).Forget();
            }
        }

        private static void LogError(string message) => Debug.LogError("[Input] " + message);
    }
}
