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
        var candidates = new List<string>();

        string baseDir = AppContext.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string roamingAppData = Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData);
        string programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string documents = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string downloads = Path.Combine(userProfile, "Downloads");
        string tempDir = Path.GetTempPath();

        // ── 1. Immediate vicinity of the running executable ──
        candidates.Add(Path.Combine(baseDir, ResourceFolderName));
        candidates.Add(Path.Combine(baseDir, "SpoofResources"));
        candidates.Add(Path.Combine(baseDir, "spoof_resources"));
        candidates.Add(Path.Combine(baseDir, "Spoofer"));

        // ── 2. Current working directory ──
        candidates.Add(Path.Combine(currentDir, ResourceFolderName));
        candidates.Add(Path.Combine(currentDir, "SpoofResources"));

        // ── 3. Project root (development mode) ──
        string? projectRoot = FindProjectRootDirectory(baseDir);
        if (!string.IsNullOrEmpty(projectRoot))
        {
            candidates.Add(Path.Combine(projectRoot, ResourceFolderName));
            candidates.Add(Path.Combine(projectRoot, "SpoofResources"));
        }

        // ── 4. Parent directory traversal (up to 5 levels) ──
        for (int i = 1; i <= 5; i++)
        {
            string relative = string.Join(Path.DirectorySeparatorChar.ToString(), Enumerable.Repeat("..", i));
            candidates.Add(Path.Combine(baseDir, relative, ResourceFolderName));
            candidates.Add(Path.Combine(baseDir, relative, "SpoofResources"));
        }

        // ── 5. User profile directories ──
        candidates.Add(Path.Combine(desktop, ResourceFolderName));
        candidates.Add(Path.Combine(desktop, "WarframeHelper", ResourceFolderName));
        candidates.Add(Path.Combine(desktop, "Warframe-AIO-Tool", ResourceFolderName));
        candidates.Add(Path.Combine(desktop, "Warframe-AIO-Tool-main", ResourceFolderName));

        candidates.Add(Path.Combine(documents, ResourceFolderName));
        candidates.Add(Path.Combine(documents, "WarframeHelper", ResourceFolderName));
        candidates.Add(Path.Combine(documents, "Warframe-AIO-Tool", ResourceFolderName));

        candidates.Add(Path.Combine(downloads, ResourceFolderName));
        candidates.Add(Path.Combine(downloads, "WarframeHelper", ResourceFolderName));
        candidates.Add(Path.Combine(downloads, "Warframe-AIO-Tool", ResourceFolderName));
        candidates.Add(Path.Combine(downloads, "Warframe-AIO-Tool-main", ResourceFolderName));

        // ── 6. AppData locations ──
        candidates.Add(Path.Combine(localAppData, "GameLauncher", ResourceFolderName));
        candidates.Add(Path.Combine(localAppData, "WarframeHelper", ResourceFolderName));
        candidates.Add(Path.Combine(roamingAppData, "GameLauncher", ResourceFolderName));
        candidates.Add(Path.Combine(roamingAppData, "WarframeHelper", ResourceFolderName));

        // ── 7. ProgramData ──
        candidates.Add(Path.Combine(programData, "GameLauncher", ResourceFolderName));
        candidates.Add(Path.Combine(programData, "WarframeHelper", ResourceFolderName));

        // ── 8. Temp directory ──
        candidates.Add(Path.Combine(tempDir, ResourceFolderName));
        candidates.Add(Path.Combine(tempDir, "GameLauncher", ResourceFolderName));
        candidates.Add(Path.Combine(tempDir, "WarframeHelper", ResourceFolderName));

        // ── 9. All fixed drives ──
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                string root = drive.RootDirectory.FullName;

                candidates.Add(Path.Combine(root, "GameLauncher", ResourceFolderName));
                candidates.Add(Path.Combine(root, "WarframeHelper", ResourceFolderName));
                candidates.Add(Path.Combine(root, "Tools", "GameLauncher", ResourceFolderName));
                candidates.Add(Path.Combine(root, "Tools", "WarframeHelper", ResourceFolderName));
            }
        }
        catch { }

        // ── 10. Hardcoded known dev/test paths ──
        candidates.Add(@"C:\Users\Administrador\Desktop\WarframeHelper\Spoof resources");
        candidates.Add(@"C:\Users\Administrador\Desktop\Warframe-AIO-Tool-main\Spoof resources");

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
