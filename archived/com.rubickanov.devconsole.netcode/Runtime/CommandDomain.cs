namespace Rubickanov.DevConsole.Netcode
{
    /// <summary>Defines where a console command is allowed to execute.</summary>
    public enum CommandDomain
    {
        /// <summary>Executes locally on whoever typed it. Default for commands without <see cref="CommandDomainAttribute"/>.</summary>
        Shared,
        /// <summary>Executes only on the local client, never sent to the server.</summary>
        Client,
        /// <summary>Executes only on the server. Clients send it via RPC automatically.</summary>
        Server
    }
}
