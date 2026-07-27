using System;
using System.IO;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public static class WarframeCleaner
{
    public static void CleanWarframeFiles(LoggerService logger, AppSettings? settings = null)
    {
        if (logger == null) throw new ArgumentNullException(nameof(logger));

        logger.LogInfo("=== STARTING WARFRAME CLEANUP ===");

        // Determine cache folder from settings or fallback to default
        var cacheDir = settings?.CacheFolder;
        if (string.IsNullOrWhiteSpace(cacheDir))
        {
            cacheDir = @"C:\Program Files\Epic Games\Warframe\Downloaded\Cache.Windows";
        }

        // 1. Clean Warframe Epic Cache Files using configured CacheFolder
        logger.LogInfo($"Cleaning Warframe cache files in: {cacheDir}");
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

        // 2. Clean Local Warframe Cache (%LocalAppData%\Warframe)
        logger.LogInfo("Cleaning LocalAppData Warframe cache directory...");
        var localAppDataPath = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "Warframe"
        );

        if (Directory.Exists(localAppDataPath))
        {
            CleanDirectoryContents(localAppDataPath, logger);
        }
        else
        {
            logger.LogInfo($"Skipped directory (does not exist): {localAppDataPath}");
        }

        logger.LogSuccess("Warframe cleanup completed successfully.");
    }

    private static void CleanDirectoryContents(string dirPath, LoggerService logger)
    {
        // 1. Clean files inside current directory
        string[] files;
        try
        {
            files = Directory.GetFiles(dirPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError($"Unauthorized access reading files in {dirPath}: {ex.Message}");
            files = Array.Empty<string>();
        }
        catch (IOException ex)
        {
            logger.LogError($"IO error reading files in {dirPath}: {ex.Message}");
            files = Array.Empty<string>();
        }

        foreach (var file in files)
        {
            DeleteFileSafely(file, logger);
        }

        // 2. Process subdirectories recursively
        string[] subDirs;
        try
        {
            subDirs = Directory.GetDirectories(dirPath);
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError($"Unauthorized access reading subdirectories in {dirPath}: {ex.Message}");
            subDirs = Array.Empty<string>();
        }
        catch (IOException ex)
        {
            logger.LogError($"IO error reading subdirectories in {dirPath}: {ex.Message}");
            subDirs = Array.Empty<string>();
        }

        foreach (var subDir in subDirs)
        {
            CleanDirectoryContents(subDir, logger);

            // Attempt to delete directory if now empty
            try
            {
                if (Directory.Exists(subDir) &&
                    Directory.GetFiles(subDir).Length == 0 &&
                    Directory.GetDirectories(subDir).Length == 0)
                {
                    Directory.Delete(subDir);
                    logger.LogInfo($"Deleted folder: {subDir}");
                }
                else if (Directory.Exists(subDir))
                {
                    logger.LogInfo($"Skipped folder (contains preserved files): {subDir}");
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                logger.LogError($"Unauthorized access deleting folder {subDir}: {ex.Message}");
            }
            catch (IOException ex)
            {
                logger.LogError($"IO error deleting folder {subDir}: {ex.Message}");
            }
        }
    }

    private static void DeleteFileSafely(string filePath, LoggerService logger)
    {
        if (Path.GetExtension(filePath).Equals(".exe", StringComparison.OrdinalIgnoreCase))
        {
            logger.LogInfo($"Skipped protected file (.exe): {filePath}");
            return;
        }

        if (!File.Exists(filePath))
        {
            logger.LogInfo($"Skipped file (does not exist): {filePath}");
            return;
        }

        try
        {
            File.Delete(filePath);
            logger.LogInfo($"Deleted file: {filePath}");
        }
        catch (UnauthorizedAccessException ex)
        {
            logger.LogError($"Unauthorized access deleting file {filePath}: {ex.Message}");
        }
        catch (IOException ex)
        {
            logger.LogError($"IO error deleting file {filePath}: {ex.Message}");
        }
    }
}
