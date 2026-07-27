using System;
using System.IO;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class DiscordSdkManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    public string SdkPath => _settings.DiscordSdkPath;
    public string BackupPath => string.IsNullOrWhiteSpace(_settings.DiscordSdkPath) 
        ? string.Empty 
        : _settings.DiscordSdkPath + ".backup";

    public bool SdkExists => !string.IsNullOrWhiteSpace(SdkPath) && File.Exists(SdkPath);
    public bool BackupExists => !string.IsNullOrWhiteSpace(BackupPath) && File.Exists(BackupPath);
    public bool IsRemoved => !SdkExists && BackupExists;

    public DiscordSdkManager(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool BackupSdk()
    {
        if (string.IsNullOrWhiteSpace(SdkPath))
        {
            _logger.LogWarning("Discord SDK path is not configured.");
            return false;
        }

        try
        {
            if (!File.Exists(SdkPath))
            {
                if (File.Exists(BackupPath))
                {
                    return true;
                }

                _logger.LogWarning($"discord_game_sdk.dll not found at: {SdkPath}");
                return false;
            }

            File.Copy(SdkPath, BackupPath, overwrite: true);
            _logger.LogSuccess($"Created backup of discord_game_sdk.dll at: {BackupPath}");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to backup discord_game_sdk.dll: {ex.Message}");
            return false;
        }
    }

    public bool RemoveSdk()
    {
        Program.WriteSdkKillerDebugLog(null, "DiscordSdkManager.RemoveSdk Initiated", SdkExists ? "SdkExists" : "SdkMissing");

        if (string.IsNullOrWhiteSpace(SdkPath))
        {
            _logger.LogWarning("Discord SDK path is not configured.");
            return false;
        }

        try
        {
            if (!BackupExists && SdkExists)
            {
                if (!BackupSdk())
                {
                    _logger.LogError("Aborting DLL removal because backup creation failed.");
                    return false;
                }
            }

            if (!File.Exists(SdkPath))
            {
                if (BackupExists)
                {
                    return true;
                }

                _logger.LogWarning($"discord_game_sdk.dll not found at: {SdkPath}");
                return false;
            }

            File.Delete(SdkPath);
            Program.WriteSdkKillerDebugLog(null, "DiscordSdkManager.RemoveSdk Completed Successfully", "Removed");
            _logger.LogSuccess("discord_game_sdk.dll removed successfully.");
            return true;
        }
        catch (Exception ex)
        {
            Program.WriteSdkKillerDebugLog(ex, "DiscordSdkManager.RemoveSdk Exception");
            _logger.LogError($"Failed to remove discord_game_sdk.dll: {ex.Message}");
            return false;
        }
    }

    public bool RestoreSdk()
    {
        if (string.IsNullOrWhiteSpace(BackupPath))
        {
            _logger.LogWarning("Discord SDK backup path is not configured.");
            return false;
        }

        try
        {
            if (!File.Exists(BackupPath))
            {
                _logger.LogWarning($"No backup file found at: {BackupPath}");
                return false;
            }

            var directory = Path.GetDirectoryName(SdkPath);
            if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }

            File.Copy(BackupPath, SdkPath, overwrite: true);
            _logger.LogSuccess("discord_game_sdk.dll restored successfully from backup.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to restore discord_game_sdk.dll: {ex.Message}");
            return false;
        }
    }

    public string GetStatusMessage()
    {
        if (SdkExists && BackupExists)
            return "Active (Backup Present)";
        if (SdkExists && !BackupExists)
            return "Active (No Backup)";
        if (!SdkExists && BackupExists)
            return "DLL Removed (Backup Present)";
        return "Not Found";
    }
}
