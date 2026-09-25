namespace Rubickanov.Log
{
    /// <summary>How much a channel says. A channel writes its own level and everything above it.</summary>
    public enum LogLevel
    {
        Verbose,
        Info,
        Warn,
        Error,
        Off,
    }
}
