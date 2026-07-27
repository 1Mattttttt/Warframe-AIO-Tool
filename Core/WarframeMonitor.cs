using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Timer = System.Threading.Timer;
using GameLauncher.Configuration;
using GameLauncher.Logging;
using GameLauncher.Models;

namespace GameLauncher.Core;

public class WarframeMonitor : IDisposable
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly HashSet<int> _knownPids = new();
    private Timer? _timer;
    private int _launchCount;
    private bool _isMonitoring;

    public event EventHandler<LaunchEvent>? ProcessDetected;
    public event EventHandler<LaunchEvent>? BootstrapDetected;
    public event EventHandler<LaunchEvent>? GameLaunchDetected;

    public int LaunchCount => _launchCount;
    public bool IsMonitoring => _isMonitoring;

    public WarframeMonitor(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void Start(int checkIntervalMs = 1000)
    {
        if (_isMonitoring)
            return;

        _isMonitoring = true;
        _timer = new Timer(CheckProcesses, null, 0, checkIntervalMs);
    }

    public void Stop()
    {
        if (!_isMonitoring)
            return;

        _isMonitoring = false;
        _timer?.Change(Timeout.Infinite, Timeout.Infinite);
        _timer?.Dispose();
        _timer = null;
    }

    public void ResetLaunchCount()
    {
        _launchCount = 0;
        _knownPids.Clear();
    }

    public void SimulateLaunch(int pid = 0, string processName = "Warframe.x64")
    {
        _launchCount++;
        var simulatedPid = pid > 0 ? pid : 1000 + _launchCount;

        var launchEvent = new LaunchEvent
        {
            Timestamp = DateTime.Now,
            ProcessName = processName,
            ProcessId = simulatedPid,
            LaunchNumber = _launchCount
        };

        ProcessDetected?.Invoke(this, launchEvent);

        if (_launchCount == 1)
        {
            BootstrapDetected?.Invoke(this, launchEvent);
        }

        if (_launchCount >= _settings.LaunchesBeforeAction)
        {
            _logger.LogSuccess($"[SIMULATION GAME LAUNCH] Process: {processName} (PID: {simulatedPid})");
            GameLaunchDetected?.Invoke(this, launchEvent);
        }
    }

    public void SimulateFullLaunchSequence()
    {
        _logger.LogInfo("[DIAGNOSTIC MODE] Executing simulated launch sequence...");
        SimulateLaunch(1001, "Warframe.x64");
        SimulateLaunch(1002, "Warframe.x64");
    }

    private void CheckProcesses(object? state)
    {
        if (!_isMonitoring)
            return;

        try
        {
            var targetNames = GetTargetProcessNames();
            var matchedProcesses = new List<Process>();

            foreach (var targetName in targetNames)
            {
                var processes = Process.GetProcessesByName(targetName);
                matchedProcesses.AddRange(processes);
            }

            var activePids = new HashSet<int>();
            foreach (var proc in matchedProcesses)
            {
                activePids.Add(proc.Id);
            }

            // Clean up exited PIDs
            _knownPids.RemoveWhere(pid => !activePids.Contains(pid));

            // Process newly launched PIDs
            foreach (var process in matchedProcesses)
            {
                if (_knownPids.Add(process.Id))
                {
                    _launchCount++;

                    DateTime startTime = DateTime.Now;
                    try
                    {
                        startTime = process.StartTime;
                    }
                    catch
                    {
                        // Fallback if StartTime query fails
                    }

                    var launchEvent = new LaunchEvent
                    {
                        Timestamp = startTime,
                        ProcessName = process.ProcessName,
                        ProcessId = process.Id,
                        LaunchNumber = _launchCount
                    };

                    ProcessDetected?.Invoke(this, launchEvent);

                    if (_launchCount == 1)
                    {
                        BootstrapDetected?.Invoke(this, launchEvent);
                    }

                    if (_launchCount >= _settings.LaunchesBeforeAction)
                    {
                        _logger.LogSuccess($"[GAME LAUNCH DETECTED] Launch #{_launchCount}: {process.ProcessName} (PID: {process.Id})");
                        GameLaunchDetected?.Invoke(this, launchEvent);
                    }
                }
            }

            foreach (var proc in matchedProcesses)
            {
                proc.Dispose();
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error checking game processes: {ex.Message}");
        }
    }

    private IEnumerable<string> GetTargetProcessNames()
    {
        var names = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        if (!string.IsNullOrWhiteSpace(_settings.WarframePath))
        {
            var filename = Path.GetFileNameWithoutExtension(_settings.WarframePath);
            if (!string.IsNullOrWhiteSpace(filename))
            {
                names.Add(filename);
            }
        }

        names.Add("Warframe.x64");
        names.Add("Warframe");

        return names;
    }

    public void Dispose()
    {
        Stop();
    }
}
