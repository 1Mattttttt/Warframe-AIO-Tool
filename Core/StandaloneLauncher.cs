using System;
using System.Diagnostics;
using System.IO;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class StandaloneLauncher
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    public StandaloneLauncher(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool LaunchWarframe(string? launcherPath = null)
    {
        return LaunchStandaloneWarframe(launcherPath);
    }

    public bool LaunchStandaloneWarframe(string? customPath = null)
    {
        _logger.LogInfo("ℹ Launching Standalone Warframe...");

        string? resolvedPath = ResolveStandalonePath(customPath);

        if (string.IsNullOrWhiteSpace(resolvedPath) || !File.Exists(resolvedPath))
        {
            _logger.LogError("✖ Standalone executable not found.");
            return false;
        }

        try
        {
            string api = string.IsNullOrWhiteSpace(_settings.WarframeGraphicsApi) ? "dx11" : _settings.WarframeGraphicsApi;
            string lang = string.IsNullOrWhiteSpace(_settings.WarframeLanguage) ? "en" : _settings.WarframeLanguage;
            string arguments = $"-graphicsDriver:{api} -gpuPreference:2 -cluster:public -language:{lang}";
            _logger.LogInfo($"Arguments: {arguments}");

            Process.Start(new ProcessStartInfo
            {
                FileName = resolvedPath,
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(resolvedPath)
            });

            _logger.LogSuccess("✔ Standalone Warframe started successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to launch Standalone Warframe: {ex.Message}");
            return false;
        }
    }

    public string? ResolveStandalonePath(string? customPath = null)
    {
        if (!string.IsNullOrWhiteSpace(customPath) && File.Exists(customPath))
        {
            return customPath;
        }

        if (!string.IsNullOrWhiteSpace(_settings.StandaloneLauncherPath) && File.Exists(_settings.StandaloneLauncherPath))
        {
            return _settings.StandaloneLauncherPath;
        }

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string defaultPath = Path.Combine(localAppData, @"Warframe\Downloaded\Public\Warframe.x64.exe");

        if (File.Exists(defaultPath))
        {
            return defaultPath;
        }

        string altPath = Path.Combine(localAppData, @"Warframe\Downloaded\Public\Launcher.exe");
        if (File.Exists(altPath))
        {
            return altPath;
        }

        return null;
    }
}
