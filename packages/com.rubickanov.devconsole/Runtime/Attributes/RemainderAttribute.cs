using System;

namespace Rubickanov.DevConsole
{
    /// <summary>
    /// Marks the last <c>string</c> parameter of a console command as taking the rest of the line: <c>bind F5 timescale 0.5</c>
    /// passes <c>"timescale 0.5"</c> without quotes around it. Several words arrive as typed, quotes included; a single
    /// word arrives unquoted.
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public sealed class RemainderAttribute : Attribute
    {
    }
}
