using System;
using GameLauncher.Configuration;
using GameLauncher.Logging;
using GameLauncher.Models;

namespace GameLauncher.Core;

public class LauncherManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly EpicLauncher _epicLauncher;
    private readonly SteamLauncher _steamLauncher;
    private readonly StandaloneLauncher _standaloneLauncher;

    public LauncherType SelectedLauncher { get; private set; }

    public LauncherManager(
        AppSettings settings,
        LoggerService logger,
        EpicLauncher epicLauncher,
        SteamLauncher steamLauncher,
        StandaloneLauncher standaloneLauncher)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _epicLauncher = epicLauncher ?? throw new ArgumentNullException(nameof(epicLauncher));
        _steamLauncher = steamLauncher ?? throw new ArgumentNullException(nameof(steamLauncher));
        _standaloneLauncher = standaloneLauncher ?? throw new ArgumentNullException(nameof(standaloneLauncher));

        SelectedLauncher = ParsePreferredLauncher(_settings.PreferredLauncher);
        if (SelectedLauncher == LauncherType.None)
        {
            SelectedLauncher = LauncherType.Epic;
            _settings.PreferredLauncher = ToSettingsValue(SelectedLauncher);
        }

        _logger.LogInfo($"Loaded launcher preference: {GetDisplayName(SelectedLauncher)}");
    }

    public void SetSelectedLauncher(LauncherType launcher, bool persist = true)
    {
        if (launcher == LauncherType.None)
        {
            return;
        }

        SelectedLauncher = launcher;
        _settings.PreferredLauncher = ToSettingsValue(launcher);

        if (persist)
        {
            _settings.Save();
        }

        _logger.LogInfo($"Launcher preference set to: {GetDisplayName(launcher)}");
    }

    public bool LaunchWarframe()
    {
        _logger.LogInfo($"Launching Warframe via {GetDisplayName(SelectedLauncher)}...");

        return SelectedLauncher switch
        {
            LauncherType.Epic => _epicLauncher.LaunchWarframeThroughEpic(),
            LauncherType.Steam => _steamLauncher.LaunchWarframe(),
            LauncherType.Standalone => LaunchStandaloneWarframe(),
            _ => LogAndReturnFalse("Cannot launch Warframe: no launcher selected.")
        };
    }

    public bool LaunchStandaloneWarframe()
    {
        return _standaloneLauncher.LaunchStandaloneWarframe();
    }

    public static string GetDisplayName(LauncherType launcher)
    {
        return launcher switch
        {
            LauncherType.Epic => "Epic Games",
            LauncherType.Steam => "Steam",
            LauncherType.Standalone => "Standalone",
            _ => "None"
        };
    }

    public static LauncherType ParsePreferredLauncher(string? preferredLauncher)
    {
        if (string.IsNullOrWhiteSpace(preferredLauncher))
        {
            return LauncherType.None;
        }

        return preferredLauncher.Trim().ToLowerInvariant() switch
        {
            "epic" => LauncherType.Epic,
            "steam" => LauncherType.Steam,
            "standalone" => LauncherType.Standalone,
            _ => LauncherType.None
        };
    }

    public static string ToSettingsValue(LauncherType launcher)
    {
        return launcher switch
        {
            LauncherType.Epic => "Epic",
            LauncherType.Steam => "Steam",
            LauncherType.Standalone => "Standalone",
            _ => string.Empty
        };
    }

    private bool LogAndReturnFalse(string message)
    {
        _logger.LogError(message);
        return false;
    }
}
