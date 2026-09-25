using System;

namespace Rubickanov.DevConsole
{
    /// <summary>Defines a single subcommand within a command group.</summary>
    public class SubcommandDefinition
    {
        public string Name = "";
        public string Description = "";
        public Func<string[], string?> Handler = null!;
        public IAutoCompleteProvider?[]? ArgProviders;

        /// <summary>
        /// Index of the argument that takes the rest of the line as typed, the way a <see cref="RemainderAttribute"/>
        /// parameter does, or -1. The handler then gets at most <c>RestFrom + 1</c> arguments.
        /// </summary>
        public int RestFrom = -1;

        /// <summary>Usage shown after the subcommand name, e.g. <c>&lt;key&gt; &lt;command...&gt;</c>. Built from the providers when empty.</summary>
        public string Usage = "";
    }
}
