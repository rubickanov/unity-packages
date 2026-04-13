using System;

namespace Rubickanov.ACS.Runtime
{
    /// <summary>
    /// Marks a field for automatic injection via <see cref="MonoEntity.Require{T}"/> in Awake.
    /// The field type must implement <see cref="IEntityAspect"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class AspectAttribute : Attribute { }
}
