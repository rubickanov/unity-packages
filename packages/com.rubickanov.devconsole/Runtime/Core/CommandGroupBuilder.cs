using System;
using System.Collections.Generic;

namespace Rubickanov.DevConsole
{
    /// <summary>Fluent builder for defining subcommands within a command group.</summary>
    public class CommandGroupBuilder
    {
        internal readonly List<SubcommandDefinition> Subcommands = new();

        public CommandGroupBuilder Add(string name, Func<string[], string?> handler,
            string description = "", params IAutoCompleteProvider?[] argProviders)
        {
            Subcommands.Add(new SubcommandDefinition
            {
                Name = name.ToLowerInvariant(),
                Description = description,
                Handler = handler,
                ArgProviders = argProviders.Length > 0 ? argProviders : null
            });
            return this;
        }
    }
}
