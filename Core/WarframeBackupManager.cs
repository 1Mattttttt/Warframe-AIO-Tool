using System;
using System.Collections.Generic;
using System.IO;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class WarframeBackupManager
{
    private readonly LoggerService _logger;
    private string? _sessionBackupDir;
    private readonly List<(string OriginalPath, string BackupPath)> _backedUpFiles = new();

    public string? ActiveSessionDirectory => _sessionBackupDir;
    public int BackedUpFileCount => _backedUpFiles.Count;

    public WarframeBackupManager(LoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public void StartUpdateSession(string gameFolder)
    {
        _backedUpFiles.Clear();
        string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");
        _sessionBackupDir = Path.Combine(gameFolder, ".GameLauncherBackup", $"UpdateSession_{timestamp}");
        Directory.CreateDirectory(_sessionBackupDir);
        _logger.LogInfo($"[BACKUP SESSION] Session backup initialized at '{_sessionBackupDir}'");
    }

    public bool BackupFileBeforeReplacement(string targetFilePath, string relativePath)
    {
        if (string.IsNullOrEmpty(_sessionBackupDir) || !File.Exists(targetFilePath))
        {
            return false;
        }

        try
        {
            string cleanRel = relativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
            string backupDestination = Path.Combine(_sessionBackupDir, cleanRel);
            string? backupDir = Path.GetDirectoryName(backupDestination);
            if (!string.IsNullOrEmpty(backupDir) && !Directory.Exists(backupDir))
            {
                Directory.CreateDirectory(backupDir);
            }

            File.Copy(targetFilePath, backupDestination, overwrite: true);
            _backedUpFiles.Add((targetFilePath, backupDestination));
            _logger.LogInfo($"[BACKUP] Staged backup for '{relativePath}' -> '{backupDestination}'");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"✖ [BACKUP] Failed to backup '{targetFilePath}': {ex.Message}");
            return false;
        }
    }

    public bool RollbackSession()
    {
        if (string.IsNullOrEmpty(_sessionBackupDir) || _backedUpFiles.Count == 0)
        {
            _logger.LogInfo("ℹ [ROLLBACK] No backup session to rollback.");
            return true;
        }

        _logger.LogWarning($"[ROLLBACK] Starting full session rollback of {_backedUpFiles.Count} files from '{_sessionBackupDir}'...");
        int restoredCount = 0;

        foreach (var (originalPath, backupPath) in _backedUpFiles)
        {
            try
            {
                if (File.Exists(backupPath))
                {
                    string? dir = Path.GetDirectoryName(originalPath);
                    if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    {
                        Directory.CreateDirectory(dir);
                    }

                    File.Copy(backupPath, originalPath, overwrite: true);
                    restoredCount++;
                    _logger.LogInfo($"[ROLLBACK] Restored '{originalPath}' from backup.");
                }
            }
            catch (Exception ex)
            {
                _logger.LogError($"✖ [ROLLBACK] Failed to restore '{originalPath}': {ex.Message}");
            }
        }

        _logger.LogSuccess($"✔ [ROLLBACK COMPLETE] Restored {restoredCount}/{_backedUpFiles.Count} files from session backup.");
        return restoredCount == _backedUpFiles.Count;
    }
}
