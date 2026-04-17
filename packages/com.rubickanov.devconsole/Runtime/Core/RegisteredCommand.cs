using System;
using System.Reflection;
using System.Text;

namespace Rubickanov.DevConsole
{
    /// <summary>Represents a registered console command with its metadata, parameters, and handler.</summary>
    public class RegisteredCommand
    {
        public string Name = "";
        public string Description = "";
        public string Category = "";
        public MethodInfo? Method;

        /// <summary>Instance target for method invocation. Null for static methods and manually registered commands.</summary>
        public object? Target;

        public ParameterInfo[] Parameters = Array.Empty<ParameterInfo>();
        public IAutoCompleteProvider?[]? ArgProviders;

        /// <summary>Raw handler for manually registered commands. Returns optional message (null = no message).</summary>
        public Func<string[], string?>? ManualHandler;

        /// <summary>Subcommand definitions for group commands. Null for regular commands.</summary>
        public SubcommandDefinition[]? Subcommands;

        [ThreadStatic] private static StringBuilder? _usageSb;

        /// <summary>Returns a formatted usage string, e.g. "tp &lt;position&gt; [speed=1]".</summary>
        public string GetUsageString()
        {
            _usageSb ??= new StringBuilder();
            _usageSb.Clear();
            _usageSb.Append(Name);

            if (Subcommands != null)
            {
                _usageSb.Append(" <");
                for (int i = 0; i < Subcommands.Length; i++)
                {
                    if (i > 0) _usageSb.Append('|');
                    _usageSb.Append(Subcommands[i].Name);
                }
                _usageSb.Append('>');
                return _usageSb.ToString();
            }

            for (int i = 0; i < Parameters.Length; i++)
            {
                var p = Parameters[i];
                var provider = ArgProviders != null && i < ArgProviders.Length ? ArgProviders[i] : null;
                var hint = provider?.Hint ?? $"<{p.Name}>";

                if (p.HasDefaultValue)
                    _usageSb.Append($" [{hint}={p.DefaultValue}]");
                else
                    _usageSb.Append(' ').Append(hint);
            }

            return _usageSb.ToString();
        }

        /// <summary>Returns a formatted usage string for a specific subcommand.</summary>
        public string GetSubcommandUsageString(SubcommandDefinition sub)
        {
            _usageSb ??= new StringBuilder();
            _usageSb.Clear();
            _usageSb.Append(Name).Append(' ').Append(sub.Name);

            if (sub.ArgProviders != null)
            {
                for (int i = 0; i < sub.ArgProviders.Length; i++)
                {
                    var hint = sub.ArgProviders[i]?.Hint ?? $"<arg{i}>";
                    _usageSb.Append(' ').Append(hint);
                }
            }

            return _usageSb.ToString();
        }
    }
}
