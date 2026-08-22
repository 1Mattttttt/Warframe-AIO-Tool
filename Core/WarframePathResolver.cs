using System;
using System.IO;

namespace GameLauncher.Core;

public static class WarframePathResolver
{
    public static string ResolveLocalPath(string gameFolder, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        string cleanRel = ValidateAndCleanRelativePath(relativePath);
        if (string.IsNullOrEmpty(cleanRel)) return string.Empty;

        // Path 1: Direct in gameFolder
        string path1 = Path.Combine(gameFolder, cleanRel);
        if (File.Exists(path1)) return path1;

        // Path 2: Inside gameFolder\Downloaded
        string path2 = Path.Combine(gameFolder, "Downloaded", cleanRel);
        if (File.Exists(path2)) return path2;

        // Path 3: If gameFolder is ...\Downloaded, check parent directory
        if (gameFolder.EndsWith("Downloaded", StringComparison.OrdinalIgnoreCase))
        {
            var parent = Directory.GetParent(gameFolder);
            if (parent != null)
            {
                string path3 = Path.Combine(parent.FullName, cleanRel);
                if (File.Exists(path3)) return path3;
            }
        }

        return string.Empty;
    }

    public static string ResolveTargetInstallationPath(string gameFolder, string relativePath)
    {
        if (string.IsNullOrWhiteSpace(gameFolder) || string.IsNullOrWhiteSpace(relativePath))
        {
            return string.Empty;
        }

        string existing = ResolveLocalPath(gameFolder, relativePath);
        if (!string.IsNullOrEmpty(existing) && File.Exists(existing))
        {
            return existing;
        }

        string cleanRel = ValidateAndCleanRelativePath(relativePath);
        if (string.IsNullOrEmpty(cleanRel)) return string.Empty;

        return Path.Combine(gameFolder, cleanRel);
    }

    public static string ValidateAndCleanRelativePath(string relativePath)
    {
        if (string.IsNullOrWhiteSpace(relativePath)) return string.Empty;
        string clean = relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
        
        // Prevent path traversal
        if (clean.Contains(".."))
        {
            throw new InvalidOperationException($"Invalid relative path containing path traversal: {relativePath}");
        }

        return clean;
    }
}
