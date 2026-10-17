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
    private const string NetFixerSubPath = "NetFixer.bat";
    private const string NetworkSettingsSubPath = "Network Settings.bat";

    private static readonly string EmbeddedNetFixerBat = @"@echo off
SETLOCAL ENABLEDELAYEDEXPANSION
SETLOCAL ENABLEEXTENSIONS

:: Generate and implement a MAC address starting with '02-'
FOR /F ""tokens=1"" %%a IN ('wmic nic where physicaladapter^=true get deviceid ^| findstr [0-9]') DO (
    SET ""MAC=02-""
    CALL :MAC
    FOR %%b IN (0 00 000) DO (
        REG QUERY ""HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002bE10318}\%%b%%a"" >NUL 2>NUL && REG ADD ""HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002bE10318}\%%b%%a"" /v NetworkAddress /t REG_SZ /d !MAC! /f >NUL 2>NUL
    )
)

:: Disable power saving mode for network adapters
FOR /F ""tokens=1"" %%a IN ('wmic nic where physicaladapter^=true get deviceid ^| findstr [0-9]') DO (
    FOR %%b IN (0 00 000) DO (
        REG QUERY ""HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002bE10318}\%%b%%a"" >NUL 2>NUL && REG ADD ""HKEY_LOCAL_MACHINE\SYSTEM\CurrentControlSet\Control\Class\{4D36E972-E325-11CE-BFC1-08002bE10318}\%%b%%a"" /v PnPCapabilities /t REG_DWORD /d 24 /f >NUL 2>NUL
    )
)

:: Reset NIC adapters so the new MAC address is implemented and the power saving mode is disabled.
FOR /F ""tokens=2 delims=, skip=2"" %%a IN ('""wmic nic where (netconnectionid like '%%') get netconnectionid,netconnectionstatus /format:csv""') DO (
    netsh interface set interface name=""%%a"" disable >NUL 2>NUL
    netsh interface set interface name=""%%a"" enable >NUL 2>NUL
)

GOTO :EOF

:MAC
SET COUNT=0
SET GEN=ABCDEF0123456789
SET GEN2=29ACFED4345686876
:MACLOOP
SET /a COUNT+=1
SET RND=%random%
SET /A RND=RND%%16
SET RNDGEN=!GEN:~%RND%,1!
SET /A RND2=RND%%4
SET RNDGEN2=!GEN2:~%RND2%,1!
SET MAC=!MAC!!RNDGEN2!
IF !COUNT! LEQ 10 GOTO MACLOOP
";

    private static readonly string EmbeddedNetworkSettingsBat = @"@echo off
setlocal EnableDelayedExpansion

net session >nul 2>&1
if %errorlevel% neq 0 (
    echo [!] Administrator privileges required.
    exit /b 1
)

ipconfig /flushdns >nul 2>&1
arp -d * >nul 2>&1
netsh interface ip delete arpcache >nul 2>&1
ipconfig /release >nul 2>&1
ipconfig /renew >nul 2>&1
ipconfig /registerdns >nul 2>&1
netsh winsock reset >nul 2>&1
netsh int ip reset >nul 2>&1

exit /b 0
";

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

        candidates.Add(Path.Combine(baseDir, ResourceFolderName));
        candidates.Add(Path.Combine(baseDir, "SpoofResources"));
        candidates.Add(Path.Combine(currentDir, ResourceFolderName));

        string? projectRoot = FindProjectRootDirectory(baseDir);
        if (!string.IsNullOrEmpty(projectRoot))
        {
            candidates.Add(Path.Combine(projectRoot, ResourceFolderName));
        }

        for (int i = 1; i <= 5; i++)
        {
            string relative = string.Join(Path.DirectorySeparatorChar.ToString(), Enumerable.Repeat("..", i));
            candidates.Add(Path.Combine(baseDir, relative, ResourceFolderName));
        }

        candidates.Add(Path.Combine(desktop, ResourceFolderName));
        candidates.Add(Path.Combine(desktop, "Warframe-AIO-Tool-main", ResourceFolderName));
        candidates.Add(Path.Combine(downloads, ResourceFolderName));
        candidates.Add(Path.Combine(localAppData, "GameLauncher", ResourceFolderName));
        candidates.Add(Path.Combine(roamingAppData, "GameLauncher", ResourceFolderName));
        candidates.Add(Path.Combine(tempDir, ResourceFolderName));

        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                string root = drive.RootDirectory.FullName;
                candidates.Add(Path.Combine(root, "GameLauncher", ResourceFolderName));
            }
        }
        catch { }

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
            catch { }
        }

        return null;
    }

    public string FindOrExtractScript(string scriptFileName, string embeddedScriptContent)
    {
        // 1. Try local disk candidate if present
        string? root = GetSpoofResourcesDirectory();
        if (!string.IsNullOrEmpty(root))
        {
            string target = Path.Combine(root, scriptFileName);
            if (File.Exists(target))
            {
                return target;
            }
        }

        // 2. Fallback: extract embedded script directly to %LOCALAPPDATA%\GameLauncher\scripts\
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string scriptsDir = Path.Combine(localAppData, "GameLauncher", "scripts");
        Directory.CreateDirectory(scriptsDir);

        string embeddedPath = Path.Combine(scriptsDir, scriptFileName);
        File.WriteAllText(embeddedPath, embeddedScriptContent);
        return embeddedPath;
    }

    public Task<bool> RunMacSpooferAsync(Action<string>? statusCallback = null)
    {
        string path = FindOrExtractScript("spoofer_mac.bat", EmbeddedNetFixerBat);
        return ExecuteToolAsync("MAC Spoofer", path, requireAdmin: true, statusCallback);
    }

    public Task<bool> RunNetFixerAsync(Action<string>? statusCallback = null)
    {
        string path = FindOrExtractScript(NetFixerSubPath, EmbeddedNetFixerBat);
        return ExecuteToolAsync("NetFixer", path, requireAdmin: true, statusCallback);
    }

    public Task<bool> RunNetworkSettingsAsync(Action<string>? statusCallback = null)
    {
        string path = FindOrExtractScript(NetworkSettingsSubPath, EmbeddedNetworkSettingsBat);
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
