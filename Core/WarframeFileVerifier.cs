using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class VerificationSummary
{
    public int TotalFiles { get; set; }
    public int ValidFiles { get; set; }
    public int MissingFiles { get; set; }
    public int MissingOtherLangFiles { get; set; }
    public int MissingOtherGraphicsFiles { get; set; }
    public int OutdatedFiles { get; set; }
    public int CorruptedFiles { get; set; }
    public int DynamicRebuildableFiles { get; set; }
    public bool RequiresUserConfirmation { get; set; }

    public List<WarframeManifestEntry> MissingList { get; set; } = new();
    public List<WarframeManifestEntry> OutdatedList { get; set; } = new();
    public List<WarframeManifestEntry> CorruptedList { get; set; } = new();
    public List<WarframeManifestEntry> DynamicRebuildableList { get; set; } = new();
}

public class WarframeFileVerifier
{
    private readonly LoggerService _logger;
    private static readonly string[] SupportedLanguages = new[] { "de", "en", "es", "fr", "it", "ja", "ko", "pl", "ru", "tc", "th", "tr", "uk", "zh", "pt" };

    public WarframeFileVerifier(LoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async Task<VerificationSummary> VerifyInstallationAsync(
        WarframeManifest manifest,
        string gameFolder,
        string activeLanguage = "en",
        string activeGraphicsApi = "dx11",
        bool verifyHashes = true,
        Action<int, int, string>? progressCallback = null,
        CancellationToken cancellationToken = default)
    {
        var summary = new VerificationSummary
        {
            TotalFiles = manifest.Entries.Count
        };

        if (manifest.Entries.Count == 0 || string.IsNullOrWhiteSpace(gameFolder))
        {
            return summary;
        }

        _logger.LogInfo($"[VERIFY] Starting verification of {manifest.TotalFiles} files in '{gameFolder}' (Language: {activeLanguage}, Graphics API: {activeGraphicsApi})...");

        int current = 0;
        int total = manifest.Entries.Count;
        int debugCount = 0;

        await Task.Run(() =>
        {
            foreach (var entry in manifest.Entries)
            {
                cancellationToken.ThrowIfCancellationRequested();
                current++;

                progressCallback?.Invoke(current, total, entry.RelativePath);

                var status = VerifySingleFileInternal(entry, gameFolder, activeLanguage, activeGraphicsApi, verifyHashes, ref debugCount);
                entry.Status = status;

                switch (status)
                {
                    case FileStatus.OK:
                        summary.ValidFiles++;
                        break;
                    case FileStatus.Missing:
                        summary.MissingFiles++;
                        summary.MissingList.Add(entry);
                        break;
                    case FileStatus.MissingOptionalLanguage:
                        summary.MissingOtherLangFiles++;
                        break;
                    case FileStatus.MissingOptionalGraphics:
                        summary.MissingOtherGraphicsFiles++;
                        break;
                    case FileStatus.Outdated:
                        summary.OutdatedFiles++;
                        summary.OutdatedList.Add(entry);
                        break;
                    case FileStatus.Corrupted:
                        summary.CorruptedFiles++;
                        summary.CorruptedList.Add(entry);
                        break;
                    case FileStatus.DynamicRebuildable:
                        summary.DynamicRebuildableFiles++;
                        summary.DynamicRebuildableList.Add(entry);
                        break;
                }
            }
        }, cancellationToken);

        int coreDownloadable = summary.MissingFiles + summary.OutdatedFiles + summary.CorruptedFiles;
        if (total > 0 && (double)coreDownloadable / total > 0.40)
        {
            summary.RequiresUserConfirmation = true;
            _logger.LogWarning($"[VERIFY SAFETY] Verification detected an unusually large number of differences ({coreDownloadable} / {total} files). Automatic repair has been paused.");
        }

        _logger.LogSuccess($"✔ [VERIFY] Classification Summary: Valid (OK): {summary.ValidFiles} | Missing Core: {summary.MissingFiles} | Missing (Opt Lang): {summary.MissingOtherLangFiles} | Missing (Opt Graphics): {summary.MissingOtherGraphicsFiles} | Outdated: {summary.OutdatedFiles} | Corrupted: {summary.CorruptedFiles} | Dynamic/Rebuildable (.toc): {summary.DynamicRebuildableFiles}");
        return summary;
    }

    public async Task<FileStatus> VerifySingleFileAsync(
        WarframeManifestEntry entry,
        string gameFolder,
        string activeLanguage = "en",
        string activeGraphicsApi = "dx11",
        bool verifyHashes = true)
    {
        int dummyCount = 999;
        return await Task.Run(() => VerifySingleFileInternal(entry, gameFolder, activeLanguage, activeGraphicsApi, verifyHashes, ref dummyCount));
    }

    public async Task<string> ExportPreFlightAuditReportAsync(
        WarframeManifest manifest,
        string gameFolder,
        string activeLanguage = "en",
        string activeGraphicsApi = "dx11")
    {
        var summary = await VerifyInstallationAsync(manifest, gameFolder, activeLanguage, activeGraphicsApi, verifyHashes: true);

        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine("         WARFRAME UPDATE PRE-FLIGHT REPORT        ");
        sb.AppendLine("==================================================");
        sb.AppendLine($"Timestamp:            {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Game Folder:          {gameFolder}");
        sb.AppendLine($"Active Language:      {activeLanguage}");
        sb.AppendLine($"Active Graphics API:  {activeGraphicsApi}");
        sb.AppendLine($"Manifest Entry Count: {manifest.TotalFiles}");
        sb.AppendLine($"Manifest Total Payload: {manifest.TotalSizeFormatted}");
        sb.AppendLine();
        sb.AppendLine("--- VERIFICATION CLASSIFICATION SUMMARY ---");
        sb.AppendLine($"Valid Files (OK):                  {summary.ValidFiles}");
        sb.AppendLine($"Missing Core Files:                {summary.MissingFiles}");
        sb.AppendLine($"Missing Optional Language Files:   {summary.MissingOtherLangFiles}");
        sb.AppendLine($"Missing Optional Graphics Files:   {summary.MissingOtherGraphicsFiles}");
        sb.AppendLine($"Outdated Files (.bulk):            {summary.OutdatedFiles}");
        sb.AppendLine($"Corrupted Files:                   {summary.CorruptedFiles}");
        sb.AppendLine($"Dynamic / Rebuildable (.toc):      {summary.DynamicRebuildableFiles}");
        sb.AppendLine("--------------------------------------------------");
        sb.AppendLine();

        sb.AppendLine("--- DETAILED MANIFEST ENTRY MAPPINGS ---");
        foreach (var entry in manifest.Entries)
        {
            string resolved = WarframePathResolver.ResolveLocalPath(gameFolder, entry.RelativePath);
            sb.AppendLine($"Manifest Path:  {entry.RelativePath}");
            sb.AppendLine($"  Local Resolved Path: {(string.IsNullOrEmpty(resolved) ? "NOT FOUND" : resolved)}");
            sb.AppendLine($"  Format:              {(entry.IsLzma ? "lzma (Compressed Payload)" : "bulk (Uncompressed)")}");
            sb.AppendLine($"  Expected Size (CDN): {entry.Size} B");
            sb.AppendLine($"  Actual Size (Disk):  {(File.Exists(resolved) ? new FileInfo(resolved).Length : 0)} B");
            sb.AppendLine($"  Expected Hash:       {entry.ContentHash}");
            sb.AppendLine($"  Classification:      {entry.Status}");
            sb.AppendLine("--------------------------------------------------");
        }

        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher", "logs");
        Directory.CreateDirectory(logDir);
        string reportFile = Path.Combine(logDir, $"warframe_update_audit_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        await File.WriteAllTextAsync(reportFile, sb.ToString());
        _logger.LogSuccess($"✔ [AUDIT EXPORT] Pre-flight audit report saved to '{reportFile}'");
        return reportFile;
    }

    private FileStatus VerifySingleFileInternal(
        WarframeManifestEntry entry,
        string gameFolder,
        string activeLanguage,
        string activeGraphicsApi,
        bool verifyHashes,
        ref int debugCount)
    {
        // 1. Language filter check (Inactive language assets are optional)
        if (IsOtherLanguageFile(entry.RelativePath, activeLanguage))
        {
            return FileStatus.MissingOptionalLanguage;
        }

        // 2. Graphics API filter check (Inactive API assets are optional)
        if (IsOtherGraphicsApiFile(entry.RelativePath, activeGraphicsApi))
        {
            return FileStatus.MissingOptionalGraphics;
        }

        // 3. Dynamic rebuildable index check (.toc files are generated/rebuilt locally by native ContentUpdate applet)
        if (entry.RelativePath.EndsWith(".toc", StringComparison.OrdinalIgnoreCase))
        {
            return FileStatus.DynamicRebuildable;
        }

        // 4. Local file existence check
        string resolvedPath = WarframePathResolver.ResolveLocalPath(gameFolder, entry.RelativePath);
        if (string.IsNullOrEmpty(resolvedPath) || !File.Exists(resolvedPath))
        {
            LogDiagnostic(ref debugCount, entry, null, "MISSING CORE", "Required game file missing on local disk");
            return FileStatus.Missing;
        }

        var fileInfo = new FileInfo(resolvedPath);

        // Zero-byte placeholder check for required active files
        if (fileInfo.Length == 0 && entry.Size > 0)
        {
            LogDiagnostic(ref debugCount, entry, resolvedPath, "MISSING CORE", "Local file exists but is a 0-byte placeholder");
            return FileStatus.Missing;
        }

        // 5. Uncompressed .bulk size verification
        // NOTE: Size equality check applies EXCLUSIVELY to .bulk entries.
        // For .lzma entries, entry.Size is the compressed CDN payload size, NOT the installed disk size.
        if (entry.IsBulk && entry.Size > 0 && fileInfo.Length != entry.Size)
        {
            LogDiagnostic(ref debugCount, entry, resolvedPath, "OUTDATED", $"Uncompressed .bulk file size mismatch (Expected: {entry.Size} B, Actual: {fileInfo.Length} B)");
            return FileStatus.Outdated;
        }

        // 6. MD5 Hash Verification (Authoritative for both .bulk and decompressed .lzma files)
        if (verifyHashes && !string.IsNullOrEmpty(entry.ContentHash))
        {
            string computedHash = WarframeDownloader.ComputeMd5(resolvedPath);
            if (!string.Equals(computedHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                LogDiagnostic(ref debugCount, entry, resolvedPath, "OUTDATED", $"MD5 Hash mismatch (Expected: {entry.ContentHash}, Computed: {computedHash})");
                return FileStatus.Outdated;
            }
        }

        return FileStatus.OK;
    }

    private static bool IsOtherLanguageFile(string relPath, string activeLang)
    {
        foreach (string lang in SupportedLanguages)
        {
            if (string.Equals(lang, activeLang, StringComparison.OrdinalIgnoreCase)) continue;

            if (relPath.EndsWith($"_{lang}.rtf", StringComparison.OrdinalIgnoreCase) ||
                relPath.EndsWith($"_{lang}.toc", StringComparison.OrdinalIgnoreCase) ||
                relPath.EndsWith($"_{lang}.cache", StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }
        return false;
    }

    private static bool IsOtherGraphicsApiFile(string relPath, string activeGraphicsApi)
    {
        string inactiveApi = string.Equals(activeGraphicsApi, "dx12", StringComparison.OrdinalIgnoreCase) ? "dx11" : "dx12";
        return (relPath.Contains("Dx12", StringComparison.OrdinalIgnoreCase) && inactiveApi == "dx12") ||
               (relPath.Contains("Dx11", StringComparison.OrdinalIgnoreCase) && inactiveApi == "dx11");
    }

    private void LogDiagnostic(ref int debugCount, WarframeManifestEntry entry, string? localPath, string classification, string reason)
    {
        if (debugCount < 10)
        {
            debugCount++;
            var fileInfo = localPath != null ? new FileInfo(localPath) : null;
            _logger.LogInfo($"[VERIFY DEBUG #{debugCount}]\n" +
                            $"Manifest Path:  {entry.RelativePath}\n" +
                            $"Resolved Local: {localPath ?? "NOT FOUND"}\n" +
                            $"Format:         {(entry.IsLzma ? "lzma (Compressed CDN Payload)" : "bulk (Uncompressed)")}\n" +
                            $"Expected Size:  {entry.Size} B (CDN Payload)\n" +
                            $"Actual Size:    {(fileInfo != null ? fileInfo.Length : 0)} B (Local File)\n" +
                            $"Expected Hash:  {entry.ContentHash}\n" +
                            $"Classification: {classification}\n" +
                            $"Reason:         {reason}\n" +
                            $"--------------------------------------------------");
        }
    }
}
