using System;

namespace Rubickanov.DevConsole.Netcode
{
    /// <summary>Marks a console command as cheat-protected. Requires <c>sv_cheats 1</c> to execute.</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CheatProtectedAttribute : Attribute { }
}
