using System;

namespace Rubickanov.DevConsole
{
    /// <summary>One parameter of an attribute command, as the source generator read it from the method.</summary>
    public sealed class CommandParameter
    {
        public string Name { get; }
        public Type Type { get; }
        public bool HasDefaultValue { get; }

        /// <summary>The value the argument takes when left out. Meaningful only with <see cref="HasDefaultValue"/>.</summary>
        public object? DefaultValue { get; }

        /// <summary>A required parameter.</summary>
        public CommandParameter(string name, Type type)
        {
            Name = name;
            Type = type;
        }

        /// <summary>An optional parameter with its default value.</summary>
        public CommandParameter(string name, Type type, object? defaultValue)
        {
            Name = name;
            Type = type;
            HasDefaultValue = true;
            DefaultValue = defaultValue;
        }
    }
}
