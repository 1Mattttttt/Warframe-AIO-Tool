using System;
using System.Collections.Generic;
using Microsoft.Win32;
using GameLauncher.Configuration;
using GameLauncher.Logging;

namespace GameLauncher.Core;

public class GuidSpoofManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;

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

    public IReadOnlyList<string> GuidHistory => _settings.GuidHistory.AsReadOnly();

    public string GenerateNewGuid(bool saveImmediately = true)
    {
        _logger.LogInfo("ℹ Generating new GUID and spoofing system MachineGuid...");

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

        _logger.LogSuccess($"✔ New GUID generated & applied:\n{newGuid}");
        return newGuid;
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
