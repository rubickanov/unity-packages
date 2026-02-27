using System;

namespace Rubickanov.DevConsole
{
    /// <summary>Marks a static method as a console command. Discovered automatically at startup.</summary>
    [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
    public class ConsoleCommandAttribute : Attribute
    {
        public string Name { get; }
        public string Description { get; }
        public string Category { get; }

        /// <param name="name">Command name used to invoke it in the console (case-insensitive).</param>
        /// <param name="description">Short description shown in help and autocomplete.</param>
        /// <param name="category">Category for grouping in help output.</param>
        public ConsoleCommandAttribute(string name, string description = "", string category = "General")
        {
            Name = name.ToLowerInvariant();
            Description = description;
            Category = category;
        }
    }
}
