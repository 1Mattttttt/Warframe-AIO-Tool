using System;
using System.Diagnostics;
using System.IO;
using System.Management;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public enum DriveTypeInfo
{
    Unknown,
    HDD,
    SSD
}

public class WarframeRepairManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    public WarframeRepairManager(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public DriveTypeInfo DetectDriveType(string folderPath)
    {
        try
        {
            if (string.IsNullOrWhiteSpace(folderPath) || !Directory.Exists(folderPath))
                return DriveTypeInfo.Unknown;

            string rootDrive = Path.GetPathRoot(folderPath)?.TrimEnd('\\', ':') ?? string.Empty;
            if (string.IsNullOrEmpty(rootDrive)) return DriveTypeInfo.Unknown;

            using var searcher = new ManagementObjectSearcher(@"SELECT DeviceID, MediaType, Model FROM Win32_DiskDrive");
            foreach (ManagementObject drive in searcher.Get())
            {
                string mediaType = drive["MediaType"]?.ToString() ?? "";
                string model = drive["Model"]?.ToString() ?? "";

                if (mediaType.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                    mediaType.Contains("Solid State", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("SSD", StringComparison.OrdinalIgnoreCase) ||
                    model.Contains("NVMe", StringComparison.OrdinalIgnoreCase))
                {
                    return DriveTypeInfo.SSD;
                }

                if (mediaType.Contains("Fixed hard disk", StringComparison.OrdinalIgnoreCase) ||
                    mediaType.Contains("Hard Disk", StringComparison.OrdinalIgnoreCase))
                {
                    return DriveTypeInfo.HDD;
                }
            }
        }
        catch
        {
            // Suppress WMI resolution errors
        }

        return DriveTypeInfo.Unknown;
    }

    public Process? LaunchApplet(string appletName, string logName, string exePath)
    {
        if (string.IsNullOrWhiteSpace(exePath) || !File.Exists(exePath))
        {
            _logger.LogError("✖ Warframe.x64.exe executable not found for applet launch.");
            return null;
        }

        string lang = _settings.WarframeLanguage ?? "en";
        string api = _settings.WarframeGraphicsApi ?? "dx11";

        string args = $"-applet:{appletName} -silent -log:/{logName} -graphicsDriver:{api} -cluster:public -language:{lang} -deferred:1 /Tools/CachePlan.txt";
        _logger.LogInfo($"[APPLET] Launching Warframe applet '{appletName}'...");
        _logger.LogInfo($"Arguments: {args}");

        try
        {
            var psi = new ProcessStartInfo
            {
                FileName = exePath,
                Arguments = args,
                UseShellExecute = true,
                WorkingDirectory = Path.GetDirectoryName(exePath)
            };

            var proc = Process.Start(psi);
            if (proc != null)
            {
                _logger.LogSuccess($"✔ Started applet process (PID: {proc.Id})");
            }
            return proc;
        }
        catch (Exception ex)
        {
            _logger.LogError($"✖ Failed to launch applet '{appletName}': {ex.Message}");
            return null;
        }
    }
}
