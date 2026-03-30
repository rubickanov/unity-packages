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
    }
}
