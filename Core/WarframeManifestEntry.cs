using System;
using System.IO;

namespace GameLauncher.Core;

public enum FileStatus
{
    Unknown,
    OK,
    Missing,
    MissingOptionalLanguage,
    MissingOptionalGraphics,
    Outdated,
    Corrupted,
    DynamicRebuildable,
    Updating
}

public class WarframeManifestEntry
{
    public string RelativePath { get; set; } = string.Empty;
    public string ContentHash { get; set; } = string.Empty;
    public long Size { get; set; }
    public bool IsLzma { get; set; }
    public bool IsBulk { get; set; }
    public FileStatus Status { get; set; } = FileStatus.Unknown;

    public string LocalRelativePath
    {
        get
        {
            if (string.IsNullOrEmpty(RelativePath)) return string.Empty;
            string clean = RelativePath.TrimStart('/', '\\');
            return clean.Replace('/', Path.DirectorySeparatorChar);
        }
    }

    public string DownloadUrl
    {
        get
        {
            if (string.IsNullOrEmpty(RelativePath)) return string.Empty;
            string ext = IsLzma ? "lzma" : "bulk";
            return $"https://content.warframe.com/origin/00000000{RelativePath}.{ContentHash}.{ext}";
        }
    }

    public string DisplaySize => FormatBytes(Size);

    public static string FormatBytes(long bytes)
    {
        if (bytes <= 0) return "0 B";
        string[] units = { "B", "KB", "MB", "GB", "TB" };
        int digitGroup = (int)(Math.Log10(bytes) / Math.Log10(1024));
        return $"{bytes / Math.Pow(1024, digitGroup):F2} {units[digitGroup]}";
    }

    public override string ToString()
    {
        return $"{RelativePath} ({DisplaySize}) [{Status}]";
    }
}
