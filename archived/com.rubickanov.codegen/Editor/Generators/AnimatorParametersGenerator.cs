using System;
using System.Collections.Generic;
using UnityEditor;
using UnityEditor.Animations;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits a nested class per AnimatorController, each holding the precomputed
    /// <c>Animator.StringToHash</c> of its parameters, so animator code uses cached int hashes
    /// instead of re-hashing magic strings every call.
    /// </summary>
    public sealed class AnimatorParametersGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "animatorParameters";

        public override string Id => GeneratorId;
        public override string DisplayName => "Animator Parameters";
        protected override string DefaultClassName => "AnimatorParameters";
        protected override IReadOnlyList<string>? Usings => new[] { "UnityEngine" };

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            foreach (var guid in AssetDatabase.FindAssets("t:AnimatorController"))
            {
                var path = AssetDatabase.GUIDToAssetPath(guid);
                var controller = AssetDatabase.LoadAssetAtPath<AnimatorController>(path);
                if (controller == null || controller.parameters.Length == 0)
                    continue;

                var group = new ConstGroup(controller.name);
                foreach (var parameter in controller.parameters)
                {
                    group.Members.Add(new ConstMember(
                        parameter.name, "int", $"Animator.StringToHash({Str(parameter.name)})",
                        MemberKind.StaticReadonly));
                }

                groups.Add(group);
            }
        }

        public override bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
            => HasController(importedAssets) || HasController(deletedAssets) || HasController(movedAssets);

        private static bool HasController(string[] paths)
        {
            foreach (var path in paths)
            {
                if (path.EndsWith(".controller", StringComparison.OrdinalIgnoreCase))
                    return true;
            }

            return false;
        }
    }
}
