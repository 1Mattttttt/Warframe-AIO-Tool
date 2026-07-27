using System;

namespace GameLauncher.Models;

public enum LogLevel
{
    Info,
    Warning,
    Error,
    Success,
    Debug
}

public record LogEntry(DateTime Timestamp, LogLevel Level, string Message)
{
    public string FormattedTimestamp => $"[{Timestamp:HH:mm:ss}]";

    public string TimestampHexColor { get; init; } = "#5C5E70";

    public string Symbol => Level switch
    {
        LogLevel.Success => "✔",
        LogLevel.Warning => "⚠",
        LogLevel.Error => "✖",
        LogLevel.Info => "ℹ",
        LogLevel.Debug => "[DEBUG]",
        _ => "ℹ"
    };

    public string HexColor => Level switch
    {
        LogLevel.Success => "#A6E3A1",
        LogLevel.Warning => "#FAB387",
        LogLevel.Error => "#F38BA8",
        LogLevel.Info => "#CDD6F4",
        LogLevel.Debug => "#6C7086",
        _ => "#CDD6F4"
    };

    public string DisplayText => $"{Symbol} {Message}";

    public override string ToString() =>
        $"{FormattedTimestamp} {Symbol} {Message}";
}
