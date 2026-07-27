using System;
using System.IO;
using System.Text.Json;

namespace GameLauncher.Configuration;

public class AppSettings
{
    public const string DefaultEpicLaunchUri =
        "com.epicgames.launcher://apps/244aaaa06bfa49d088205b13b9d2d115%3A9b6e3ff688c448f4971a9c752094f286%3A398965b67f314d31b0683b8ea11c93a4?action=launch&silent=true";

    public string WarframePath { get; set; } = @"C:\Program Files\Epic Games\Warframe\Downloaded\Warframe.x64.exe";
    public string DiscordSdkPath { get; set; } = @"C:\Program Files\Epic Games\Warframe\Downloaded\Tools\Windows\x64\discord_game_sdk.dll";

    /// <summary>
    /// Epic Games URI protocol launch string. Authentication is handled by Epic Games Launcher.
    /// </summary>
    public string EpicLaunchUri { get; set; } = DefaultEpicLaunchUri;

    public string SteamAppId { get; set; } = "230410";
    public string StandaloneLauncherPath { get; set; } = string.Empty;

    /// <summary>
    /// User-selected launcher: Epic, Steam, or Standalone.
    /// </summary>
    public string PreferredLauncher { get; set; } = "Epic";

    public int LaunchesBeforeAction { get; set; } = 2;

    // General Options
    public bool StartMonitoringOnLaunch { get; set; } = false;
    public bool StartMinimized { get; set; } = false;
    public bool AutoLaunchWarframe { get; set; } = false;
    public bool EnableStartupAnimation { get; set; } = true;
    public bool EnableStartupSound { get; set; } = true;
    public bool EnableStartupJingle { get; set; } = true;
    public string StartupWindowMode { get; set; } = "Fullscreen";

    // Application Behavior Options
    public bool StartMinimizedToTray { get; set; } = false;
    public bool LaunchOnSystemStartup { get; set; } = false;
    public bool LaunchMinimizedOnSystemStartup { get; set; } = false;
    public bool CloseToTray { get; set; } = true;

    // Ambient Music System Options
    public bool EnableBackgroundMusic { get; set; } = true;
    public double BackgroundMusicVolume { get; set; } = 80.0;
    public bool BackgroundMusicMuted { get; set; } = false;

    // Warframe Paths
    public string WarframeInstallFolder { get; set; } = @"C:\Program Files\Epic Games\Warframe\Downloaded";
    public string CacheFolder { get; set; } = @"C:\Program Files\Epic Games\Warframe\Downloaded\Cache.Windows";

    // Maintenance
    public bool EnableCacheCleaner { get; set; } = true;

    // External Tools
    public bool EnableOwHelper { get; set; } = true;
    public bool LaunchOwHelperHidden { get; set; } = false;

    // GUID Spoof System
    public bool EnableGuidSpoof { get; set; } = false;
    public string CurrentGuid { get; set; } = string.Empty;
    public System.Collections.Generic.List<string> GuidHistory { get; set; } = new();

    // Logging
    public bool EnableFileLogging { get; set; } = true;
    public int MaxLogEntries { get; set; } = 1000;

    public static string GetConfigPath()
    {
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var folder = Path.Combine(localAppData, "GameLauncher");
        if (!Directory.Exists(folder))
        {
            Directory.CreateDirectory(folder);
        }
        return Path.Combine(folder, "appsettings.json");
    }

    public static AppSettings Load(string? filePath = null)
    {
        try
        {
            string targetPath = filePath ?? GetConfigPath();

            if (File.Exists(targetPath))
            {
                var json = File.ReadAllText(targetPath);
                var settings = JsonSerializer.Deserialize<AppSettings>(json);
                if (settings != null)
                {
                    if (string.IsNullOrWhiteSpace(settings.EpicLaunchUri))
                    {
                        settings.EpicLaunchUri = DefaultEpicLaunchUri;
                    }
                    return settings;
                }
            }
            else if (filePath == null)
            {
                // Backward compatibility: check for old appsettings.json next to executable
                var legacyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "appsettings.json");
                if (!File.Exists(legacyPath) && File.Exists("appsettings.json"))
                {
                    legacyPath = "appsettings.json";
                }

                if (File.Exists(legacyPath))
                {
                    var json = File.ReadAllText(legacyPath);
                    var legacySettings = JsonSerializer.Deserialize<AppSettings>(json);
                    if (legacySettings != null)
                    {
                        if (string.IsNullOrWhiteSpace(legacySettings.EpicLaunchUri))
                        {
                            legacySettings.EpicLaunchUri = DefaultEpicLaunchUri;
                        }

                        // Automatically migrate legacy configuration to LocalAppData
                        legacySettings.Save(targetPath);
                        return legacySettings;
                    }
                }
            }
        }
        catch
        {
            // Fall back to default settings on read or parse errors
        }

        var defaultSettings = new AppSettings();
        if (filePath == null)
        {
            defaultSettings.Save(GetConfigPath());
        }
        return defaultSettings;
    }

    public void Save(string? filePath = null)
    {
        try
        {
            string targetPath = filePath ?? GetConfigPath();
            var directory = Path.GetDirectoryName(targetPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            var options = new JsonSerializerOptions { WriteIndented = true };
            var json = JsonSerializer.Serialize(this, options);
            File.WriteAllText(targetPath, json);
        }
        catch
        {
            // Suppress save failures gracefully
        }
    }
}
