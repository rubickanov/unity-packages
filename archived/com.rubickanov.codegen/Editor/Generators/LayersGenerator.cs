using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits a name constant and an index constant for every defined layer, for use with
    /// <c>LayerMask.GetMask(Layers.Player)</c> or <c>gameObject.layer == Layers.PlayerIndex</c>.
    /// </summary>
    public sealed class LayersGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "layers";

        public override string Id => GeneratorId;
        public override string DisplayName => "Layers";
        protected override string DefaultClassName => "Layers";

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            for (var i = 0; i < 32; i++)
            {
                var name = LayerMask.LayerToName(i);
                if (string.IsNullOrEmpty(name))
                    continue;

                rootMembers.Add(new ConstMember(name, "string", Str(name)));
                rootMembers.Add(new ConstMember($"{name}Index", "int", i.ToString()));
            }
        }
    }
}
