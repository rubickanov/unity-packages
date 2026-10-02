using System;
using System.Collections.Generic;
using System.ComponentModel;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// What the source generator wrote for every assembly that references the console: one registration of its static
    /// <c>[ConsoleCommand]</c> methods, and one binding per type with instance ones. Generated code adds them as the
    /// domain loads, in the editor and in a player alike; nothing here is for hand-written code.
    /// </summary>
    [EditorBrowsable(EditorBrowsableState.Never)]
    public static class GeneratedCommands
    {
        // Kept across Play sessions: the code that adds them runs once per domain, or once per Play when domain
        // reload is off and SubsystemRegistration calls it again, so an add is ignored when already there
        private static readonly List<Action<CommandRegistry>> StaticCommands = new();
        private static readonly Dictionary<Type, Action<CommandRegistry, object>> TargetBindings = new();

        /// <summary>Adds the registration of static commands of an assembly or a type.</summary>
        public static void Add(Action<CommandRegistry> register)
        {
            if (register == null) throw new ArgumentNullException(nameof(register));
            if (!StaticCommands.Contains(register)) StaticCommands.Add(register);
        }

        /// <summary>Adds the binding of the instance commands declared in <paramref name="type"/>.</summary>
        public static void AddTarget(Type type, Action<CommandRegistry, object> bind)
        {
            if (type == null) throw new ArgumentNullException(nameof(type));
            TargetBindings[type] = bind ?? throw new ArgumentNullException(nameof(bind));
        }

        internal static void RegisterStatic(CommandRegistry registry)
        {
            for (int i = 0; i < StaticCommands.Count; i++)
                StaticCommands[i](registry);
        }

        /// <summary>
        /// Registers the instance commands of <paramref name="target"/>, those its base types declare first, so a type's
        /// own command wins over a base's of the same name. False when no type of it declares any.
        /// </summary>
        internal static bool BindTarget(CommandRegistry registry, object target)
        {
            List<Action<CommandRegistry, object>>? found = null;
            for (var type = target.GetType(); type != null; type = type.BaseType)
            {
                if (!TargetBindings.TryGetValue(type, out var bind)) continue;
                found ??= new List<Action<CommandRegistry, object>>();
                found.Add(bind);
            }

            if (found == null) return false;
            for (int i = found.Count - 1; i >= 0; i--)
                found[i](registry, target);
            return true;
        }
    }
}
