using System.Collections.Generic;
using UnityEngine;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Emits a name constant and an id constant for every sorting layer, for use with
    /// <c>renderer.sortingLayerName = SortingLayers.Foreground</c> or the matching id.
    /// </summary>
    public sealed class SortingLayersGenerator : BuiltInConstantsGenerator
    {
        public const string GeneratorId = "sortingLayers";

        public override string Id => GeneratorId;
        public override string DisplayName => "Sorting Layers";
        protected override string DefaultClassName => "SortingLayers";

        protected override void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups)
        {
            foreach (var layer in SortingLayer.layers)
            {
                rootMembers.Add(new ConstMember(layer.name, "string", Str(layer.name)));
                rootMembers.Add(new ConstMember($"{layer.name}Id", "int", layer.id.ToString()));
            }
        }
    }
}
