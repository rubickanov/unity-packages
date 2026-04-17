using System;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ILogger = Microsoft.Extensions.Logging.ILogger;

namespace Rubickanov.Logging
{
    /// <summary>
    /// Bridges Unity's <c>Application.logMessageReceived</c> to MEL <see cref="ILogger"/> for unified file logging.
    /// </summary>
    public sealed class UnityLogInterceptor : IDisposable
    {
        private readonly ILogger _logger;

        [ThreadStatic]
        private static bool _isForwarding;

        public UnityLogInterceptor(ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger("Unity");
            Application.logMessageReceivedThreaded += OnLogMessageReceived;
        }

        // Reset thread-static guard on Play Mode enter so a stuck-true value
        // from a prior session doesn't silently drop logs when
        // Enter Play Mode > Reload Domain is disabled.
        [RuntimeInitializeOnLoadMethod(RuntimeInitializeLoadType.SubsystemRegistration)]
        private static void ResetStatics()
        {
            _isForwarding = false;
        }

        public void Dispose()
        {
            Application.logMessageReceivedThreaded -= OnLogMessageReceived;
        }

        private void OnLogMessageReceived(string message, string stackTrace, LogType type)
        {
            if (_isForwarding)
                return;

            _isForwarding = true;
            try
            {
                var logLevel = type switch
                {
                    LogType.Log => LogLevel.Information,
                    LogType.Warning => LogLevel.Warning,
                    LogType.Error => LogLevel.Error,
                    LogType.Exception => LogLevel.Error,
                    LogType.Assert => LogLevel.Critical,
                    _ => LogLevel.Information,
                };

                _logger.Log(logLevel, "{Message}", message);
            }
            finally
            {
                _isForwarding = false;
            }
        }
    }
}
