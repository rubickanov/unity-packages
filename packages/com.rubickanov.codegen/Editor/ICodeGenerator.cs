namespace Rubickanov.Codegen.Editor
{
    /// <summary>
    /// A single code generator contributed to the central codegen pipeline. Implementations are
    /// discovered automatically by <see cref="CodeGeneratorRegistry"/> (they must have a public
    /// parameterless constructor), surfaced in the Project Settings panel, and driven by the
    /// shared postprocessor. Implementations should be stateless — configuration is passed in.
    /// </summary>
    public interface ICodeGenerator
    {
        /// <summary>Stable identifier used as the settings key. Must be unique and not change.</summary>
        string Id { get; }

        /// <summary>Human-readable name shown in the settings panel and logs.</summary>
        string DisplayName { get; }

        /// <summary>Creates the default configuration used to seed settings on first run.</summary>
        GeneratorConfig CreateDefaultConfig();

        /// <summary>Finds inputs, builds source, and writes the output file using the given config.</summary>
        void Generate(GeneratorConfig config);

        /// <summary>
        /// Returns true if any of the supplied asset changes should trigger regeneration. Used by
        /// the shared postprocessor for auto-regeneration. Generators whose inputs are not assets
        /// (layers, tags, ...) return false and rely on manual or other triggers.
        /// </summary>
        bool HandlesAssetChange(string[] importedAssets, string[] deletedAssets, string[] movedAssets);
    }
}
