using System.Collections.Generic;

namespace Rubickanov.Codegen.Editor.Generators
{
    /// <summary>
    /// Base for the built-in Unity-constant generators (scenes, layers, tags, ...). Handles config
    /// defaults and the build-and-write flow; subclasses only collect members. Built-ins default to
    /// <see cref="GeneratorConfig.Enabled"/> = false so installing the package does not silently
    /// drop generated files into every project — the user opts each one in from the settings panel.
    /// </summary>
    public abstract class BuiltInConstantsGenerator : ICodeGenerator
    {
        public abstract string Id { get; }
        public abstract string DisplayName { get; }

        /// <summary>Default generated class name, also used to derive the default output path.</summary>
        protected abstract string DefaultClassName { get; }

        /// <summary>Using directives emitted before the namespace, or null when none are needed.</summary>
        protected virtual IReadOnlyList<string>? Usings => null;

        public virtual GeneratorConfig CreateDefaultConfig() => new()
        {
            Id = Id,
            Enabled = false,
            AutoRegenerate = true,
            OutputPath = $"Assets/Codegen/{DefaultClassName}.Generated.cs",
            Namespace = "Game.Generated",
            ClassName = DefaultClassName,
            Access = GeneratedAccess.Public,
        };

        public void Generate(GeneratorConfig config)
        {
            var rootMembers = new List<ConstMember>();
            var groups = new List<ConstGroup>();
            Collect(rootMembers, groups);

            var code = ConstantsClassBuilder.Build(
                DisplayName, config, rootMembers, groups.Count > 0 ? groups : null, Usings);

            GeneratedFileWriter.Write(config.OutputPath, code);
        }

        /// <summary>Populates the members (and optionally nested groups) to emit.</summary>
        protected abstract void Collect(List<ConstMember> rootMembers, List<ConstGroup> groups);

        public virtual bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets)
            => false;

        /// <summary>Produces a safely-escaped C# string literal, including the surrounding quotes.</summary>
        protected static string Str(string value)
            => "\"" + value.Replace("\\", "\\\\").Replace("\"", "\\\"") + "\"";
    }
}
