using System;
using System.Collections.Generic;
using System.Reflection;

namespace Rubickanov.ACS.Runtime.Netcode
{
    /// <summary>
    /// Resolves a <see cref="NetworkScope"/> for a component type by reading
    /// <see cref="NetworkScopeAttribute"/>. Results are cached per type.
    /// </summary>
    internal static class NetworkScopeScanner
    {
        private static readonly Dictionary<Type, NetworkScope> Cache = new();

        public static NetworkScope GetScope(Type type)
        {
            if (Cache.TryGetValue(type, out var cached))
                return cached;

            var attr = type.GetCustomAttribute<NetworkScopeAttribute>(inherit: true);
            var scope = attr?.Scope ?? NetworkScope.Everywhere;
            Cache[type] = scope;
            return scope;
        }
    }
}
