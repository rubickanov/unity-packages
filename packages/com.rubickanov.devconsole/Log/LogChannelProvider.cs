using System;
using System.Collections.Generic;
using Rubickanov.Log;
using UnityEngine.Scripting;

namespace Rubickanov.DevConsole.Log
{
    /// <summary>Suggests the log channels there are so far, and <c>*</c> for all of them.</summary>
    [Preserve]
    public sealed class LogChannelProvider : IAutoCompleteProvider
    {
        public string Hint => "<channel>";

        public void GetSuggestions(string partial, List<string> results)
        {
            if (partial.Length == 0 || partial == LogChannelCommands.Every)
                results.Add(LogChannelCommands.Every);

            var channels = LogChannel.All;
            for (int i = 0; i < channels.Count; i++)
            {
                if (channels[i].Name.StartsWith(partial, StringComparison.OrdinalIgnoreCase))
                    results.Add(channels[i].Name);
            }
        }
    }
}
