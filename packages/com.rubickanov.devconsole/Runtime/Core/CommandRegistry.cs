using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnityEngine;

namespace Rubickanov.DevConsole
{
    /// <summary>Central registry for all console commands. Discovers attributed methods and allows runtime registration.</summary>
    public class CommandRegistry
    {
        private static CommandRegistry? _instance;
        public static CommandRegistry Instance => _instance ??= new CommandRegistry();

        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics() => _instance = null;

        /// <summary>Delegate for custom argument parsers. Returns true on success and sets <paramref name="result"/>.</summary>
        public delegate bool ArgumentParserDelegate(string input, out object? result);

        private readonly Dictionary<string, RegisteredCommand> _commands = new();
        private readonly Dictionary<Type, IAutoCompleteProvider> _providerCache = new();
        private readonly Dictionary<Type, ArgumentParserDelegate> _customParsers = new();
        private readonly Dictionary<Type, IAutoCompleteProvider> _defaultProviders = new();
        private bool _initialized;

        private string[] _sortedKeys = Array.Empty<string>();
        private bool _sortedKeysDirty;
        private readonly List<string> _tokenBuffer = new();

        /// <summary>All registered commands keyed by lowercase name.</summary>
        public IReadOnlyDictionary<string, RegisteredCommand> Commands => _commands;

        /// <summary>Optional filter invoked before command execution. Return non-null to override.</summary>
        public Func<RegisteredCommand, string[], ExecutionResult?>? PreExecuteFilter;

        /// <summary>Discovers and registers all commands. Safe to call multiple times (no-op after first).</summary>
        public void Initialize()
        {
            if (_initialized) return;
            _initialized = true;

            DiscoverCommands();
            RegisterBuiltInCommands();

            Debug.Log($"[DevConsole] Registered {_commands.Count} commands.");
            ConsoleLog.LogSuccess($"Initialization complete. Registered {_commands.Count} commands.");

            DevConsole.Commands.ConsoleCommands.RunAutoexec(this);
        }

        /// <summary>Registers a custom parser for type <typeparamref name="T"/>. Returns this for chaining.</summary>
        public CommandRegistry RegisterParser<T>(Func<string, (bool ok, T? value)> parser)
        {
            if (parser == null) throw new ArgumentNullException(nameof(parser));
            _customParsers[typeof(T)] = (string input, out object? result) =>
            {
                var (ok, val) = parser(input);
                result = val;
                return ok;
            };
            return this;
        }

        /// <summary>Registers a default autocomplete provider for parameters of type <typeparamref name="T"/>.</summary>
        public CommandRegistry RegisterDefaultProvider<T>(IAutoCompleteProvider provider)
        {
            return RegisterDefaultProvider(typeof(T), provider);
        }

        /// <summary>Registers a default autocomplete provider for parameters of the given type.</summary>
        public CommandRegistry RegisterDefaultProvider(Type type, IAutoCompleteProvider provider)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            if (provider == null) throw new ArgumentNullException(nameof(provider));
            _defaultProviders[type] = provider;
            return this;
        }

        /// <summary>Registers a command at runtime. Handler receives string args and returns an optional message.</summary>
        public void Register(string name, Func<string[], string?> handler, string description = "",
            string category = "General", IAutoCompleteProvider?[]? argProviders = null)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Command name must be non-empty.", nameof(name));
            if (handler == null)
                throw new ArgumentNullException(nameof(handler));

            var key = name.ToLowerInvariant();
            _commands[key] = new RegisteredCommand
            {
                Name = key,
                Description = description,
                Category = category,
                Method = null,
                Parameters = Array.Empty<ParameterInfo>(),
                ArgProviders = argProviders,
                ManualHandler = handler
            };
            _sortedKeysDirty = true;
        }

        /// <summary>Registers a command at runtime with no return value.</summary>
        public void Register(string name, Action<string[]> action, string description = "",
            string category = "General", IAutoCompleteProvider?[]? argProviders = null)
        {
            if (action == null) throw new ArgumentNullException(nameof(action));
            Register(name, args =>
            {
                action(args);
                return null;
            }, description, category, argProviders);
        }

        /// <summary>Registers a command group with subcommands. Each subcommand gets its own handler and autocomplete providers.</summary>
        public void RegisterGroup(string name, string description, string category,
            Action<CommandGroupBuilder> configure)
        {
            if (string.IsNullOrWhiteSpace(name))
                throw new ArgumentException("Group name must be non-empty.", nameof(name));
            if (configure == null) throw new ArgumentNullException(nameof(configure));

            var builder = new CommandGroupBuilder(this);
            configure(builder);

            var subcommands = builder.Subcommands.ToArray();
            var cmdName = name.ToLowerInvariant();

            _commands[cmdName] = new RegisteredCommand
            {
                Name = cmdName,
                Description = description,
                Category = category,
                Method = null,
                Parameters = Array.Empty<ParameterInfo>(),
                ManualHandler = args => ExecuteGroup(cmdName, subcommands, args),
                Subcommands = subcommands
            };
            _sortedKeysDirty = true;
        }

        /// <summary>Shorthand alias for <see cref="RegisterGroup"/>.</summary>
        public void Group(string name, string description, string category,
            Action<CommandGroupBuilder> configure)
            => RegisterGroup(name, description, category, configure);

        /// <summary>
        /// Scans <paramref name="target"/>'s instance methods for [ConsoleCommand] attributes and registers them.
        /// Returns this for chaining.
        /// </summary>
        public CommandRegistry RegisterTarget(object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            var type = target.GetType();
            foreach (var method in type.GetMethods(BindingFlags.Instance | BindingFlags.Public |
                                                   BindingFlags.NonPublic))
            {
                var attr = method.GetCustomAttribute<ConsoleCommandAttribute>();
                if (attr != null) RegisterMethod(method, attr, target);
            }
            return this;
        }

        /// <summary>
        /// Removes all commands previously registered for <paramref name="target"/> via <see cref="RegisterTarget"/>.
        /// Returns this for chaining.
        /// </summary>
        public CommandRegistry UnregisterTarget(object target)
        {
            if (target == null) throw new ArgumentNullException(nameof(target));

            List<string>? toRemove = null;
            foreach (var kvp in _commands)
            {
                if (kvp.Value.Target != null && ReferenceEquals(kvp.Value.Target, target))
                {
                    toRemove ??= new List<string>();
                    toRemove.Add(kvp.Key);
                }
            }

            if (toRemove != null)
            {
                for (int i = 0; i < toRemove.Count; i++)
                    _commands.Remove(toRemove[i]);
                _sortedKeysDirty = true;
            }
            return this;
        }

        /// <summary>Removes a command by name. Returns true if it existed.</summary>
        public bool Unregister(string name)
        {
            var key = name.ToLowerInvariant();
            if (_commands.Remove(key))
            {
                _sortedKeysDirty = true;
                return true;
            }
            return false;
        }

        private static string? ExecuteGroup(string groupName, SubcommandDefinition[] subcommands, string[] args)
        {
            if (args.Length == 0)
            {
                var sb = new StringBuilder();
                sb.Append($"Usage: {groupName} <subcommand>\nSubcommands:");
                for (int i = 0; i < subcommands.Length; i++)
                {
                    var desc = string.IsNullOrEmpty(subcommands[i].Description)
                        ? ""
                        : $" - {subcommands[i].Description}";
                    sb.Append($"\n  {subcommands[i].Name}{desc}");
                }
                return sb.ToString();
            }

            var subName = args[0].ToLowerInvariant();
            for (int i = 0; i < subcommands.Length; i++)
            {
                if (subcommands[i].Name == subName)
                    return subcommands[i].Handler(args[1..]);
            }

            throw new CommandException($"Unknown subcommand '{args[0]}'. Type '{groupName}' for available subcommands.");
        }

        // A subcommand added with AddWithRest gets the rest of the line as its last argument, as typed
        private static string[] MergeSubcommandRest(SubcommandDefinition[] subcommands, CommandLine line, string[] args)
        {
            if (args.Length == 0) return args;

            var subName = args[0].ToLowerInvariant();
            for (int i = 0; i < subcommands.Length; i++)
            {
                var sub = subcommands[i];
                if (sub.Name != subName) continue;

                // args[0] is the subcommand, so its argument RestFrom is args[1 + RestFrom], token 2 + RestFrom
                var restArg = 1 + sub.RestFrom;
                if (sub.RestFrom < 0 || args.Length <= restArg + 1) return args;

                var merged = new string[restArg + 1];
                Array.Copy(args, merged, restArg);
                merged[restArg] = line.Remainder(restArg + 1);
                return merged;
            }

            return args;
        }

        private string[] GetSortedKeys()
        {
            if (_sortedKeysDirty)
            {
                _sortedKeys = new string[_commands.Count];
                _commands.Keys.CopyTo(_sortedKeys, 0);
                Array.Sort(_sortedKeys, StringComparer.Ordinal);
                _sortedKeysDirty = false;
            }
            return _sortedKeys;
        }

        private void DiscoverCommands()
        {
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var asmName = assembly.GetName().Name;
                if (asmName == null) continue;
                if (asmName.StartsWith("System") || asmName.StartsWith("Unity") ||
                    asmName.StartsWith("mscorlib") || asmName.StartsWith("Mono") ||
                    asmName.StartsWith("Microsoft") || asmName.StartsWith("netstandard"))
                    continue;

                try
                {
                    foreach (var type in assembly.GetTypes())
                    foreach (var method in type.GetMethods(BindingFlags.Static | BindingFlags.Public |
                                                           BindingFlags.NonPublic))
                    {
                        var attr = method.GetCustomAttribute<ConsoleCommandAttribute>();
                        if (attr != null) RegisterMethod(method, attr, null);
                    }
                }
                catch (ReflectionTypeLoadException e)
                {
                    var loaderMsg = e.LoaderExceptions.Length > 0 && e.LoaderExceptions[0] != null
                        ? e.LoaderExceptions[0]!.Message
                        : e.Message;
                    Debug.LogWarning($"[DevConsole] Skipped assembly '{asmName}': {loaderMsg}");
                }
            }
        }

        private void RegisterMethod(MethodInfo method, ConsoleCommandAttribute attr, object? target)
        {
            var parameters = method.GetParameters();
            var autoCompleteAttrs = method.GetCustomAttributes<AutoCompleteAttribute>().ToArray();
            var providers = new IAutoCompleteProvider?[parameters.Length];

            foreach (var ac in autoCompleteAttrs)
                if (ac.ArgumentIndex < providers.Length)
                    providers[ac.ArgumentIndex] = GetOrCreateProvider(ac.ProviderType, ac.ProviderArgs);

            for (int i = 0; i < parameters.Length; i++)
                providers[i] ??= ResolveProviderForType(parameters[i].ParameterType);

            if (_commands.TryGetValue(attr.Name, out _))
                Debug.LogWarning($"[DevConsole] Duplicate command '{attr.Name}', overwriting.");

            var hasRemainder = false;
            for (int i = 0; i < parameters.Length; i++)
            {
                if (parameters[i].GetCustomAttribute<RemainderAttribute>() == null) continue;
                if (i == parameters.Length - 1 && parameters[i].ParameterType == typeof(string))
                    hasRemainder = true;
                else
                    Debug.LogWarning(
                        $"[DevConsole] [Remainder] on '{parameters[i].Name}' of '{attr.Name}' ignored: it must be the last parameter and a string.");
            }

            _commands[attr.Name] = new RegisteredCommand
            {
                Name = attr.Name,
                Description = attr.Description,
                Category = attr.Category,
                Method = method,
                Target = target,
                Parameters = parameters,
                ArgProviders = providers,
                HasRemainder = hasRemainder
            };
            _sortedKeysDirty = true;
        }

        internal IAutoCompleteProvider? ResolveProviderForType(Type paramType)
        {
            paramType = Nullable.GetUnderlyingType(paramType) ?? paramType;
            if (_defaultProviders.TryGetValue(paramType, out var defaultProvider))
                return defaultProvider;
            if (paramType.IsEnum)
                return GetOrCreateProvider(typeof(EnumAutoCompleteProvider), paramType);
            if (paramType == typeof(bool))
                return BoolAutoCompleteProvider.Instance;
            return null;
        }

        private IAutoCompleteProvider? GetOrCreateProvider(Type providerType, params object[] args)
        {
            if (args.Length > 0)
            {
                try
                {
                    return (IAutoCompleteProvider?)Activator.CreateInstance(providerType, args);
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DevConsole] Failed to create provider {providerType.Name}: {e.Message}");
                    return null;
                }
            }

            if (!_providerCache.TryGetValue(providerType, out var provider))
            {
                try
                {
                    provider = (IAutoCompleteProvider?)Activator.CreateInstance(providerType, args);
                    if (provider != null)
                        _providerCache[providerType] = provider;
                }
                catch (Exception e)
                {
                    Debug.LogError($"[DevConsole] Failed to create provider {providerType.Name}: {e.Message}");
                    return null;
                }
            }

            return provider;
        }

        public struct ExecutionResult
        {
            public bool Success;
            public string? Message;
            public static ExecutionResult Ok(string? msg = "") => new() { Success = true, Message = msg };
            public static ExecutionResult Error(string msg) => new() { Success = false, Message = msg };
        }

        /// <summary>Parses and executes a raw command string.</summary>
        public ExecutionResult Execute(string rawInput) => Execute(rawInput, 0);

        /// <summary>
        /// Echoes <paramref name="rawInput"/> to <see cref="ConsoleLog"/>, executes it and logs the result's message, as
        /// typing it into the console would.
        /// </summary>
        public ExecutionResult ExecuteAndLog(string rawInput)
        {
            ConsoleLog.LogInput(rawInput);
            var result = Execute(rawInput);
            if (!string.IsNullOrEmpty(result.Message))
            {
                if (result.Success)
                    ConsoleLog.Log(result.Message!);
                else
                    ConsoleLog.LogError(result.Message!);
            }

            return result;
        }

        private ExecutionResult Execute(string rawInput, int aliasDepth)
        {
            if (string.IsNullOrWhiteSpace(rawInput)) return ExecutionResult.Error("Empty command.");

            var statements = new List<string>();
            CommandLine.SplitStatements(rawInput, statements);
            if (statements.Count == 0) return ExecutionResult.Error("Empty command.");
            if (statements.Count == 1) return ExecuteStatement(statements[0], aliasDepth);

            // Every command of a chain runs, like a shell's `a; b`. The ones before the last log their own result,
            // since only one result goes back to the caller.
            for (int i = 0; i < statements.Count - 1; i++)
            {
                var result = ExecuteStatement(statements[i], aliasDepth);

                // `wait` hands what is left of the chain to DeferredCommands
                if (PendingWait is { } frames)
                {
                    PendingWait = null;
                    DeferredCommands.Schedule(this, string.Join("; ", statements.GetRange(i + 1, statements.Count - i - 1)), frames);
                    return result;
                }

                if (string.IsNullOrEmpty(result.Message)) continue;
                if (result.Success)
                    ConsoleLog.Log(result.Message!);
                else
                    ConsoleLog.LogError(result.Message!);
            }

            return ExecuteStatement(statements[^1], aliasDepth);
        }

        /// <summary>
        /// Frames the last executed statement asked to wait, set by <c>wait</c> and cleared when the next statement
        /// starts. A chain or file checks it after each statement and defers the rest; left set after the last
        /// statement of an alias, it delays what follows the alias in the caller's chain.
        /// </summary>
        internal int? PendingWait;

        private ExecutionResult ExecuteStatement(string rawInput, int aliasDepth)
        {
            PendingWait = null;

            var line = new CommandLine(rawInput);
            if (line.Count == 0) return ExecutionResult.Error("Empty command.");

            var cmdName = line.Tokens[0].ToLowerInvariant();
            var args = line.Tokens.GetRange(1, line.Count - 1).ToArray();

            // Alias expansion
            if (!_commands.ContainsKey(cmdName) && AliasRegistry.Instance.TryResolve(cmdName, out var aliasCommand))
            {
                if (aliasDepth >= 8)
                    return ExecutionResult.Error("Alias recursion limit reached (max 8).");

                return Execute(AliasRegistry.Expand(aliasCommand, line), aliasDepth + 1);
            }

            if (!_commands.TryGetValue(cmdName, out var cmd))
                return ExecutionResult.Error($"Unknown command: '{cmdName}'. Type 'help' for available commands.");

            if (cmd.Subcommands != null)
                args = MergeSubcommandRest(cmd.Subcommands, line, args);

            if (PreExecuteFilter != null)
            {
                var overrideResult = PreExecuteFilter(cmd, args);
                if (overrideResult.HasValue) return overrideResult.Value;
            }

            if (cmd.ManualHandler != null)
            {
                try
                {
                    var msg = cmd.ManualHandler(args);
                    return ExecutionResult.Ok(msg);
                }
                catch (CommandException e)
                {
                    return ExecutionResult.Error(e.Message);
                }
                catch (Exception e)
                {
                    return ExecutionResult.Error($"Error: {e.Message}");
                }
            }

            return ExecuteReflection(cmd, line, args);
        }

        private ExecutionResult ExecuteReflection(RegisteredCommand cmd, CommandLine line, string[] args)
        {
            var parameters = cmd.Parameters;
            var parsedArgs = new object?[parameters.Length];

            // Extra words used to be dropped without a word, so `echo hello world` printed only "hello"
            if (!cmd.HasRemainder && args.Length > parameters.Length)
                return ExecutionResult.Error(
                    $"Too many arguments: expected at most {parameters.Length}, got {args.Length}.\nUsage: {cmd.GetUsageString()}");

            for (int i = 0; i < parameters.Length; i++)
            {
                if (cmd.HasRemainder && i == parameters.Length - 1 && i < args.Length)
                    parsedArgs[i] = line.Remainder(i + 1);
                else if (i < args.Length)
                {
                    if (!TryParseArg(args[i], parameters[i].ParameterType, out parsedArgs[i]))
                        return ExecutionResult.Error(
                            $"Cannot parse '{args[i]}' as {parameters[i].ParameterType.Name} for '{parameters[i].Name}'.\nUsage: {cmd.GetUsageString()}");
                }
                else if (parameters[i].HasDefaultValue)
                    parsedArgs[i] = parameters[i].DefaultValue!;
                else
                    return ExecutionResult.Error(
                        $"Missing required argument '{parameters[i].Name}'.\nUsage: {cmd.GetUsageString()}");
            }

            try
            {
                var result = cmd.Method!.Invoke(cmd.Target, parsedArgs);
                return result != null ? ExecutionResult.Ok(result.ToString()) : ExecutionResult.Ok();
            }
            catch (TargetInvocationException e) when (e.InnerException is CommandException)
            {
                return ExecutionResult.Error(e.InnerException.Message);
            }
            catch (TargetInvocationException e)
            {
                return ExecutionResult.Error($"Command error: {e.InnerException?.Message ?? e.Message}");
            }
            catch (Exception e)
            {
                return ExecutionResult.Error($"Execution error: {e.Message}");
            }
        }

        /// <summary>Parses <paramref name="input"/> as <paramref name="targetType"/>. Consults custom parsers first, then built-ins.</summary>
        public bool TryParseArg(string input, Type targetType, out object? result)
        {
            result = null;

            if (_customParsers.TryGetValue(targetType, out var custom))
                return custom(input, out result);

            // An optional argument is declared as `int? value = null`, so leaving it out reads as "show the current
            // value" without a sentinel like -1 leaking into the usage string
            var underlying = Nullable.GetUnderlyingType(targetType);
            if (underlying != null)
                return TryParseArg(input, underlying, out result);

            try
            {
                if (targetType == typeof(string))
                {
                    result = input;
                    return true;
                }

                if (targetType == typeof(int))
                {
                    result = int.Parse(input, CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(float))
                {
                    result = float.Parse(input, CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(ulong))
                {
                    result = ulong.Parse(input, CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(long))
                {
                    result = long.Parse(input, CultureInfo.InvariantCulture);
                    return true;
                }

                if (targetType == typeof(bool))
                {
                    result = bool.Parse(input);
                    return true;
                }

                if (targetType.IsEnum)
                {
                    result = Enum.Parse(targetType, input, true);
                    return true;
                }

                if (targetType == typeof(Vector3))
                {
                    var p = input.Split(',');
                    if (p.Length == 3)
                    {
                        result = new Vector3(
                            float.Parse(p[0].Trim(), CultureInfo.InvariantCulture),
                            float.Parse(p[1].Trim(), CultureInfo.InvariantCulture),
                            float.Parse(p[2].Trim(), CultureInfo.InvariantCulture));
                        return true;
                    }
                }

                return false;
            }
            catch
            {
                return false;
            }
        }

        /// <summary>Fills <paramref name="results"/> with autocomplete suggestions for the current input. Zero-alloc.</summary>
        public void GetSuggestions(string input, List<string> results, int maxResults = 10)
            => GetSuggestions(input, results, maxResults, 0);

        private void GetSuggestions(string input, List<string> results, int maxResults, int aliasDepth)
        {
            var sortedKeys = GetSortedKeys();

            // Only the command being typed counts: `timescale 1; qu` completes `qu`
            var statementStart = CommandLine.LastStatementStart(input);
            if (statementStart > 0)
                input = input.Substring(statementStart).TrimStart();

            if (string.IsNullOrEmpty(input))
            {
                int count = Math.Min(sortedKeys.Length, maxResults);
                for (int i = 0; i < count; i++)
                    results.Add(sortedKeys[i]);
                return;
            }

            _tokenBuffer.Clear();
            Tokenize(input, _tokenBuffer);
            // Non-empty input can still tokenize to zero tokens (leading space, or a lone
            // quote char). Bail before the _tokenBuffer[0] access below, which would otherwise
            // throw IndexOutOfRangeException on a normal keystroke.
            if (_tokenBuffer.Count == 0) return;
            var endsWithSpace = input[input.Length - 1] == ' ';

            if (_tokenBuffer.Count == 1 && !endsWithSpace)
            {
                var partial = _tokenBuffer[0].ToLowerInvariant();
                for (int i = 0; i < sortedKeys.Length; i++)
                {
                    if (sortedKeys[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    {
                        results.Add(sortedKeys[i]);
                        if (results.Count >= maxResults) return;
                    }
                }

                var aliasNames = AliasRegistry.Instance.SortedNames;
                for (int i = 0; i < aliasNames.Count; i++)
                {
                    if (aliasNames[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase) &&
                        !_commands.ContainsKey(aliasNames[i]))
                    {
                        results.Add(aliasNames[i]);
                        if (results.Count >= maxResults) return;
                    }
                }

                return;
            }

            var cmdName = _tokenBuffer[0].ToLowerInvariant();
            if (!_commands.TryGetValue(cmdName, out var cmd))
            {
                // An alias that only prefixes one command completes as that command would: `tp ` after
                // `alias tp teleport` suggests teleport's arguments. One with $ or ; has no such single place.
                if (aliasDepth < 8 && AliasRegistry.Instance.TryResolve(cmdName, out var aliasCommand) &&
                    aliasCommand.IndexOf('$') < 0 && aliasCommand.IndexOf(';') < 0)
                {
                    var call = new CommandLine(input);
                    GetSuggestions(aliasCommand + input.Substring(call.End(0)), results, maxResults, aliasDepth + 1);
                }

                return;
            }

            var argIndex = endsWithSpace ? _tokenBuffer.Count - 1 : _tokenBuffer.Count - 2;
            var partial2 = endsWithSpace ? "" : _tokenBuffer[_tokenBuffer.Count - 1];

            // Subcommand-aware autocomplete
            if (cmd.Subcommands != null)
            {
                if (argIndex == 0)
                {
                    // Suggest subcommand names
                    for (int i = 0; i < cmd.Subcommands.Length; i++)
                    {
                        if (string.IsNullOrEmpty(partial2) ||
                            cmd.Subcommands[i].Name.StartsWith(partial2, StringComparison.OrdinalIgnoreCase))
                        {
                            results.Add(cmd.Subcommands[i].Name);
                            if (results.Count >= maxResults) return;
                        }
                    }
                    return;
                }

                // Look up the subcommand's providers
                var subToken = _tokenBuffer[1].ToLowerInvariant();
                SubcommandDefinition? matchedSub = null;
                for (int i = 0; i < cmd.Subcommands.Length; i++)
                {
                    if (cmd.Subcommands[i].Name == subToken)
                    {
                        matchedSub = cmd.Subcommands[i];
                        break;
                    }
                }

                if (matchedSub?.ArgProviders == null) return;

                var subArgIndex = argIndex - 1;
                if (TryNestedSuggestions(matchedSub.ArgProviders, subArgIndex, 2, input, results, maxResults, aliasDepth))
                    return;
                if (subArgIndex < 0 || subArgIndex >= matchedSub.ArgProviders.Length) return;

                var subProvider = matchedSub.ArgProviders[subArgIndex];
                if (subProvider == null) return;

                int subCountBefore = results.Count;
                subProvider.GetSuggestions(partial2, results);
                if (results.Count - subCountBefore > maxResults)
                    results.RemoveRange(subCountBefore + maxResults, results.Count - subCountBefore - maxResults);
                return;
            }

            if (TryNestedSuggestions(cmd.ArgProviders, argIndex, 1, input, results, maxResults, aliasDepth))
                return;

            if (cmd.ArgProviders == null || argIndex >= cmd.ArgProviders.Length || argIndex < 0)
                return;

            var provider = cmd.ArgProviders[argIndex];
            if (provider == null) return;

            int countBefore = results.Count;
            provider.GetSuggestions(partial2, results);

            // Trim to maxResults
            if (results.Count - countBefore > maxResults)
                results.RemoveRange(countBefore + maxResults, results.Count - countBefore - maxResults);
        }

        // An argument completed by CommandLineProvider is itself a command line: `bind set F5 time` completes `time`
        // as a command, and its arguments after that as that command's. It is always the last argument.
        private bool TryNestedSuggestions(IAutoCompleteProvider?[]? providers, int argIndex, int firstArgToken,
            string input, List<string> results, int maxResults, int depth)
        {
            if (providers == null || providers.Length == 0) return false;
            var last = providers.Length - 1;
            if (providers[last] is not CommandLineProvider || argIndex < last) return false;
            if (depth >= 8) return true;

            var line = new CommandLine(input);
            var tokenIndex = firstArgToken + last;
            var nested = tokenIndex < line.Count ? input.Substring(line.Start(tokenIndex)) : "";
            GetSuggestions(nested, results, maxResults, depth + 1);
            return true;
        }

        /// <summary>
        /// Returns <paramref name="input"/> with <paramref name="suggestion"/> accepted: it replaces the word being typed
        /// in the last <c>;</c>-separated command, or is appended after a trailing space, followed by a space. The rest
        /// of the line stays as typed, quotes included, and a suggestion containing a space is quoted.
        /// </summary>
        public static string ApplySuggestion(string input, string suggestion)
        {
            if (suggestion.IndexOf(' ') >= 0) suggestion = "\"" + suggestion + "\"";

            if (input.Length == 0 || input[^1] == ' ' || input[^1] == ';')
                return input + suggestion + " ";

            var statementStart = CommandLine.LastStatementStart(input);
            var line = new CommandLine(input.Substring(statementStart));
            if (line.Count == 0)
                return input + suggestion + " ";

            return input.Substring(0, statementStart + line.Start(line.Count - 1)) + suggestion + " ";
        }

        /// <summary>Splits input into tokens, respecting quoted strings. Returns a new array.</summary>
        public static string[] Tokenize(string input)
        {
            var tokens = new List<string>();
            Tokenize(input, tokens);
            return tokens.ToArray();
        }

        /// <summary>Splits input into tokens, appending to <paramref name="tokens"/>. Zero-alloc (except token strings).</summary>
        public static void Tokenize(string input, List<string> tokens) => CommandLine.Tokenize(input, tokens);

        private string? Help(string[] args)
        {
            if (args.Length == 0)
            {
                LogCommands(_commands.Values);
                LogAliases();
                return null;
            }

            var topic = string.Join(" ", args);
            var key = topic.ToLowerInvariant();

            if (_commands.TryGetValue(key, out var cmd))
            {
                ConsoleLog.Log($"<b>{cmd.GetUsageString()}</b>");
                if (!string.IsNullOrEmpty(cmd.Description)) ConsoleLog.Log($"  {cmd.Description}");
                ConsoleLog.Log($"  Category: {cmd.Category}");

                if (cmd.Subcommands != null)
                {
                    ConsoleLog.Log("\n  Subcommands:");
                    for (int i = 0; i < cmd.Subcommands.Length; i++)
                    {
                        var sub = cmd.Subcommands[i];
                        var desc = string.IsNullOrEmpty(sub.Description) ? "" : $" - {sub.Description}";
                        ConsoleLog.Log($"    {cmd.GetSubcommandUsageString(sub)}{desc}");
                    }
                }

                // `help scene` names both the command and the Scene category
                var sameNamed = _commands.Values
                    .Where(c => string.Equals(c.Category, topic, StringComparison.OrdinalIgnoreCase))
                    .OrderBy(c => c.Name, StringComparer.Ordinal).ToList();
                if (sameNamed.Count > 0)
                    ConsoleLog.Log(
                        $"\n  Commands in category {sameNamed[0].Category}: {string.Join(", ", sameNamed.Select(c => c.Name))}");

                return null;
            }

            if (AliasRegistry.Instance.TryResolve(key, out var aliasCommand))
            {
                ConsoleLog.Log($"<b>{key}</b> is an alias for: {aliasCommand}");
                return null;
            }

            var inCategory = _commands.Values
                .Where(c => string.Equals(c.Category, topic, StringComparison.OrdinalIgnoreCase)).ToList();
            if (inCategory.Count > 0)
            {
                LogCommands(inCategory);
                return null;
            }

            // Otherwise a search: `help time` finds timescale and whatever mentions time in its description
            var matches = _commands.Values.Where(c => Mentions(c, topic)).ToList();
            if (matches.Count == 0)
                throw new CommandException($"No command, category or description matches '{topic}'.");

            LogCommands(matches);
            return null;
        }

        private static bool Mentions(RegisteredCommand cmd, string text)
        {
            if (cmd.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                cmd.Description.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                return true;

            if (cmd.Subcommands == null) return false;
            foreach (var sub in cmd.Subcommands)
            {
                if (sub.Name.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0 ||
                    sub.Description.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
                    return true;
            }

            return false;
        }

        private static void LogCommands(IEnumerable<RegisteredCommand> commands)
        {
            foreach (var group in commands.GroupBy(c => c.Category).OrderBy(g => g.Key))
            {
                ConsoleLog.Log($"\n<b>=== {group.Key} ===</b>");
                foreach (var c in group.OrderBy(c => c.Name))
                {
                    var desc = string.IsNullOrEmpty(c.Description) ? "" : $" - {c.Description}";
                    ConsoleLog.Log($"  {c.Name}{desc}");
                }
            }
        }

        private static void LogAliases()
        {
            var aliases = AliasRegistry.Instance;
            var names = aliases.SortedNames;
            if (names.Count == 0) return;

            ConsoleLog.Log("\n<b>=== Aliases ===</b>");
            for (int i = 0; i < names.Count; i++)
                ConsoleLog.Log($"  {names[i]} → {aliases.Aliases[names[i]]}");
        }

        /// <summary>Suggests command names, alias names and categories after <c>help</c>.</summary>
        private sealed class HelpTopicProvider : IAutoCompleteProvider
        {
            private readonly CommandRegistry _registry;
            private readonly List<string> _categories = new();

            public HelpTopicProvider(CommandRegistry registry) => _registry = registry;

            public string Hint => "<topic>";

            public void GetSuggestions(string partial, List<string> results)
            {
                var keys = _registry.GetSortedKeys();
                for (int i = 0; i < keys.Length; i++)
                    if (keys[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase)) results.Add(keys[i]);

                var aliases = AliasRegistry.Instance.SortedNames;
                for (int i = 0; i < aliases.Count; i++)
                    if (aliases[i].StartsWith(partial, StringComparison.OrdinalIgnoreCase)) results.Add(aliases[i]);

                _categories.Clear();
                foreach (var cmd in _registry._commands.Values)
                {
                    if (!_categories.Contains(cmd.Category) &&
                        cmd.Category.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                        _categories.Add(cmd.Category);
                }

                _categories.Sort(StringComparer.OrdinalIgnoreCase);
                results.AddRange(_categories);
            }
        }

        private void RegisterBuiltInCommands()
        {
            Register("help", Help,
                "List commands; help <command> for details, help <category> or help <text> to narrow the list",
                "System", new IAutoCompleteProvider?[] { new HelpTopicProvider(this) });

            Register("clear", _ =>
            {
                ConsoleLog.Clear();
                return null;
            }, "Clear console output", "System");

            DevConsole.Commands.ConsoleCommands.Register(this);
        }
    }
}
