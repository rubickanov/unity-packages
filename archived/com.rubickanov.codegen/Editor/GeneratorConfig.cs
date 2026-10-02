using System;
using UnityEngine;

namespace Rubickanov.Codegen.Editor
{
    /// <summary>Access modifier applied to a generated class and its members.</summary>
    public enum GeneratedAccess
    {
        Public,
        Internal,
    }

    /// <summary>
    /// Per-generator configuration persisted in the central <see cref="CodegenSettings"/> store.
    /// One serializable superset covers every generator: built-ins use the common fields, the
    /// gameplay tags generator additionally honours <see cref="Access"/> and <see cref="MakePartial"/>,
    /// and generators that do not need a field simply ignore it.
    /// </summary>
    [Serializable]
    public class GeneratorConfig
    {
        /// <summary>Stable key matching <see cref="ICodeGenerator.Id"/>.</summary>
        public string Id = string.Empty;

        /// <summary>Whether "Generate All" and auto-regeneration include this generator.</summary>
        public bool Enabled = true;

        /// <summary>Whether asset changes trigger regeneration for this generator.</summary>
        public bool AutoRegenerate = true;

        /// <summary>Output file path for the generated source, relative to the project root.</summary>
        public string OutputPath = string.Empty;

        /// <summary>C# namespace for the generated class.</summary>
        public string Namespace = string.Empty;

        /// <summary>Name of the generated outer static class.</summary>
        public string ClassName = string.Empty;

        /// <summary>Access modifier for the generated class and members.</summary>
        public GeneratedAccess Access = GeneratedAccess.Public;

        /// <summary>If true, the generated outer class is declared <c>partial</c>.</summary>
        public bool MakePartial;

        /// <summary>The C# keyword form of <see cref="Access"/> ("public" or "internal").</summary>
        public string AccessKeyword => Access == GeneratedAccess.Internal ? "internal" : "public";

        /// <summary>Returns a deep copy so callers can edit without mutating the stored instance.</summary>
        public GeneratorConfig Clone() => new()
        {
            Id = Id,
            Enabled = Enabled,
            AutoRegenerate = AutoRegenerate,
            OutputPath = OutputPath,
            Namespace = Namespace,
            ClassName = ClassName,
            Access = Access,
            MakePartial = MakePartial,
        };
    }
}
