using System;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class DownloadProgressEventArgs : EventArgs
{
    public string Message { get; set; } = string.Empty;
    public long BytesReceived { get; set; }
    public long TotalBytes { get; set; }
    public int Percent { get; set; }
    public double SpeedBytesPerSec { get; set; }
    public TimeSpan RemainingTime { get; set; }
}

public class WarframeDownloader
{
    private static readonly HttpClient HttpClient = new HttpClient
    {
        Timeout = TimeSpan.FromSeconds(30)
    };

    private readonly LoggerService _logger;
    private readonly WarframeFileVerifier _verifier;

    public event EventHandler<DownloadProgressEventArgs>? ProgressChanged;

    public WarframeDownloader(LoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _verifier = new WarframeFileVerifier(_logger);
    }

    public static string? FindSevenZip()
    {
        string[] candidatePaths =
        {
            @"C:\Program Files\7-Zip\7z.exe",
            @"C:\Program Files (x86)\7-Zip\7z.exe",
            Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "7z.exe"),
            "7z.exe"
        };

        foreach (var path in candidatePaths)
        {
            if (File.Exists(path)) return path;
        }

        return null;
    }

    public async Task<WarframeManifest> DownloadAndParseManifestAsync(string? targetGameFolder = null, CancellationToken cancellationToken = default)
    {
        _logger.LogInfo("[MANIFEST] Requesting Warframe update manifest metadata...");

        DateTime? manifestLastModified = await GetUrlLastModifiedAsync(WarframeManifest.DefaultIndexUrl, cancellationToken);
        
        DateTime? exeLastModified = null;
        if (!string.IsNullOrWhiteSpace(targetGameFolder))
        {
            string exePath = WarframePathResolver.ResolveLocalPath(targetGameFolder, "/Warframe.x64.exe");
            if (File.Exists(exePath))
            {
                exeLastModified = File.GetLastWriteTimeUtc(exePath);
            }
        }

        string tempLzmaPath = Path.Combine(Path.GetTempPath(), $"wf_index_{Guid.NewGuid():N}.txt.lzma");
        string tempTxtPath = Path.Combine(Path.GetTempPath(), $"wf_index_{Guid.NewGuid():N}.txt");

        try
        {
            _logger.LogInfo($"[MANIFEST] Downloading manifest from {WarframeManifest.DefaultIndexUrl}...");
            await DownloadFileInternalAsync(WarframeManifest.DefaultIndexUrl, tempLzmaPath, "Downloading manifest", cancellationToken);

            _logger.LogInfo("[MANIFEST] Extracting index manifest archive...");
            bool extracted = DecompressLzmaFile(tempLzmaPath, tempTxtPath);

            if (!extracted || !File.Exists(tempTxtPath))
            {
                _logger.LogError("✖ [MANIFEST] Failed to extract index.txt from manifest archive.");
                return new WarframeManifest { ManifestLastModified = manifestLastModified, ExeLastModified = exeLastModified };
            }

            string content = await File.ReadAllTextAsync(tempTxtPath, cancellationToken);
            var manifest = WarframeManifest.Parse(content, manifestLastModified, exeLastModified);

            _logger.LogSuccess($"✔ [MANIFEST] Loaded {manifest.TotalFiles} entries ({manifest.TotalSizeFormatted}) from manifest.");
            if (manifestLastModified.HasValue)
            {
                _logger.LogInfo($"[MANIFEST] Index UTC: {manifestLastModified.Value:yyyy-MM-dd HH:mm:ss} UTC");
            }
            if (exeLastModified.HasValue)
            {
                _logger.LogInfo($"[MANIFEST] Installed EXE UTC: {exeLastModified.Value:yyyy-MM-dd HH:mm:ss} UTC");
            }

            return manifest;
        }
        catch (OperationCanceledException)
        {
            _logger.LogInfo("ℹ [MANIFEST] Manifest download cancelled by user.");
            throw;
        }
        catch (Exception ex)
        {
            _logger.LogError($"✖ [MANIFEST] Manifest update failed: {ex.Message}");
            return new WarframeManifest { ManifestLastModified = manifestLastModified, ExeLastModified = exeLastModified };
        }
        finally
        {
            TryDeleteFile(tempLzmaPath);
            TryDeleteFile(tempTxtPath);
        }
    }

    public async Task<bool> DownloadEntryAsync(
        WarframeManifestEntry entry,
        string gameFolder,
        string activeLanguage = "en",
        string activeGraphicsApi = "dx11",
        CancellationToken cancellationToken = default)
    {
        if (entry == null || string.IsNullOrWhiteSpace(gameFolder)) return false;

        string targetInstallPath = WarframePathResolver.ResolveTargetInstallationPath(gameFolder, entry.RelativePath);
        string tempPath = targetInstallPath + ".tmp";
        string lzmaTempPath = targetInstallPath + ".lzma.tmp";
        string backupPath = targetInstallPath + ".bak";

        string targetDir = Path.GetDirectoryName(targetInstallPath)!;
        if (!Directory.Exists(targetDir))
        {
            Directory.CreateDirectory(targetDir);
        }

        int maxRetries = 3;
        for (int attempt = 1; attempt <= maxRetries; attempt++)
        {
            try
            {
                cancellationToken.ThrowIfCancellationRequested();
                entry.Status = FileStatus.Updating;

                _logger.LogInfo($"[DOWNLOAD] [{attempt}/{maxRetries}] Downloading {entry.RelativePath} ({entry.DisplaySize}) [CDN URL: {entry.DownloadUrl}]...");
                
                string downloadUrl = entry.DownloadUrl;
                string downloadDest = entry.IsLzma ? lzmaTempPath : tempPath;

                await DownloadFileInternalAsync(downloadUrl, downloadDest, $"Downloading {Path.GetFileName(targetInstallPath)}", cancellationToken);

                if (entry.IsLzma)
                {
                    long downloadedLzmaBytes = new FileInfo(lzmaTempPath).Length;
                    _logger.LogInfo($"[DECOMPRESS] Downloaded {downloadedLzmaBytes} B compressed LZMA payload for {entry.RelativePath}. Extracting...");

                    bool success = DecompressLzmaFile(lzmaTempPath, tempPath);
                    TryDeleteFile(lzmaTempPath);

                    if (!success || !File.Exists(tempPath))
                    {
                        _logger.LogError($"✖ [DECOMPRESS] Failed to decompress LZMA payload for {entry.RelativePath}");
                        TryDeleteFile(tempPath);
                        continue;
                    }
                }

                if (!File.Exists(tempPath))
                {
                    _logger.LogError($"✖ [DOWNLOAD] Temporary download file for {entry.RelativePath} not found.");
                    continue;
                }

                long uncompressedSize = new FileInfo(tempPath).Length;
                _logger.LogInfo($"[DOWNLOAD STAGE] Downloaded payload uncompressed size: {uncompressedSize} B (Expected CDN Payload Size: {entry.Size} B, Format: {(entry.IsLzma ? "lzma" : "bulk")})");

                // Verification of downloaded payload:
                // For .bulk files, check uncompressed size equality
                if (entry.IsBulk && entry.Size > 0 && uncompressedSize != entry.Size)
                {
                    _logger.LogError($"✖ [DOWNLOAD VERIFY] .bulk Size mismatch for {entry.RelativePath}: local={uncompressedSize}, expected={entry.Size}");
                    TryDeleteFile(tempPath);
                    continue;
                }

                // Authoritative MD5 Hash Verification on extracted temp file before replacement
                if (!string.IsNullOrWhiteSpace(entry.ContentHash))
                {
                    string computedHash = ComputeMd5(tempPath);
                    if (!string.Equals(computedHash, entry.ContentHash, StringComparison.OrdinalIgnoreCase))
                    {
                        _logger.LogError($"✖ [DOWNLOAD VERIFY] Hash mismatch for {entry.RelativePath}: local={computedHash}, expected={entry.ContentHash}");
                        TryDeleteFile(tempPath);
                        continue;
                    }
                }

                // Atomic Replacement with Backup Safety
                TryDeleteFile(backupPath);
                if (File.Exists(targetInstallPath))
                {
                    File.Move(targetInstallPath, backupPath);
                }

                File.Move(tempPath, targetInstallPath);

                // Authoritative Post-Download Verification reusing WarframeFileVerifier
                FileStatus finalStatus = await _verifier.VerifySingleFileAsync(entry, gameFolder, activeLanguage, activeGraphicsApi, verifyHashes: true);
                if (finalStatus == FileStatus.OK)
                {
                    TryDeleteFile(backupPath);
                    entry.Status = FileStatus.OK;
                    _logger.LogSuccess($"✔ [DOWNLOAD] Successfully updated {entry.RelativePath} (Final Size: {new FileInfo(targetInstallPath).Length} B, MD5: {entry.ContentHash})");
                    return true;
                }
                else
                {
                    _logger.LogError($"✖ [DOWNLOAD] Final verification failed for {entry.RelativePath} after replacement (Status: {finalStatus}). Restoring previous file...");
                    TryDeleteFile(targetInstallPath);
                    if (File.Exists(backupPath))
                    {
                        File.Move(backupPath, targetInstallPath);
                    }
                    entry.Status = finalStatus;
                    continue;
                }
            }
            catch (OperationCanceledException)
            {
                TryDeleteFile(tempPath);
                TryDeleteFile(lzmaTempPath);
                TryDeleteFile(backupPath);
                throw;
            }
            catch (Exception ex)
            {
                _logger.LogError($"✖ [DOWNLOAD] Download attempt {attempt} failed for {entry.RelativePath}: {ex.Message}");
                TryDeleteFile(tempPath);
                TryDeleteFile(lzmaTempPath);
                if (File.Exists(backupPath) && !File.Exists(targetInstallPath))
                {
                    try { File.Move(backupPath, targetInstallPath); } catch { }
                }
                if (attempt < maxRetries)
                {
                    await Task.Delay(1000 * attempt, cancellationToken);
                }
            }
        }

        entry.Status = FileStatus.Corrupted;
        return false;
    }

    private async Task DownloadFileInternalAsync(string url, string destPath, string progressMessage, CancellationToken cancellationToken)
    {
        using var response = await HttpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead, cancellationToken);
        response.EnsureSuccessStatusCode();

        long totalBytes = response.Content.Headers.ContentLength ?? -1;
        using var contentStream = await response.Content.ReadAsStreamAsync(cancellationToken);
        using var fileStream = new FileStream(destPath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true);

        byte[] buffer = new byte[16384];
        long totalRead = 0;
        int bytesRead;

        var stopwatch = Stopwatch.StartNew();
        long lastReportedBytes = 0;
        var lastReportTime = stopwatch.ElapsedMilliseconds;

        while ((bytesRead = await contentStream.ReadAsync(buffer, 0, buffer.Length, cancellationToken)) > 0)
        {
            await fileStream.WriteAsync(buffer, 0, bytesRead, cancellationToken);
            totalRead += bytesRead;

            long currentTime = stopwatch.ElapsedMilliseconds;
            if (currentTime - lastReportTime >= 250 || (totalBytes > 0 && totalRead == totalBytes))
            {
                double seconds = (currentTime - lastReportTime) / 1000.0;
                double speed = seconds > 0 ? (totalRead - lastReportedBytes) / seconds : 0;
                int percent = totalBytes > 0 ? (int)((totalRead * 100) / totalBytes) : 0;
                TimeSpan remaining = (speed > 0 && totalBytes > 0) ? TimeSpan.FromSeconds((totalBytes - totalRead) / speed) : TimeSpan.Zero;

                OnProgress(progressMessage, totalRead, totalBytes, percent, speed, remaining);

                lastReportedBytes = totalRead;
                lastReportTime = currentTime;
            }
        }

        OnProgress(progressMessage, totalRead, totalBytes, 100, 0, TimeSpan.Zero);
    }

    public static bool DecompressLzmaFile(string lzmaPath, string outPath)
    {
        string? sevenZip = FindSevenZip();
        if (!string.IsNullOrEmpty(sevenZip))
        {
            try
            {
                string outDir = Path.GetDirectoryName(outPath)!;
                string tempExtractedFile = Path.Combine(outDir, Path.GetFileNameWithoutExtension(lzmaPath));

                var psi = new ProcessStartInfo
                {
                    FileName = sevenZip,
                    Arguments = $"e -y -o\"{outDir}\" \"{lzmaPath}\"",
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var proc = Process.Start(psi);
                if (proc != null)
                {
                    proc.WaitForExit();
                    if (proc.ExitCode == 0)
                    {
                        if (File.Exists(outPath)) return true;
                        
                        if (File.Exists(tempExtractedFile))
                        {
                            if (File.Exists(outPath)) File.Delete(outPath);
                            File.Move(tempExtractedFile, outPath);
                            return true;
                        }

                        var dirFiles = Directory.GetFiles(outDir, Path.GetFileNameWithoutExtension(lzmaPath) + "*");
                        if (dirFiles.Length > 0 && File.Exists(dirFiles[0]))
                        {
                            if (File.Exists(outPath)) File.Delete(outPath);
                            File.Move(dirFiles[0], outPath);
                            return true;
                        }
                    }
                }
            }
            catch
            {
                // Fall back if process execution fails
            }
        }

        return FalseLzmaFallback(lzmaPath, outPath);
    }

    private static bool FalseLzmaFallback(string lzmaPath, string outPath)
    {
        try
        {
            byte[] bytes = File.ReadAllBytes(lzmaPath);
            File.WriteAllBytes(outPath, bytes);
            return true;
        }
        catch
        {
            return false;
        }
    }

    public static string ComputeMd5(string filePath)
    {
        if (!File.Exists(filePath)) return string.Empty;
        using var md5 = MD5.Create();
        using var stream = File.OpenRead(filePath);
        byte[] hash = md5.ComputeHash(stream);
        var sb = new StringBuilder(hash.Length * 2);
        foreach (byte b in hash)
        {
            sb.Append(b.ToString("X2"));
        }
        return sb.ToString();
    }

    private async Task<DateTime?> GetUrlLastModifiedAsync(string url, CancellationToken cancellationToken)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Head, url);
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            if (response.IsSuccessStatusCode && response.Content.Headers.LastModified.HasValue)
            {
                return response.Content.Headers.LastModified.Value.UtcDateTime;
            }
        }
        catch
        {
            // Suppress HEAD request failure
        }
        return null;
    }

    private void OnProgress(string message, long bytesRecv, long totalBytes, int percent, double speed, TimeSpan remaining)
    {
        ProgressChanged?.Invoke(this, new DownloadProgressEventArgs
        {
            Message = message,
            BytesReceived = bytesRecv,
            TotalBytes = totalBytes,
            Percent = percent,
            SpeedBytesPerSec = speed,
            RemainingTime = remaining
        });
    }

    private static void TryDeleteFile(string path)
    {
        try
        {
            if (File.Exists(path)) File.Delete(path);
        }
        catch
        {
            // Suppress deletion errors
        }
    }
}
