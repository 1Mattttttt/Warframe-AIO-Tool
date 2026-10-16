using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Media;
using System.Windows.Media.Effects;
using GameLauncher.Configuration;
using GameLauncher.Core;
using GameLauncher.Logging;

namespace GameLauncher.UI;

public class SpooferLogItem
{
    public string Timestamp { get; set; } = string.Empty;
    public string Message { get; set; } = string.Empty;
    public string MessageHexColor { get; set; } = "#CDD6F4";
}

public partial class SpooferControl : UserControl
{
    private SpooferManager? _spooferManager;
    private GuidSpoofManager? _guidSpoofManager;
    private LoggerService? _logger;
    private bool _autoSpoofCompleted;

    public SpooferControl()
    {
        InitializeComponent();
    }

    public void Initialize(SpooferManager spooferManager, LoggerService logger)
    {
        Initialize(spooferManager, new GuidSpoofManager(AppSettings.Load(), logger), logger);
    }

    public void Initialize(SpooferManager spooferManager, GuidSpoofManager guidSpoofManager, LoggerService logger)
    {
        _spooferManager = spooferManager ?? throw new ArgumentNullException(nameof(spooferManager));
        _guidSpoofManager = guidSpoofManager ?? throw new ArgumentNullException(nameof(guidSpoofManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        UpdateGuidDisplay();
        AddLogEntry("✓ Spoofer ready.", "#A6E3A1");
    }

    private void UpdateGuidDisplay()
    {
        if (_guidSpoofManager != null)
        {
            if (txtCurrentGuidDisplay != null)
            {
                txtCurrentGuidDisplay.Text = _guidSpoofManager.CurrentGuid;
            }
            if (txtCurrentComputerName != null)
            {
                txtCurrentComputerName.Text = _guidSpoofManager.CurrentComputerName;
            }
            if (txtCurrentUserName != null)
            {
                txtCurrentUserName.Text = _guidSpoofManager.CurrentUserName;
            }
        }
    }

    private void btnSpoofGuid_Click(object sender, RoutedEventArgs e)
    {
        if (_guidSpoofManager == null) return;

        string newGuid = _guidSpoofManager.GenerateNewGuid();
        UpdateGuidDisplay();
        AddLogEntry($"🎲 New MachineGUID: {newGuid}", "#89B4FA");
        UpdateStatusText("✔ MachineGUID updated");
    }

    private void btnRandomizeComputerName_Click(object sender, RoutedEventArgs e)
    {
        if (_guidSpoofManager == null) return;

        string oldName = _guidSpoofManager.CurrentComputerName;
        string newName = _guidSpoofManager.GenerateRandomComputerName();

        bool success = _guidSpoofManager.SpoofComputerName(newName);
        UpdateGuidDisplay();

        if (success)
        {
            AddLogEntry($"🎲 Hostname changed: '{oldName}' -> '{newName}' (takes effect on reboot)", "#89B4FA");
            UpdateStatusText("✔ Hostname changed");
        }
        else
        {
            AddLogEntry($"⚠ Failed to change hostname. Run as Administrator.", "#FAB387");
        }
    }

    private void btnCopyGuid_Click(object sender, RoutedEventArgs e)
    {
        if (_guidSpoofManager != null && !string.IsNullOrEmpty(_guidSpoofManager.CurrentGuid))
        {
            try
            {
                Clipboard.SetText(_guidSpoofManager.CurrentGuid);
                AddLogEntry("📋 Copied to clipboard.", "#A6E3A1");
            }
            catch (Exception ex)
            {
                AddLogEntry($"⚠ Could not copy: {ex.Message}", "#FAB387");
            }
        }
    }

    private async void btnAutoSpoof_Click(object sender, RoutedEventArgs e)
    {
        if (_spooferManager == null || _autoSpoofCompleted) return;

        Window? ownerWindow = Window.GetWindow(this);
        var confirmDialog = new AutoSpoofConfirmDialog
        {
            Owner = ownerWindow
        };

        if (confirmDialog.ShowDialog() != true)
        {
            AddLogEntry("⚠ Cancelled.", "#FAB387");
            return;
        }

        // Lock button permanently until app restart
        _autoSpoofCompleted = true;
        btnAutoSpoof.IsEnabled = false;
        txtAutoSpoofBtnTitle.Text = "Done";
        txtAutoSpoofBtnSub.Text = "finished";
        txtAutoSpoofBtnTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
        txtAutoSpoofBtnSub.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8"));

        // Activate Spoofer Red Theme Mode
        ApplyRedThemeMode();

        SetButtonsEnabled(false);
        AddLogEntry("🚨 Starting Auto Spoof...", "#F38BA8");
        UpdateStatusText("🚨 Running Auto Spoof...");

        // Stage 1: System GUID & Hostname Spoofing
        AddLogEntry("[1/5] Spoofing MachineGUID & Hostname...", "#F38BA8");
        if (_guidSpoofManager != null)
        {
            string newGuid = _guidSpoofManager.GenerateNewGuid();
            string newHost = _guidSpoofManager.GenerateRandomComputerName();
            bool hostChanged = _guidSpoofManager.SpoofComputerName(newHost);
            UpdateGuidDisplay();
            AddLogEntry($"✓ [1/5] New MachineGUID: {newGuid}", "#A6E3A1");
            if (hostChanged)
            {
                AddLogEntry($"✓ [1/5] New Hostname: {newHost}", "#A6E3A1");
            }
        }
        else
        {
            AddLogEntry("⚠ [1/5] GUID Spoof Manager unavailable.", "#FAB387");
        }

        // Stage 2: NetFixer.bat
        AddLogEntry("[2/5] Running NetFixer (MAC address reset)...", "#F38BA8");
        bool step2 = await _spooferManager.RunNetFixerAsync(UpdateStatusText);
        AddLogEntry(step2 ? "✓ [2/5] NetFixer done." : "⚠ [2/5] NetFixer finished with warnings.", step2 ? "#A6E3A1" : "#FAB387");

        // Stage 3: Standby Delay 10 seconds
        AddLogEntry("⏳ [3/5] Waiting 10s for network to settle...", "#89B4FA");
        UpdateStatusText("⏳ Waiting 10s...");
        for (int i = 10; i > 0; i--)
        {
            await Task.Delay(1000);
            UpdateStatusText($"⏳ Waiting {i}s...");
        }
        AddLogEntry("✓ [3/5] Ready.", "#A6E3A1");

        // Stage 4: Clean Warframe Files & Registry
        AddLogEntry("🧹 [4/5] Cleaning Warframe files, account hashes & H.Misc cache...", "#E78284");
        UpdateStatusText("🧹 Cleaning files & cache...");
        await Task.Run(() =>
        {
            try
            {
                if (_logger != null)
                {
                    WarframeCleaner.CleanWarframeFiles(_logger, AppSettings.Load());
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Auto Spoof cleanup exception: {ex.Message}");
            }
        });
        AddLogEntry("✓ [4/5] Files, cache and registry keys cleaned.", "#A6E3A1");

        // Stage 5: Network Settings.bat
        AddLogEntry("[5/5] Running Network Settings (DNS & IP reset)...", "#F38BA8");
        bool step5 = await _spooferManager.RunNetworkSettingsAsync(UpdateStatusText);
        AddLogEntry(step5 ? "✓ [5/5] Network Settings done." : "⚠ [5/5] Network Settings finished with warnings.", step5 ? "#A6E3A1" : "#FAB387");

        AddLogEntry("✔ Auto Spoof finished successfully.", "#A6E3A1");
        UpdateStatusText("✔ Auto Spoof done.");

        SetButtonsEnabled(true);
    }

    private void ApplyRedThemeMode()
    {
        Dispatcher.InvokeAsync(() =>
        {
            var redBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#F38BA8"));

            txtSpooferIcon.Text = "🚨";
            txtSpooferTitle.Foreground = redBrush;
            glowSpooferTitle.Color = (Color)ColorConverter.ConvertFromString("#F38BA8");

            badgeSpooferModule.Background = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#3B1822"));
            badgeSpooferModule.BorderBrush = redBrush;
            txtSpooferBadgeText.Foreground = redBrush;
            txtSpooferBadgeText.Text = "SPOOFED";

            cardAutoSpoofBanner.BorderBrush = redBrush;
            cardModuleControls.BorderBrush = redBrush;
            cardLogConsole.BorderBrush = redBrush;
            txtLogHeader.Foreground = redBrush;
        });
    }

    private async void btnCleanWarframeFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_logger == null) return;

        SetButtonsEnabled(false);
        AddLogEntry("⏳ Cleaning Warframe files, cache and registry...", "#E78284");
        UpdateStatusText("⏳ Cleaning files & cache...");

        await Task.Run(() =>
        {
            try
            {
                WarframeCleaner.CleanWarframeFiles(_logger, AppSettings.Load());
            }
            catch (Exception ex)
            {
                _logger.LogError($"Warframe cleanup exception: {ex.Message}");
            }
        });

        AddLogEntry("✓ Warframe files, cache and registry cleaned.", "#A6E3A1");
        UpdateStatusText("✔ Cleaned.");
        SetButtonsEnabled(true);
    }

    private async void btnRunNetFixer_Click(object sender, RoutedEventArgs e)
    {
        if (_spooferManager == null) return;

        SetButtonsEnabled(false);
        AddLogEntry("⏳ Running NetFixer...", "#89B4FA");

        bool success = await _spooferManager.RunNetFixerAsync(UpdateStatusText);

        if (success)
        {
            AddLogEntry("✓ NetFixer done.", "#A6E3A1");
        }
        else
        {
            AddLogEntry("⚠ NetFixer finished with warnings or was cancelled.", "#FAB387");
        }

        SetButtonsEnabled(true);
    }

    private async void btnRunNetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_spooferManager == null) return;

        SetButtonsEnabled(false);
        AddLogEntry("⏳ Running Network Settings...", "#89B4FA");

        bool success = await _spooferManager.RunNetworkSettingsAsync(UpdateStatusText);

        if (success)
        {
            AddLogEntry("✓ Network Settings done.", "#A6E3A1");
        }
        else
        {
            AddLogEntry("⚠ Network Settings finished with warnings or was cancelled.", "#FAB387");
        }

        SetButtonsEnabled(true);
    }

    private void btnRunDynamicIpChanger_Click(object sender, RoutedEventArgs e)
    {
        Window? ownerWindow = Window.GetWindow(this);

        var dialog = new DynamicIpChangerDialog
        {
            Owner = ownerWindow
        };

        AddLogEntry("ℹ Opened Dynamic IP instructions.", "#89B4FA");
        dialog.ShowDialog();
    }

    private void btnSpooferTutorial_Click(object sender, RoutedEventArgs e)
    {
        Window? ownerWindow = Window.GetWindow(this);

        var dialog = new SpooferTutorialDialog
        {
            Owner = ownerWindow
        };

        AddLogEntry("ℹ Opened Tutorial.", "#F5C2E7");
        dialog.ShowDialog();
    }

    private void SetButtonsEnabled(bool isEnabled)
    {
        btnAutoSpoof.IsEnabled = !_autoSpoofCompleted && isEnabled;
        btnSpoofGuid.IsEnabled = isEnabled;
        btnRandomizeComputerName.IsEnabled = isEnabled;
        btnCleanWarframeFiles.IsEnabled = isEnabled;
        btnRunNetFixer.IsEnabled = isEnabled;
        btnRunNetworkSettings.IsEnabled = isEnabled;
        btnRunDynamicIpChanger.IsEnabled = isEnabled;
        btnSpooferTutorial.IsEnabled = isEnabled;
    }

    private void UpdateStatusText(string statusMessage)
    {
        Dispatcher.InvokeAsync(() =>
        {
            txtSpooferStatus.Text = statusMessage;

            string color = "#CDD6F4";
            if (statusMessage.StartsWith("✔") || statusMessage.StartsWith("✓"))
            {
                color = "#A6E3A1";
            }
            else if (statusMessage.StartsWith("❌") || statusMessage.StartsWith("🚨"))
            {
                color = "#F38BA8";
            }
            else if (statusMessage.StartsWith("⚠"))
            {
                color = "#FAB387";
            }
            else if (statusMessage.StartsWith("⚡") || statusMessage.StartsWith("⏳"))
            {
                color = "#89B4FA";
            }

            txtSpooferStatus.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(color));
            AddLogEntry(statusMessage, color);
        });
    }

    private void AddLogEntry(string message, string hexColor = "#CDD6F4")
    {
        Dispatcher.InvokeAsync(() =>
        {
            var item = new SpooferLogItem
            {
                Timestamp = $"[{DateTime.Now:HH:mm:ss}]",
                Message = message,
                MessageHexColor = hexColor
            };

            lstSpooferLogs.Items.Add(item);

            while (lstSpooferLogs.Items.Count > 100)
            {
                lstSpooferLogs.Items.RemoveAt(0);
            }

            if (lstSpooferLogs.Items.Count > 0)
            {
                lstSpooferLogs.ScrollIntoView(lstSpooferLogs.Items[^1]);
            }
        });
    }
}
