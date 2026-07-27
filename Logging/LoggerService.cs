using System;
using System.IO;
using GameLauncher.Models;

namespace GameLauncher.Logging;

public class LoggerService
{
    private static readonly object FileLock = new();

    public event EventHandler<LogEntry>? LogReceived;

    public bool EnableFileLogging { get; set; } = true;

    public void LogInfo(string message)
    {
        // Suppress routine informational logs per Phase 17.1.11 specifications
    }

    public void LogWarning(string message) => Log(LogLevel.Warning, message);
    public void LogError(string message) => Log(LogLevel.Error, message);
    public void LogSuccess(string message) => Log(LogLevel.Success, message);

    public void LogDebug(string message)
    {
        // Suppress debug logs
    }

    public void Log(LogLevel level, string message)
    {
        // Filter out routine Info & Debug logs completely
        if (level == LogLevel.Info || level == LogLevel.Debug)
            return;

        var entry = new LogEntry(DateTime.Now, level, message);
        LogReceived?.Invoke(this, entry);

        if (EnableFileLogging)
        {
            WriteLogToFile(entry);
        }
    }

    private void WriteLogToFile(LogEntry entry)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logsDir = Path.Combine(localAppData, "GameLauncher", "logs");
            if (!Directory.Exists(logsDir))
            {
                Directory.CreateDirectory(logsDir);
            }

            var logFilePath = Path.Combine(logsDir, "app.log");
            lock (FileLock)
            {
                File.AppendAllText(logFilePath, entry.ToString() + Environment.NewLine);
            }
        }
        catch
        {
            // Suppress file write errors gracefully
        }
    }
}
