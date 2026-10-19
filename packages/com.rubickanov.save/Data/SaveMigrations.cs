using System;
using System.Collections.Generic;

namespace Rubickanov.Save.Data
{
    /// <summary>
    /// The chain that brings a document of any released version to the current one. The classes an old version was
    /// written from are gone, so each step works on the tree: a step v-1 → v is a list of operations over key paths
    /// ("till/cash"). An operation on a path that is not there does nothing: the field may never have been written.
    /// </summary>
    public sealed class SaveMigrations
    {
        private readonly List<SaveMigrationStep> _steps = new();

        /// <summary>The version the chain ends at: 1 and one more for each step.</summary>
        public int Current => 1 + _steps.Count;

        /// <summary>Adds the step to version <paramref name="to"/>, which must be the next one: 2, then 3, and so on.</summary>
        public SaveMigrations Step(int to, Action<SaveMigrationStep> step)
        {
            if (to != Current + 1)
            {
                throw new ArgumentException($"The next step goes to v{Current + 1}, not v{to}.", nameof(to));
            }

            var s = new SaveMigrationStep();
            step(s);
            _steps.Add(s);
            return this;
        }

        /// <summary>
        /// A copy of <paramref name="data"/> walked from version <paramref name="from"/> to <see cref="Current"/>;
        /// <paramref name="data"/> itself is not changed. Throws what a step's own code throws.
        /// </summary>
        public SaveNode Walk(SaveNode data, int from, List<string> notes = null)
        {
            if (from < 1 || from > Current)
            {
                throw new ArgumentOutOfRangeException(nameof(from), $"v{from} is not between v1 and v{Current}.");
            }

            SaveNode walked = data.Clone();
            for (int v = from; v < Current; v++)
            {
                _steps[v - 1].Apply(walked);
            }

            if (from < Current)
            {
                notes?.Add($"migrated from v{from} to v{Current}");
            }

            return walked;
        }
    }

    /// <summary>The operations of one step, applied in the order they are added.</summary>
    public sealed class SaveMigrationStep
    {
        private readonly List<Action<SaveNode>> _ops = new();

        /// <summary>Renames the last key of <paramref name="path"/> to <paramref name="name"/>, in the same object.</summary>
        public SaveMigrationStep Rename(string path, string name) => Add(root =>
        {
            SaveNode parent = Parent(root, path, false, out string key);
            SaveNode value = parent?[key];
            if (value != null)
            {
                parent.Remove(key);
                parent[name] = value;
            }
        });

        /// <summary>Moves a value anywhere; the objects on the way to <paramref name="to"/> are made when missing.</summary>
        public SaveMigrationStep Move(string from, string to) => Add(root =>
        {
            SaveNode parent = Parent(root, from, false, out string key);
            SaveNode value = parent?[key];
            if (value != null)
            {
                parent.Remove(key);
                Parent(root, to, true, out string toKey)[toKey] = value;
            }
        });

        public SaveMigrationStep Remove(string path) => Add(root => Parent(root, path, false, out string key)?.Remove(key));

        /// <summary>Sets a value, over what was there; the objects on the way are made when missing.</summary>
        public SaveMigrationStep Set(string path, SaveNode value) =>
            Add(root => Parent(root, path, true, out string key)[key] = value.Clone());

        /// <summary>Replaces a value with what <paramref name="edit"/> makes of it; null removes it.</summary>
        public SaveMigrationStep Edit(string path, Func<SaveNode, SaveNode> edit) => Add(root =>
        {
            SaveNode parent = Parent(root, path, false, out string key);
            SaveNode value = parent?[key];
            if (value != null)
            {
                parent[key] = edit(value);
            }
        });

        /// <summary>
        /// The operations <paramref name="step"/> adds, applied inside each object entry of the map or list at
        /// <paramref name="mapOrList"/>.
        /// </summary>
        public SaveMigrationStep Each(string mapOrList, Action<SaveMigrationStep> step)
        {
            var inner = new SaveMigrationStep();
            step(inner);
            return Add(root =>
            {
                SaveNode all = root.At(mapOrList);
                if (all == null)
                {
                    return;
                }

                if (all.IsObject)
                {
                    IReadOnlyList<string> ids = all.Keys;
                    for (int i = 0; i < ids.Count; i++)
                    {
                        SaveNode entry = all[ids[i]];
                        if (entry.IsObject)
                        {
                            inner.Apply(entry);
                        }
                    }
                }
                else if (all.IsArray)
                {
                    for (int i = 0; i < all.Count; i++)
                    {
                        if (all[i].IsObject)
                        {
                            inner.Apply(all[i]);
                        }
                    }
                }
            });
        }

        /// <summary>Own code over the whole data object, for what the operations above cannot say.</summary>
        public SaveMigrationStep Run(Action<SaveNode> edit) => Add(edit);

        internal void Apply(SaveNode root)
        {
            for (int i = 0; i < _ops.Count; i++)
            {
                _ops[i](root);
            }
        }

        private SaveMigrationStep Add(Action<SaveNode> op)
        {
            _ops.Add(op);
            return this;
        }

        // The object that holds the path's last key; null when a part is missing and make is false.
        private static SaveNode Parent(SaveNode root, string path, bool make, out string key)
        {
            int slash = path.LastIndexOf('/');
            key = slash < 0 ? path : path.Substring(slash + 1);
            if (slash < 0)
            {
                return root;
            }

            SaveNode node = root;
            int start = 0;
            while (start <= slash)
            {
                int next = path.IndexOf('/', start);
                string part = path.Substring(start, next - start);
                SaveNode child = node[part];
                if (child == null || !child.IsObject)
                {
                    if (!make)
                    {
                        return null;
                    }

                    if (child != null)
                    {
                        throw new InvalidOperationException($"{path}: {part} is {child.Describe()}, not an object.");
                    }

                    child = SaveNode.NewObject();
                    node[part] = child;
                }

                node = child;
                start = next + 1;
            }

            return node;
        }
    }
}
