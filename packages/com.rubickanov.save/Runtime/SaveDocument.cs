using System;
using Cysharp.Threading.Tasks;
using UnityEngine;

namespace Rubickanov.Save
{
    /// <summary>
    /// One small document of the player's kept whole as text under one key, as the settings, the progress and the keys
    /// are: read once, then written whole after each change. Reads and writes take turns, the read first, and a write
    /// takes its text when its turn comes, so what is kept is always the owner's latest and never anything made before
    /// the document was read. Text its owner cannot understand is kept aside under <see cref="BrokenKey"/>
    /// (<c>progress.broken.json</c> beside <c>progress.json</c>) before it is written over. The store's failures are
    /// reported, not thrown.
    /// </summary>
    public sealed class SaveDocument
    {
        private const string BrokenEnd = ".broken.json";

        private readonly ISaveStore _store;
        private readonly Action<string> _logError;
        private UniTask _turn = UniTask.CompletedTask;

        /// <param name="logError">
        /// Where the store's failures go, as one line naming the document and what failed: the owner's own log channel.
        /// Unity's error log when null.
        /// </param>
        public SaveDocument(ISaveStore store, SaveKey key, Action<string> logError = null)
        {
            _store = store;
            _logError = logError ?? Debug.LogError;
            Key = key;
            BrokenKey = new SaveKey(key.Area, BrokenName(key.Name));
        }

        public SaveKey Key { get; }

        /// <summary>Where text that could not be understood is kept aside.</summary>
        public SaveKey BrokenKey { get; }

        /// <summary>Where the document is kept, for logs and the console.</summary>
        public string Where => _store.Describe(Key);

        /// <summary>Where text that could not be understood is kept, for logs.</summary>
        public string BrokenWhere => _store.Describe(BrokenKey);

        /// <summary>
        /// Reads the document and hands its text to <paramref name="take"/>, null when there is none or the store could
        /// not read it; no write comes before <paramref name="take"/> is done.
        /// </summary>
        public UniTask ReadAsync(Action<string> take) => InTurn(async () =>
        {
            string text = null;
            try
            {
                text = await _store.ReadTextAsync(Key);
            }
            catch (SaveStoreException e)
            {
                _logError($"{Where} could not be read: {e.Message}");
            }

            take(text);
        });

        /// <summary>Keeps <paramref name="text"/>, which the owner could not understand, aside under <see cref="BrokenKey"/>.</summary>
        public UniTask KeepBrokenAsync(string text) => InTurn(async () =>
        {
            try
            {
                await _store.WriteTextAsync(BrokenKey, text);
            }
            catch (SaveStoreException e)
            {
                _logError($"{Where} could not be kept as {BrokenWhere}: {e.Message}");
            }
        });

        /// <summary>
        /// Writes the text <paramref name="text"/> gives when its turn comes; null from it writes nothing. Comes back with
        /// the text written, or null when nothing was or the store could not.
        /// </summary>
        public async UniTask<string> WriteAsync(Func<string> text)
        {
            string written = null;
            await InTurn(async () =>
            {
                string now = text();
                if (now == null)
                {
                    return;
                }

                try
                {
                    await _store.WriteTextAsync(Key, now);
                    written = now;
                }
                catch (SaveStoreException e)
                {
                    _logError($"{Where} could not be written: {e.Message}");
                }
            });
            return written;
        }

        // settings.json's is settings.broken.json, beside it.
        private static string BrokenName(string name)
        {
            int dot = name.LastIndexOf('.');
            return (dot > name.LastIndexOf('/') ? name.Substring(0, dot) : name) + BrokenEnd;
        }

        // Each turn is a completion source's task, which any number may wait on.
        private UniTask InTurn(Func<UniTask> work)
        {
            var done = new UniTaskCompletionSource();
            UniTask before = _turn;
            _turn = done.Task;
            TakeTurn(before, work, done).Forget();
            return done.Task;
        }

        private static async UniTaskVoid TakeTurn(UniTask before, Func<UniTask> work, UniTaskCompletionSource done)
        {
            try
            {
                await before;
            }
            catch (Exception)
            {
                // Its own caller heard it; the next turn still comes.
            }

            try
            {
                await work();
                done.TrySetResult();
            }
            catch (Exception e)
            {
                done.TrySetException(e);
            }
        }
    }
}
