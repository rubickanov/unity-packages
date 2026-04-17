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

            return $"Unknown subcommand '{args[0]}'. Type '{groupName}' for available subcommands.";
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
            {
                if (providers[i] != null) continue;
                var paramType = parameters[i].ParameterType;
                if (_defaultProviders.TryGetValue(paramType, out var defaultProvider))
                    providers[i] = defaultProvider;
                else if (paramType.IsEnum)
                    providers[i] = GetOrCreateProvider(typeof(EnumAutoCompleteProvider), paramType);
                else if (paramType == typeof(bool))
                    providers[i] = BoolAutoCompleteProvider.Instance;
            }

            if (_commands.TryGetValue(attr.Name, out _))
                Debug.LogWarning($"[DevConsole] Duplicate command '{attr.Name}', overwriting.");

            _commands[attr.Name] = new RegisteredCommand
            {
                Name = attr.Name,
                Description = attr.Description,
                Category = attr.Category,
                Method = method,
                Target = target,
                Parameters = parameters,
                ArgProviders = providers
            };
            _sortedKeysDirty = true;
        }

        internal IAutoCompleteProvider? ResolveProviderForType(Type paramType)
        {
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

        private ExecutionResult Execute(string rawInput, int aliasDepth)
        {
            if (string.IsNullOrWhiteSpace(rawInput)) return ExecutionResult.Error("Empty command.");

            var tokens = Tokenize(rawInput);
            if (tokens.Length == 0) return ExecutionResult.Error("Empty command.");

            var cmdName = tokens[0].ToLowerInvariant();
            var args = tokens[1..];

            // Alias expansion
            if (!_commands.ContainsKey(cmdName) && AliasRegistry.Instance.TryResolve(cmdName, out var aliasCommand))
            {
                if (aliasDepth >= 8)
                    return ExecutionResult.Error("Alias recursion limit reached (max 8).");

                // Substitute: alias value + remaining args
                var expanded = args.Length > 0
                    ? aliasCommand + " " + string.Join(" ", args)
                    : aliasCommand;
                return Execute(expanded, aliasDepth + 1);
            }

            if (!_commands.TryGetValue(cmdName, out var cmd))
                return ExecutionResult.Error($"Unknown command: '{cmdName}'. Type 'help' for available commands.");

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
                catch (Exception e)
                {
                    return ExecutionResult.Error($"Error: {e.Message}");
                }
            }

            return ExecuteReflection(cmd, args);
        }

        private ExecutionResult ExecuteReflection(RegisteredCommand cmd, string[] args)
        {
            var parameters = cmd.Parameters;
            var parsedArgs = new object?[parameters.Length];

            for (int i = 0; i < parameters.Length; i++)
            {
                if (i < args.Length)
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
        {
            var sortedKeys = GetSortedKeys();

            if (string.IsNullOrEmpty(input))
            {
                int count = Math.Min(sortedKeys.Length, maxResults);
                for (int i = 0; i < count; i++)
                    results.Add(sortedKeys[i]);
                return;
            }

            _tokenBuffer.Clear();
            Tokenize(input, _tokenBuffer);
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

                return;
            }

            var cmdName = _tokenBuffer[0].ToLowerInvariant();
            if (!_commands.TryGetValue(cmdName, out var cmd)) return;

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
                if (subArgIndex < 0 || subArgIndex >= matchedSub.ArgProviders.Length) return;

                var subProvider = matchedSub.ArgProviders[subArgIndex];
                if (subProvider == null) return;

                int subCountBefore = results.Count;
                subProvider.GetSuggestions(partial2, results);
                if (results.Count - subCountBefore > maxResults)
                    results.RemoveRange(subCountBefore + maxResults, results.Count - subCountBefore - maxResults);
                return;
            }

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

        /// <summary>Splits input into tokens, respecting quoted strings. Returns a new array.</summary>
        public static string[] Tokenize(string input)
        {
            var tokens = new List<string>();
            Tokenize(input, tokens);
            return tokens.ToArray();
        }

        /// <summary>Splits input into tokens, appending to <paramref name="tokens"/>. Zero-alloc (except token strings).</summary>
        public static void Tokenize(string input, List<string> tokens)
        {
            var current = new StringBuilder();
            var inQuotes = false;

            for (int i = 0; i < input.Length; i++)
            {
                var c = input[i];
                if (c == '"')
                {
                    inQuotes = !inQuotes;
                    continue;
                }

                if (c == ' ' && !inQuotes)
                {
                    if (current.Length > 0)
                    {
                        tokens.Add(current.ToString());
                        current.Clear();
                    }

                    continue;
                }

                current.Append(c);
            }

            if (current.Length > 0) tokens.Add(current.ToString());
        }

        private void RegisterBuiltInCommands()
        {
            Register("help", args =>
            {
                if (args.Length > 0 && _commands.TryGetValue(args[0].ToLowerInvariant(), out var cmd))
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

                    return null;
                }

                foreach (var group in _commands.Values.GroupBy(c => c.Category).OrderBy(g => g.Key))
                {
                    ConsoleLog.Log($"\n<b>=== {group.Key} ===</b>");
                    foreach (var c in group.OrderBy(c => c.Name))
                    {
                        var desc = string.IsNullOrEmpty(c.Description) ? "" : $" - {c.Description}";
                        ConsoleLog.Log($"  {c.Name}{desc}");
                    }
                }

                return null;
            }, "Show all commands or details for a specific command", "System");

            Register("clear", _ =>
            {
                ConsoleLog.Clear();
                return null;
            }, "Clear console output", "System");
        }
    }
}
