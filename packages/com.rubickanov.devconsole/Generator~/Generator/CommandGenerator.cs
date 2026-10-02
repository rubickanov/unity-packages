using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CSharp.Syntax;

namespace Rubickanov.DevConsole.Generator
{
    /// <summary>
    /// Finds the <c>[ConsoleCommand]</c> methods of an assembly and writes the code that registers and calls them, so
    /// the console needs no reflection and code stripping sees every command, provider and parameter as plain code.
    /// Unity runs it for the console's assembly and every assembly that references it.
    /// </summary>
    [Generator(LanguageNames.CSharp)]
    public sealed class CommandGenerator : IIncrementalGenerator
    {
        private const string CommandAttribute = "Rubickanov.DevConsole.ConsoleCommandAttribute";

        public void Initialize(IncrementalGeneratorInitializationContext context)
        {
            var commands = context.SyntaxProvider
                .ForAttributeWithMetadataName(CommandAttribute,
                    static (node, _) => node is MethodDeclarationSyntax,
                    static (ctx, cancellation) => CommandReader.Read(ctx, cancellation))
                .Where(static command => command != null)
                .Select(static (command, _) => command!);

            var assembly = context.CompilationProvider.Select(static (compilation, _) => AssemblyModel.From(compilation));

            context.RegisterSourceOutput(commands.Collect().Combine(assembly),
                static (spc, input) => CommandWriter.Write(spc, input.Left, input.Right));
        }
    }
}
