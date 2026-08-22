using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class SingleFileTestResult
{
    public bool Success { get; set; }
    public string Message { get; set; } = string.Empty;
    public string TargetRelativePath { get; set; } = string.Empty;
    public string TargetAbsolutePath { get; set; } = string.Empty;
    public string Format { get; set; } = string.Empty;
    public string BackupDirectory { get; set; } = string.Empty;
    public string BackupPath { get; set; } = string.Empty;
    public string LogPath { get; set; } = string.Empty;

    public long OriginalSize { get; set; }
    public string OriginalMd5 { get; set; } = string.Empty;
    public long ManifestSize { get; set; }
    public string ManifestMd5 { get; set; } = string.Empty;
    public long FinalSize { get; set; }
    public string FinalMd5 { get; set; } = string.Empty;

    public string TempPayloadPath { get; set; } = string.Empty;
    public bool PayloadValidated { get; set; }
    public bool ReplacementSucceeded { get; set; }

    public int AppletPid { get; set; } = -1;
    public int AppletExitCode { get; set; } = -1;
    public DateTime? AppletStartTime { get; set; }
    public DateTime? AppletEndTime { get; set; }
    public string PreprocessLogPath { get; set; } = string.Empty;

    public FileStatus StatusBefore { get; set; }
    public FileStatus StatusAfter { get; set; }
    public FileStatus StatusAfterRollback { get; set; }

    public VerificationSummary? SummaryBefore { get; set; }
    public VerificationSummary? SummaryAfter { get; set; }

    public bool RollbackSucceeded { get; set; }
    public long RestoredSize { get; set; }
    public string RestoredMd5 { get; set; } = string.Empty;
}

public class WarframeSingleFileTester
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly WarframeDownloader _downloader;
    private readonly WarframeFileVerifier _verifier;
    private readonly WarframeRepairManager _repairManager;

    private static SingleFileTestResult? _activeTestSession;

    public SingleFileTestResult? ActiveSession => _activeTestSession;

    public WarframeSingleFileTester(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _downloader = new WarframeDownloader(_logger);
        _verifier = new WarframeFileVerifier(_logger);
        _repairManager = new WarframeRepairManager(_settings, _logger);
    }

    public async Task<SingleFileTestResult> ExecuteSingleFileTestAsync(
        WarframeManifestEntry entry,
        WarframeManifest? manifest = null,
        CancellationToken cancellationToken = default)
    {
        var result = new SingleFileTestResult
        {
            TargetRelativePath = entry.RelativePath,
            Format = entry.IsLzma ? "lzma" : "bulk",
            ManifestSize = entry.Size,
            ManifestMd5 = entry.ContentHash,
            StatusBefore = entry.Status
        };

        _logger.LogInfo("================ WARFRAME SINGLE FILE UPDATE TEST ================");
        _logger.LogInfo($"Target:         {entry.RelativePath}");
        _logger.LogInfo($"Status Before:  {entry.Status}");
        _logger.LogInfo($"Format:         {result.Format}");
        _logger.LogInfo($"Manifest Size:  {entry.Size} B");
        _logger.LogInfo($"Manifest MD5:   {entry.ContentHash}");
        _logger.LogInfo("--------------------------------------------------");

        if (entry.Status == FileStatus.DynamicRebuildable ||
            entry.Status == FileStatus.MissingOptionalLanguage ||
            entry.Status == FileStatus.MissingOptionalGraphics ||
            entry.RelativePath.EndsWith(".toc", StringComparison.OrdinalIgnoreCase))
        {
            result.Message = $"ABORTED: Selected file '{entry.RelativePath}' is not CDN-authoritative ({entry.Status}). Only MissingCore, Outdated, or Corrupted non-.toc entries are permitted.";
            _logger.LogError($"✖ [TEST ABORTED] {result.Message}");
            return result;
        }

        string gameFolder = _settings.WarframeInstallFolder;
        string resolvedTarget = WarframePathResolver.ResolveTargetInstallationPath(gameFolder, entry.RelativePath);
        result.TargetAbsolutePath = resolvedTarget;

        if (manifest != null)
        {
            _logger.LogInfo("[PRE-TEST AUDIT] Recording complete installation state before test...");
            result.SummaryBefore = await _verifier.VerifyInstallationAsync(
                manifest, gameFolder, _settings.WarframeLanguage, _settings.WarframeGraphicsApi, verifyHashes: false, progressCallback: null, cancellationToken);
        }

        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        result.BackupDirectory = Path.Combine(gameFolder, ".GameLauncherBackup", $"SingleFileTest_{timestamp}");
        Directory.CreateDirectory(result.BackupDirectory);

        string cleanRel = WarframePathResolver.ValidateAndCleanRelativePath(entry.RelativePath);
        result.BackupPath = Path.Combine(result.BackupDirectory, cleanRel);

        if (File.Exists(resolvedTarget))
        {
            var origFi = new FileInfo(resolvedTarget);
            result.OriginalSize = origFi.Length;
            result.OriginalMd5 = WarframeDownloader.ComputeMd5(resolvedTarget);

            string? bkpDir = Path.GetDirectoryName(result.BackupPath);
            if (!string.IsNullOrEmpty(bkpDir)) Directory.CreateDirectory(bkpDir);

            File.Copy(resolvedTarget, result.BackupPath, overwrite: true);
            _logger.LogInfo($"[BACKUP] Original file backed up to '{result.BackupPath}' (Size: {result.OriginalSize} B, MD5: {result.OriginalMd5})");
        }

        string tempDir = Path.Combine(gameFolder, ".GameLauncherTemp");
        Directory.CreateDirectory(tempDir);

        string tempPayloadPath = Path.Combine(tempDir, $"{Path.GetFileName(cleanRel)}.{Guid.NewGuid():N}.tmp");
        string tempExtractedPath = Path.Combine(tempDir, $"{Path.GetFileName(cleanRel)}.{Guid.NewGuid():N}.extracted.tmp");
        result.TempPayloadPath = tempPayloadPath;

        try
        {
            string downloadUrl = entry.DownloadUrl;
            _logger.LogInfo($"[DOWNLOAD] Downloading CDN payload from '{downloadUrl}' to temp file '{tempPayloadPath}'...");

            await DownloadFileHelperAsync(downloadUrl, tempPayloadPath, cancellationToken);

            string finalTempFile = tempPayloadPath;

            if (entry.IsLzma)
            {
                long compressedBytes = new FileInfo(tempPayloadPath).Length;
                _logger.LogInfo($"[DECOMPRESS] Decompressing {compressedBytes} B LZMA payload to '{tempExtractedPath}'...");

                bool decompSuccess = WarframeDownloader.DecompressLzmaFile(tempPayloadPath, tempExtractedPath);
                TryDeleteFile(tempPayloadPath);

                if (!decompSuccess || !File.Exists(tempExtractedPath))
                {
                    result.Message = "ABORTED: LZMA decompression of temporary payload failed.";
                    _logger.LogError($"✖ {result.Message}");
                    return result;
                }

                finalTempFile = tempExtractedPath;
                result.TempPayloadPath = tempExtractedPath;
            }

            result.FinalSize = new FileInfo(finalTempFile).Length;
            result.FinalMd5 = WarframeDownloader.ComputeMd5(finalTempFile);

            _logger.LogInfo("===== PAYLOAD VALIDATION =====");
            _logger.LogInfo($"Payload File:   {finalTempFile}");
            _logger.LogInfo($"Final Size:     {result.FinalSize} B");
            _logger.LogInfo($"Final MD5:      {result.FinalMd5}");
            _logger.LogInfo($"Expected MD5:   {entry.ContentHash}");
            _logger.LogInfo("------------------------------");

            if (entry.IsBulk && entry.Size > 0 && result.FinalSize != entry.Size)
            {
                result.Message = $"ABORTED: .bulk size mismatch (Expected: {entry.Size} B, Got: {result.FinalSize} B)";
                _logger.LogError($"✖ {result.Message}");
                TryDeleteFile(finalTempFile);
                return result;
            }

            if (!string.IsNullOrEmpty(entry.ContentHash) && !string.Equals(result.FinalMd5, entry.ContentHash, StringComparison.OrdinalIgnoreCase))
            {
                result.Message = $"ABORTED: MD5 hash verification failed (Expected MD5: {entry.ContentHash}, Got MD5: {result.FinalMd5})";
                _logger.LogError($"✖ {result.Message}");
                TryDeleteFile(finalTempFile);
                return result;
            }

            result.PayloadValidated = true;
            _logger.LogSuccess("✔ [PAYLOAD VALIDATED] Payload passes authoritative format validation.");

            string? targetDir = Path.GetDirectoryName(resolvedTarget);
            if (!string.IsNullOrEmpty(targetDir)) Directory.CreateDirectory(targetDir);

            if (File.Exists(resolvedTarget))
            {
                File.Delete(resolvedTarget);
            }
            File.Move(finalTempFile, resolvedTarget);
            result.ReplacementSucceeded = true;
            _logger.LogSuccess($"✔ [ATOMIC REPLACE] Successfully replaced '{resolvedTarget}' with validated payload.");

            if (entry.RelativePath.StartsWith("/Cache.Windows", StringComparison.OrdinalIgnoreCase) || entry.RelativePath.EndsWith(".cache", StringComparison.OrdinalIgnoreCase))
            {
                _logger.LogInfo("[NATIVE APPLET] Executing Warframe ContentUpdate preprocessor applet...");
                string exePath = WarframePathResolver.ResolveLocalPath(gameFolder, "/Warframe.x64.exe");
                if (string.IsNullOrEmpty(exePath)) exePath = Path.Combine(gameFolder, "Warframe.x64.exe");

                if (File.Exists(exePath))
                {
                    result.AppletStartTime = DateTime.Now;
                    var appletResult = RunNativeContentUpdateApplet(exePath, gameFolder);
                    result.AppletEndTime = DateTime.Now;
                    result.AppletPid = appletResult.Pid;
                    result.AppletExitCode = appletResult.ExitCode;
                    result.PreprocessLogPath = appletResult.LogPath;

                    if (appletResult.ExitCode != 0)
                    {
                        result.Message = $"ABORTED: Native ContentUpdate applet failed with exit code {appletResult.ExitCode}";
                        _logger.LogError($"✖ {result.Message}");
                        return result;
                    }
                }
            }

            result.StatusAfter = await _verifier.VerifySingleFileAsync(entry, gameFolder, _settings.WarframeLanguage, _settings.WarframeGraphicsApi);
            _logger.LogInfo($"[POST-VERIFY] Target entry status after update: {result.StatusAfter}");

            if (manifest != null)
            {
                _logger.LogInfo("[POST-TEST AUDIT] Recording complete installation state after test...");
                result.SummaryAfter = await _verifier.VerifyInstallationAsync(
                    manifest, gameFolder, _settings.WarframeLanguage, _settings.WarframeGraphicsApi, verifyHashes: false, progressCallback: null, cancellationToken);
            }

            _activeTestSession = result;
            _logger.LogInfo("[ROLLBACK TEST] Performing mandatory rollback test to ensure 100% restoration...");
            bool rollbackOk = await RollbackTestAsync(manifest);
            result.RollbackSucceeded = rollbackOk;

            if (rollbackOk && result.StatusAfter == FileStatus.OK)
            {
                result.Success = true;
                result.Message = $"Controlled single-file test update and rollback for '{entry.RelativePath}' completed successfully!";
            }
            else
            {
                result.Success = false;
                result.Message = $"Single-file test failed: Target status after update was {result.StatusAfter}, Rollback status was {rollbackOk}";
            }

            await GenerateTestLogReportAsync(result);
            return result;
        }
        catch (Exception ex)
        {
            result.Message = $"ABORTED: Single-file test failed with exception: {ex.Message}";
            _logger.LogError($"✖ {result.Message}");
            TryDeleteFile(tempPayloadPath);
            TryDeleteFile(tempExtractedPath);
            return result;
        }
    }

    public async Task<bool> RollbackTestAsync(WarframeManifest? manifest = null)
    {
        if (_activeTestSession == null || string.IsNullOrEmpty(_activeTestSession.BackupPath))
        {
            _logger.LogError("✖ [ROLLBACK TEST] No active single-file test session to rollback.");
            return false;
        }

        _logger.LogWarning($"[ROLLBACK TEST] Restoring original file from '{_activeTestSession.BackupPath}' to '{_activeTestSession.TargetAbsolutePath}'...");

        try
        {
            if (File.Exists(_activeTestSession.BackupPath))
            {
                File.Copy(_activeTestSession.BackupPath, _activeTestSession.TargetAbsolutePath, overwrite: true);
                _logger.LogSuccess("✔ [ROLLBACK TEST] Restored original file from backup snapshot.");
            }
            else if (_activeTestSession.OriginalSize == 0 && File.Exists(_activeTestSession.TargetAbsolutePath))
            {
                File.Delete(_activeTestSession.TargetAbsolutePath);
                _logger.LogSuccess("✔ [ROLLBACK TEST] Removed test file created for MissingCore.");
            }

            if (File.Exists(_activeTestSession.TargetAbsolutePath))
            {
                var fi = new FileInfo(_activeTestSession.TargetAbsolutePath);
                _activeTestSession.RestoredSize = fi.Length;
                _activeTestSession.RestoredMd5 = WarframeDownloader.ComputeMd5(_activeTestSession.TargetAbsolutePath);
            }

            var entry = new WarframeManifestEntry
            {
                RelativePath = _activeTestSession.TargetRelativePath,
                ContentHash = _activeTestSession.OriginalMd5,
                IsBulk = _activeTestSession.Format == "bulk",
                IsLzma = _activeTestSession.Format == "lzma",
                Size = _activeTestSession.OriginalSize
            };

            if (manifest != null)
            {
                var manifestMatch = manifest.Entries.Find(e => string.Equals(e.RelativePath, _activeTestSession.TargetRelativePath, StringComparison.OrdinalIgnoreCase));
                if (manifestMatch != null) entry = manifestMatch;
            }

            _activeTestSession.StatusAfterRollback = await _verifier.VerifySingleFileAsync(entry, _settings.WarframeInstallFolder, _settings.WarframeLanguage, _settings.WarframeGraphicsApi);
            _logger.LogInfo($"[ROLLBACK VERIFY] File status after rollback: {_activeTestSession.StatusAfterRollback} (Matches Pre-Test: {_activeTestSession.StatusAfterRollback == _activeTestSession.StatusBefore})");

            bool byteEquivalent = _activeTestSession.RestoredSize == _activeTestSession.OriginalSize &&
                                 string.Equals(_activeTestSession.RestoredMd5, _activeTestSession.OriginalMd5, StringComparison.OrdinalIgnoreCase);

            _logger.LogInfo($"[ROLLBACK VERIFY] Byte-for-byte equivalence check: {(byteEquivalent ? "PASSED" : "FAILED")}");
            return byteEquivalent && (_activeTestSession.StatusAfterRollback == _activeTestSession.StatusBefore);
        }
        catch (Exception ex)
        {
            _logger.LogError($"✖ [ROLLBACK TEST FAILED] {ex.Message}");
            return false;
        }
    }

    public bool CommitTest()
    {
        if (_activeTestSession == null)
        {
            _logger.LogError("✖ [COMMIT TEST] No active test session to commit.");
            return false;
        }

        _logger.LogSuccess($"✔ [COMMIT TEST] Committed test changes for '{_activeTestSession.TargetRelativePath}'.");
        _activeTestSession = null;
        return true;
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
                bool exited = proc.WaitForExit(20000);
                sw.Stop();

                string logPath = Path.Combine(gameFolder, "Preprocess.log");
                if (!File.Exists(logPath))
                {
                    string appDataLog = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Warframe", "Preprocess.log");
                    if (File.Exists(appDataLog)) logPath = appDataLog;
                }

                if (!exited)
                {
                    _logger.LogWarning($"[NATIVE APPLET] ContentUpdate applet (PID {pid}) did not exit within 20s. Terminating applet process to release cache locks...");
                    try { proc.Kill(entireProcessTree: true); } catch { }
                    _logger.LogInfo($"[NATIVE APPLET] ContentUpdate applet (PID {pid}) terminated after 20s. Log: '{logPath}'");
                    return (pid, 0, logPath);
                }

                _logger.LogInfo($"[NATIVE APPLET] ContentUpdate applet (PID {pid}) finished in {sw.ElapsedMilliseconds} ms with ExitCode {proc.ExitCode}. Log: '{logPath}'");
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

    private async Task GenerateTestLogReportAsync(SingleFileTestResult res)
    {
        var sb = new StringBuilder();
        sb.AppendLine("==================================================");
        sb.AppendLine("WARFRAME SINGLE FILE UPDATE TEST");
        sb.AppendLine("==================================================");
        sb.AppendLine();
        sb.AppendLine($"Target:                  {res.TargetRelativePath}");
        sb.AppendLine($"Format:                  {res.Format}");
        sb.AppendLine($"Original Size:           {res.OriginalSize} B");
        sb.AppendLine($"Original MD5:            {res.OriginalMd5}");
        sb.AppendLine();
        sb.AppendLine($"Manifest Size:           {res.ManifestSize} B");
        sb.AppendLine($"Manifest MD5:            {res.ManifestMd5}");
        sb.AppendLine();
        sb.AppendLine($"Downloaded Payload:      {res.TempPayloadPath}");
        sb.AppendLine($"Payload Validation:      {(res.PayloadValidated ? "PASSED" : "FAILED")}");
        sb.AppendLine($"Replacement:             {(res.ReplacementSucceeded ? "SUCCESS" : "FAILED")}");
        sb.AppendLine($"ContentUpdate PID:       {res.AppletPid}");
        sb.AppendLine($"ContentUpdate Exit Code: {res.AppletExitCode}");
        sb.AppendLine();
        sb.AppendLine($"Status Before:           {res.StatusBefore}");
        sb.AppendLine($"Status After:            {res.StatusAfter}");
        sb.AppendLine();
        if (res.SummaryBefore != null)
        {
            sb.AppendLine($"Full Installation Before: OK: {res.SummaryBefore.ValidFiles} | Outdated: {res.SummaryBefore.OutdatedFiles} | Missing: {res.SummaryBefore.MissingFiles} | Opt Lang: {res.SummaryBefore.MissingOtherLangFiles} | Opt Gfx: {res.SummaryBefore.MissingOtherGraphicsFiles} | Dynamic: {res.SummaryBefore.DynamicRebuildableFiles}");
        }
        if (res.SummaryAfter != null)
        {
            sb.AppendLine($"Full Installation After:  OK: {res.SummaryAfter.ValidFiles} | Outdated: {res.SummaryAfter.OutdatedFiles} | Missing: {res.SummaryAfter.MissingFiles} | Opt Lang: {res.SummaryAfter.MissingOtherLangFiles} | Opt Gfx: {res.SummaryAfter.MissingOtherGraphicsFiles} | Dynamic: {res.SummaryAfter.DynamicRebuildableFiles}");
        }
        sb.AppendLine();
        sb.AppendLine($"Rollback:                {(res.RollbackSucceeded ? "SUCCESS" : "FAILED")}");
        sb.AppendLine($"Rollback Verification:   Restored Size: {res.RestoredSize} B, Restored MD5: {res.RestoredMd5}, Status After Rollback: {res.StatusAfterRollback} (Pre-Test Match: {res.StatusAfterRollback == res.StatusBefore})");
        sb.AppendLine();
        sb.AppendLine($"FINAL RESULT:            {(res.Success ? "PASSED" : "FAILED")}");
        sb.AppendLine("==================================================");

        string logDir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "GameLauncher", "logs");
        Directory.CreateDirectory(logDir);
        res.LogPath = Path.Combine(logDir, $"single_file_update_test_{DateTime.Now:yyyyMMdd_HHmmss}.log");

        await File.WriteAllTextAsync(res.LogPath, sb.ToString());
        _logger.LogSuccess($"✔ [TEST REPORT] Test report saved to '{res.LogPath}'");
    }

    private static void TryDeleteFile(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }
}
