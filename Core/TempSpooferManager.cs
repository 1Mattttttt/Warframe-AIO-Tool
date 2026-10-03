// Core/TempSpooferManager.cs
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Security.Principal;
using System.Text;
using System.Threading.Tasks;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public record HardwareSerialsReport(
    string DiskDrives,
    string CpuSerial,
    string BiosSerial,
    string BaseboardSerial,
    string SmbiosUuid,
    string MacAddresses,
    DateTime Timestamp
);

public class TempSpooferManager
{
    private const string ResourceFolderName = "Spoof resources";
    private const string TempSpooferSubFolder = "TempSpoofer";
    private const string KdmapperBinary = "kdmapper.exe";
    private const string DriverBinary = "AcpiDev.sys";
    private const string KduBinary = "kdu.exe";

    private readonly LoggerService _logger;

    public bool IsDriverMapped { get; private set; }
    public DateTime? MappedTime { get; private set; }

    public TempSpooferManager(LoggerService logger)
    {
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public static bool IsAdministrator()
    {
        using var identity = WindowsIdentity.GetCurrent();
        var principal = new WindowsPrincipal(identity);
        return principal.IsInRole(WindowsBuiltInRole.Administrator);
    }

    public string? GetTempSpooferDirectory()
    {
        var candidates = new List<string>();

        string baseDir = AppContext.BaseDirectory;
        string currentDir = Directory.GetCurrentDirectory();
        string userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string tempDir = Path.GetTempPath();

        // 1. Output directory
        candidates.Add(Path.Combine(baseDir, ResourceFolderName, TempSpooferSubFolder));
        candidates.Add(Path.Combine(baseDir, TempSpooferSubFolder));

        // 2. Working directory
        candidates.Add(Path.Combine(currentDir, ResourceFolderName, TempSpooferSubFolder));
        candidates.Add(Path.Combine(currentDir, TempSpooferSubFolder));

        // 3. Project Root (Development environment)
        string? projectRoot = FindProjectRootDirectory(baseDir);
        if (!string.IsNullOrEmpty(projectRoot))
        {
            candidates.Add(Path.Combine(projectRoot, ResourceFolderName, TempSpooferSubFolder));
            candidates.Add(Path.Combine(projectRoot, TempSpooferSubFolder));
        }

        // 4. Desktop explicit paths
        candidates.Add(Path.Combine(desktop, "Warframe-AIO-Tool-main", ResourceFolderName, TempSpooferSubFolder));
        candidates.Add(Path.Combine(desktop, "temp spoofer"));

        // 5. Parent traversals
        for (int i = 1; i <= 5; i++)
        {
            string relative = string.Join(Path.DirectorySeparatorChar.ToString(), Enumerable.Repeat("..", i));
            candidates.Add(Path.Combine(baseDir, relative, ResourceFolderName, TempSpooferSubFolder));
            candidates.Add(Path.Combine(baseDir, relative, "temp spoofer"));
        }

        // 6. AppData / Temp fallback
        candidates.Add(Path.Combine(localAppData, "WarframeAioTool", TempSpooferSubFolder));
        candidates.Add(Path.Combine(tempDir, "WarframeAioTool", TempSpooferSubFolder));

        foreach (var path in candidates)
        {
            if (Directory.Exists(path))
            {
                string driverFile = Path.Combine(path, DriverBinary);
                if (File.Exists(driverFile))
                {
                    _logger.LogInfo($"Resolved Temp Spoofer directory: {path}");
                    return Path.GetFullPath(path);
                }
            }
        }

        return null;
    }

    public async Task<bool> MapTempDriverAsync(Action<string>? statusCallback = null)
    {
        return await Task.Run(async () =>
        {
            try
            {
                if (!IsAdministrator())
                {
                    string adminMsg = "Administrative privileges required to map kernel driver.";
                    _logger.LogError(adminMsg);
                    statusCallback?.Invoke($"❌ {adminMsg}");
                    return false;
                }

                string? spooferDir = GetTempSpooferDirectory();
                if (string.IsNullOrEmpty(spooferDir))
                {
                    string missingDirMsg = "Temp Spoofer resources folder not found. Ensure AcpiDev.sys is present.";
                    _logger.LogError(missingDirMsg);
                    statusCallback?.Invoke($"❌ {missingDirMsg}");
                    return false;
                }

                string driverPath = Path.Combine(spooferDir, DriverBinary);
                if (!File.Exists(driverPath))
                {
                    string missingDriverMsg = $"Driver binary '{DriverBinary}' missing at: {spooferDir}";
                    _logger.LogError(missingDriverMsg);
                    statusCallback?.Invoke($"❌ {missingDriverMsg}");
                    return false;
                }

                string kdmapperPath = Path.Combine(spooferDir, KdmapperBinary);
                string kduPath = Path.Combine(spooferDir, KduBinary);

                bool hasKdmapper = File.Exists(kdmapperPath);
                bool hasKdu = File.Exists(kduPath);

                if (!hasKdmapper && !hasKdu)
                {
                    string noMapperMsg = "Neither kdmapper.exe nor kdu.exe was found in Temp Spoofer directory.";
                    _logger.LogError(noMapperMsg);
                    statusCallback?.Invoke($"❌ {noMapperMsg}");
                    return false;
                }

                statusCallback?.Invoke("⏳ Initializing vulnerable driver kernel mapping (BYOVD)...");
                _logger.LogInfo($"Mapping driver '{driverPath}' into kernel...");

                bool mappingSuccess = false;

                // Priority 1: Map via kdmapper (Intel iqvw64e.sys physical memory exploit)
                if (hasKdmapper)
                {
                    mappingSuccess = await ExecuteKdmapperAsync(kdmapperPath, driverPath, spooferDir, statusCallback);
                }

                // Priority 2: Fallback to KDU if kdmapper fails or is unavailable
                if (!mappingSuccess && hasKdu)
                {
                    _logger.LogWarning("Attempting fallback kernel mapping via KDU provider...");
                    statusCallback?.Invoke("⚠ Retrying driver mapping via secondary KDU kernel provider...");
                    mappingSuccess = await ExecuteKduFallbackAsync(kduPath, driverPath, spooferDir, statusCallback);
                }

                if (!mappingSuccess)
                {
                    string failMsg = "Kernel driver mapping failed. Check security/hypervisor software.";
                    _logger.LogError(failMsg);
                    statusCallback?.Invoke($"❌ {failMsg}");
                    return false;
                }

                IsDriverMapped = true;
                MappedTime = DateTime.Now;

                // Step 2: Flush WMI Cache & Restart Winmgmt so WMI queries immediately reflect kernel spoofed data
                statusCallback?.Invoke("🧹 Flushing WMI repository cache & restarting winmgmt service...");
                _logger.LogInfo("Flushing WMI cache (taskkill WmiPrvSE / net restart winmgmt)...");
                await FlushWmiCacheAsync();

                // Step 3: Run Hardware Verification
                statusCallback?.Invoke("🔍 Running hardware serial verification suite...");
                var report = await VerifyHardwareSerialsAsync(statusCallback);

                statusCallback?.Invoke("✔ Temp Spoofer successfully active (Kernel Memory Resident).");
                _logger.LogSuccess("Temp Spoofer loaded! Hardware serials dynamically altered until next system restart.");

                return true;
            }
            catch (Exception ex)
            {
                string errMsg = $"Temp Spoofer exception: {ex.Message}";
                _logger.LogError(errMsg);
                statusCallback?.Invoke($"❌ {errMsg}");
                return false;
            }
        });
    }

    private async Task<bool> ExecuteKdmapperAsync(string mapperPath, string driverPath, string workDir, Action<string>? statusCallback)
    {
        return await Task.Run(() =>
        {
            try
            {
                _logger.LogInfo($"Executing kdmapper: \"{mapperPath}\" \"{driverPath}\"");
                statusCallback?.Invoke("⚡ Loading Intel vulnerable driver & mapping memory sections...");

                var psi = new ProcessStartInfo
                {
                    FileName = mapperPath,
                    Arguments = $"\"{driverPath}\"",
                    WorkingDirectory = workDir,
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true
                };

                using var process = new Process { StartInfo = psi };
                var outputBuilder = new StringBuilder();
                var errorBuilder = new StringBuilder();

                process.OutputDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        outputBuilder.AppendLine(e.Data);
                        _logger.LogInfo($"[kdmapper] {e.Data}");
                        if (e.Data.Contains("[+]") || e.Data.Contains("success"))
                        {
                            statusCallback?.Invoke($"⚡ [kdmapper] {e.Data.Trim()}");
                        }
                    }
                };

                process.ErrorDataReceived += (s, e) =>
                {
                    if (!string.IsNullOrWhiteSpace(e.Data))
                    {
                        errorBuilder.AppendLine(e.Data);
                        _logger.LogWarning($"[kdmapper-err] {e.Data}");
                    }
                };

                process.Start();
                process.BeginOutputReadLine();
                process.BeginErrorReadLine();

                bool exited = process.WaitForExit(35000);
                if (!exited)
                {
                    try { process.Kill(true); } catch { }
                    _logger.LogError("kdmapper timed out after 35 seconds and was killed.");
                    return false;
                }

                string fullOutput = outputBuilder.ToString();
                int exitCode = process.ExitCode;

                if (exitCode == 0 || fullOutput.Contains("[+] success") || fullOutput.Contains("DriverEntry"))
                {
                    _logger.LogSuccess($"kdmapper mapped driver successfully (ExitCode: {exitCode}).");
                    return true;
                }

                _logger.LogWarning($"kdmapper exited with code {exitCode}. Output: {fullOutput} Errors: {errorBuilder}");
                return false;
            }
            catch (Exception ex)
            {
                _logger.LogError($"ExecuteKdmapperAsync exception: {ex.Message}");
                return false;
            }
        });
    }

    private async Task<bool> ExecuteKduFallbackAsync(string kduPath, string driverPath, string workDir, Action<string>? statusCallback)
    {
        return await Task.Run(() =>
        {
            // Providers: 55 (ThrottleStop), 1 (RTCore64), 6 (DBUtil), 14 (ProcessHacker)
            int[] providers = { 55, 1, 6, 14 };

            foreach (int prv in providers)
            {
                try
                {
                    _logger.LogInfo($"Executing KDU Provider {prv}: \"{kduPath}\" -prv {prv} -map \"{driverPath}\"");
                    statusCallback?.Invoke($"⚡ Testing KDU Provider {prv}...");

                    var psi = new ProcessStartInfo
                    {
                        FileName = kduPath,
                        Arguments = $"-prv {prv} -map \"{driverPath}\"",
                        WorkingDirectory = workDir,
                        UseShellExecute = false,
                        CreateNoWindow = true,
                        WindowStyle = ProcessWindowStyle.Hidden,
                        RedirectStandardOutput = true,
                        RedirectStandardError = true
                    };

                    using var process = Process.Start(psi);
                    if (process == null) continue;

                    string stdOut = process.StandardOutput.ReadToEnd();
                    string stdErr = process.StandardError.ReadToEnd();
                    process.WaitForExit(20000);

                    if (process.ExitCode == 0)
                    {
                        _logger.LogSuccess($"KDU Provider {prv} successfully mapped driver!");
                        return true;
                    }
                }
                catch (Exception ex)
                {
                    _logger.LogWarning($"KDU provider {prv} exception: {ex.Message}");
                }
            }

            return false;
        });
    }

    public async Task FlushWmiCacheAsync()
    {
        await Task.Run(() =>
        {
            try
            {
                RunHiddenCommand("taskkill /F /IM WmiPrvSE.exe");
                RunHiddenCommand("net stop winmgmt /y");
                RunHiddenCommand("net start winmgmt");
                RunHiddenCommand("taskkill /F /IM WmiPrvSE.exe");
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"FlushWmiCache exception: {ex.Message}");
            }
        });
    }

    public async Task<HardwareSerialsReport> VerifyHardwareSerialsAsync(Action<string>? outputCallback = null)
    {
        return await Task.Run(() =>
        {
            outputCallback?.Invoke("📋 Querying Disk Drive Model & SerialNumber...");
            string disks = RunQueryCommand("wmic", "diskdrive get model,serialnumber");

            outputCallback?.Invoke("📋 Querying CPU SerialNumber...");
            string cpu = RunQueryCommand("wmic", "cpu get serialnumber");

            outputCallback?.Invoke("📋 Querying BIOS SerialNumber...");
            string bios = RunQueryCommand("wmic", "bios get serialnumber");

            outputCallback?.Invoke("📋 Querying Baseboard (Motherboard) SerialNumber...");
            string baseboard = RunQueryCommand("wmic", "baseboard get serialnumber");

            outputCallback?.Invoke("📋 Querying smBIOS UUID...");
            string uuid = RunQueryCommand("wmic", "path win32_computersystemproduct get uuid");

            outputCallback?.Invoke("📋 Querying Physical MAC Address Telemetry...");
            string mac = RunQueryCommand("getmac", "");

            var report = new HardwareSerialsReport(
                DiskDrives: CleanWmicOutput(disks),
                CpuSerial: CleanWmicOutput(cpu),
                BiosSerial: CleanWmicOutput(bios),
                BaseboardSerial: CleanWmicOutput(baseboard),
                SmbiosUuid: CleanWmicOutput(uuid),
                MacAddresses: mac.Trim(),
                Timestamp: DateTime.Now
            );

            _logger.LogInfo($"[Hardware Verification]\nDisk: {report.DiskDrives}\nCPU: {report.CpuSerial}\nBIOS: {report.BiosSerial}\nMotherboard: {report.BaseboardSerial}\nUUID: {report.SmbiosUuid}\nMAC:\n{report.MacAddresses}");

            return report;
        });
    }

    private static string RunQueryCommand(string exe, string args)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exe,
                Arguments = args,
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true
            };

            using var process = Process.Start(psi);
            if (process == null) return "Query Failed";

            string output = process.StandardOutput.ReadToEnd();
            process.WaitForExit(5000);
            return string.IsNullOrWhiteSpace(output) ? "N/A" : output;
        }
        catch (Exception ex)
        {
            return $"Error: {ex.Message}";
        }
    }

    private static void RunHiddenCommand(string cmdLine)
    {
        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = "cmd.exe",
                Arguments = $"/c {cmdLine}",
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden
            };

            using var process = Process.Start(psi);
            process?.WaitForExit(6000);
        }
        catch
        {
            // Suppress secondary cleanup exceptions
        }
    }

    private static string CleanWmicOutput(string raw)
    {
        if (string.IsNullOrWhiteSpace(raw)) return "No instance available";
        var lines = raw.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                       .Select(l => l.Trim())
                       .Where(l => !string.IsNullOrEmpty(l))
                       .ToList();

        if (lines.Count == 0) return "No instance available";

        if (lines.Count == 1)
        {
            string line = lines[0];
            string lower = line.ToLowerInvariant();
            if (lower == "serialnumber" || lower == "uuid" || lower.Contains("model") || lower == "caption")
            {
                return "No instance available";
            }
            return line;
        }

        var dataLines = lines.Skip(1).ToList();
        return string.Join(" | ", dataLines);
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
            // Suppress traversal errors
        }

        return null;
    }
}
