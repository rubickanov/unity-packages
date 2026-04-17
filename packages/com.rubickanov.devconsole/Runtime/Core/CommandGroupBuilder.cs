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
                if (!TryParseTypedArg<T1>(args, 0, name, out var a1, out var err)) return err;
                handler(a1!);
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
                if (!TryParseTypedArg<T1>(args, 0, name, out var a1, out var err)) return err;
                if (!TryParseTypedArg<T2>(args, 1, name, out var a2, out err)) return err;
                handler(a1!, a2!);
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
                if (!TryParseTypedArg<T1>(args, 0, name, out var a1, out var err)) return err;
                if (!TryParseTypedArg<T2>(args, 1, name, out var a2, out err)) return err;
                if (!TryParseTypedArg<T3>(args, 2, name, out var a3, out err)) return err;
                handler(a1!, a2!, a3!);
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
                if (!TryParseTypedArg<T1>(args, 0, name, out var a1, out var err)) return err;
                return handler(a1!);
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
                if (!TryParseTypedArg<T1>(args, 0, name, out var a1, out var err)) return err;
                if (!TryParseTypedArg<T2>(args, 1, name, out var a2, out err)) return err;
                return handler(a1!, a2!);
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
                if (!TryParseTypedArg<T1>(args, 0, name, out var a1, out var err)) return err;
                if (!TryParseTypedArg<T2>(args, 1, name, out var a2, out err)) return err;
                if (!TryParseTypedArg<T3>(args, 2, name, out var a3, out err)) return err;
                return handler(a1!, a2!, a3!);
            }, description, providers);
        }

        private bool TryParseTypedArg<T>(string[] args, int index, string subName, out T? value, out string? error)
        {
            if (index >= args.Length)
            {
                value = default;
                error = $"Missing required argument #{index + 1} for '{subName}'.";
                return false;
            }

            if (!_registry.TryParseArg(args[index], typeof(T), out var parsed))
            {
                value = default;
                error = $"Cannot parse '{args[index]}' as {typeof(T).Name} for argument #{index + 1} of '{subName}'.";
                return false;
            }

            value = (T?)parsed;
            error = null;
            return true;
        }
    }
}
