using System;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Marks a method as a console command. Static methods are discovered automatically at startup;
    /// instance methods must be registered explicitly via <see cref="CommandRegistry.RegisterTarget(object)"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ConsoleCommandAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        public string Category { get; }

        /// <param name="name">
        /// Command name used to invoke it in the console (case-insensitive). Several words, <c>scene load</c>, make
        /// it a subcommand of the group named by the words before the last.
        /// </param>
        /// <param name="description">Short description shown in help and autocomplete.</param>
        /// <param name="category">Category for grouping in help output.</param>
        public ConsoleCommandAttribute(string name, string description = "", string category = "General")
        {
            Name = CommandRegistry.NormalizeName(name);
            Description = description;
            Category = category;
        }
    }
}
