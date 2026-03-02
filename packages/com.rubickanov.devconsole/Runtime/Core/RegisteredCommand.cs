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
        public ParameterInfo[] Parameters = Array.Empty<ParameterInfo>();
        public IAutoCompleteProvider?[]? ArgProviders;

        /// <summary>Raw handler for manually registered commands. Returns optional message (null = no message).</summary>
        public Func<string[], string?>? ManualHandler;

        [ThreadStatic] private static StringBuilder? _usageSb;

        /// <summary>Returns a formatted usage string, e.g. "tp &lt;position&gt; [speed=1]".</summary>
        public string GetUsageString()
        {
            _usageSb ??= new StringBuilder();
            _usageSb.Clear();
            _usageSb.Append(Name);

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
    }
}
