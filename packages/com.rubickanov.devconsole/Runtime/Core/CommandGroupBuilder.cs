using System;
using System.Collections.Generic;

namespace Rubickanov.DevConsole
{
    /// <summary>Fluent builder for defining subcommands within a command group.</summary>
    public class CommandGroupBuilder
    {
        internal readonly List<SubcommandDefinition> Subcommands = new();
        private readonly CommandRegistry _registry;

        public CommandGroupBuilder() : this(CommandRegistry.Instance) { }

        internal CommandGroupBuilder(CommandRegistry registry)
        {
            _registry = registry;
        }

        /// <summary>Adds a subcommand with a raw string-array handler.</summary>
        public CommandGroupBuilder Add(string name, Func<string[], string?> handler,
            string description = "", params IAutoCompleteProvider?[] argProviders)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Subcommand name must be non-empty.", nameof(name));
            if (handler == null) throw new ArgumentNullException(nameof(handler));

            Subcommands.Add(new SubcommandDefinition
            {
                Name = name.ToLowerInvariant(),
                Description = description,
                Handler = handler,
                ArgProviders = argProviders.Length > 0 ? argProviders : null
            });
            return this;
        }

        /// <summary>
        /// Adds a subcommand whose argument <paramref name="restFrom"/> takes the rest of the line as typed:
        /// <c>AddWithRest("set", 1, …)</c> makes <c>bind set F5 timescale 0.5</c> call the handler with
        /// <c>["F5", "timescale 0.5"]</c>. A <see cref="CommandLineProvider"/> in that position completes the rest as a
        /// command.
        /// </summary>
        public CommandGroupBuilder AddWithRest(string name, int restFrom, Func<string[], string?> handler,
            string description = "", string usage = "", params IAutoCompleteProvider?[] argProviders)
        {
            if (restFrom < 0) throw new ArgumentOutOfRangeException(nameof(restFrom));
            Add(name, handler, description, argProviders);
            Subcommands[^1].RestFrom = restFrom;
            Subcommands[^1].Usage = usage;
            return this;
        }

        // Action overloads (no return value) ----------------------------------------------------

        public CommandGroupBuilder Add(string name, Action handler, string description = "")
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Add(name, _ =>
            {
                handler();
                return null;
            }, description);
        }

        public CommandGroupBuilder Add<T1>(string name, Action<T1> handler, string description = "")
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var providers = new IAutoCompleteProvider?[] { _registry.ResolveProviderForType(typeof(T1)) };
            return Add(name, args =>
            {
                CheckArgumentCount(args, 1, name);
                var a1 = ParseTypedArg<T1>(args, 0, name);
                handler(a1);
                return null;
            }, description, providers);
        }

        public CommandGroupBuilder Add<T1, T2>(string name, Action<T1, T2> handler, string description = "")
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var providers = new IAutoCompleteProvider?[]
            {
                _registry.ResolveProviderForType(typeof(T1)),
                _registry.ResolveProviderForType(typeof(T2))
            };
            return Add(name, args =>
            {
                CheckArgumentCount(args, 2, name);
                var a1 = ParseTypedArg<T1>(args, 0, name);
                var a2 = ParseTypedArg<T2>(args, 1, name);
                handler(a1, a2);
                return null;
            }, description, providers);
        }

        public CommandGroupBuilder Add<T1, T2, T3>(string name, Action<T1, T2, T3> handler, string description = "")
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var providers = new IAutoCompleteProvider?[]
            {
                _registry.ResolveProviderForType(typeof(T1)),
                _registry.ResolveProviderForType(typeof(T2)),
                _registry.ResolveProviderForType(typeof(T3))
            };
            return Add(name, args =>
            {
                CheckArgumentCount(args, 3, name);
                var a1 = ParseTypedArg<T1>(args, 0, name);
                var a2 = ParseTypedArg<T2>(args, 1, name);
                var a3 = ParseTypedArg<T3>(args, 2, name);
                handler(a1, a2, a3);
                return null;
            }, description, providers);
        }

        // Func<string?> overloads (return optional message) -------------------------------------

        public CommandGroupBuilder Add(string name, Func<string?> handler, string description = "")
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            return Add(name, _ => handler(), description);
        }

        public CommandGroupBuilder Add<T1>(string name, Func<T1, string?> handler, string description = "")
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var providers = new IAutoCompleteProvider?[] { _registry.ResolveProviderForType(typeof(T1)) };
            return Add(name, args =>
            {
                CheckArgumentCount(args, 1, name);
                var a1 = ParseTypedArg<T1>(args, 0, name);
                return handler(a1);
            }, description, providers);
        }

        public CommandGroupBuilder Add<T1, T2>(string name, Func<T1, T2, string?> handler, string description = "")
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var providers = new IAutoCompleteProvider?[]
            {
                _registry.ResolveProviderForType(typeof(T1)),
                _registry.ResolveProviderForType(typeof(T2))
            };
            return Add(name, args =>
            {
                CheckArgumentCount(args, 2, name);
                var a1 = ParseTypedArg<T1>(args, 0, name);
                var a2 = ParseTypedArg<T2>(args, 1, name);
                return handler(a1, a2);
            }, description, providers);
        }

        public CommandGroupBuilder Add<T1, T2, T3>(string name, Func<T1, T2, T3, string?> handler,
            string description = "")
        {
            if (handler == null) throw new ArgumentNullException(nameof(handler));
            var providers = new IAutoCompleteProvider?[]
            {
                _registry.ResolveProviderForType(typeof(T1)),
                _registry.ResolveProviderForType(typeof(T2)),
                _registry.ResolveProviderForType(typeof(T3))
            };
            return Add(name, args =>
            {
                CheckArgumentCount(args, 3, name);
                var a1 = ParseTypedArg<T1>(args, 0, name);
                var a2 = ParseTypedArg<T2>(args, 1, name);
                var a3 = ParseTypedArg<T3>(args, 2, name);
                return handler(a1, a2, a3);
            }, description, providers);
        }

        private static void CheckArgumentCount(string[] args, int expected, string subName)
        {
            if (args.Length > expected)
                throw new CommandException(
                    $"Too many arguments for '{subName}': expected {expected}, got {args.Length}.");
        }

        private T ParseTypedArg<T>(string[] args, int index, string subName)
        {
            if (index >= args.Length)
                throw new CommandException($"Missing required argument #{index + 1} for '{subName}'.");

            if (!_registry.TryParseArg(args[index], typeof(T), out var parsed))
                throw new CommandException(
                    $"Cannot parse '{args[index]}' as {typeof(T).Name} for argument #{index + 1} of '{subName}'.");

            return (T)parsed!;
        }
    }
}
