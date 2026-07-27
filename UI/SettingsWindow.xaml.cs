using System;
using System.IO;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using GameLauncher.Configuration;
using GameLauncher.Core;
using GameLauncher.Logging;

namespace GameLauncher.UI;

public partial class SettingsWindow : Window
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly GuidSpoofManager _guidSpoofManager;

    public SettingsWindow(AppSettings settings, LoggerService logger)
    {
        InitializeComponent();

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _guidSpoofManager = new GuidSpoofManager(_settings, _logger);

        LoadSettingsToControls();
    }

    private void LoadSettingsToControls()
    {
        // General
        chkStartMonitoringOnLaunch.IsChecked = _settings.StartMonitoringOnLaunch;
        chkStartMinimized.IsChecked = _settings.StartMinimized;
        chkAutoLaunchWarframe.IsChecked = _settings.AutoLaunchWarframe;
        chkEnableStartupAnimation.IsChecked = _settings.EnableStartupAnimation;
        // Audio & Music
        chkEnableBackgroundMusic.IsChecked = _settings.EnableBackgroundMusic;
        sliderSettingsMusicVolume.Value = _settings.BackgroundMusicVolume;
        txtSettingsMusicVolumeValue.Text = $"{(int)_settings.BackgroundMusicVolume}%";

        // Application Behavior
        chkStartMinimizedToTray.IsChecked = _settings.StartMinimizedToTray;
        chkLaunchOnSystemStartup.IsChecked = _settings.LaunchOnSystemStartup;
        chkLaunchMinimizedOnSystemStartup.IsChecked = _settings.LaunchMinimizedOnSystemStartup;
        chkCloseToTray.IsChecked = _settings.CloseToTray;

        if (_settings.StartupWindowMode.Equals("Windowed", StringComparison.OrdinalIgnoreCase))
        {
            cboStartupWindowMode.SelectedIndex = 1;
        }
        else
        {
            cboStartupWindowMode.SelectedIndex = 0;
        }

        // Launcher
        txtEpicLaunchUri.Text = _settings.EpicLaunchUri;
        txtSteamAppId.Text = _settings.SteamAppId;
        txtStandaloneLauncherPath.Text = _settings.StandaloneLauncherPath;

        // Warframe
        txtWarframeFolder.Text = _settings.WarframeInstallFolder;
        txtCacheFolder.Text = _settings.CacheFolder;
        txtDiscordSdkPath.Text = _settings.DiscordSdkPath;

        // Maintenance
        txtLaunchThreshold.Text = _settings.LaunchesBeforeAction.ToString();
        chkEnableCacheCleaner.IsChecked = _settings.EnableCacheCleaner;

        // External Tools
        chkEnableOwHelper.IsChecked = _settings.EnableOwHelper;
        chkLaunchOwHelperHidden.IsChecked = _settings.LaunchOwHelperHidden;

        // Logging
        chkEnableFileLogging.IsChecked = _settings.EnableFileLogging;
        txtMaxLogEntries.Text = _settings.MaxLogEntries.ToString();

        // GUID Spoof
        chkEnableGuidSpoof.IsChecked = _settings.EnableGuidSpoof;
        txtCurrentGuid.Text = _guidSpoofManager.CurrentGuid;
        lstGuidHistory.ItemsSource = _guidSpoofManager.GuidHistory;

        // Load profile avatar image
        var profileImage = ImageAssetManager.Load("Assets/profile.png", _logger);
        if (profileImage != null)
        {
            imgSettingsProfile.Source = profileImage;
        }
    }

    private void btnBrowseStandalone_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select Standalone Launcher.exe",
            Filter = "Executable files (*.exe)|*.exe|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            txtStandaloneLauncherPath.Text = dialog.FileName;
        }
    }

    private void btnBrowseWarframeFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Warframe Installation Folder"
        };
        if (dialog.ShowDialog(this) == true)
        {
            txtWarframeFolder.Text = dialog.FolderName;
        }
    }

    private void btnBrowseCacheFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFolderDialog
        {
            Title = "Select Cache Folder"
        };
        if (dialog.ShowDialog(this) == true)
        {
            txtCacheFolder.Text = dialog.FolderName;
        }
    }

    private void btnBrowseDiscordSdk_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new OpenFileDialog
        {
            Title = "Select discord_game_sdk.dll",
            Filter = "DLL files (*.dll)|*.dll|All files (*.*)|*.*"
        };
        if (dialog.ShowDialog(this) == true)
        {
            txtDiscordSdkPath.Text = dialog.FileName;
        }
    }

    private void btnExportLogs_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new SaveFileDialog
        {
            Title = "Export Application Logs",
            Filter = "Log Files (*.log)|*.log|Text Files (*.txt)|*.txt|All Files (*.*)|*.*",
            FileName = $"Warframe_AIO_Tool_Log_{DateTime.Now:yyyyMMdd_HHmmss}.log"
        };

        if (dialog.ShowDialog(this) == true)
        {
            try
            {
                var content = $"=== Warframe AIO Tool Exported Activity Logs [{DateTime.Now:G}] ===\n" +
                              $"Selected Launcher: {_settings.PreferredLauncher}\n" +
                              $"Warframe Path: {_settings.WarframePath}\n" +
                              $"Discord SDK Path: {_settings.DiscordSdkPath}\n" +
                              $"Epic URI: {_settings.EpicLaunchUri}\n";

                File.WriteAllText(dialog.FileName, content);
                _logger.LogSuccess($"Logs exported successfully to {dialog.FileName}");

                MessageBox.Show(
                    $"Logs successfully exported to:\n{dialog.FileName}",
                    "Export Logs",
                    MessageBoxButton.OK,
                    MessageBoxImage.Information);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Failed to export logs: {ex.Message}");
                MessageBox.Show(
                    $"Failed to export logs:\n{ex.Message}",
                    "Export Failed",
                    MessageBoxButton.OK,
                    MessageBoxImage.Error);
            }
        }
    }

    private void btnRestoreDefaults_Click(object sender, RoutedEventArgs e)
    {
        var result = MessageBox.Show(
            "Are you sure you want to restore all settings to default values?",
            "Restore Defaults",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            var defaultSettings = new AppSettings();

            chkStartMonitoringOnLaunch.IsChecked = defaultSettings.StartMonitoringOnLaunch;
            chkStartMinimized.IsChecked = defaultSettings.StartMinimized;
            chkAutoLaunchWarframe.IsChecked = defaultSettings.AutoLaunchWarframe;
            chkEnableStartupAnimation.IsChecked = defaultSettings.EnableStartupAnimation;
            chkEnableStartupJingle.IsChecked = defaultSettings.EnableStartupJingle;

            chkStartMinimizedToTray.IsChecked = defaultSettings.StartMinimizedToTray;
            chkLaunchOnSystemStartup.IsChecked = defaultSettings.LaunchOnSystemStartup;
            chkLaunchMinimizedOnSystemStartup.IsChecked = defaultSettings.LaunchMinimizedOnSystemStartup;
            chkCloseToTray.IsChecked = defaultSettings.CloseToTray;

            txtEpicLaunchUri.Text = defaultSettings.EpicLaunchUri;
            txtSteamAppId.Text = defaultSettings.SteamAppId;
            txtStandaloneLauncherPath.Text = defaultSettings.StandaloneLauncherPath;

            txtWarframeFolder.Text = defaultSettings.WarframeInstallFolder;
            txtCacheFolder.Text = defaultSettings.CacheFolder;
            txtDiscordSdkPath.Text = defaultSettings.DiscordSdkPath;

            txtLaunchThreshold.Text = defaultSettings.LaunchesBeforeAction.ToString();
            chkEnableCacheCleaner.IsChecked = defaultSettings.EnableCacheCleaner;

            chkEnableFileLogging.IsChecked = defaultSettings.EnableFileLogging;
            txtMaxLogEntries.Text = defaultSettings.MaxLogEntries.ToString();

            chkEnableGuidSpoof.IsChecked = defaultSettings.EnableGuidSpoof;
            txtCurrentGuid.Text = _guidSpoofManager.CurrentGuid;
            lstGuidHistory.ItemsSource = _guidSpoofManager.GuidHistory;
        }
    }

    private void btnAutoDetectPaths_Click(object sender, RoutedEventArgs e)
    {
        _logger.LogInfo("🔍 Auto detecting launcher installation paths...");
        int detectedCount = 0;

        // 1. Standalone Detection (%LocalAppData%\Warframe\Downloaded\Public\Warframe.x64.exe)
        string localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        string standalonePath = Path.Combine(localAppData, @"Warframe\Downloaded\Public\Warframe.x64.exe");
        if (File.Exists(standalonePath))
        {
            txtStandaloneLauncherPath.Text = standalonePath;
            _settings.StandaloneLauncherPath = standalonePath;
            _logger.LogSuccess($"✔ Standalone detected:\n{standalonePath}");
            detectedCount++;
        }
        else
        {
            _logger.LogWarning("⚠ Standalone installation not found.");
        }

        // 2. Epic Games Detection
        var epicCandidates = new[]
        {
            @"C:\Program Files\Epic Games\Warframe\Downloaded\Warframe.x64.exe",
            @"C:\Program Files (x86)\Epic Games\Warframe\Downloaded\Warframe.x64.exe",
            @"D:\Epic Games\Warframe\Downloaded\Warframe.x64.exe",
            @"E:\Epic Games\Warframe\Downloaded\Warframe.x64.exe"
        };
        string? foundEpic = Array.Find(epicCandidates, File.Exists);
        if (foundEpic != null)
        {
            string epicFolder = Path.GetDirectoryName(foundEpic) ?? string.Empty;
            txtWarframeFolder.Text = epicFolder;
            _settings.WarframeInstallFolder = epicFolder;

            string cacheFolder = Path.Combine(epicFolder, "Cache.Windows");
            if (Directory.Exists(cacheFolder))
            {
                txtCacheFolder.Text = cacheFolder;
                _settings.CacheFolder = cacheFolder;
            }

            _logger.LogSuccess($"✔ Epic Games Warframe detected:\n{foundEpic}");
            detectedCount++;
        }
        else
        {
            _logger.LogWarning("⚠ Epic Games installation not found at standard locations.");
        }

        // 3. Steam Detection
        var steamCandidates = new[]
        {
            @"C:\Program Files (x86)\Steam\steamapps\common\Warframe\Downloaded\Public\Warframe.x64.exe",
            @"C:\Program Files\Steam\steamapps\common\Warframe\Downloaded\Public\Warframe.x64.exe",
            @"D:\SteamLibrary\steamapps\common\Warframe\Downloaded\Public\Warframe.x64.exe",
            @"E:\SteamLibrary\steamapps\common\Warframe\Downloaded\Public\Warframe.x64.exe"
        };
        string? foundSteam = Array.Find(steamCandidates, File.Exists);
        if (foundSteam != null)
        {
            _logger.LogSuccess($"✔ Steam Warframe detected:\n{foundSteam}");
            detectedCount++;
        }
        else
        {
            _logger.LogWarning("⚠ Steam installation not found at standard locations.");
        }

        _settings.Save();

        System.Windows.MessageBox.Show(
            $"Auto path detection completed.\nDetected installations: {detectedCount}",
            "Auto Path Detection",
            MessageBoxButton.OK,
            MessageBoxImage.Information);
    }

    private void btnGenerateGuid_Click(object sender, RoutedEventArgs e)
    {
        string newGuid = _guidSpoofManager.GenerateNewGuid(saveImmediately: false);
        txtCurrentGuid.Text = newGuid;
        lstGuidHistory.ItemsSource = null;
        lstGuidHistory.ItemsSource = _guidSpoofManager.GuidHistory;
    }

    private void btnSave_Click(object sender, RoutedEventArgs e)
    {
        // Validation
        var epicUri = txtEpicLaunchUri.Text?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(epicUri) || !epicUri.StartsWith("com.epicgames.launcher://", StringComparison.OrdinalIgnoreCase))
        {
            MessageBox.Show(
                "Epic Launch URI must not be empty and must start with com.epicgames.launcher://",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(txtLaunchThreshold.Text, out int threshold) || threshold < 1)
        {
            MessageBox.Show(
                "Launch threshold must be a valid positive integer.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        if (!int.TryParse(txtMaxLogEntries.Text, out int maxLogs) || maxLogs < 1)
        {
            MessageBox.Show(
                "Maximum log entries must be a valid positive integer.",
                "Validation Error",
                MessageBoxButton.OK,
                MessageBoxImage.Warning);
            return;
        }

        // Apply settings
        _settings.StartMonitoringOnLaunch = chkStartMonitoringOnLaunch.IsChecked == true;
        _settings.StartMinimized = chkStartMinimized.IsChecked == true;
        _settings.AutoLaunchWarframe = chkAutoLaunchWarframe.IsChecked == true;
        _settings.EnableStartupAnimation = chkEnableStartupAnimation.IsChecked == true;
        _settings.EnableStartupJingle = chkEnableStartupJingle.IsChecked == true;
        _settings.EnableBackgroundMusic = chkEnableBackgroundMusic.IsChecked == true;
        _settings.BackgroundMusicVolume = sliderSettingsMusicVolume.Value;

        _settings.StartMinimizedToTray = chkStartMinimizedToTray.IsChecked == true;
        _settings.LaunchOnSystemStartup = chkLaunchOnSystemStartup.IsChecked == true;
        _settings.LaunchMinimizedOnSystemStartup = chkLaunchMinimizedOnSystemStartup.IsChecked == true;
        _settings.CloseToTray = chkCloseToTray.IsChecked == true;

        // Synchronize Windows Startup registration
        StartupManager.SetStartWithWindows(_settings.LaunchOnSystemStartup, _settings.LaunchMinimizedOnSystemStartup, _logger);

        if (cboStartupWindowMode.SelectedItem is ComboBoxItem modeItem && modeItem.Tag is string selectedMode)
        {
            _settings.StartupWindowMode = selectedMode;
        }

        _settings.EpicLaunchUri = epicUri;
        _settings.SteamAppId = txtSteamAppId.Text?.Trim() ?? "230410";
        _settings.StandaloneLauncherPath = txtStandaloneLauncherPath.Text?.Trim() ?? string.Empty;

        _settings.WarframeInstallFolder = txtWarframeFolder.Text?.Trim() ?? string.Empty;
        _settings.CacheFolder = txtCacheFolder.Text?.Trim() ?? string.Empty;
        _settings.DiscordSdkPath = txtDiscordSdkPath.Text?.Trim() ?? string.Empty;

        _settings.LaunchesBeforeAction = threshold;
        _settings.EnableCacheCleaner = chkEnableCacheCleaner.IsChecked == true;

        _settings.EnableOwHelper = chkEnableOwHelper.IsChecked == true;
        _settings.LaunchOwHelperHidden = chkLaunchOwHelperHidden.IsChecked == true;

        _settings.EnableFileLogging = chkEnableFileLogging.IsChecked == true;
        _settings.MaxLogEntries = maxLogs;

        _settings.EnableGuidSpoof = chkEnableGuidSpoof.IsChecked == true;

        _settings.Save();
        _logger.LogSuccess("Settings updated and saved successfully.");

        DialogResult = true;
        Close();
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        DialogResult = false;
        Close();
    }

    private void sliderSettingsMusicVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (txtSettingsMusicVolumeValue != null)
        {
            txtSettingsMusicVolumeValue.Text = $"{(int)e.NewValue}%";
        }
    }
}
