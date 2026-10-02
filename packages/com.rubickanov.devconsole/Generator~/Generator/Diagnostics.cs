using Microsoft.CodeAnalysis;

namespace Rubickanov.DevConsole.Generator
{
    internal static class Diagnostics
    {
        private const string Category = "DevConsole";

        public static readonly DiagnosticDescriptor EmptyName = Error("DEVCON001",
            "Console command without a name",
            "[ConsoleCommand] on '{0}' has an empty name");

        public static readonly DiagnosticDescriptor Generic = Error("DEVCON002",
            "Generic console command",
            "Console command '{0}' is generic or in a generic type; the console cannot choose its type arguments");

        public static readonly DiagnosticDescriptor ByReference = Error("DEVCON003",
            "Console command with a ref, out or in parameter",
            "Parameter '{0}' of console command '{1}' is passed by reference; the console passes arguments by value");

        public static readonly DiagnosticDescriptor NotReachable = Error("DEVCON004",
            "Console command the generated code cannot reach",
            "Console command '{0}' cannot be called from generated code: {1}. Make it internal or public, or declare " +
            "{2} partial so the code is generated inside.");

        public static readonly DiagnosticDescriptor ArgumentIndex = Error("DEVCON005",
            "[AutoComplete] past the last parameter",
            "[AutoComplete({0}, ...)] on console command '{1}' names a parameter it does not have; it has {2}");

        public static readonly DiagnosticDescriptor NotAProvider = Error("DEVCON006",
            "[AutoComplete] type is not a provider",
            "'{0}' in [AutoComplete] on console command '{1}' must be a non-abstract class implementing IAutoCompleteProvider");

        public static readonly DiagnosticDescriptor NoConstructor = Error("DEVCON007",
            "Provider without a fitting constructor",
            "'{0}' has no accessible constructor taking {1} string argument(s), as [AutoComplete] on console command " +
            "'{2}' passes");

        public static readonly DiagnosticDescriptor Remainder = Error("DEVCON008",
            "[Remainder] not on the last string parameter",
            "[Remainder] on '{0}' of console command '{1}' must be on its last parameter, a string");

        public static readonly DiagnosticDescriptor InstanceKind = Error("DEVCON009",
            "Instance console command outside a class",
            "Instance console command '{0}' is declared in {1}; instance commands belong to classes, " +
            "registered with RegisterTarget");

        public static readonly DiagnosticDescriptor Duplicate = Warning("DEVCON010",
            "Two static console commands with one name",
            "Console command '{0}' is declared again here; the one registered last replaces the other");

        public static readonly DiagnosticDescriptor NoEngine = Error("DEVCON011",
            "Console commands in an assembly without engine references",
            "Assembly '{0}' declares console commands but does not reference UnityEngine, which the registration " +
            "code needs; turn off No Engine References in its assembly definition");

        public static readonly DiagnosticDescriptor ProviderTwice = Warning("DEVCON012",
            "Two [AutoComplete] for one parameter",
            "Parameter {0} of console command '{1}' has more than one [AutoComplete]; the last one is used");

        private static DiagnosticDescriptor Error(string id, string title, string message) =>
            new(id, title, message, Category, DiagnosticSeverity.Error, isEnabledByDefault: true);

        private static DiagnosticDescriptor Warning(string id, string title, string message) =>
            new(id, title, message, Category, DiagnosticSeverity.Warning, isEnabledByDefault: true);
    }
}
