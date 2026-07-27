using System;
using System.Diagnostics;
using Microsoft.Win32;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public static class StartupManager
{
    private const string RunRegistryKeyPath = @"SOFTWARE\Microsoft\Windows\CurrentVersion\Run";
    private const string AppRegistryName = "GameLauncher";

    public static bool SetStartWithWindows(bool enable, bool startMinimized, LoggerService? logger = null)
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: true);
            if (key == null)
            {
                logger?.LogError("Could not open Windows Run Registry Key.");
                return false;
            }

            if (enable)
            {
                string? exePath = Environment.ProcessPath;
                if (string.IsNullOrEmpty(exePath))
                {
                    exePath = Process.GetCurrentProcess().MainModule?.FileName;
                }

                if (string.IsNullOrEmpty(exePath))
                {
                    logger?.LogError("Could not determine executable path for startup registration.");
                    return false;
                }

                string command = startMinimized ? $"\"{exePath}\" --minimized" : $"\"{exePath}\"";
                key.SetValue(AppRegistryName, command);
                logger?.LogSuccess($"Registered Windows startup command: {command}");
                return true;
            }
            else
            {
                if (key.GetValue(AppRegistryName) != null)
                {
                    key.DeleteValue(AppRegistryName, false);
                    logger?.LogInfo("Unregistered Windows startup entry.");
                }
                return true;
            }
        }
        catch (Exception ex)
        {
            logger?.LogError($"Failed to update Windows startup registry entry: {ex.Message}");
            return false;
        }
    }

    public static bool IsStartWithWindowsEnabled()
    {
        try
        {
            using var key = Registry.CurrentUser.OpenSubKey(RunRegistryKeyPath, writable: false);
            return key?.GetValue(AppRegistryName) != null;
        }
        catch
        {
            return false;
        }
    }
}
