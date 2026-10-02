using System;
using System.Collections.Generic;
using UnityEngine.LowLevel;

namespace Rubickanov.DevConsole
{
    /// <summary>Adds the console's own systems to Unity's player loop.</summary>
    internal static class PlayerLoopSystems
    {
        /// <summary>
        /// Puts a system of type <paramref name="system"/> at the start or the end of <paramref name="phase"/>. A system
        /// of that type already there is replaced, so registering again, as every play session does, adds no second one.
        /// </summary>
        internal static void Insert(Type phase, Type system, PlayerLoopSystem.UpdateFunction update, bool atStart)
        {
            var root = PlayerLoop.GetCurrentPlayerLoop();
            var phases = root.subSystemList;
            if (phases == null) return;

            for (int i = 0; i < phases.Length; i++)
            {
                if (phases[i].type != phase) continue;

                var old = phases[i].subSystemList ?? Array.Empty<PlayerLoopSystem>();
                var systems = new List<PlayerLoopSystem>(old.Length + 1);
                var added = new PlayerLoopSystem { type = system, updateDelegate = update };
                if (atStart) systems.Add(added);
                foreach (var each in old)
                {
                    if (each.type != system)
                        systems.Add(each);
                }

                if (!atStart) systems.Add(added);
                phases[i].subSystemList = systems.ToArray();
                root.subSystemList = phases;
                PlayerLoop.SetPlayerLoop(root);
                return;
            }
        }
    }
}
