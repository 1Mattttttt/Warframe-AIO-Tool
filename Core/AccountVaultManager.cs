using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GameLauncher.Configuration;
using GameLauncher.Logging;
using GameLauncher.Models;

namespace GameLauncher.Core;

public class AccountVaultManager
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly GuidSpoofManager _guidSpoofManager;
    private readonly SpooferManager _spooferManager;

    private readonly string _vaultBaseDir;
    private readonly string _profilesJsonFile;
    private readonly List<AccountProfile> _profiles = new();

    public AccountVaultManager(
        AppSettings settings,
        LoggerService logger,
        GuidSpoofManager guidSpoofManager,
        SpooferManager spooferManager)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _guidSpoofManager = guidSpoofManager ?? throw new ArgumentNullException(nameof(guidSpoofManager));
        _spooferManager = spooferManager ?? throw new ArgumentNullException(nameof(spooferManager));

        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        _vaultBaseDir = Path.Combine(localAppData, "GameLauncher", "AccountVault");
        Directory.CreateDirectory(_vaultBaseDir);

        _profilesJsonFile = Path.Combine(_vaultBaseDir, "vault_profiles.json");
        LoadProfiles();
    }

    public IReadOnlyList<AccountProfile> Profiles => _profiles.AsReadOnly();

    public string? ActiveProfileName => _settings.ActiveAccountVaultProfile;

    public void LoadProfiles()
    {
        _profiles.Clear();
        try
        {
            if (File.Exists(_profilesJsonFile))
            {
                string json = File.ReadAllText(_profilesJsonFile);
                var list = JsonSerializer.Deserialize<List<AccountProfile>>(json);
                if (list != null)
                {
                    _profiles.AddRange(list);
                }
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to load vault profiles: {ex.Message}");
        }
    }

    public void SaveProfilesList()
    {
        try
        {
            var options = new JsonSerializerOptions { WriteIndented = true };
            string json = JsonSerializer.Serialize(_profiles, options);
            File.WriteAllText(_profilesJsonFile, json);
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to save vault profiles list: {ex.Message}");
        }
    }

    /// <summary>
    /// Detects current Warframe account hash directory in %LOCALAPPDATA%\Warframe.
    /// </summary>
    public string? DetectCurrentWarframeAccountHash()
    {
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string wfDir = Path.Combine(localAppData, "Warframe");
        if (!Directory.Exists(wfDir)) return null;

        var subDirs = Directory.GetDirectories(wfDir);
        foreach (var dir in subDirs)
        {
            string name = Path.GetFileName(dir);
            // 32-character hex hash
            if (name.Length == 32 && IsHexString(name))
            {
                string settingsFile = Path.Combine(dir, "settings");
                if (File.Exists(settingsFile))
                {
                    return name;
                }
            }
        }

        return null;
    }

    /// <summary>
    /// Saves the current session from %LOCALAPPDATA%\Warframe into the secure vault.
    /// </summary>
    public bool SaveCurrentSessionToVault(string profileName, string notes = "")
    {
        if (string.IsNullOrWhiteSpace(profileName))
        {
            _logger.LogWarning("Profile name cannot be empty.");
            return false;
        }

        profileName = profileName.Trim();
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string wfDir = Path.Combine(localAppData, "Warframe");

        string? hash = DetectCurrentWarframeAccountHash();
        if (string.IsNullOrEmpty(hash))
        {
            _logger.LogWarning("No active session with 'settings' binary found in %LOCALAPPDATA%\\Warframe.");
            return false;
        }

        try
        {
            string sourceDir = Path.Combine(wfDir, hash);
            string profileDir = Path.Combine(_vaultBaseDir, profileName, "Data", hash);
            Directory.CreateDirectory(profileDir);

            // Copy all settings and account configs into vault
            foreach (var file in Directory.GetFiles(sourceDir))
            {
                string destFile = Path.Combine(profileDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            var existing = _profiles.FirstOrDefault(p => string.Equals(p.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
            if (existing != null)
            {
                existing.AccountHash = hash;
                existing.LastUsedAt = DateTime.Now;
                existing.CustomNotes = notes;
                existing.AssociatedGuid = _guidSpoofManager.CurrentGuid;
                existing.HasSettingsBinary = true;
            }
            else
            {
                _profiles.Add(new AccountProfile
                {
                    ProfileName = profileName,
                    AccountHash = hash,
                    CreatedAt = DateTime.Now,
                    LastUsedAt = DateTime.Now,
                    AssociatedGuid = _guidSpoofManager.CurrentGuid,
                    CustomNotes = notes,
                    HasSettingsBinary = true
                });
            }

            _settings.ActiveAccountVaultProfile = profileName;
            _settings.Save();
            SaveProfilesList();

            _logger.LogSuccess($"✔ Account '{profileName}' (Hash: {hash.Substring(0, 8)}...) successfully stored in secure vault!");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Error storing account in vault: {ex.Message}");
            return false;
        }
    }

    /// <summary>
    /// Switches to the target account profile. If autoSpoof is enabled, automatically purges
    /// previous telemetry and runs GUID + MAC spoofing before deploying the target profile.
    /// </summary>
    public async Task<bool> SwitchAccountAsync(string profileName, bool autoSpoof = true, Action<string>? statusCallback = null)
    {
        var profile = _profiles.FirstOrDefault(p => string.Equals(p.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
        if (profile == null)
        {
            _logger.LogError($"Account profile '{profileName}' not found in vault.");
            return false;
        }

        string profileDataDir = Path.Combine(_vaultBaseDir, profile.ProfileName, "Data", profile.AccountHash);
        if (!Directory.Exists(profileDataDir))
        {
            _logger.LogError($"Profile data for '{profileName}' not found on disk.");
            return false;
        }

        statusCallback?.Invoke($"Initiating switch to account '{profileName}'...");
        _logger.LogInfo($"=== SWITCHING ACCOUNT IN VAULT: {profileName} ===");

        try
        {
            // 1. Surgical telemetry cleanup (preserving EE.cfg)
            statusCallback?.Invoke("Executing anti-tracking telemetry cleanup...");
            _logger.LogInfo("Executing surgical cleanup of Warframe telemetry residues...");
            WarframeCleaner.CleanWarframeFiles(_logger, _settings);

            // 2. Integrated Auto-Spoofing (if enabled)
            if (autoSpoof)
            {
                statusCallback?.Invoke("Generating new Machine GUID...");
                _logger.LogInfo("Generating and applying new MachineGuid in Windows Registry...");
                _guidSpoofManager.GenerateNewGuid(saveImmediately: true);

                statusCallback?.Invoke("Spoofing physical network MAC addresses...");
                _logger.LogInfo("Executing Spoofer MAC address randomization script...");
                await _spooferManager.RunMacSpooferAsync(msg => _logger.LogInfo($"[Spoofer] {msg}"));
            }

            // 3. Restore target account data into %LOCALAPPDATA%\Warframe
            statusCallback?.Invoke($"Deploying tokens and configurations for account '{profileName}'...");
            string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            string destDir = Path.Combine(localAppData, "Warframe", profile.AccountHash);
            Directory.CreateDirectory(destDir);

            foreach (var file in Directory.GetFiles(profileDataDir))
            {
                string destFile = Path.Combine(destDir, Path.GetFileName(file));
                File.Copy(file, destFile, overwrite: true);
            }

            profile.LastUsedAt = DateTime.Now;
            profile.AssociatedGuid = _guidSpoofManager.CurrentGuid;
            _settings.ActiveAccountVaultProfile = profileName;
            _settings.Save();
            SaveProfilesList();

            statusCallback?.Invoke($"Account '{profileName}' activated successfully!");
            _logger.LogSuccess($"✔ Account switch complete: '{profileName}' is ready to play with total isolation!");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to switch account in vault: {ex.Message}");
            return false;
        }
    }

    public bool DeleteProfile(string profileName)
    {
        var profile = _profiles.FirstOrDefault(p => string.Equals(p.ProfileName, profileName, StringComparison.OrdinalIgnoreCase));
        if (profile == null) return false;

        try
        {
            string profileDir = Path.Combine(_vaultBaseDir, profile.ProfileName);
            if (Directory.Exists(profileDir))
            {
                Directory.Delete(profileDir, recursive: true);
            }

            _profiles.Remove(profile);
            if (string.Equals(_settings.ActiveAccountVaultProfile, profileName, StringComparison.OrdinalIgnoreCase))
            {
                _settings.ActiveAccountVaultProfile = string.Empty;
                _settings.Save();
            }

            SaveProfilesList();
            _logger.LogInfo($"Profile '{profileName}' removed from vault.");
            return true;
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to delete profile '{profileName}': {ex.Message}");
            return false;
        }
    }

    private static bool IsHexString(string test)
    {
        return test.All(c => (c >= '0' && c <= '9') || (c >= 'a' && c <= 'f') || (c >= 'A' && c <= 'F'));
    }
}
