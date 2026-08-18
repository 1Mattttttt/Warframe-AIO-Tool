using System;
using System.Diagnostics;
using System.Management;
using GameLauncher.Configuration;
using GameLauncher.Logging;


namespace GameLauncher.Core;

public class EpicLauncher
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    public EpicLauncher(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool LaunchWarframeThroughEpic()
    {
        var uri = _settings.EpicLaunchUri?.Trim();
        if (string.IsNullOrWhiteSpace(uri))
        {
            _logger.LogError("Epic Games Launch URI is not configured. Set EpicLaunchUri in Settings.");
            return false;
        }

        if (!uri.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase))
        {
            _logger.LogError("Epic Launch URI must use the com.epicgames.launcher:// protocol.");
            return false;
        }

        try
        {
            _logger.LogInfo("Launching Warframe via Epic Games URI protocol...");
            _logger.LogInfo($"URI: {uri}");

            Process.Start(new ProcessStartInfo
            {
                FileName = uri,
                UseShellExecute = true
            });

            _logger.LogSuccess("Epic Games Launcher protocol request sent successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to launch Warframe via Epic Games Launcher: {ex.Message}");
            return false;
        }
    }

    public void InspectEpicProcessRelationship(string targetProcessName = "Warframe.x64")
    {
        _logger.LogInfo("=== EPIC GAMES LAUNCH DIAGNOSTIC INSPECTION ===");

        try
        {
            var processes = Process.GetProcessesByName(targetProcessName);
            if (processes.Length == 0)
            {
                _logger.LogInfo($"No active '{targetProcessName}' processes found for inspection.");
                InspectRunningEpicProcesses();
                return;
            }

            foreach (var proc in processes)
            {
                var ppid = GetParentProcessId(proc.Id);
                string parentName = "Unknown";
                if (ppid > 0)
                {
                    try
                    {
                        var parentProc = Process.GetProcessById(ppid);
                        parentName = $"{parentProc.ProcessName}.exe (PID: {parentProc.Id})";
                    }
                    catch
                    {
                        parentName = $"PID: {ppid} (Exited)";
                    }
                }

                string cmdLine = GetProcessCommandLine(proc.Id);

                _logger.LogInfo($"[PROCESS DIAGNOSTIC] Name: {proc.ProcessName}.exe | PID: {proc.Id}");
                _logger.LogInfo($"  ├─ Parent Process: {parentName}");
                _logger.LogInfo($"  └─ Command Line: {(string.IsNullOrEmpty(cmdLine) ? "(Not accessible)" : cmdLine)}");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Diagnostic inspection error: {ex.Message}");
        }
    }

    public void InspectRunningEpicProcesses()
    {
        try
        {
            var epicProcesses = Process.GetProcessesByName("EpicGamesLauncher");
            if (epicProcesses.Length > 0)
            {
                _logger.LogInfo($"Found {epicProcesses.Length} active EpicGamesLauncher instance(s):");
                foreach (var ep in epicProcesses)
                {
                    _logger.LogInfo($"  ├─ EpicGamesLauncher.exe (PID: {ep.Id})");
                }
            }
            else
            {
                _logger.LogInfo("EpicGamesLauncher.exe is currently not running.");
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error inspecting Epic Games Launcher processes: {ex.Message}");
        }
    }

    private int GetParentProcessId(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT ParentProcessId FROM Win32_Process WHERE ProcessId = {processId}");
            using var collection = searcher.Get();
            foreach (var item in collection)
            {
                return Convert.ToInt32(item["ParentProcessId"]);
            }
        }
        catch
        {
            // Suppress fallback
        }
        return -1;
    }

    private string GetProcessCommandLine(int processId)
    {
        try
        {
            using var searcher = new ManagementObjectSearcher($"SELECT CommandLine FROM Win32_Process WHERE ProcessId = {processId}");
            using var collection = searcher.Get();
            foreach (var item in collection)
            {
                var cmdLine = item["CommandLine"]?.ToString();
                if (!string.IsNullOrEmpty(cmdLine))
                {
                    return SanitizeCommandLine(cmdLine);
                }
            }
        }
        catch
        {
            // Suppress fallback
        }
        return string.Empty;
    }

    private string SanitizeCommandLine(string cmdLine)
    {
        // Safe logging filter: mask any potential auth tokens or secret parameters
        if (cmdLine.Contains("AUTH_TOKEN=", StringComparison.OrdinalIgnoreCase))
        {
            return "[Masked Command Line with Auth Token]";
        }
        return cmdLine;
    }
}
