using System;

namespace Rubickanov.ACS.Runtime.Reactive
{
    /// <summary>
    /// Marks an aspect field as a derived (computed) value — its content is a function of
    /// other reactive fields, never written directly. Purely a marker: it carries intent for
    /// readers and tooling (e.g. <c>acs.debug</c> can badge the field, future <c>acs.codegen</c>
    /// can generate the wiring) and signals "do not replicate / persist / dirty this" to the
    /// attribute scanners, which all opt-in anyway.
    /// <para/>
    /// The field itself is typically a <see cref="ComputedProperty{T}"/> built in the aspect
    /// constructor via <see cref="ComputedProperty.From{T1,TOut}"/>.
    /// </summary>
    [AttributeUsage(AttributeTargets.Field)]
    public sealed class ComputedAttribute : Attribute { }
}
