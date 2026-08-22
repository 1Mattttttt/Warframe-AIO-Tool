using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Configuration;
using GameLauncher.Logging;
using GameLauncher.Models;

namespace GameLauncher.Core;

public class WarframeUpdateManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly WarframeDownloader _downloader;
    private readonly WarframeFileVerifier _verifier;
    private readonly WarframeRepairManager _repairManager;
    private readonly WarframeLaunchManager _launchManager;
    private readonly WarframeBackupManager _backupManager;

    private CancellationTokenSource? _cts;

    public bool IsBusy { get; private set; }
    public bool SafetyAuditLockEnabled { get; set; } = false;
    public WarframeManifest? CurrentManifest { get; private set; }
    public VerificationSummary? LastVerificationSummary { get; private set; }

    public event EventHandler<WarframeManifest?>? ManifestLoaded;
    public event EventHandler<VerificationSummary?>? VerificationCompleted;
    public event EventHandler<DownloadProgressEventArgs>? ProgressUpdated;

    public WarframeUpdateManager(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        _downloader = new WarframeDownloader(_logger);
        _verifier = new WarframeFileVerifier(_logger);
        _repairManager = new WarframeRepairManager(_settings, _logger);
        _launchManager = new WarframeLaunchManager(_settings, _logger);
        _backupManager = new WarframeBackupManager(_logger);

        _downloader.ProgressChanged += (s, e) => ProgressUpdated?.Invoke(this, e);
    }

    public async Task<WarframeManifest?> LoadManifestAsync()
    {
        if (IsBusy)
        {
            _logger.LogInfo("ℹ An update task is already in progress.");
            return CurrentManifest;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();

        try
        {
            _logger.LogInfo("Loading Warframe manifest...");
            CurrentManifest = await _downloader.DownloadAndParseManifestAsync(_settings.WarframeInstallFolder, _cts.Token);
            
            if (CurrentManifest != null && CurrentManifest.TotalFiles > 0)
            {
                _logger.LogSuccess($"[INFO] Manifest loaded ({CurrentManifest.TotalFiles} files, {CurrentManifest.TotalSizeFormatted}).");
            }
            else
            {
                _logger.LogError("✖ Manifest unavailable.");
            }

            ManifestLoaded?.Invoke(this, CurrentManifest);
            return CurrentManifest;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInfo("ℹ Manifest load cancelled.");
            return CurrentManifest;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<VerificationSummary?> InspectLocalInstallationAsync(bool verifyHashes = true, Action<int, int, string>? progressCallback = null)
    {
        if (CurrentManifest == null || CurrentManifest.TotalFiles == 0)
        {
            _logger.LogInfo("ℹ Loading manifest prior to local file inspection...");
            await LoadManifestAsync();
            if (CurrentManifest == null || CurrentManifest.TotalFiles == 0)
            {
                _logger.LogError("✖ Cannot verify installation without a valid manifest.");
                return null;
            }
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();

        try
        {
            _logger.LogInfo("Checking local files...");
            LastVerificationSummary = await _verifier.VerifyInstallationAsync(
                CurrentManifest,
                _settings.WarframeInstallFolder,
                _settings.WarframeLanguage,
                _settings.WarframeGraphicsApi,
                verifyHashes,
                progressCallback,
                _cts.Token);

            int downloadableUpdates = LastVerificationSummary.MissingFiles + LastVerificationSummary.OutdatedFiles + LastVerificationSummary.CorruptedFiles;
            if (downloadableUpdates > 0)
            {
                _logger.LogInfo($"[INFO] {downloadableUpdates} files require CDN download updates.");
            }
            else
            {
                _logger.LogSuccess("[SUCCESS] All downloadable CDN files are up to date.");
            }

            VerificationCompleted?.Invoke(this, LastVerificationSummary);
            return LastVerificationSummary;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInfo("ℹ File verification cancelled by user.");
            return LastVerificationSummary;
        }
        finally
        {
            IsBusy = false;
        }
    }

    public async Task<string> AnalyzeInstallationAsync()
    {
        _logger.LogInfo("[DIAGNOSTIC] Running read-only installation analysis...");
        if (CurrentManifest == null || CurrentManifest.TotalFiles == 0)
        {
            await LoadManifestAsync();
            if (CurrentManifest == null || CurrentManifest.TotalFiles == 0)
            {
                _logger.LogError("✖ Analysis failed: Manifest unavailable.");
                return string.Empty;
            }
        }

        string preflightReport = await _verifier.ExportPreFlightAuditReportAsync(
            CurrentManifest,
            _settings.WarframeInstallFolder,
            _settings.WarframeLanguage,
            _settings.WarframeGraphicsApi);

        string nativeAuditReport = await WarframeNativeAudit.GenerateNativeAuditReportAsync(_settings.WarframeInstallFolder, _logger);
        return preflightReport;
    }

    public async Task<bool> DownloadToolsAsync()
    {
        if (SafetyAuditLockEnabled)
        {
            _logger.LogWarning("[SAFETY LOCK] UPDATE DISABLED — SAFETY AUDIT REQUIRED");
            return false;
        }

        if (CurrentManifest == null)
        {
            await LoadManifestAsync();
            if (CurrentManifest == null) return false;
        }

        var toolsEntries = CurrentManifest.Entries.FindAll(e => e.RelativePath.StartsWith("/Tools", StringComparison.OrdinalIgnoreCase));
        if (toolsEntries.Count == 0)
        {
            _logger.LogInfo("ℹ No Tool files found in current manifest.");
            return true;
        }

        return await ProcessDownloadBatchAsync(toolsEntries, "Tools");
    }

    public async Task<bool> DownloadCacheAsync()
    {
        if (SafetyAuditLockEnabled)
        {
            _logger.LogWarning("[SAFETY LOCK] UPDATE DISABLED — SAFETY AUDIT REQUIRED");
            return false;
        }

        if (CurrentManifest == null)
        {
            await LoadManifestAsync();
            if (CurrentManifest == null) return false;
        }

        var cacheEntries = CurrentManifest.Entries.FindAll(e => e.RelativePath.StartsWith("/Cache.Windows", StringComparison.OrdinalIgnoreCase));
        if (cacheEntries.Count == 0)
        {
            _logger.LogInfo("ℹ No Cache.Windows files found in current manifest.");
            return true;
        }

        return await ProcessDownloadBatchAsync(cacheEntries, "Cache");
    }

    public async Task<bool> UpdateExeAsync()
    {
        if (SafetyAuditLockEnabled)
        {
            _logger.LogWarning("[SAFETY LOCK] UPDATE DISABLED — SAFETY AUDIT REQUIRED");
            return false;
        }

        if (CurrentManifest == null)
        {
            await LoadManifestAsync();
            if (CurrentManifest == null) return false;
        }

        var exeEntry = CurrentManifest.Entries.Find(e => e.RelativePath.EndsWith("Warframe.x64.exe", StringComparison.OrdinalIgnoreCase));
        if (exeEntry == null)
        {
            _logger.LogError("✖ Warframe.x64.exe not found in manifest.");
            return false;
        }

        _logger.LogInfo("Downloading Warframe.x64.exe...");
        bool success = await ProcessDownloadBatchAsync(new List<WarframeManifestEntry> { exeEntry }, "Warframe.x64.exe");
        if (success)
        {
            _logger.LogSuccess("[SUCCESS] Warframe.x64.exe verified.");
        }
        return success;
    }

    public async Task<bool> PerformContentUpdateAsync()
    {
        if (SafetyAuditLockEnabled)
        {
            _logger.LogWarning("[SAFETY LOCK] UPDATE DISABLED — SAFETY AUDIT REQUIRED");
            return false;
        }

        _logger.LogInfo("[INFO] Performing complete content update...");
        return await PerformRepairCacheAsync();
    }

    public async Task<bool> PerformRepairCacheAsync()
    {
        if (SafetyAuditLockEnabled)
        {
            _logger.LogWarning("[SAFETY LOCK] UPDATE DISABLED — SAFETY AUDIT REQUIRED");
            return false;
        }

        _logger.LogInfo("Repairing files...");
        await InspectLocalInstallationAsync(verifyHashes: true);
        if (LastVerificationSummary == null) return false;

        var downloadQueue = LastVerificationSummary.MissingList
            .Concat(LastVerificationSummary.OutdatedList)
            .Concat(LastVerificationSummary.CorruptedList)
            .Where(e => e.Status == FileStatus.Missing || e.Status == FileStatus.Outdated || e.Status == FileStatus.Corrupted)
            .ToList();

        _logger.LogInfo("================ UPDATE QUEUE AUDIT ================");
        _logger.LogInfo($"Missing Core:               {LastVerificationSummary.MissingFiles}");
        _logger.LogInfo($"Outdated (.bulk Cache):     {LastVerificationSummary.OutdatedFiles}");
        _logger.LogInfo($"Corrupted (Hash Mismatch):  {LastVerificationSummary.CorruptedFiles}");
        _logger.LogInfo($"Dynamic/Rebuildable (.toc): {LastVerificationSummary.DynamicRebuildableFiles} (Excluded from download queue)");
        _logger.LogInfo($"Optional Language:          {LastVerificationSummary.MissingOtherLangFiles} (Excluded from download queue)");
        _logger.LogInfo($"Optional Graphics:          {LastVerificationSummary.MissingOtherGraphicsFiles} (Excluded from download queue)");
        _logger.LogInfo($"Download Queue Total:       {downloadQueue.Count} files");
        _logger.LogInfo("--------------------------------------------------");

        if (downloadQueue.Any(x => x.Status == FileStatus.DynamicRebuildable))
        {
            _logger.LogError("✖ [ASSERTION FAILURE] Download queue contains DynamicRebuildable (.toc) files! Aborting update.");
            return false;
        }
        if (downloadQueue.Any(x => x.Status == FileStatus.MissingOptionalLanguage))
        {
            _logger.LogError("✖ [ASSERTION FAILURE] Download queue contains MissingOptionalLanguage files! Aborting update.");
            return false;
        }
        if (downloadQueue.Any(x => x.Status == FileStatus.MissingOptionalGraphics))
        {
            _logger.LogError("✖ [ASSERTION FAILURE] Download queue contains MissingOptionalGraphics files! Aborting update.");
            return false;
        }

        if (downloadQueue.Count == 0)
        {
            _logger.LogSuccess("✔ [SUCCESS] No downloadable CDN files require updates. Installation is up to date.");
            return true;
        }

        // Initialize Session-Level Rollback Backup
        _backupManager.StartUpdateSession(_settings.WarframeInstallFolder);
        foreach (var entry in downloadQueue)
        {
            string existingPath = WarframePathResolver.ResolveLocalPath(_settings.WarframeInstallFolder, entry.RelativePath);
            if (!string.IsNullOrEmpty(existingPath) && File.Exists(existingPath))
            {
                _backupManager.BackupFileBeforeReplacement(existingPath, entry.RelativePath);
            }
        }

        bool batchSuccess = await ProcessDownloadBatchAsync(downloadQueue, "Repair Cache");

        // Post-Update Full Verification & Rollback Protection
        if (batchSuccess)
        {
            _logger.LogInfo("[VERIFY POST-UPDATE] Running full post-update verification...");
            var postSummary = await InspectLocalInstallationAsync(verifyHashes: true);
            if (postSummary == null || postSummary.CorruptedFiles > 0 || postSummary.MissingFiles > 0)
            {
                _logger.LogError("✖ [UPDATE SESSION FAILED] Post-update verification failed. Initiating full session rollback...");
                _backupManager.RollbackSession();
                return false;
            }

            _logger.LogSuccess($"✔ [UPDATE SESSION SUCCESS] All {downloadQueue.Count} files updated and verified.");
            return true;
        }
        else
        {
            _logger.LogError("✖ [UPDATE SESSION FAILED] Download batch failed. Initiating full session rollback...");
            _backupManager.RollbackSession();
            return false;
        }
    }

    public void LaunchAppletContentUpdate()
    {
        string exePath = GetResolvedExePath();
        _repairManager.LaunchApplet("/EE/Types/Framework/ContentUpdate", "Preprocess.log", exePath);
    }

    public void LaunchAppletRepairCache()
    {
        string exePath = GetResolvedExePath();
        _repairManager.LaunchApplet("/EE/Types/Framework/CacheRepair", "Repair.log", exePath);
    }

    public DriveTypeInfo GetStorageDriveType()
    {
        return _repairManager.DetectDriveType(_settings.WarframeInstallFolder);
    }

    public void LaunchAppletDefrag()
    {
        if (SafetyAuditLockEnabled)
        {
            _logger.LogWarning("[SAFETY LOCK] UPDATE DISABLED — SAFETY AUDIT REQUIRED");
            return;
        }

        string exePath = GetResolvedExePath();
        var driveType = GetStorageDriveType();

        if (driveType == DriveTypeInfo.SSD)
        {
            _logger.LogInfo("[OPTIMIZE] Target storage device is SSD. Executing safe filesystem/game-file optimization...");
        }
        else
        {
            _logger.LogInfo("[OPTIMIZE] Target storage device is HDD. Executing game cache defragmentation applet...");
        }

        _repairManager.LaunchApplet("/EE/Types/Framework/CacheDefraggerAsync", "Defrag.log", exePath);
    }

    public bool LaunchGame()
    {
        _logger.LogInfo("[INFO] Preparing Warframe launch...");

        if (_launchManager.IsWarframeRunning())
        {
            _logger.LogError("✖ Warframe is currently running.");
            return false;
        }

        string exePath = GetResolvedExePath();
        if (string.IsNullOrEmpty(exePath) || !File.Exists(exePath))
        {
            _logger.LogError("✖ Warframe.x64.exe executable not found in configured install path.");
            return false;
        }

        bool result = _launchManager.LaunchWarframeExecutable(exePath);
        if (result)
        {
            _logger.LogSuccess("[SUCCESS] Warframe launched.");
        }
        return result;
    }

    public string GetResolvedExePath()
    {
        string resolved = WarframePathResolver.ResolveLocalPath(_settings.WarframeInstallFolder, "/Warframe.x64.exe");
        if (!string.IsNullOrEmpty(resolved) && File.Exists(resolved))
        {
            return resolved;
        }

        return Path.Combine(_settings.WarframeInstallFolder, "Warframe.x64.exe");
    }

    public void CancelActiveOperation()
    {
        _cts?.Cancel();
    }

    private async Task<bool> ProcessDownloadBatchAsync(List<WarframeManifestEntry> batch, string batchName)
    {
        if (IsBusy)
        {
            _logger.LogInfo("ℹ Operation in progress.");
            return false;
        }

        IsBusy = true;
        _cts = new CancellationTokenSource();

        try
        {
            _logger.LogInfo($"Starting batch '{batchName}' ({batch.Count} files)...");
            int successCount = 0;
            int total = batch.Count;

            foreach (var entry in batch)
            {
                _cts.Token.ThrowIfCancellationRequested();
                bool ok = await _downloader.DownloadEntryAsync(
                    entry,
                    _settings.WarframeInstallFolder,
                    _settings.WarframeLanguage,
                    _settings.WarframeGraphicsApi,
                    _cts.Token);

                if (ok) successCount++;
            }

            _logger.LogSuccess($"✔ Batch '{batchName}' completed. Downloaded: {successCount}/{total} files.");
            return successCount == total;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInfo($"ℹ Batch '{batchName}' cancelled.");
            return false;
        }
        finally
        {
            IsBusy = false;
        }
    }
}
