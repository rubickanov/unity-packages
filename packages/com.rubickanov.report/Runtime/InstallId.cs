using System;
using Cysharp.Threading.Tasks;
using Rubickanov.Log;
using Rubickanov.Save;
using Rubickanov.Save.Data;

namespace Rubickanov.Report
{
    /// <summary>
    /// This install's own random id, the only thing about the player bug reports carry: made once and kept in a small
    /// save document the game names, so Sentry counts players without a hardware fingerprint or a store name.
    /// A store with nothing in it (a fresh batch run) makes a new one.
    /// </summary>
    public sealed class InstallId
    {
        private static readonly LogChannel Log = LogChannel.Get("Report");

        private readonly SaveDocument _file;
        private readonly string _version;
        private SaveNode _tree;
        private string _id;
        private bool _newer;

        /// <param name="store">The game's save store.</param>
        /// <param name="key">Where the id is kept, such as <c>report.json</c> in the game's own area.</param>
        /// <param name="version">The build's version, written beside the id.</param>
        public InstallId(ISaveStore store, SaveKey key, string version)
        {
            _version = version;
            _file = new SaveDocument(store, key, message => Log.Error(message));
            // The desktop store answers at once: the id is known before reports start.
            _file.ReadAsync(Read).Forget();
        }

        /// <summary>The chain of the file's versions; v1 is the first.</summary>
        public static SaveMigrations Chain { get; } = new();

        private static SaveFormat Format { get; } = new("report", Chain);

        /// <summary>The id, made and written the first time it is asked when the file has none.</summary>
        public string Value
        {
            get
            {
                if (_id != null)
                {
                    return _id;
                }

                _id = Guid.NewGuid().ToString("N");
                SaveWriter writer = SaveWriter.Over(_tree);
                writer.String("install", _id);
                SaveNode tree = writer.Tree;
                _tree = tree;
                if (!_newer)
                {
                    _file.WriteAsync(() => Format.Write(tree, _version, message => Log.Error(message))).Forget();
                }

                return _id;
            }
        }

        private void Read(string text)
        {
            if (text == null)
            {
                return;
            }

            SaveRead read = Format.Read(text);
            if (!read.IsOk)
            {
                Log.Warn($"{_file.Where}: {read.Error}; a new install id instead");
                _newer = read.Status == SaveReadStatus.Newer;
                if (!_newer)
                {
                    _file.KeepBrokenAsync(text).Forget();
                }

                return;
            }

            _tree = read.Data;
            string id = SaveReader.Of(read.Data).String("install");
            if (!string.IsNullOrEmpty(id) && _id == null)
            {
                _id = id;
            }
        }
    }
}
