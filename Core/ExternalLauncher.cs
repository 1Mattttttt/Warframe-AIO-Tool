using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading.Tasks;
using System.Windows;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class ExternalLauncher
{
    [DllImport("user32.dll", SetLastError = true)]
    private static extern bool ShowWindow(IntPtr hWnd, int nCmdShow);
    private const int SW_HIDE = 0;

    private const string OwHelperExeName = "OwHelperLoader.exe";
    private const string OwHelperProcessName = "OwHelperLoader";

    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    public ExternalLauncher(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public bool IsOwHelperActive(out string activeDetails)
    {
        activeDetails = string.Empty;
        try
        {
            // 1. Check if OwHelperLoader.exe is still running
            var bootstrapProcesses = Process.GetProcessesByName(OwHelperProcessName);
            if (bootstrapProcesses.Length > 0)
            {
                foreach (var p in bootstrapProcesses) p.Dispose();
                activeDetails = "bootstrap";
                return true;
            }

            // 2. Filter process candidates starting with 'wf' before querying MainModule
            var processes = Process.GetProcesses();
            foreach (var process in processes)
            {
                try
                {
                    if (process.ProcessName.StartsWith("wf", StringComparison.OrdinalIgnoreCase))
                    {
                        string? mainModulePath = process.MainModule?.FileName;
                        if (!string.IsNullOrEmpty(mainModulePath))
                        {
                            string normalizedPath = Path.GetFullPath(mainModulePath);
                            string fileName = Path.GetFileName(normalizedPath);

                            if (normalizedPath.Contains(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase) &&
                                fileName.StartsWith("wf_", StringComparison.OrdinalIgnoreCase))
                            {
                                activeDetails = normalizedPath;
                                return true;
                            }
                        }
                    }
                }
                catch
                {
                    // Gracefully ignore AccessDenied / 32-64bit access restrictions
                }
                finally
                {
                    process.Dispose();
                }
            }
        }
        catch
        {
            // Gracefully ignore process enumeration failures
        }

        return false;
    }

    public bool IsOwHelperActive() => IsOwHelperActive(out _);
    public bool IsOwHelperRunning() => IsOwHelperActive(out _);

    public string? FindOwHelperExecutable()
    {
        _logger.LogInfo($"Searching for {OwHelperExeName}...");

        var candidatePaths = new List<string>();

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
        candidatePaths.Add(Path.Combine(baseDir, OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "Cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "tools", OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "Tools", OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "bin", OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "helper", OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "Helper", OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "external", OwHelperExeName));
        candidatePaths.Add(Path.Combine(baseDir, "External", OwHelperExeName));

        // ── 2. Current working directory ──
        candidatePaths.Add(Path.Combine(currentDir, OwHelperExeName));
        candidatePaths.Add(Path.Combine(currentDir, "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(currentDir, "Cheat", OwHelperExeName));

        // ── 3. Project root (development mode) ──
        string? projectRoot = FindProjectRootDirectory(baseDir);
        if (!string.IsNullOrEmpty(projectRoot))
        {
            candidatePaths.Add(Path.Combine(projectRoot, OwHelperExeName));
            candidatePaths.Add(Path.Combine(projectRoot, "cheat", OwHelperExeName));
            candidatePaths.Add(Path.Combine(projectRoot, "Cheat", OwHelperExeName));
            candidatePaths.Add(Path.Combine(projectRoot, "bin", "Release", OwHelperExeName));
            candidatePaths.Add(Path.Combine(projectRoot, "bin", "Debug", OwHelperExeName));
        }

        // ── 4. Parent directory traversal (up to 5 levels) ──
        for (int i = 1; i <= 5; i++)
        {
            string relative = string.Join(Path.DirectorySeparatorChar.ToString(), Enumerable.Repeat("..", i));
            candidatePaths.Add(Path.Combine(baseDir, relative, OwHelperExeName));
            candidatePaths.Add(Path.Combine(baseDir, relative, "cheat", OwHelperExeName));
            candidatePaths.Add(Path.Combine(baseDir, relative, "Cheat", OwHelperExeName));
        }

        // ── 5. User profile directories ──
        candidatePaths.Add(Path.Combine(desktop, OwHelperExeName));
        candidatePaths.Add(Path.Combine(desktop, "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(desktop, "Cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(desktop, "WarframeHelper", OwHelperExeName));
        candidatePaths.Add(Path.Combine(desktop, "WarframeHelper", "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(desktop, "Warframe-AIO-Tool", OwHelperExeName));
        candidatePaths.Add(Path.Combine(desktop, "Warframe-AIO-Tool", "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(desktop, "Warframe-AIO-Tool-main", OwHelperExeName));
        candidatePaths.Add(Path.Combine(desktop, "Warframe-AIO-Tool-main", "cheat", OwHelperExeName));

        candidatePaths.Add(Path.Combine(documents, OwHelperExeName));
        candidatePaths.Add(Path.Combine(documents, "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(documents, "WarframeHelper", OwHelperExeName));
        candidatePaths.Add(Path.Combine(documents, "Warframe-AIO-Tool", OwHelperExeName));

        candidatePaths.Add(Path.Combine(downloads, OwHelperExeName));
        candidatePaths.Add(Path.Combine(downloads, "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(downloads, "WarframeHelper", OwHelperExeName));
        candidatePaths.Add(Path.Combine(downloads, "Warframe-AIO-Tool", OwHelperExeName));
        candidatePaths.Add(Path.Combine(downloads, "Warframe-AIO-Tool-main", OwHelperExeName));

        // ── 6. AppData locations ──
        candidatePaths.Add(Path.Combine(localAppData, "GameLauncher", OwHelperExeName));
        candidatePaths.Add(Path.Combine(localAppData, "GameLauncher", "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(localAppData, "WarframeHelper", OwHelperExeName));
        candidatePaths.Add(Path.Combine(localAppData, "WarframeHelper", "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(roamingAppData, "GameLauncher", OwHelperExeName));
        candidatePaths.Add(Path.Combine(roamingAppData, "GameLauncher", "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(roamingAppData, "WarframeHelper", OwHelperExeName));

        // ── 7. ProgramData ──
        candidatePaths.Add(Path.Combine(programData, "GameLauncher", OwHelperExeName));
        candidatePaths.Add(Path.Combine(programData, "GameLauncher", "cheat", OwHelperExeName));
        candidatePaths.Add(Path.Combine(programData, "WarframeHelper", OwHelperExeName));

        // ── 8. Temp directory ──
        candidatePaths.Add(Path.Combine(tempDir, OwHelperExeName));
        candidatePaths.Add(Path.Combine(tempDir, "GameLauncher", OwHelperExeName));
        candidatePaths.Add(Path.Combine(tempDir, "WarframeHelper", OwHelperExeName));

        // ── 9. Common install locations (all fixed drives) ──
        try
        {
            foreach (var drive in DriveInfo.GetDrives())
            {
                if (drive.DriveType != DriveType.Fixed || !drive.IsReady) continue;
                string root = drive.RootDirectory.FullName;

                candidatePaths.Add(Path.Combine(root, "GameLauncher", OwHelperExeName));
                candidatePaths.Add(Path.Combine(root, "GameLauncher", "cheat", OwHelperExeName));
                candidatePaths.Add(Path.Combine(root, "WarframeHelper", OwHelperExeName));
                candidatePaths.Add(Path.Combine(root, "WarframeHelper", "cheat", OwHelperExeName));
                candidatePaths.Add(Path.Combine(root, "Tools", "GameLauncher", OwHelperExeName));
                candidatePaths.Add(Path.Combine(root, "Tools", "WarframeHelper", OwHelperExeName));
            }
        }
        catch { }

        // ── 10. Hardcoded known dev/test paths ──
        candidatePaths.Add(@"C:\Users\Administrador\Desktop\WarframeHelper\cheat\" + OwHelperExeName);
        candidatePaths.Add(@"C:\Users\Administrador\Desktop\WarframeHelper\" + OwHelperExeName);
        candidatePaths.Add(@"C:\Users\Administrador\Desktop\Warframe-AIO-Tool-main\cheat\" + OwHelperExeName);

        // ── 11. PATH environment variable directories ──
        try
        {
            string? pathEnv = Environment.GetEnvironmentVariable("PATH");
            if (!string.IsNullOrEmpty(pathEnv))
            {
                foreach (string dir in pathEnv.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
                {
                    candidatePaths.Add(Path.Combine(dir, OwHelperExeName));
                }
            }
        }
        catch { }

        // ── Deduplicate and probe ──
        var testedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var rawPath in candidatePaths)
        {
            try
            {
                string fullPath = Path.GetFullPath(rawPath);
                if (!testedPaths.Add(fullPath)) continue;

                _logger.LogInfo($"Checking location: {fullPath}");
                if (File.Exists(fullPath))
                {
                    _logger.LogSuccess($"Executable found: {fullPath}");
                    return fullPath;
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"Could not inspect path '{rawPath}': {ex.Message}");
            }
        }

        _logger.LogError($"{OwHelperExeName} was not found in any checked location.");
        return null;
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
            // Ignore directory traversal errors gracefully
        }

        return null;
    }

    public bool LaunchOwHelper()
    {
        if (!_settings.EnableOwHelper)
        {
            _logger.LogWarning("OwHelper integration is disabled.");
            return false;
        }

        try
        {
            if (IsOwHelperActive())
            {
                _logger.LogWarning("OwHelper is already running.");
                return false;
            }

            string? exePath = FindOwHelperExecutable();
            if (string.IsNullOrEmpty(exePath))
            {
                MessageBox.Show(
                    $"{OwHelperExeName} was not found in any of the expected locations.\n\nPrimary search location:\n{AppContext.BaseDirectory}",
                    "OwHelper Executable Not Found",
                    MessageBoxButton.OK,
                    MessageBoxImage.Warning);
                return false;
            }

            string workDir = Path.GetDirectoryName(exePath) ?? AppContext.BaseDirectory;

            var startInfo = new ProcessStartInfo
            {
                FileName = exePath,
                WorkingDirectory = workDir
            };

            bool isHidden = _settings.LaunchOwHelperHidden;

            if (isHidden)
            {
                _logger.LogInfo("Launching OwHelper in hidden mode...");
                startInfo.UseShellExecute = false;
                startInfo.CreateNoWindow = true;
                startInfo.WindowStyle = ProcessWindowStyle.Hidden;
                startInfo.RedirectStandardOutput = true;
                startInfo.RedirectStandardError = true;
                startInfo.RedirectStandardInput = true;
            }
            else
            {
                _logger.LogInfo($"Launching OwHelper from: {exePath}");
                startInfo.UseShellExecute = true;
                startInfo.CreateNoWindow = false;
                startInfo.WindowStyle = ProcessWindowStyle.Normal;
            }

            var process = Process.Start(startInfo);
            if (process != null)
            {
                int bootstrapPid = process.Id;
                _logger.LogInfo($"OwHelper PID: {bootstrapPid}");

                // Asynchronously audit process lifespan and child payload console creation
                AuditPayloadConsoleCreationAsync(process, exePath, isHidden);

                return true;
            }

            _logger.LogError("Failed to start OwHelper.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to start OwHelper: {ex.Message}");
            return false;
        }
    }

    private void AuditPayloadConsoleCreationAsync(Process bootstrapProcess, string loaderExePath, bool isHidden)
    {
        int bootstrapPid = bootstrapProcess.Id;
        Task.Run(async () =>
        {
            try
            {
                var sw = Stopwatch.StartNew();
                bool loaderExited = false;
                long loaderExitMs = 0;
                bool payloadDetected = false;
                int payloadPid = 0;
                string payloadName = string.Empty;
                string payloadPath = string.Empty;
                long payloadCreationMs = 0;

                for (int i = 0; i < 30; i++) // Monitor for ~3 seconds
                {
                    await Task.Delay(100).ConfigureAwait(false);

                    if (!loaderExited)
                    {
                        try
                        {
                            if (bootstrapProcess.HasExited)
                            {
                                loaderExited = true;
                                loaderExitMs = sw.ElapsedMilliseconds;
                            }
                        }
                        catch { }
                    }

                    if (!payloadDetected)
                    {
                        // 1. Search via WMI for child processes spawned by bootstrap PID
                        var children = FindChildProcessesWmi(bootstrapPid);
                        if (children.Count > 0)
                        {
                            var child = children[0];
                            payloadPid = child.pid;
                            payloadName = child.name;
                            payloadPath = child.path;
                            payloadCreationMs = sw.ElapsedMilliseconds;
                            payloadDetected = true;
                        }
                        else
                        {
                            // 2. Search via AppData\Local\Temp\wf_*.exe process scanning
                            var tempPayload = FindTempPayloadProcess();
                            if (tempPayload.hasPayload)
                            {
                                payloadPid = tempPayload.pid;
                                payloadName = tempPayload.name;
                                payloadPath = tempPayload.path;
                                payloadCreationMs = sw.ElapsedMilliseconds;
                                payloadDetected = true;
                            }
                        }
                    }

                    if (loaderExited && payloadDetected)
                    {
                        break;
                    }
                }

                if (payloadDetected)
                {
                    _logger.LogInfo($"Payload detected: {payloadName}");
                    _logger.LogInfo($"    Payload PID: {payloadPid}");
                    _logger.LogInfo($"    Payload Path: {payloadPath}");
                    _logger.LogInfo($"    Time to payload creation: {payloadCreationMs} ms");

                    // Inspect payload executable PE Subsystem
                    ushort payloadSubsystem = InspectPeSubsystem(payloadPath);
                    string subsystemDesc = payloadSubsystem switch
                    {
                        2 => "GUI (/SUBSYSTEM:WINDOWS)",
                        3 => "Console (/SUBSYSTEM:CONSOLE)",
                        _ => $"Unknown ({payloadSubsystem})"
                    };
                    _logger.LogInfo($"Payload PE Subsystem: {subsystemDesc}");

                    if (loaderExited)
                    {
                        _logger.LogInfo($"OwHelperLoader exited after {loaderExitMs} ms (Payload continues running)");
                    }

                    if (isHidden)
                    {
                        _logger.LogWarning($"Hidden mode cannot be enforced because {payloadName} creates its own console window.");
                    }
                }
                else
                {
                    if (loaderExited)
                    {
                        _logger.LogInfo($"OwHelperLoader exited after {loaderExitMs} ms");
                    }

                    if (isHidden)
                    {
                        _logger.LogSuccess("OwHelper launched hidden.");
                    }
                    else
                    {
                        _logger.LogSuccess("OwHelper started successfully.");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"OwHelper diagnostic audit notice: {ex.Message}");
            }
        });
    }

    private static List<(int pid, string name, string path)> FindChildProcessesWmi(int parentPid)
    {
        var list = new List<(int pid, string name, string path)>();
        try
        {
            using var searcher = new ManagementObjectSearcher(
                $"SELECT ProcessId, Name, ExecutablePath FROM Win32_Process WHERE ParentProcessId = {parentPid}");
            using var collection = searcher.Get();

            foreach (ManagementObject obj in collection)
            {
                try
                {
                    int pid = Convert.ToInt32(obj["ProcessId"]);
                    string name = obj["Name"]?.ToString() ?? "Unknown";
                    string path = obj["ExecutablePath"]?.ToString() ?? string.Empty;
                    list.Add((pid, name, path));
                }
                catch { }
            }
        }
        catch { }
        return list;
    }

    private static (bool hasPayload, int pid, string name, string path) FindTempPayloadProcess()
    {
        try
        {
            var processes = Process.GetProcesses();
            foreach (var p in processes)
            {
                try
                {
                    string? mainModulePath = p.MainModule?.FileName;
                    if (!string.IsNullOrEmpty(mainModulePath))
                    {
                        string normalizedPath = Path.GetFullPath(mainModulePath);
                        string fileName = Path.GetFileName(normalizedPath);

                        if (normalizedPath.Contains(@"\AppData\Local\Temp\", StringComparison.OrdinalIgnoreCase) &&
                            fileName.StartsWith("wf_", StringComparison.OrdinalIgnoreCase))
                        {
                            return (true, p.Id, fileName, normalizedPath);
                        }
                    }
                }
                catch { }
                finally
                {
                    p.Dispose();
                }
            }
        }
        catch { }
        return (false, 0, string.Empty, string.Empty);
    }

    private static ushort InspectPeSubsystem(string exePath)
    {
        try
        {
            if (!File.Exists(exePath)) return 0;
            using var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
            using var br = new BinaryReader(fs);

            if (fs.Length < 0x40) return 0;
            fs.Seek(0x3C, SeekOrigin.Begin);
            int peOffset = br.ReadInt32();

            if (fs.Length < peOffset + 0x60) return 0;
            fs.Seek(peOffset + 0x5C, SeekOrigin.Begin);
            return br.ReadUInt16();
        }
        catch
        {
            return 0;
        }
    }
}
