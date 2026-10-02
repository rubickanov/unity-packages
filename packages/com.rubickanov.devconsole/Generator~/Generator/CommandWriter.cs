using System.Collections.Generic;
using System.Collections.Immutable;
using System.Linq;
using System.Text;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp;

namespace Rubickanov.DevConsole.Generator
{
    /// <summary>
    /// Writes the registration code: one class per assembly for the commands it can reach from outside, and a partial
    /// part inside each type whose commands it cannot. Each registers itself with <c>GeneratedCommands</c> as the domain
    /// loads, through Unity's load callbacks, which the linker keeps along with everything they call.
    /// </summary>
    internal static class CommandWriter
    {
        private const string Registry = "global::Rubickanov.DevConsole.CommandRegistry";
        private const string Catalog = "global::Rubickanov.DevConsole.GeneratedCommands";
        private const string Parameter = "global::Rubickanov.DevConsole.CommandParameter";
        private const string Provider = "global::Rubickanov.DevConsole.IAutoCompleteProvider";

        public static void Write(SourceProductionContext context, ImmutableArray<CommandModel> commands,
            AssemblyModel assembly)
        {
            foreach (var command in commands)
            foreach (var diagnostic in command.Diagnostics)
                context.ReportDiagnostic(diagnostic.ToDiagnostic());

            ReportDuplicates(context, commands);

            var valid = commands.Where(c => !c.HasErrors).ToList();
            if (valid.Count == 0) return;

            if (!assembly.HasEngine)
            {
                context.ReportDiagnostic(Diagnostic.Create(Diagnostics.NoEngine, valid[0].Location?.ToLocation(),
                    assembly.Name));
                return;
            }

            var outside = valid.Where(c => !c.InsideType).ToList();
            var assemblyClass = "DevConsoleCommands_" + Sanitize(assembly.Name);
            context.AddSource("DevConsoleCommands.g.cs", AssemblyFile(assemblyClass, outside, !assembly.HasAlwaysLink));

            foreach (var group in valid.Where(c => c.InsideType).GroupBy(c => c.DeclaringType))
            {
                var first = group.First();
                context.AddSource($"DevConsoleCommands.{Sanitize(first.DeclaringType.Replace("global::", ""))}.g.cs",
                    TypeFile(first, group.ToList()));
            }
        }

        private static void ReportDuplicates(SourceProductionContext context, ImmutableArray<CommandModel> commands)
        {
            var seen = new HashSet<string>();
            foreach (var command in commands.Where(c => c.IsStatic && c.Name.Length > 0))
            {
                if (!seen.Add(command.Name))
                    context.ReportDiagnostic(Diagnostic.Create(Diagnostics.Duplicate, command.Location?.ToLocation(),
                        command.Name));
            }
        }

        private static string AssemblyFile(string className, List<CommandModel> commands, bool alwaysLink)
        {
            var sb = Header();
            // Nothing may reference a commands-only assembly, and the linker skips such assemblies whole, load
            // callbacks included
            if (alwaysLink) sb.AppendLine("[assembly: global::UnityEngine.Scripting.AlwaysLinkAssembly]").AppendLine();
            if (commands.Count == 0) return sb.ToString();

            var statics = commands.Where(c => c.IsStatic).ToList();
            var byType = commands.Where(c => !c.IsStatic).GroupBy(c => c.DeclaringType).ToList();

            sb.AppendLine("namespace Rubickanov.DevConsole.Generated");
            sb.AppendLine("{");
            sb.AppendLine($"    internal static class {className}");
            sb.AppendLine("    {");
            AppendInstall(sb, "        ", "Install", statics.Count > 0 ? "Register" : null,
                byType.Select((g, i) => (g.Key, $"Bind{i}")));

            if (statics.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"        private static void Register({Registry} r)");
                sb.AppendLine("        {");
                foreach (var command in statics) AppendCommand(sb, "            ", command, null);
                sb.AppendLine("        }");
            }

            for (int i = 0; i < byType.Count; i++)
            {
                sb.AppendLine();
                sb.AppendLine($"        private static void Bind{i}({Registry} r, object target)");
                sb.AppendLine("        {");
                sb.AppendLine($"            var t = ({byType[i].Key})target;");
                foreach (var command in byType[i]) AppendCommand(sb, "            ", command, "t");
                sb.AppendLine("        }");
            }

            sb.AppendLine("    }");
            sb.AppendLine("}");
            return sb.ToString();
        }

        // Inside the type, where its private members are in reach; it registers itself, since code outside may not
        // be able to name the type at all
        private static string TypeFile(CommandModel first, List<CommandModel> commands)
        {
            var sb = Header();
            var indent = "";
            if (first.Namespace != null)
            {
                sb.AppendLine($"namespace {first.Namespace}");
                sb.AppendLine("{");
                indent = "    ";
            }

            foreach (var part in first.TypeChain)
            {
                sb.AppendLine($"{indent}{(part.IsStatic ? "static " : "")}partial {part.Keyword} {part.Name}");
                sb.AppendLine($"{indent}{{");
                indent += "    ";
            }

            var statics = commands.Where(c => c.IsStatic).ToList();
            var instance = commands.Where(c => !c.IsStatic).ToList();
            var bindings = instance.Count > 0
                ? new[] { (first.DeclaringType, "__DevConsoleBind") }
                : System.Array.Empty<(string, string)>();
            AppendInstall(sb, indent, "__DevConsoleInstall", statics.Count > 0 ? "__DevConsoleRegister" : null, bindings);

            if (statics.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}private static void __DevConsoleRegister({Registry} r)");
                sb.AppendLine($"{indent}{{");
                foreach (var command in statics) AppendCommand(sb, indent + "    ", command, null);
                sb.AppendLine($"{indent}}}");
            }

            if (instance.Count > 0)
            {
                sb.AppendLine();
                sb.AppendLine($"{indent}private static void __DevConsoleBind({Registry} r, object target)");
                sb.AppendLine($"{indent}{{");
                sb.AppendLine($"{indent}    var t = ({first.DeclaringType})target;");
                foreach (var command in instance) AppendCommand(sb, indent + "    ", command, "t");
                sb.AppendLine($"{indent}}}");
            }

            for (int i = first.TypeChain.Count; i > 0; i--)
            {
                indent = indent.Substring(4);
                sb.AppendLine($"{indent}}}");
            }

            if (first.Namespace != null) sb.AppendLine("}");
            return sb.ToString();
        }

        // In the editor on every domain load, so edit-mode code and tests have the commands too; in a player, and
        // again on each Play without a domain reload, at SubsystemRegistration. Adding twice is harmless.
        private static void AppendInstall(StringBuilder sb, string indent, string name, string? register,
            IEnumerable<(string Type, string Bind)> bindings)
        {
            sb.AppendLine("#if UNITY_EDITOR");
            sb.AppendLine($"{indent}[global::UnityEditor.InitializeOnLoadMethod]");
            sb.AppendLine("#endif");
            sb.AppendLine($"{indent}[global::UnityEngine.RuntimeInitializeOnLoadMethod(" +
                          "global::UnityEngine.RuntimeInitializeLoadType.SubsystemRegistration)]");
            sb.AppendLine($"{indent}private static void {name}()");
            sb.AppendLine($"{indent}{{");
            if (register != null) sb.AppendLine($"{indent}    {Catalog}.Add({register});");
            foreach (var (type, bind) in bindings)
                sb.AppendLine($"{indent}    {Catalog}.AddTarget(typeof({type}), {bind});");
            sb.AppendLine($"{indent}}}");
        }

        private static void AppendCommand(StringBuilder sb, string indent, CommandModel command, string? target)
        {
            var inner = indent + "    ";
            sb.AppendLine($"{indent}r.AddGenerated(");
            sb.AppendLine($"{inner}{Literal(command.Name)}, {Literal(command.Description)}, {Literal(command.Category)},");

            if (command.Parameters.Count == 0)
            {
                sb.AppendLine($"{inner}global::System.Array.Empty<{Parameter}>(),");
                sb.AppendLine($"{inner}global::System.Array.Empty<{Provider}?>(),");
            }
            else
            {
                sb.AppendLine($"{inner}new {Parameter}[]");
                sb.AppendLine($"{inner}{{");
                foreach (var p in command.Parameters)
                {
                    var defaultValue = p.DefaultValue == null ? "" : $", {p.DefaultValue}";
                    sb.AppendLine($"{inner}    new {Parameter}({Literal(p.Name)}, typeof({p.Type}){defaultValue}),");
                }
                sb.AppendLine($"{inner}}},");

                sb.AppendLine($"{inner}new {Provider}?[]");
                sb.AppendLine($"{inner}{{");
                for (int i = 0; i < command.Parameters.Count; i++)
                    sb.AppendLine($"{inner}    {ProviderExpression(command, i)},");
                sb.AppendLine($"{inner}}},");
            }

            sb.AppendLine($"{inner}{(command.HasRemainder ? "true" : "false")}, {target ?? "null"},");

            var args = string.Join(", ", command.Parameters.Select((p, i) => Argument(p, i)));
            var call = $"{target ?? command.DeclaringType}.{command.Method}({args})";
            var lambda = target == null ? "static a" : "a";
            sb.AppendLine(command.ReturnsVoid
                ? $"{inner}{lambda} => {{ {call}; return null; }});"
                : $"{inner}{lambda} => {call});");
        }

        private static string ProviderExpression(CommandModel command, int index)
        {
            var provider = command.Providers.FirstOrDefault(p => p.Index == index);
            if (provider == null) return "null";
            if (provider.Arguments.Count > 0)
                return $"new {provider.Type}({string.Join(", ", provider.Arguments)})";
            return $"r.SharedProvider(typeof({provider.Type}), static () => new {provider.Type}())";
        }

        // Arguments arrive parsed and boxed: a value type unboxes, nullable or not, and a reference type casts
        private static string Argument(ParameterModel parameter, int index) =>
            $"({parameter.Type})a[{index}]!";

        private static StringBuilder Header() =>
            new StringBuilder()
                .AppendLine("// <auto-generated>")
                .AppendLine("// Console command registration, written by the Rubickanov.DevConsole source generator.")
                .AppendLine("// </auto-generated>")
                .AppendLine("#nullable enable")
                .AppendLine("#pragma warning disable")
                .AppendLine();

        private static string Literal(string value) => SymbolDisplay.FormatLiteral(value, quote: true);

        private static string Sanitize(string name)
        {
            var sb = new StringBuilder(name.Length);
            foreach (var c in name) sb.Append(char.IsLetterOrDigit(c) ? c : '_');
            return sb.ToString();
        }
    }
}
