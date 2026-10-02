using System;

namespace Rubickanov.BehaviorTree.Runtime
{
    /// <summary>
    /// Provides display name, category, and description for a BT node in the editor graph view.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public class BTNodeDescriptionAttribute : Attribute
    {
        public string Name { get; }
        public string Category { get; }
        public string Description { get; }

        public BTNodeDescriptionAttribute(string name, string category, string description = "")
        {
            Name = name;
            Category = category;
            Description = description;
        }
    }
}