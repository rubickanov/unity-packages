using System;

namespace Rubickanov.DevConsole.Netcode
{
    /// <summary>Marks a console command with a specific network domain (Client, Server, or Shared).</summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class CommandDomainAttribute : Attribute
    {
        public CommandDomain Domain { get; }
        public CommandDomainAttribute(CommandDomain domain) => Domain = domain;
    }
}
