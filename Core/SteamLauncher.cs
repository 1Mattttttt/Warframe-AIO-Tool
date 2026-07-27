using System;
using System.Diagnostics;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class SteamLauncher
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    public SteamLauncher(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool LaunchWarframe()
    {
        if (string.IsNullOrWhiteSpace(_settings.SteamAppId))
        {
            _logger.LogError("Steam App ID is not configured in settings.");
            return false;
        }

        var launchUri = $"steam://run/{_settings.SteamAppId.Trim()}";

        try
        {
            _logger.LogInfo("Launching Warframe via Steam protocol...");
            _logger.LogInfo($"URI: {launchUri}");

            Process.Start(new ProcessStartInfo
            {
                FileName = launchUri,
                UseShellExecute = true
            });

            _logger.LogSuccess("Steam protocol launch request sent successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to launch Warframe via Steam: {ex.Message}");
            return false;
        }
    }
}
