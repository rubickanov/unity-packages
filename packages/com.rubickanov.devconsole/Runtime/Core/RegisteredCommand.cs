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

        /// <summary>Whether the last parameter takes the rest of the line (<see cref="RemainderAttribute"/>).</summary>
        public bool HasRemainder;

        /// <summary>Raw handler for manually registered commands. Returns optional message (null = no message).</summary>
        public Func<string[], string?>? ManualHandler;

        /// <summary>
        /// Index of the argument of a <see cref="ManualHandler"/> command that takes the rest of the line as typed, or -1.
        /// </summary>
        public int RestFrom = -1;

        /// <summary>Usage shown after the name, e.g. <c>&lt;key&gt; &lt;command...&gt;</c>. Built from the parameters when empty.</summary>
        public string Usage = "";

        [ThreadStatic] private static StringBuilder? _usageSb;

        /// <summary>Returns a formatted usage string, e.g. "tp &lt;position&gt; [speed=1]".</summary>
        public string GetUsageString()
        {
            _usageSb ??= new StringBuilder();
            _usageSb.Clear();
            _usageSb.Append(Name);

            if (Usage.Length > 0)
                return _usageSb.Append(' ').Append(Usage).ToString();

            // A command registered with a handler has no parameters to name, only its providers' hints
            if (Method == null && ArgProviders != null)
            {
                for (int i = 0; i < ArgProviders.Length; i++)
                    _usageSb.Append(' ').Append(ArgProviders[i]?.Hint ?? $"<arg{i}>");
                return _usageSb.ToString();
            }

            for (int i = 0; i < Parameters.Length; i++)
            {
                var p = Parameters[i];
                var provider = ArgProviders != null && i < ArgProviders.Length ? ArgProviders[i] : null;
                var hint = provider?.Hint ?? $"<{p.Name}>";
                if (HasRemainder && i == Parameters.Length - 1)
                    hint = $"<{p.Name}...>";

                if (p.HasDefaultValue && (p.DefaultValue == null || p.DefaultValue is string { Length: 0 }))
                    _usageSb.Append($" [{hint}]");
                else if (p.HasDefaultValue)
                    _usageSb.Append($" [{hint}={p.DefaultValue}]");
                else
                    _usageSb.Append(' ').Append(hint);
            }

            return _usageSb.ToString();
        }
    }
}
