using System;
using System.IO;
using System.Linq;
using Microsoft.Extensions.Logging;
using UnityEngine;
using ZLogger;
#if UNITY_EDITOR
using ZLogger.Unity;
#endif

namespace Rubickanov.Logging
{
    /// <summary>
    /// Builds an <see cref="ILoggerFactory"/> with ZLogger file rotation and platform-specific outputs.
    /// </summary>
    public static class LoggerFactoryBuilder
    {
        public static ILoggerFactory Build(LoggingSettings settings, bool enableFileLogging)
        {
            string? filePath = null;

            if (enableFileLogging)
            {
                var logDir = Path.Combine(Application.persistentDataPath, settings.LogDirectoryName);
                Directory.CreateDirectory(logDir);
                CleanupOldLogs(logDir, settings.FilePrefix, settings.MaxLogFiles - 1);

                var timestamp = DateTime.Now.ToString(settings.TimestampFormat);
                filePath = Path.Combine(logDir, $"{settings.FilePrefix}_{timestamp}.log");
            }

            return LoggerFactory.Create(logging =>
            {
                logging.SetMinimumLevel(settings.MinimumLevel);

                if (filePath != null)
                {
                    logging.AddZLoggerFile(filePath, options => ConfigureFormatter(options));
                }

#if UNITY_EDITOR
                logging.AddZLoggerUnityDebug(options =>
                {
                    options.PrettyStacktrace = settings.PrettyStacktrace;
                    ConfigureFormatter(options);
                });

                // UnityLogInterceptor forwards Application.logMessageReceived under the "Unity"
                // category. Without this filter that re-enters the Unity Debug provider and calls
                // Debug.Log again, so every original Debug.Log/LogError shows up a second time in
                // the console. Drop the "Unity" category from the Debug provider ONLY — the file
                // provider still captures intercepted Unity logs.
                logging.AddFilter<ZLoggerUnityDebugLoggerProvider>("Unity", LogLevel.None);
#elif UNITY_SERVER
                logging.AddZLoggerConsole(options => ConfigureFormatter(options));
#endif
            });
        }

        private static void ConfigureFormatter(ZLoggerOptions options)
        {
            options.UsePlainTextFormatter(formatter =>
            {
                formatter.SetPrefixFormatter(
                    $"{0:local} [{1:short}] {2} | ",
                    (in MessageTemplate template, in LogInfo info) =>
                        template.Format(info.Timestamp, info.LogLevel, info.Category));
            });
        }

        private static void CleanupOldLogs(string logDir, string filePrefix, int keepCount)
        {
            try
            {
                var files = Directory.GetFiles(logDir, $"{filePrefix}_*.log")
                    .OrderByDescending(File.GetCreationTimeUtc)
                    .Skip(keepCount)
                    .ToArray();

                foreach (var file in files)
                {
                    File.Delete(file);
                }
            }
            catch (Exception ex)
            {
                Debug.LogWarning($"Failed to cleanup old log files: {ex.Message}");
            }
        }
    }
}
