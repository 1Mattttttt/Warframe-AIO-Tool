using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class SpooferManager
{
    private const string ResourceFolderName = "Spoof resources";
    private const string MainSpooferSubPath = @"Spoofer\PSWOA - run as admin to spoof.exe";
    private const string NetFixerSubPath = "NetFixer.bat";
    private const string NetworkSettingsSubPath = "Network Settings.bat";

    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    public SpooferManager(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string? GetSpoofResourcesDirectory()
    {
        var candidates = new List<string>
        {
            Path.Combine(AppContext.BaseDirectory, ResourceFolderName),
            Path.Combine(Directory.GetCurrentDirectory(), ResourceFolderName)
        };

        // Development mode: Project root search
        string? projectRoot = FindProjectRootDirectory(AppContext.BaseDirectory);
        if (!string.IsNullOrEmpty(projectRoot))
        {
            candidates.Add(Path.Combine(projectRoot, ResourceFolderName));
        }

        // Hardcoded absolute fallback for local environment
        candidates.Add(@"C:\Users\Administrador\Desktop\WarframeHelper\Spoof resources");

        var tested = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var candidate in candidates)
        {
            try
            {
                string fullPath = Path.GetFullPath(candidate);
                if (!tested.Add(fullPath)) continue;

                if (Directory.Exists(fullPath))
                {
                    return fullPath;
                }
            }
            catch
            {
                // Ignore path format exceptions gracefully
            }
        }

        return null;
    }

    public string? FindResourcePath(string subPath)
    {
        string? root = GetSpoofResourcesDirectory();
        if (string.IsNullOrEmpty(root)) return null;

        string target = Path.Combine(root, subPath);
        return File.Exists(target) ? target : null;
    }

    public Task<bool> RunMainSpooferAsync(Action<string>? statusCallback = null)
    {
        string? path = FindResourcePath(MainSpooferSubPath);
        if (string.IsNullOrEmpty(path))
        {
            string msg = "Spoofer executable (PSWOA - run as admin to spoof.exe) not found.";
            _logger.LogError(msg);
            statusCallback?.Invoke($"❌ {msg}");
            return Task.FromResult(false);
        }

        return ExecuteToolAsync("Run Spoofer", path, requireAdmin: true, statusCallback);
    }

    public Task<bool> RunNetFixerAsync(Action<string>? statusCallback = null)
    {
        string? path = FindResourcePath(NetFixerSubPath);
        if (string.IsNullOrEmpty(path))
        {
            string msg = "NetFixer.bat script not found.";
            _logger.LogError(msg);
            statusCallback?.Invoke($"❌ {msg}");
            return Task.FromResult(false);
        }

        return ExecuteToolAsync("NetFixer", path, requireAdmin: true, statusCallback);
    }

    public Task<bool> RunNetworkSettingsAsync(Action<string>? statusCallback = null)
    {
        string? path = FindResourcePath(NetworkSettingsSubPath);
        if (string.IsNullOrEmpty(path))
        {
            string msg = "Network Settings.bat script not found.";
            _logger.LogError(msg);
            statusCallback?.Invoke($"❌ {msg}");
            return Task.FromResult(false);
        }

        return ExecuteToolAsync("Network Settings", path, requireAdmin: true, statusCallback);
    }

    private Task<bool> ExecuteToolAsync(string toolName, string filePath, bool requireAdmin, Action<string>? statusCallback)
    {
        return Task.Run(() =>
        {
            try
            {
                string workDir = Path.GetDirectoryName(filePath) ?? AppContext.BaseDirectory;
                _logger.LogInfo($"Executing {toolName} from: {filePath}");
                statusCallback?.Invoke($"⏳ Starting {toolName}...");

                var startInfo = new ProcessStartInfo
                {
                    FileName = filePath,
                    WorkingDirectory = workDir,
                    UseShellExecute = true
                };

                if (requireAdmin)
                {
                    startInfo.Verb = "runas";
                }

                var sw = Stopwatch.StartNew();
                using var process = Process.Start(startInfo);

                if (process == null)
                {
                    string failMsg = $"Failed to launch {toolName}.";
                    _logger.LogError(failMsg);
                    statusCallback?.Invoke($"❌ {failMsg}");
                    return false;
                }

                _logger.LogSuccess($"{toolName} process started (PID: {process.Id}). Standing by for completion...");
                statusCallback?.Invoke($"⚡ {toolName} running (PID: {process.Id})...");

                process.WaitForExit();
                sw.Stop();

                int exitCode = process.ExitCode;
                if (exitCode == 0)
                {
                    string successMsg = $"{toolName} completed successfully in {sw.ElapsedMilliseconds} ms (Exit Code: 0).";
                    _logger.LogSuccess(successMsg);
                    statusCallback?.Invoke($"✔ {toolName} completed successfully.");
                    return true;
                }
                else
                {
                    string warnMsg = $"{toolName} finished with exit code {exitCode} after {sw.ElapsedMilliseconds} ms.";
                    _logger.LogWarning(warnMsg);
                    statusCallback?.Invoke($"⚠ {toolName} finished with exit code {exitCode}.");
                    return false;
                }
            }
            catch (System.ComponentModel.Win32Exception winEx) when (winEx.NativeErrorCode == 1223)
            {
                // User cancelled UAC prompt
                string cancelMsg = $"{toolName} execution cancelled by user (UAC prompt declined).";
                _logger.LogWarning(cancelMsg);
                statusCallback?.Invoke($"⚠ {toolName} cancelled by user.");
                return false;
            }
            catch (Exception ex)
            {
                string errMsg = $"Error executing {toolName}: {ex.Message}";
                _logger.LogError(errMsg);
                statusCallback?.Invoke($"❌ Error executing {toolName}.");
                return false;
            }
        });
    }

    private static string? FindProjectRootDirectory(string startingDirectory)
    {
        try
        {
            var dir = new DirectoryInfo(startingDirectory);
            int maxDepth = 6;
            int depth = 0;

            while (dir != null && depth < maxDepth)
            {
                if (File.Exists(Path.Combine(dir.FullName, "GameLauncher.csproj")) ||
                    File.Exists(Path.Combine(dir.FullName, "WarframeHelper.sln")))
                {
                    return dir.FullName;
                }

                dir = dir.Parent;
                depth++;
            }
        }
        catch
        {
            // Suppress directory traversal exceptions gracefully
        }

        return null;
    }
}
