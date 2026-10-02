using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEngine;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits a cached <c>Shader.PropertyToID</c> per distinct property across the project's shaders,
    /// so material writes use a precomputed int id instead of re-hashing a magic string every call.
    /// Only shaders under <c>Assets/</c> are scanned, to avoid dumping every URP/built-in property.
    /// </summary>
    public sealed class ShaderPropertiesGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "shaderProperties";

        public override string Id => GeneratorId;
        public override string DisplayName => "Shader Property IDs";
        protected override string DefaultClassName => "ShaderProps";
        protected override IReadOnlyList<string>? Usings => new[] { "UnityEngine" };

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            var names = new List<string>();
            var seen = new HashSet<string>(StringComparer.Ordinal);

            foreach (var guid in AssetDatabase.FindAssets("t:Shader"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                if (!path.StartsWith("Assets/", StringComparison.Ordinal))
                    continue;

                var shader = AssetDatabase.LoadAssetAtPath<Shader>(path);
                if (shader == null)
                    continue;

                var count = ShaderUtil.GetPropertyCount(shader);
                for (var i = 0; i < count; i++)
                {
                    var name = ShaderUtil.GetPropertyName(shader, i);
                    if (!string.IsNullOrEmpty(name) && seen.Add(name))
                        names.Add(name);
                }
            }

            // PropertyToID is global to the name, so a deduplicated, sorted set is deterministic.
            names.Sort(StringComparer.Ordinal);

            foreach (var name in names)
                rootMembers.Add(new ConstMember(
                    name, "int", $"Shader.PropertyToID({Str(name)})", MemberKind.StaticReadonly));
        }

        public override bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
            => HasShader(importedAssets) || HasShader(deletedAssets) || HasShader(movedAssets);

        private static bool HasShader(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".shader", StringComparison.OrdinalIgnoreCase) ||
                    path.EndsWith(".shadergraph", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
