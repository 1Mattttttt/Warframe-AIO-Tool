using System;
using System.Collections.Generic;
using System.IO;
using System.Text.RegularExpressions;

namespace GameLauncher.Core;

public class WarframeManifest
{
    public const string DefaultIndexUrl = "https://content.warframe.com/origin/00000000/index.txt.lzma";

    public DateTime? ManifestLastModified { get; set; }
    public DateTime? ExeLastModified { get; set; }
    public List<WarframeManifestEntry> Entries { get; set; } = new();

    public int TotalFiles => Entries.Count;
    public long TotalSizeBytes
    {
        get
        {
            long sum = 0;
            foreach (var entry in Entries)
            {
                sum += entry.Size;
            }
            return sum;
        }
    }

    public string TotalSizeFormatted => WarframeManifestEntry.FormatBytes(TotalSizeBytes);

    private static readonly Regex LineRegex = new(@"^(.+)\.([A-F0-9]{32})\.(lzma|bulk),(\d+)$", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static WarframeManifest Parse(string manifestContent, DateTime? manifestLastModified = null, DateTime? exeLastModified = null)
    {
        var manifest = new WarframeManifest
        {
            ManifestLastModified = manifestLastModified,
            ExeLastModified = exeLastModified
        };

        if (string.IsNullOrWhiteSpace(manifestContent)) return manifest;

        using var reader = new StringReader(manifestContent);
        string? line;
        while ((line = reader.ReadLine()) != null)
        {
            line = line.Trim();
            if (string.IsNullOrEmpty(line)) continue;

            var match = LineRegex.Match(line);
            if (match.Success)
            {
                var entry = new WarframeManifestEntry
                {
                    RelativePath = match.Groups[1].Value,
                    ContentHash = match.Groups[2].Value.ToUpperInvariant(),
                    IsLzma = string.Equals(match.Groups[3].Value, "lzma", StringComparison.OrdinalIgnoreCase),
                    IsBulk = string.Equals(match.Groups[3].Value, "bulk", StringComparison.OrdinalIgnoreCase),
                    Size = long.TryParse(match.Groups[4].Value, out long size) ? size : 0
                };
                manifest.Entries.Add(entry);
            }
        }

        return manifest;
    }
}
