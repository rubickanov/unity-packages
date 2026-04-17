#nullable enable
using System;

namespace Rubickanov.Utils
{
    /// <summary>
    /// Attaches a short text description to a MonoBehaviour, displayed in the Inspector.
    /// </summary>
    [AttributeUsage(AttributeTargets.Class, Inherited = false)]
    public sealed class DescriptionAttribute : Attribute
    {
        public string Description { get; }
        public DescriptionAttribute(string description) => Description = description;
    }
}
