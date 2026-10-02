using System.Collections.Generic;
using UnityEditorInternal;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits a name constant for every defined Unity tag, for use with <c>CompareTag(Tags.Player)</c>.
    /// </summary>
    public sealed class TagsGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "tags";

        public override string Id => GeneratorId;
        public override string DisplayName => "Tags";
        protected override string DefaultClassName => "Tags";

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            foreach (var tag in InternalEditorUtility.tags)
            {
                if (string.IsNullOrEmpty(tag))
                    continue;

                rootMembers.Add(new ConstMember(tag, "string", Str(tag)));
            }
        }
    }
}
