using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class MultiFileTestItemResult
{
    public string RelativePath { get; set; } = string.Empty;
    public string AbsolutePath { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public long OriginalSize { get; set; }
    public string OriginalMd5 { get; set; } = string.Empty;
    public long FinalSize { get; set; }
    public string FinalMd5 { get; set; } = string.Empty;
    public string ExpectedMd5 { get; set; } = string.Empty;
    public FileStatus StatusBefore { get; set; }
    public FileStatus StatusAfter { get; set; }
    public bool ValidationPassed { get; set; }
}

public class MultiFileTestSessionResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string BackupDirectory { get; set; } = string.Empty;
    public string TempDirectory { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;
    public List<MultiFileTestItemResult> Items { get; set; } = new();
    public int AppletPid { get; set; } = -1;
    public int AppletExitCode { get; set; } = -1;
    public string PreprocessLogPath { get; set; } = string.Empty;
    public VerificationSummary? SummaryBefore { get; set; }
    public VerificationSummary? SummaryAfter { get; set; }
}

public class WarframeMultiFileTester
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly WarframeDownloader _downloader;
    private readonly WarframeFileVerifier _verifier;

    private static MultiFileTestSessionResult? _activeSession;

    public static MultiFileTestSessionResult? ActiveSession => _activeSession;

    public WarframeMultiFileTester(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _downloader = new WarframeDownloader(_logger);
        _verifier = new WarframeFileVerifier(_logger);
    }

    public async Task<MultiFileTestSessionResult> ExecuteMultiFileTestAsync(
        List<WarframeManifestEntry> selectedEntries,
        CancellationToken cancellationToken = default)
    {
        var session = new MultiFileTestSessionResult();

        if (selectedEntries == null || selectedEntries.Count == 0)
        {
            session.Message = "ABORTED: No manifest entries selected.";
            _logger.LogError($"✖ [MULTI-FILE TEST ABORTED] {session.Message}");
            return session;
        }

        // Task 1: Test Limit Assertion (Max 3 files)
        if (selectedEntries.Count > 3)
        {
            session.Message = $"ABORTED: Selected {selectedEntries.Count} files. Maximum allowed for controlled testing is 3 files.";
            _logger.LogError($"✖ [MULTI-FILE TEST ABORTED] {session.Message}");
            return session;
        }

        // Validate entry eligibility
        foreach (var entry in selectedEntries)
        {
            if (entry.Status == FileStatus.DynamicRebuildable ||
                entry.Status == FileStatus.MissingOptionalLanguage ||
                entry.Status == FileStatus.MissingOptionalGraphics ||
                entry.RelativePath.EndsWith(".toc", StringComparison.OrdinalIgnoreCase))
            {
                session.Message = $"ABORTED: File '{entry.RelativePath}' is not CDN-authoritative ({entry.Status}). Only MissingCore, Outdated, or Corrupted non-.toc entries are permitted.";
                _logger.LogError($"✖ [MULTI-FILE TEST ABORTED] {session.Message}");
                return session;
            }
        }

        string gameFolder = _settings.WarframeInstallFolder;

        _logger.LogInfo("================ MULTI-FILE CONTROLLED UPDATE TEST ================");
        _logger.LogInfo($"Selected Files Count: {selectedEntries.Count} (Max allowed: 3)");

        // Task 3: Multi-File Session Backup
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        session.BackupDirectory = Path.Combine(gameFolder, ".GameLauncherBackup", $"MultiFileTest_{timestamp}");
        Directory.CreateDirectory(session.BackupDirectory);

        session.TempDirectory = Path.Combine(gameFolder, ".GameLauncherTemp", $"Batch_{Guid.NewGuid():N}");
        Directory.CreateDirectory(session.TempDirectory);

        // Pre-test Verification Summary
        if (_updateManagerManifest != null)
        {
            session.SummaryBefore = await _verifier.VerifyInstallationAsync(_updateManagerManifest, gameFolder, _settings.WarframeLanguage, _settings.WarframeGraphicsApi);
        }

        var stagedDownloads = new List<(WarframeManifestEntry Entry, string TempPath, MultiFileTestItemResult Item)>();

        // Task 4: Snapshot and Download & Validate Payloads Sequentially
        foreach (var entry in selectedEntries)
        {
            cancellationToken.ThrowIfCancellationRequested();

            string resolvedTarget = WarframePathResolver.ResolveTargetInstallationPath(gameFolder, entry.RelativePath);
            string cleanRel = WarframePathResolver.ValidateAndCleanRelativePath(entry.RelativePath);
            string backupPath = Path.Combine(session.BackupDirectory, cleanRel);

            var itemResult = new MultiFileTestItemResult
            {
                RelativePath = entry.RelativePath,
                AbsolutePath = resolvedTarget,
                BackupPath = backupPath,
                ExpectedMd5 = entry.ContentHash,
                StatusBefore = entry.Status
            };
            session.Items.Add(itemResult);

            if (File.Exists(resolvedTarget))
            {
                var fi = new FileInfo(resolvedTarget);
                itemResult.OriginalSize = fi.Length;
                itemResult.OriginalMd5 = WarframeDownloader.ComputeMd5(resolvedTarget);

                string? bkpDir = Path.GetDirectoryName(backupPath);
                if (!string.IsNullOrEmpty(bkpDir)) Directory.CreateDirectory(bkpDir);

                File.Copy(resolvedTarget, backupPath, overwrite: true);
                _logger.LogInfo($"[BACKUP] Staged backup: '{entry.RelativePath}' -> '{backupPath}' (Size: {itemResult.OriginalSize} B, MD5: {itemResult.OriginalMd5})");
            }
            else
            {
                _logger.LogInfo($"[BACKUP] Target file '{entry.RelativePath}' does not exist on disk yet. MissingCore test.");
            }

            // Download to temporary folder
            string payloadTempFile = Path.Combine(session.TempDirectory, $"{Path.GetFileName(cleanRel)}.{Guid.NewGuid():N}.tmp");
            string extractedTempFile = Path.Combine(session.TempDirectory, $"{Path.GetFileName(cleanRel)}.{Guid.NewGuid():N}.extracted.tmp");

            _logger.LogInfo($"[DOWNLOAD] [{session.Items.Count}/{selectedEntries.Count}] Downloading CDN payload for '{entry.RelativePath}'...");
            await DownloadFileHelperAsync(entry.DownloadUrl, payloadTempFile, cancellationToken);

            string finalTempFile = payloadTempFile;

            if (entry.IsLzma)
            {
                long compressedSize = new FileInfo(payloadTempFile).Length;
                _logger.LogInfo($"[DECOMPRESS] Decompressing {compressedSize} B LZMA payload for '{entry.RelativePath}'...");
                bool decompOk = WarframeDownloader.DecompressLzmaFile(payloadTempFile, extractedTempFile);
                TryDeleteFile(payloadTempFile);

                if (!decompOk || !File.Exists(extractedTempFile))
                {
                    session.Message = $"ABORTED: LZMA decompression failed for '{entry.RelativePath}'. Aborting multi-file test session!";
                    _logger.LogError($"✖ {session.Message}");
                    CleanupTempDirectory(session.TempDirectory);
                    return session;
                }
                finalTempFile = extractedTempFile;
            }

            // Validate temp file size & hash
            itemResult.FinalSize = new FileInfo(finalTempFile).Length;
            itemResult.FinalMd5 = WarframeDownloader.ComputeMd5(finalTempFile);

            if (entry.IsBulk && entry.Size > 0 && itemResult.FinalSize != entry.Size)
            {
                session.Message = $"ABORTED: .bulk size mismatch for '{entry.RelativePath}' (Expected: {entry.Size} B, Got: {itemResult.FinalSize} B). Aborting session!";
                _logger.LogError($"✖ {session.Message}");
                CleanupTempDirectory(session.TempDirectory);
                return session;
            }

            if (!string.IsNullOrEmpty(entry.ContentHash) && !string.Equals(itemResult.FinalMd5, entry.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                session.Message = $"ABORTED: MD5 hash mismatch for '{entry.RelativePath}' (Expected: {entry.ContentHash}, Got: {itemResult.FinalMd5}). Aborting session!";
                _logger.LogError($"✖ {session.Message}");
                CleanupTempDirectory(session.TempDirectory);
                return session;
            }

            itemResult.ValidationPassed = true;
            stagedDownloads.Add((entry, finalTempFile, itemResult));
            _logger.LogSuccess($"✔ [DOWNLOAD VALIDATED] '{entry.RelativePath}' validated successfully (Size: {itemResult.FinalSize} B, MD5: {itemResult.FinalMd5}).");
        }

        // Task 6: Atomic Replacement of ALL selected files
        _logger.LogInfo("[ATOMIC REPLACE] All selected downloads passed validation. Performing atomic replacement...");
        try
        {
            foreach (var (entry, tempFile, itemResult) in stagedDownloads)
            {
                string? dir = Path.GetDirectoryName(itemResult.AbsolutePath);
                if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                if (File.Exists(itemResult.AbsolutePath))
                {
                    File.Delete(itemResult.AbsolutePath);
                }
                File.Move(tempFile, itemResult.AbsolutePath);
                _logger.LogInfo($"[REPLACED] '{entry.RelativePath}' -> '{itemResult.AbsolutePath}'");
            }
        }
        catch (Exception ex)
        {
            session.Message = $"ABORTED: Atomic replacement failed: {ex.Message}. Rolling back session...";
            _logger.LogError($"✖ {session.Message}");
            await RollbackSessionInternalAsync(session, gameFolder);
            CleanupTempDirectory(session.TempDirectory);
            return session;
        }

        // Task 7: Run Native ContentUpdate EXACTLY ONCE for the batch
        bool hasCachePackage = selectedEntries.Any(e => e.RelativePath.StartsWith("/Cache.Windows", StringComparison.OrdinalIgnoreCase) || e.RelativePath.EndsWith(".cache", StringComparison.OrdinalIgnoreCase));
        if (hasCachePackage)
        {
            _logger.LogInfo("[NATIVE APPLET] Running Warframe ContentUpdate preprocessor EXACTLY ONCE for the batch...");
            string exePath = WarframePathResolver.ResolveLocalPath(gameFolder, "/Warframe.x64.exe");
            if (string.IsNullOrEmpty(exePath)) exePath = Path.Combine(gameFolder, "Warframe.x64.exe");

            if (File.Exists(exePath))
            {
                var appletRes = RunNativeContentUpdateApplet(exePath, gameFolder);
                session.AppletPid = appletRes.Pid;
                session.AppletExitCode = appletRes.ExitCode;
                session.PreprocessLogPath = appletRes.LogPath;

                if (appletRes.ExitCode != 0)
                {
                    session.Message = $"ABORTED: Native ContentUpdate applet failed with exit code {appletRes.ExitCode}. Rolling back session...";
                    _logger.LogError($"✖ {session.Message}");
                    await RollbackSessionInternalAsync(session, gameFolder);
                    CleanupTempDirectory(session.TempDirectory);
                    return session;
                }
            }
        }

        // Task 8: Post-Update Verification
        foreach (var item in session.Items)
        {
            var entry = selectedEntries.First(e => e.RelativePath == item.RelativePath);
            item.StatusAfter = await _verifier.VerifySingleFileAsync(entry, gameFolder, _settings.WarframeLanguage, _settings.WarframeGraphicsApi);
            _logger.LogInfo($"[POST-VERIFY] '{item.RelativePath}' status after update: {item.StatusAfter}");
        }

        if (_updateManagerManifest != null)
        {
            session.SummaryAfter = await _verifier.VerifyInstallationAsync(_updateManagerManifest, gameFolder, _settings.WarframeLanguage, _settings.WarframeGraphicsApi);
        }

        CleanupTempDirectory(session.TempDirectory);

        session.Success = true;
        session.Message = $"Multi-file test update of {selectedEntries.Count} files completed successfully!";
        _activeSession = session;

        await GenerateMultiFileTestReportAsync(session);
        return session;
    }

    public async Task<bool> RollbackActiveSessionAsync()
    {
        if (_activeSession == null)
        {
            _logger.LogError("✖ [ROLLBACK MULTI-FILE] No active multi-file test session to rollback.");
            return false;
        }

        _logger.LogWarning($"[ROLLBACK MULTI-FILE] Rolling back {_activeSession.Items.Count} files from '{_activeSession.BackupDirectory}'...");
        bool result = await RollbackSessionInternalAsync(_activeSession, _settings.WarframeInstallFolder);
        if (result)
        {
            _activeSession = null;
        }
        return result;
    }

    public bool CommitActiveSession()
    {
        if (_activeSession == null)
        {
            _logger.LogError("✖ [COMMIT MULTI-FILE] No active multi-file test session to commit.");
            return false;
        }

        _logger.LogSuccess($"✔ [COMMIT MULTI-FILE] Committed changes for {_activeSession.Items.Count} files.");
        _activeSession = null;
        return true;
    }

    private static WarframeManifest? _updateManagerManifest;
    public static void SetManifestForAudit(WarframeManifest? manifest)
    {
        _updateManagerManifest = manifest;
    }

    private async Task<bool> RollbackSessionInternalAsync(MultiFileTestSessionResult session, string gameFolder)
    {
        int restored = 0;
        foreach (var item in session.Items)
        {
            try
            {
                if (File.Exists(item.BackupPath))
                {
                    string? dir = Path.GetDirectoryName(item.AbsolutePath);
                    if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

                    File.Copy(item.BackupPath, item.AbsolutePath, overwrite: true);
                    restored++;
                    _logger.LogInfo($"[RESTORED] Restored original '{item.RelativePath}' from backup.");
                }
                else if (item.OriginalSize == 0 && File.Exists(item.AbsolutePath))
                {
                    File.Delete(item.AbsolutePath);
                    restored++;
                    _logger.LogInfo($"[RESTORED] Removed missing core file '{item.RelativePath}'.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"✖ [RESTORE FAILED] Failed to restore '{item.RelativePath}': {ex.Message}");
            }
        }

        _logger.LogSuccess($"✔ [ROLLBACK COMPLETE] Restored {restored}/{session.Items.Count} files.");

        // Post-rollback verification scan
        if (_updateManagerManifest != null)
        {
            _logger.LogInfo("[POST-ROLLBACK VERIFY] Running full read-only verification after rollback...");
            var summary = await _verifier.VerifyInstallationAsync(_updateManagerManifest, gameFolder, _settings.WarframeLanguage, _settings.WarframeGraphicsApi);
            _logger.LogSuccess($"✔ [POST-ROLLBACK VERIFY COMPLETE] Valid: {summary.ValidFiles} | Outdated: {summary.OutdatedFiles} | Corrupted: {summary.CorruptedFiles}");
        }

        return restored == session.Items.Count;
    }

    private (int Pid, int ExitCode, string LogPath) RunNativeContentUpdateApplet(string exePath, string gameFolder)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = "-applet:/EE/Types/Framework/ContentUpdate -silent",
                WorkingDirectory = gameFolder,
                UseShellExecute = false,
                CreateNoWindow = true
            };

            var sw = Stopwatch.StartNew();
            using var proc = Process.Start(psi);
            if (proc != null)
            {
                int pid = proc.Id;
                proc.WaitForExit();
                sw.Stop();

                string logPath = Path.Combine(gameFolder, "Preprocess.log");
                _logger.LogInfo($"[NATIVE APPLET] Applet (PID {pid}) finished in {sw.ElapsedMilliseconds} ms with ExitCode {proc.ExitCode}. Log: '{logPath}'");
                return (pid, proc.ExitCode, logPath);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"✖ [NATIVE APPLET FAILED] {ex.Message}");
        }

        return (-1, -1, string.Empty);
    }

    private async Task DownloadFileHelperAsync(string url, string destPath, CancellationToken cancellationToken)
    {
        using var client = new System.Net.Http.HttpClient();
        using var response = await client.GetAsync(url, System.Net.Http.HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        using var content = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);
        await content.CopyToAsync(fileStream, cancellationToken);
    }

    private async Task GenerateMultiFileTestReportAsync(MultiFileTestSessionResult session)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine("     WARFRAME MULTI-FILE CONTROLLED TEST REPORT   ");
        sb.AppendLine("==================================================");
        sb.AppendLine($"Timestamp:            {DateTime.Now:yyyy-MM-dd HH:mm:ss}");
        sb.AppendLine($"Selected Files Count: {session.Items.Count}");
        sb.AppendLine($"Backup Directory:     {session.BackupDirectory}");
        sb.AppendLine($"Applet PID:           {session.AppletPid}");
        sb.AppendLine($"Applet Exit Code:     {session.AppletExitCode}");
        sb.AppendLine($"Preprocess.log:       {session.PreprocessLogPath}");
        sb.AppendLine($"Overall Test Result:  {(session.Success ? "PASSED" : "FAILED")} ({session.Message})");
        sb.AppendLine();
        sb.AppendLine("--- ITEMIZED TEST RESULTS ---");

        foreach (var item in session.Items)
        {
            sb.AppendLine($"Relative Path: {item.RelativePath}");
            sb.AppendLine($"  Absolute Target: {item.AbsolutePath}");
            sb.AppendLine($"  Backup Location: {item.BackupPath}");
            sb.AppendLine($"  Original Size:   {item.OriginalSize} B");
            sb.AppendLine($"  Original MD5:    {item.OriginalMd5}");
            sb.AppendLine($"  Final Size:      {item.FinalSize} B");
            sb.AppendLine($"  Final MD5:       {item.FinalMd5}");
            sb.AppendLine($"  Expected MD5:    {item.ExpectedMd5}");
            sb.AppendLine($"  Status Before:   {item.StatusBefore}");
            sb.AppendLine($"  Status After:    {item.StatusAfter}");
            sb.AppendLine("--------------------------------------------------");
        }

        if (session.SummaryBefore != null && session.SummaryAfter != null)
        {
            sb.AppendLine();
            sb.AppendLine("--- CLASSIFICATION SUMMARY BEFORE vs AFTER ---");
            sb.AppendLine($"Valid Files (OK):     Before: {session.SummaryBefore.ValidFiles}  => After: {session.SummaryAfter.ValidFiles}");
            sb.AppendLine($"Outdated Files:       Before: {session.SummaryBefore.OutdatedFiles}  => After: {session.SummaryAfter.OutdatedFiles}");
            sb.AppendLine($"Missing Core Files:   Before: {session.SummaryBefore.MissingFiles}  => After: {session.SummaryAfter.MissingFiles}");
            sb.AppendLine($"Corrupted Files:      Before: {session.SummaryBefore.CorruptedFiles}  => After: {session.SummaryAfter.CorruptedFiles}");
            sb.AppendLine($"Dynamic (.toc):       Before: {session.SummaryBefore.DynamicRebuildableFiles}  => After: {session.SummaryAfter.DynamicRebuildableFiles}");
            sb.AppendLine("--------------------------------------------------");
        }

        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher", "logs");
        Directory.CreateDirectory(logDir);
        session.LogPath = Path.Combine(logDir, $"multi_file_update_test_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        await File.WriteAllTextAsync(session.LogPath, sb.ToString());
        _logger.LogSuccess($"✔ [MULTI-FILE TEST REPORT] Saved report to '{session.LogPath}'");
    }

    private static void CleanupTempDirectory(string path)
    {
        try
        {
            if (Directory.Exists(path)) Directory.Delete(path, recursive: true);
        }
        catch { }
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
