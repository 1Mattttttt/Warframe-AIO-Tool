// Core/GuidSpoofManager.cs
using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using Microsoft.Win32;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public record SystemIdentitySnapshot(
    string CurrentGuid,
    string ComputerName,
    string UserName,
    string OsVersion,
    DateTime Timestamp
);

public class GuidSpoofManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

    // Win32 API for setting physical computer name (ComputerNamePhysicalDnsHostname = 5)
    private const int ComputerNamePhysicalDnsHostname = 5;

    [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Unicode)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool SetComputerNameExW(int NameType, string lpBuffer);

    public GuidSpoofManager(AppSettings settings, LoggerService logger)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        if (string.IsNullOrWhiteSpace(_settings.CurrentGuid))
        {
            GenerateNewGuid(saveImmediately: false);
        }
    }

    public string CurrentGuid => _settings.CurrentGuid;

    public string CurrentComputerName => Environment.MachineName;

    public string CurrentUserName => Environment.UserName;

    public IReadOnlyList<string> GuidHistory => _settings.GuidHistory.AsReadOnly();

    public string GenerateNewGuid(bool saveImmediately = true)
    {
        _logger.LogInfo("ℹ Generating new GUID and updating system MachineGuid...");

        string oldGuid = _settings.CurrentGuid;
        string newGuid = Guid.NewGuid().ToString();

        if (!string.IsNullOrWhiteSpace(oldGuid) && !_settings.GuidHistory.Contains(oldGuid))
        {
            _settings.GuidHistory.Insert(0, oldGuid);
            while (_settings.GuidHistory.Count > 10)
            {
                _settings.GuidHistory.RemoveAt(_settings.GuidHistory.Count - 1);
            }
        }

        _settings.CurrentGuid = newGuid;

        if (saveImmediately)
        {
            _settings.Save();
        }

        // Spoof Windows MachineGuid in Registry
        SpoofRegistryMachineGuid(newGuid);

        _logger.LogSuccess($"✔ New System GUID generated & applied:\n{newGuid}");
        return newGuid;
    }

    public string GenerateRandomComputerName()
    {
        const string chars = "ABCDEFGHIJKLMNOPQRSTUVWXYZ0123456789";
        Span<char> suffix = stackalloc char[7];
        using (var rng = RandomNumberGenerator.Create())
        {
            byte[] bytes = new byte[7];
            rng.GetBytes(bytes);
            for (int i = 0; i < 7; i++)
            {
                suffix[i] = chars[bytes[i] % chars.Length];
            }
        }
        return $"DESKTOP-{suffix.ToString()}";
    }

    public bool SpoofComputerName(string? newName = null)
    {
        string targetName = string.IsNullOrWhiteSpace(newName) ? GenerateRandomComputerName() : newName.Trim().ToUpperInvariant();

        // Enforce NetBIOS 15-char maximum limit
        if (targetName.Length > 15)
        {
            targetName = targetName[..15];
        }

        _logger.LogInfo($"ℹ Updating Windows ComputerName to '{targetName}'...");

        bool registrySuccess = false;
        try
        {
            // 1. Update HKLM\SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName
            using (var compKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\ComputerName\ComputerName", writable: true))
            {
                if (compKey != null)
                {
                    compKey.SetValue("ComputerName", targetName, RegistryValueKind.String);
                    registrySuccess = true;
                }
            }

            // 2. Update HKLM\SYSTEM\CurrentControlSet\Services\Tcpip\Parameters
            using (var tcpKey = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\Tcpip\Parameters", writable: true))
            {
                if (tcpKey != null)
                {
                    tcpKey.SetValue("NV Hostname", targetName, RegistryValueKind.String);
                    tcpKey.SetValue("Hostname", targetName, RegistryValueKind.String);
                }
            }

            // 3. Inform Windows Subsystem via SetComputerNameExW
            try
            {
                if (SetComputerNameExW(ComputerNamePhysicalDnsHostname, targetName))
                {
                    _logger.LogSuccess($"✔ SetComputerNameExW applied for physical DNS hostname: '{targetName}'");
                }
            }
            catch (Exception ex)
            {
                _logger.LogWarning($"SetComputerNameExW fallback exception: {ex.Message}");
            }

            if (registrySuccess)
            {
                _logger.LogSuccess($"✔ Windows ComputerName successfully updated in Registry to '{targetName}' (Effective upon system restart/session reload).");
                return true;
            }
            else
            {
                _logger.LogWarning("⚠ Could not write to ComputerName registry key. Ensure tool is running as Administrator.");
                return false;
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("⚠ Administrator privileges required to change Windows ComputerName.");
            return false;
        }
        catch (Exception ex)
        {
            _logger.LogError($"ComputerName update error: {ex.Message}");
            return false;
        }
    }

    public SystemIdentitySnapshot GetSystemIdentitySnapshot()
    {
        return new SystemIdentitySnapshot(
            CurrentGuid: CurrentGuid,
            ComputerName: CurrentComputerName,
            UserName: CurrentUserName,
            OsVersion: Environment.OSVersion.ToString(),
            Timestamp: DateTime.Now
        );
    }

    private void SpoofRegistryMachineGuid(string newGuid)
    {
        try
        {
            using var cryptoKey = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography", writable: true);
            if (cryptoKey != null)
            {
                cryptoKey.SetValue("MachineGuid", newGuid, RegistryValueKind.String);
                _logger.LogSuccess($"✔ Updated HKLM\\SOFTWARE\\Microsoft\\Cryptography\\MachineGuid to {newGuid}");
            }
            else
            {
                _logger.LogWarning("⚠ Could not open HKLM\\SOFTWARE\\Microsoft\\Cryptography for writing (Admin privileges required).");
            }
        }
        catch (UnauthorizedAccessException)
        {
            _logger.LogWarning("⚠ Admin privileges required to update system MachineGuid in Registry.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Registry MachineGuid update error: {ex.Message}");
        }
    }
}
