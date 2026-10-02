using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rubickanov.DevConsole.Generator
{
    /// <summary>Reads one <c>[ConsoleCommand]</c> method into a <see cref="CommandModel"/>, checking what the console needs.</summary>
    internal static class CommandReader
    {
        private const string AutoCompleteAttribute = "Rubickanov.DevConsole.AutoCompleteAttribute";
        private const string RemainderAttribute = "Rubickanov.DevConsole.RemainderAttribute";
        private const string ProviderInterface = "Rubickanov.DevConsole.IAutoCompleteProvider";

        private static readonly SymbolDisplayFormat TypeFormat = SymbolDisplayFormat.FullyQualifiedFormat;

        public static CommandModel? Read(GeneratorAttributeSyntaxContext context, CancellationToken cancellation)
        {
            if (context.TargetSymbol is not IMethodSymbol method) return null;
            var attribute = context.Attributes.FirstOrDefault();
            if (attribute == null || attribute.ConstructorArguments.Length < 3) return null;

            var compilation = context.SemanticModel.Compilation;
            var location = LocationInfo.From(method.Locations.FirstOrDefault());
            var diagnostics = new List<DiagnosticInfo>();
            void Report(DiagnosticDescriptor descriptor, params string[] args) =>
                diagnostics.Add(new DiagnosticInfo(descriptor, location, new EquatableArray<string>(args)));

            var rawName = attribute.ConstructorArguments[0].Value as string ?? "";
            var name = NormalizeName(rawName);
            var description = attribute.ConstructorArguments[1].Value as string ?? "";
            var category = attribute.ConstructorArguments[2].Value as string ?? "General";
            var label = name.Length > 0 ? name : method.Name;
            if (name.Length == 0) Report(Diagnostics.EmptyName, method.Name);

            var type = method.ContainingType;
            if (method.IsGenericMethod || Chain(type).Any(t => t.IsGenericType))
                Report(Diagnostics.Generic, label);
            if (!method.IsStatic && (type.TypeKind != TypeKind.Class))
                Report(Diagnostics.InstanceKind, label, type.TypeKind == TypeKind.Struct ? "a struct" : "an interface");

            // Code in the assembly's own generated class reaches only what is internal or public all the way out;
            // the rest needs the code generated inside the type, which needs every type around it partial
            var assembly = compilation.Assembly;
            var unreachable = new List<string>();
            if (!compilation.IsSymbolAccessibleWithin(method, assembly))
                unreachable.Add($"it is {Accessibility(method)}");
            else if (!compilation.IsSymbolAccessibleWithin(type, assembly))
                unreachable.Add($"{type.Name} is {Accessibility(type)}");

            // Parameters
            var parameters = new List<ParameterModel>();
            var hasRemainder = false;
            for (int i = 0; i < method.Parameters.Length; i++)
            {
                var parameter = method.Parameters[i];
                if (parameter.RefKind != RefKind.None)
                    Report(Diagnostics.ByReference, parameter.Name, label);
                if (!compilation.IsSymbolAccessibleWithin(parameter.Type, assembly))
                    unreachable.Add($"its parameter type {parameter.Type.Name} is {Accessibility(parameter.Type)}");

                if (parameter.GetAttributes().Any(a => a.AttributeClass?.ToDisplayString() == RemainderAttribute))
                {
                    if (i == method.Parameters.Length - 1 && parameter.Type.SpecialType == SpecialType.System_String)
                        hasRemainder = true;
                    else
                        Report(Diagnostics.Remainder, parameter.Name, label);
                }

                parameters.Add(new ParameterModel(parameter.Name, TypeName(parameter.Type), DefaultValue(parameter)));
            }

            // Providers
            var providers = new Dictionary<int, ProviderModel>();
            foreach (var autoComplete in method.GetAttributes())
            {
                cancellation.ThrowIfCancellationRequested();
                if (autoComplete.AttributeClass?.ToDisplayString() != AutoCompleteAttribute) continue;
                var args = autoComplete.ConstructorArguments;
                if (args.Length < 3 || args[0].Value is not int index ||
                    args[1].Value is not INamedTypeSymbol provider) continue;
                var providerArgs = args[2].Kind == TypedConstantKind.Array
                    ? args[2].Values.Select(v => v.Value as string).ToList()
                    : new List<string?>();

                if (index < 0 || index >= method.Parameters.Length)
                {
                    Report(Diagnostics.ArgumentIndex, index.ToString(CultureInfo.InvariantCulture), label,
                        Plural(method.Parameters.Length, "parameter"));
                    continue;
                }

                if (provider.IsAbstract || provider.TypeKind != TypeKind.Class || provider.IsUnboundGenericType ||
                    !provider.AllInterfaces.Any(i => i.ToDisplayString() == ProviderInterface))
                {
                    Report(Diagnostics.NotAProvider, provider.ToDisplayString(), label);
                    continue;
                }

                var constructor = FindConstructor(provider, providerArgs.Count, compilation, type);
                if (constructor == null)
                {
                    Report(Diagnostics.NoConstructor, provider.ToDisplayString(),
                        providerArgs.Count.ToString(CultureInfo.InvariantCulture), label);
                    continue;
                }

                if (!compilation.IsSymbolAccessibleWithin(constructor, assembly))
                    unreachable.Add($"the constructor of {provider.Name} is {Accessibility(constructor)}");

                if (providers.ContainsKey(index))
                    diagnostics.Add(new DiagnosticInfo(Diagnostics.ProviderTwice, location,
                        new EquatableArray<string>(new[] { index.ToString(CultureInfo.InvariantCulture), label })));
                providers[index] = new ProviderModel(index, TypeName(provider),
                    new EquatableArray<string>(providerArgs.Select(a => a == null ? "null" : Literal(a))));
            }

            var insideType = unreachable.Count > 0;
            if (insideType && !Chain(type).All(IsPartial))
            {
                var notPartial = Chain(type).Where(t => !IsPartial(t)).Select(t => t.Name).Reverse();
                Report(Diagnostics.NotReachable, label, unreachable[0], string.Join(" and ", notPartial));
            }

            return new CommandModel(
                name,
                description,
                category,
                TypeName(type),
                type.ContainingNamespace is { IsGlobalNamespace: false } ns ? ns.ToDisplayString() : null,
                new EquatableArray<TypePart>(Chain(type).Reverse().Select(Part)),
                insideType,
                method.IsStatic,
                Identifier(method.Name),
                method.ReturnsVoid,
                new EquatableArray<ParameterModel>(parameters),
                new EquatableArray<ProviderModel>(providers.Values.OrderBy(p => p.Index)),
                hasRemainder,
                location,
                new EquatableArray<DiagnosticInfo>(diagnostics));
        }

        /// <summary>Lowercase words separated by one space, as the registry stores command names.</summary>
        internal static string NormalizeName(string name) =>
            string.Join(" ", name.Split((char[]?)null, System.StringSplitOptions.RemoveEmptyEntries)).ToLowerInvariant();

        // A constructor that string arguments can call: as many strings, or fewer before a params string[]
        private static IMethodSymbol? FindConstructor(INamedTypeSymbol provider, int count, Compilation compilation,
            INamedTypeSymbol from)
        {
            foreach (var constructor in provider.InstanceConstructors)
            {
                if (!compilation.IsSymbolAccessibleWithin(constructor, from)) continue;
                var ps = constructor.Parameters;
                var isParams = ps.Length > 0 && ps[ps.Length - 1].IsParams && ps[ps.Length - 1].Type is IArrayTypeSymbol
                {
                    ElementType.SpecialType: SpecialType.System_String
                };
                var fixedCount = isParams ? ps.Length - 1 : ps.Length;
                var required = ps.Take(fixedCount).Count(p => !p.HasExplicitDefaultValue);
                if (ps.Take(fixedCount).Any(p => p.Type.SpecialType != SpecialType.System_String)) continue;
                if (count >= required && (isParams || count <= fixedCount)) return constructor;
            }

            return null;
        }

        private static IEnumerable<INamedTypeSymbol> Chain(INamedTypeSymbol type)
        {
            for (var t = type; t != null; t = t.ContainingType)
                yield return t;
        }

        private static bool IsPartial(INamedTypeSymbol type) =>
            type.DeclaringSyntaxReferences.Any(r =>
                r.GetSyntax() is TypeDeclarationSyntax declaration &&
                declaration.Modifiers.Any(m => m.IsKind(SyntaxKind.PartialKeyword)));

        private static TypePart Part(INamedTypeSymbol type)
        {
            var keyword = type.TypeKind == TypeKind.Struct ? "struct" : "class";
            if (type.IsRecord) keyword = type.TypeKind == TypeKind.Struct ? "record struct" : "record";
            return new TypePart(keyword, Identifier(type.Name), type.IsStatic);
        }

        private static string TypeName(ITypeSymbol type)
        {
            // typeof(string?) does not compile
            if (type.IsReferenceType) type = type.WithNullableAnnotation(NullableAnnotation.NotAnnotated);
            return type.ToDisplayString(TypeFormat);
        }

        private static string Accessibility(ISymbol symbol) =>
            symbol.DeclaredAccessibility switch
            {
                Microsoft.CodeAnalysis.Accessibility.Private => "private",
                Microsoft.CodeAnalysis.Accessibility.Protected => "protected",
                Microsoft.CodeAnalysis.Accessibility.ProtectedAndInternal => "private protected",
                _ => "not accessible"
            };

        private static string Plural(int count, string word) =>
            count.ToString(CultureInfo.InvariantCulture) + " " + word + (count == 1 ? "" : "s");

        internal static string Identifier(string name) =>
            SyntaxFacts.GetKeywordKind(name) != SyntaxKind.None ? "@" + name : name;

        private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

        /// <summary>The default value as a C# expression of the parameter's type, or null when it has none.</summary>
        private static string? DefaultValue(IParameterSymbol parameter)
        {
            if (!parameter.HasExplicitDefaultValue) return null;
            var type = parameter.Type;
            var typeName = TypeName(type);
            var value = parameter.ExplicitDefaultValue;

            if (value == null)
                return type.IsValueType && type.OriginalDefinition.SpecialType != SpecialType.System_Nullable_T
                    ? $"default({typeName})"
                    : "null";

            string literal = value switch
            {
                string s => Literal(s),
                char c => SymbolDisplay.FormatLiteral(c, quote: true),
                bool b => b ? "true" : "false",
                float f when float.IsNaN(f) => "float.NaN",
                float f when float.IsPositiveInfinity(f) => "float.PositiveInfinity",
                float f when float.IsNegativeInfinity(f) => "float.NegativeInfinity",
                float f => f.ToString("R", CultureInfo.InvariantCulture) + "f",
                double d when double.IsNaN(d) => "double.NaN",
                double d when double.IsPositiveInfinity(d) => "double.PositiveInfinity",
                double d when double.IsNegativeInfinity(d) => "double.NegativeInfinity",
                double d => d.ToString("R", CultureInfo.InvariantCulture) + "d",
                decimal m => m.ToString(CultureInfo.InvariantCulture) + "m",
                _ => System.Convert.ToString(value, CultureInfo.InvariantCulture) ?? "default"
            };

            // An enum's default arrives as its underlying number; the cast gives it back its type
            return $"({typeName})({literal})";
        }
    }
}
