using System;

namespace GameLauncher.Models;

public class LaunchEvent : EventArgs
{
    public DateTime Timestamp { get; set; } = DateTime.Now;
    public string ProcessName { get; set; } = string.Empty;
    public int ProcessId { get; set; }
    public int LaunchNumber { get; set; }
    public double LifetimeSeconds { get; set; }
    public bool IsBootstrap => LaunchNumber == 1;
    public bool IsGameLaunch => LaunchNumber >= 2;
}
