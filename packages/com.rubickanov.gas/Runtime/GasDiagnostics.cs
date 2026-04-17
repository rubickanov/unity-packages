using System;

namespace Rubickanov.GAS
{
    public static class GasDiagnostics
    {
        public static Action<string>? Warning;

        internal static void EmitWarning(string message) => Warning?.Invoke(message);
    }
}
