using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Codegen.Editor
{
    /// <summary>
    /// Discovers every <see cref="ICodeGenerator"/> implementation in the project and caches the
    /// instances. Discovery runs at most once per domain via <see cref="TypeCache"/> reflection —
    /// the cold-path, cache-the-result pattern the repo permits for scan/spawn-time code.
    /// </summary>
    public static class CodeGeneratorRegistry
    {
        private static List<ICodeGenerator>? _generators;

        /// <summary>All discovered generators, sorted by display name.</summary>
        public static IReadOnlyList<ICodeGenerator> All
        {
            get
            {
                EnsureLoaded();
                return _generators!;
            }
        }

        /// <summary>Returns the generator with the given <see cref="ICodeGenerator.Id"/>, or null.</summary>
        public static ICodeGenerator? FindById(string id)
        {
            EnsureLoaded();
            foreach (var generator in _generators!)
            {
                if (generator.Id == id)
                    return generator;
            }

            return null;
        }

        private static void EnsureLoaded()
        {
            if (_generators != null)
                return;

            _generators = new List<ICodeGenerator>();

            foreach (var type in TypeCache.GetTypesDerivedFrom<ICodeGenerator>())
            {
                if (type.IsAbstract || type.IsInterface)
                    continue;
                if (type.GetConstructor(Type.EmptyTypes) == null)
                    continue;

                try
                {
                    _generators.Add((ICodeGenerator)Activator.CreateInstance(type));
                }
                catch (Exception e)
                {
                    Debug.LogError($"[Codegen] Failed to instantiate generator '{type.FullName}': {e.Message}");
                }
            }

            _generators.Sort((a, b) => string.CompareOrdinal(a.DisplayName, b.DisplayName));
        }
    }
}
