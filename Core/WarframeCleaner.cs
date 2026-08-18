using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using Microsoft.Win32;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public static class WarframeCleaner
{
    public static void CleanWarframeFiles(LoggerService logger, AppSettings? settings = null)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        logger.LogInfo("=== STARTING COMPREHENSIVE WARFRAME FILE & REGISTRY CLEANUP ===");

        // 0. Terminate running Warframe processes to prevent locked file errors
        KillWarframeProcesses(logger);

        // 1. Define target directories across LocalAppData, AppData, and Temp
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string appData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string tempPath = Path.GetTempPath();

        var targetDirectories = new List<string>
        {
            Path.Combine(localAppData, "Warframe"),
            Path.Combine(localAppData, "DigitalExtremes"),
            Path.Combine(localAppData, "Digital Extremes"),
            Path.Combine(appData, "Warframe"),
            Path.Combine(appData, "DigitalExtremes"),
            Path.Combine(appData, "Digital Extremes"),
            Path.Combine(tempPath, "Warframe"),
            Path.Combine(tempPath, "DigitalExtremes")
        };

        // Clean configured CacheFolder or standard cache directories
        var cacheDir = settings?.CacheFolder;
        if (!string.IsNullOrWhiteSpace(cacheDir) && Directory.Exists(cacheDir))
        {
            logger.LogInfo($"Cleaning configured CacheFolder: {cacheDir}");
            CleanSpecificCacheFiles(cacheDir, logger);
        }

        string[] commonCacheDirs =
        [
            @"C:\Program Files\Epic Games\Warframe\Downloaded\Cache.Windows",
            @"C:\Program Files (x86)\Steam\steamapps\common\Warframe\Downloaded\Cache.Windows",
            Path.Combine(localAppData, @"Warframe\Downloaded\Cache.Windows")
        ];

        foreach (var cDir in commonCacheDirs)
        {
            if (Directory.Exists(cDir))
            {
                logger.LogInfo($"Cleaning game cache folder: {cDir}");
                CleanSpecificCacheFiles(cDir, logger);
            }
        }

        // Purge target directories thoroughly
        foreach (var dir in targetDirectories)
        {
            if (Directory.Exists(dir))
            {
                logger.LogInfo($"Purging directory: {dir}");
                CleanDirectoryContents(dir, logger);
            }
            else
            {
                logger.LogInfo($"Directory not found (skipped): {dir}");
            }
        }

        // 2. Registry Cleanup (Digital Extremes HKCU tokens)
        CleanWarframeRegistry(logger);

        logger.LogSuccess("✔ Warframe comprehensive deep cleanup completed successfully.");
    }

    private static void KillWarframeProcesses(LoggerService logger)
    {
        string[] processNames = ["Warframe.x64", "Warframe", "Launcher"];
        foreach (var name in processNames)
        {
            try
            {
                var procs = Process.GetProcessesByName(name);
                foreach (var p in procs)
                {
                    logger.LogWarning($"Terminating running process {p.ProcessName} (PID: {p.Id}) to release file locks...");
                    p.Kill();
                    p.WaitForExit(2000);
                    p.Dispose();
                }
            }
            catch (Exception ex)
            {
                logger.LogWarning($"Could not terminate process {name}: {ex.Message}");
            }
        }
    }

    private static void CleanSpecificCacheFiles(string cacheDir, LoggerService logger)
    {
        string[] targetCacheFiles =
        [
            Path.Combine(cacheDir, "H.Misc.cache"),
            Path.Combine(cacheDir, "H.Misc.toc"),
            Path.Combine(cacheDir, "H.Misc_en_cache"),
            Path.Combine(cacheDir, "H.Misc_en_toc"),
            Path.Combine(cacheDir, "H.Misc_pt.cache"),
            Path.Combine(cacheDir, "H.Misc_pt.toc"),
            Path.Combine(cacheDir, "H.Misc_xx.cache"),
            Path.Combine(cacheDir, "H.Misc_xx.toc")
        ];

        foreach (var filePath in targetCacheFiles)
        {
            DeleteFileSafely(filePath, logger);
        }
    }

    private static void CleanDirectoryContents(string dirPath, LoggerService logger)
    {
        string[] files;
        try
        {
            files = Directory.GetFiles(dirPath);
        }
        catch (Exception ex)
        {
            logger.LogError($"Error reading files in {dirPath}: {ex.Message}");
            files = Array.Empty<string>();
        }

        foreach (var file in files)
        {
            DeleteFileSafely(file, logger);
        }

        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dirPath);
        }
        catch (Exception ex)
        {
            logger.LogError($"Error reading subdirectories in {dirPath}: {ex.Message}");
            subDirs = Array.Empty<string>();
        }

        foreach (var subDir in subDirs)
        {
            CleanDirectoryContents(subDir, logger);

            try
            {
                if (Directory.Exists(subDir) &&
                    Directory.GetFiles(subDir).Length == 0 &&
                    Directory.GetDirectories(subDir).Length == 0)
                {
                    Directory.Delete(subDir);
                    logger.LogInfo($"Deleted empty folder: {subDir}");
                }
            }
            catch (Exception ex)
            {
                logger.LogError($"Error deleting folder {subDir}: {ex.Message}");
            }
        }
    }

    private static void DeleteFileSafely(string filePath, LoggerService logger)
    {
        if (Path.GetExtension(filePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInfo($"Skipped executable binary: {filePath}");
            return;
        }

        if (!File.Exists(filePath)) return;

        try
        {
            File.SetAttributes(filePath, FileAttributes.Normal);
            File.Delete(filePath);
            logger.LogInfo($"Deleted file: {filePath}");
        }
        catch (Exception ex)
        {
            logger.LogError($"Failed deleting file {filePath}: {ex.Message}");
        }
    }

    private static void CleanWarframeRegistry(LoggerService logger)
    {
        try
        {
            using var hkcu = Registry.CurrentUser;
            using var deKey = hkcu.OpenSubKey(@"Software\Digital Extremes", writable: true);
            if (deKey != null)
            {
                logger.LogInfo("Purging HKCU\\Software\\Digital Extremes registry key...");
                hkcu.DeleteSubKeyTree(@"Software\Digital Extremes", throwOnMissingSubKey: false);
                logger.LogSuccess("✔ Successfully purged Digital Extremes registry tokens.");
            }
        }
        catch (Exception ex)
        {
            logger.LogError($"Registry cleanup exception: {ex.Message}");
        }
    }
}
