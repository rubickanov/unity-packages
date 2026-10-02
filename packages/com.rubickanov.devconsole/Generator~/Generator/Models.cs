using System;
using System.Collections;
using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.Text;

namespace System.Runtime.CompilerServices
{
    // Records need it, and netstandard2.0 does not have it
    internal static class IsExternalInit
    {
    }
}

namespace Rubickanov.DevConsole.Generator
{
    /// <summary>An immutable array compared by its items, so the incremental pipeline can tell an unchanged model.</summary>
    internal readonly struct EquatableArray<T> : IEquatable<EquatableArray<T>>, IEnumerable<T>
        where T : IEquatable<T>
    {
        private readonly T[]? _items;

        public EquatableArray(IEnumerable<T> items) => _items = items.ToArray();

        public int Count => _items?.Length ?? 0;
        public T this[int index] => _items![index];

        public bool Equals(EquatableArray<T> other)
        {
            if (Count != other.Count) return false;
            for (int i = 0; i < Count; i++)
                if (!_items![i].Equals(other._items![i])) return false;
            return true;
        }

        public override bool Equals(object? obj) => obj is EquatableArray<T> other && Equals(other);

        public override int GetHashCode()
        {
            var hash = 17;
            if (_items != null)
                foreach (var item in _items)
                    hash = hash * 31 + item.GetHashCode();
            return hash;
        }

        public IEnumerator<T> GetEnumerator() => ((IEnumerable<T>)(_items ?? Array.Empty<T>())).GetEnumerator();
        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }

    /// <summary>A location kept as plain data: a <see cref="Location"/> holds the syntax tree alive.</summary>
    internal sealed record LocationInfo(string FilePath, TextSpan Span, LinePositionSpan LineSpan)
    {
        public Location ToLocation() => Location.Create(FilePath, Span, LineSpan);

        public static LocationInfo? From(Location? location) =>
            location?.SourceTree == null
                ? null
                : new LocationInfo(location.SourceTree.FilePath, location.SourceSpan, location.GetLineSpan().Span);
    }

    internal sealed record DiagnosticInfo(DiagnosticDescriptor Descriptor, LocationInfo? Location, EquatableArray<string> Args)
        : IEquatable<DiagnosticInfo>
    {
        public Diagnostic ToDiagnostic() =>
            Diagnostic.Create(Descriptor, Location?.ToLocation(), Args.Cast<object>().ToArray());
    }

    /// <summary>One type around a command, as a partial declaration repeats it.</summary>
    internal sealed record TypePart(string Keyword, string Name, bool IsStatic);

    internal sealed record ParameterModel(string Name, string Type, string? DefaultValue);

    /// <summary>
    /// A provider named in <c>[AutoComplete]</c>: shared by type when it takes no arguments, else made per command with
    /// <see cref="Arguments"/>, already written as C# literals.
    /// </summary>
    internal sealed record ProviderModel(int Index, string Type, EquatableArray<string> Arguments);

    /// <summary>Everything the writer needs about one <c>[ConsoleCommand]</c> method, and what was wrong with it.</summary>
    internal sealed record CommandModel(
        string Name,
        string Description,
        string Category,
        string DeclaringType,
        string? Namespace,
        EquatableArray<TypePart> TypeChain,
        bool InsideType,
        bool IsStatic,
        string Method,
        bool ReturnsVoid,
        EquatableArray<ParameterModel> Parameters,
        EquatableArray<ProviderModel> Providers,
        bool HasRemainder,
        LocationInfo? Location,
        EquatableArray<DiagnosticInfo> Diagnostics)
    {
        public bool HasErrors => Diagnostics.Any(d => d.Descriptor.DefaultSeverity == DiagnosticSeverity.Error);
    }

    internal sealed record AssemblyModel(string Name, bool HasAlwaysLink, bool HasEngine)
    {
        public static AssemblyModel From(Compilation compilation) => new(
            compilation.AssemblyName ?? "Assembly",
            compilation.Assembly.GetAttributes().Any(a =>
                a.AttributeClass?.ToDisplayString() == "UnityEngine.Scripting.AlwaysLinkAssemblyAttribute"),
            compilation.GetTypeByMetadataName("UnityEngine.RuntimeInitializeOnLoadMethodAttribute") != null);
    }
}
