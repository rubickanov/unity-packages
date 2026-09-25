using System;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Thrown from a command to fail it with a message: <c>Execute</c> returns an error result carrying the message as
    /// is, without the "Command error:" prefix an unexpected exception gets. A command that only logs an error still
    /// counts as a success, which a bind, a <c>;</c> chain or <c>-command</c> cannot tell from one that worked.
    /// </summary>
    public sealed class CommandException : Exception
    {
        public CommandException(string message) : base(message)
        {
        }
    }
}
