using System;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using GameLauncher.Configuration;
using GameLauncher.Core;
using GameLauncher.Logging;

namespace GameLauncher.UI;

public partial class AccountVaultControl : UserControl
{
    private AppSettings? _settings;
    private LoggerService? _logger;
    private AccountVaultManager? _vaultManager;
    private GuidSpoofManager? _guidSpoofer;

    public event EventHandler<string>? AccountSwitched;

    public AccountVaultControl()
    {
        InitializeComponent();
    }

    public void Initialize(AppSettings settings, LoggerService logger, AccountVaultManager vaultManager, GuidSpoofManager guidSpoofer)
    {
        _settings = settings;
        _logger = logger;
        _vaultManager = vaultManager;
        _guidSpoofer = guidSpoofer;

        UpdateUi();
    }

    public void UpdateUi()
    {
        if (_settings == null || _vaultManager == null || _guidSpoofer == null) return;

        // Auto-Spoof button state
        bool autoSpoof = _settings.EnableAccountVaultAutoSpoof;
        btnToggleAutoSpoof.Content = autoSpoof ? "✔ ENABLED" : "✖ DISABLED";
        btnToggleAutoSpoof.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString(autoSpoof ? "#A6E3A1" : "#F38BA8"));

        // Current active session
        string? activeHash = _vaultManager.DetectCurrentWarframeAccountHash();
        string activeProfile = _settings.ActiveAccountVaultProfile;

        if (!string.IsNullOrEmpty(activeProfile))
        {
            txtCurrentActiveAccount.Text = $"Active Profile: {activeProfile}";
            txtCurrentActiveAccount.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6E3A1"));
        }
        else if (!string.IsNullOrEmpty(activeHash))
        {
            txtCurrentActiveAccount.Text = "Unsaved Live Session";
            txtCurrentActiveAccount.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAB387"));
        }
        else
        {
            txtCurrentActiveAccount.Text = "No Active Session";
            txtCurrentActiveAccount.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#6C7086"));
        }

        txtCurrentHashInfo.Text = !string.IsNullOrEmpty(activeHash)
            ? $"Local Hash: {activeHash}"
            : "No 'settings' binary found in %LOCALAPPDATA%\\Warframe";

        txtCurrentGuidInfo.Text = $"Current Machine GUID: {_guidSpoofer.CurrentGuid}";

        // Profiles list
        var profiles = _vaultManager.Profiles;
        itemsProfiles.ItemsSource = null;
        itemsProfiles.ItemsSource = profiles;

        txtProfilesCount.Text = $"{profiles.Count} Account(s) in Vault";
        txtEmptyVault.Visibility = profiles.Count == 0 ? Visibility.Visible : Visibility.Collapsed;
    }

    private void btnToggleAutoSpoof_Click(object sender, RoutedEventArgs e)
    {
        if (_settings == null) return;
        _settings.EnableAccountVaultAutoSpoof = !_settings.EnableAccountVaultAutoSpoof;
        _settings.Save();
        UpdateUi();
        _logger?.LogInfo($"Auto-Spoof on Account Switch: {(_settings.EnableAccountVaultAutoSpoof ? "ENABLED" : "DISABLED")}");
    }

    private void btnSaveCurrentSession_Click(object sender, RoutedEventArgs e)
    {
        if (_vaultManager == null) return;

        string name = txtNewProfileName.Text.Trim();
        if (string.IsNullOrEmpty(name))
        {
            MessageBox.Show("Please enter a name for the account profile to save.", "Warning", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        bool ok = _vaultManager.SaveCurrentSessionToVault(name);
        if (ok)
        {
            txtNewProfileName.Text = string.Empty;
            UpdateUi();
            MessageBox.Show($"Account '{name}' has been successfully stored in the Vault!", "Account Vault", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show("Unable to save session. Make sure you have launched Warframe at least once to create local data.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void btnSwitchAccount_Click(object sender, RoutedEventArgs e)
    {
        if (_vaultManager == null || _settings == null || sender is not Button btn) return;
        string? profileName = btn.Tag as string;
        if (string.IsNullOrEmpty(profileName)) return;

        bool autoSpoof = _settings.EnableAccountVaultAutoSpoof;
        string confirmMsg = autoSpoof
            ? $"Do you want to switch to account '{profileName}'?\n\nWarframe AIO Tool will:\n1. Wipe telemetry & lingering account tokens;\n2. Generate and apply a new Machine GUID;\n3. Randomize physical NIC MAC addresses via Spoofer module;\n4. Deploy '{profileName}' profile data."
            : $"Do you want to switch to account '{profileName}' without Auto-Spoof?";

        var res = MessageBox.Show(confirmMsg, "Confirm Account Switch", MessageBoxButton.YesNo, MessageBoxImage.Question);
        if (res != MessageBoxResult.Yes) return;

        borderVaultProgress.Visibility = Visibility.Visible;
        txtVaultProgress.Text = $"Starting secure transition to '{profileName}'...";

        btn.IsEnabled = false;

        try
        {
            bool ok = await _vaultManager.SwitchAccountAsync(profileName, autoSpoof, status =>
            {
                Dispatcher.Invoke(() => txtVaultProgress.Text = status);
            });

            if (ok)
            {
                UpdateUi();
                AccountSwitched?.Invoke(this, profileName);
                MessageBox.Show($"You are now on account '{profileName}' with complete isolation!", "Account Switched", MessageBoxButton.OK, MessageBoxImage.Information);
            }
            else
            {
                MessageBox.Show($"Failed to switch to '{profileName}'. Check activity logs for details.", "Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        }
        finally
        {
            btn.IsEnabled = true;
            borderVaultProgress.Visibility = Visibility.Collapsed;
        }
    }

    private void btnDeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        if (_vaultManager == null || sender is not Button btn) return;
        string? profileName = btn.Tag as string;
        if (string.IsNullOrEmpty(profileName)) return;

        var res = MessageBox.Show($"Are you sure you want to delete account '{profileName}' from the vault?", "Confirm Deletion", MessageBoxButton.YesNo, MessageBoxImage.Warning);
        if (res == MessageBoxResult.Yes)
        {
            _vaultManager.DeleteProfile(profileName);
            UpdateUi();
        }
    }
}
