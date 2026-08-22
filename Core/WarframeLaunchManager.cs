using System;
using System.Diagnostics;
using System.IO;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class WarframeLaunchManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    public WarframeLaunchManager(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsWarframeRunning()
    {
        try
        {
            var processes = Process.GetProcessesByName("Warframe.x64");
            if (processes.Length > 0) return true;

            var x86Processes = Process.GetProcessesByName("Warframe");
            return x86Processes.Length > 0;
        }
        catch
        {
            return false;
        }
    }

    public void CleanDiscordSdkInternal(string? gameFolder = null)
    {
        _logger.LogInfo("Removing Discord SDK...");

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string defaultSdkPath = Path.Combine(localAppData, @"Warframe\Downloaded\Public\Tools\Windows\x64\discord_game_sdk.dll");

        string? targetPath = null;
        if (!string.IsNullOrWhiteSpace(_settings.DiscordSdkPath) && File.Exists(_settings.DiscordSdkPath))
        {
            targetPath = _settings.DiscordSdkPath;
        }
        else if (File.Exists(defaultSdkPath))
        {
            targetPath = defaultSdkPath;
        }
        else if (!string.IsNullOrWhiteSpace(gameFolder))
        {
            string gameSdkPath = Path.Combine(gameFolder, @"Tools\Windows\x64\discord_game_sdk.dll");
            if (File.Exists(gameSdkPath))
            {
                targetPath = gameSdkPath;
            }
        }

        if (string.IsNullOrEmpty(targetPath) || !File.Exists(targetPath))
        {
            _logger.LogInfo("ℹ Discord SDK file not found (already clean or absent).");
            return;
        }

        try
        {
            File.Delete(targetPath);
            _logger.LogSuccess("Discord SDK removed successfully.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"✖ Failed to remove Discord SDK file: {ex.Message}");
        }
    }

    public bool LaunchWarframeExecutable(string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            _logger.LogError("✖ Warframe.x64.exe executable not found.");
            return false;
        }

        if (IsWarframeRunning())
        {
            _logger.LogError("✖ Warframe is already running. Please close the active instance before launching.");
            return false;
        }

        CleanDiscordSdkInternal(Path.GetDirectoryName(exePath));

        string lang = _settings.WarframeLanguage ?? "en";
        string api = _settings.WarframeGraphicsApi ?? "dx11";

        string arguments = $"-graphicsDriver:{api} -gpuPreference:2 -cluster:public -language:{lang}";
        _logger.LogInfo($"[LAUNCH] Launching Warframe standalone executable...");
        _logger.LogInfo($"Executable: {exePath}");
        _logger.LogInfo($"Arguments: {arguments}");

        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = arguments,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            });

            _logger.LogSuccess("✔ Warframe launched successfully.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"✖ Failed to launch Warframe: {ex.Message}");
            return false;
        }
    }
}
