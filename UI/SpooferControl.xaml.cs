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
        AddLogEntry("✓ Spoofer module initialized successfully.", "#A6E3A1");
    }

    private void UpdateGuidDisplay()
    {
        if (_guidSpoofManager != null && txtCurrentGuidDisplay != null)
        {
            txtCurrentGuidDisplay.Text = _guidSpoofManager.CurrentGuid;
        }
    }

    private void btnSpoofGuid_Click(object sender, RoutedEventArgs e)
    {
        if (_guidSpoofManager == null) return;

        string newGuid = _guidSpoofManager.GenerateNewGuid();
        UpdateGuidDisplay();
        AddLogEntry($"🎲 New System MachineGUID Spoofed:\n{newGuid}", "#89B4FA");
        UpdateStatusText("✔ System MachineGUID Spoofed");
    }

    private void btnCopyGuid_Click(object sender, RoutedEventArgs e)
    {
        if (_guidSpoofManager != null && !string.IsNullOrEmpty(_guidSpoofManager.CurrentGuid))
        {
            try
            {
                Clipboard.SetText(_guidSpoofManager.CurrentGuid);
                AddLogEntry("📋 GUID copied to clipboard.", "#A6E3A1");
            }
            catch (Exception ex)
            {
                AddLogEntry($"⚠ Could not copy GUID to clipboard: {ex.Message}", "#FAB387");
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
            AddLogEntry("⚠ Auto Spoof execution cancelled by user.", "#FAB387");
            return;
        }

        // Lock button permanently until app restart
        _autoSpoofCompleted = true;
        btnAutoSpoof.IsEnabled = false;
        txtAutoSpoofBtnTitle.Text = "Auto Spoof Completed";
        txtAutoSpoofBtnSub.Text = "executed";
        txtAutoSpoofBtnTitle.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#CDD6F4"));
        txtAutoSpoofBtnSub.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6ADC8"));

        // Activate Spoofer Red Theme Mode
        ApplyRedThemeMode();

        SetButtonsEnabled(false);
        AddLogEntry("🚨 Starting Auto Spoof sequence...", "#F38BA8");
        UpdateStatusText("🚨 Auto Spoof sequence active...");

        // Stage 1: System GUID Spoofing
        AddLogEntry("[1/6] Generating and Spoofing new System MachineGUID...", "#F38BA8");
        if (_guidSpoofManager != null)
        {
            string newGuid = _guidSpoofManager.GenerateNewGuid();
            UpdateGuidDisplay();
            AddLogEntry($"✓ [1/6] MachineGUID Spoofed: {newGuid}", "#A6E3A1");
        }
        else
        {
            AddLogEntry("⚠ [1/6] GUID Spoof Manager unavailable.", "#FAB387");
        }

        // Stage 2: Run Spoofer (PSWOA)
        AddLogEntry("[2/6] Executing Run Spoofer (PSWOA)...", "#F38BA8");
        bool step2 = await _spooferManager.RunMainSpooferAsync(UpdateStatusText);
        AddLogEntry(step2 ? "✓ [2/6] Spoofer completed successfully." : "⚠ [2/6] Spoofer finished with warnings.", step2 ? "#A6E3A1" : "#FAB387");

        // Stage 3: NetFixer.bat
        AddLogEntry("[3/6] Executing NetFixer.bat...", "#F38BA8");
        bool step3 = await _spooferManager.RunNetFixerAsync(UpdateStatusText);
        AddLogEntry(step3 ? "✓ [3/6] NetFixer completed." : "⚠ [3/6] NetFixer finished with warnings.", step3 ? "#A6E3A1" : "#FAB387");

        // Stage 4: Standby Delay 10 seconds
        AddLogEntry("⏳ [4/6] Waiting 10 seconds before deep file cleanup...", "#89B4FA");
        UpdateStatusText("⏳ Waiting 10s before deep file cleanup...");
        for (int i = 10; i > 0; i--)
        {
            await Task.Delay(1000);
            UpdateStatusText($"⏳ Waiting {i}s before deep file cleanup...");
        }
        AddLogEntry("✓ [4/6] Standby period finished.", "#A6E3A1");

        // Stage 5: Comprehensive Clean Warframe Files & Registry
        AddLogEntry("🧹 [5/6] Executing comprehensive Warframe file & registry deep cleanup...", "#E78284");
        UpdateStatusText("🧹 Deep cleaning Warframe files & registry...");
        await Task.Run(() =>
        {
            try
            {
                if (_logger != null)
                {
                    WarframeCleaner.CleanWarframeFiles(_logger);
                }
            }
            catch (Exception ex)
            {
                _logger?.LogError($"Auto Spoof deep cleanup exception: {ex.Message}");
            }
        });
        AddLogEntry("✓ [5/6] Warframe files & registry purged.", "#A6E3A1");

        // Stage 6: Network Settings.bat
        AddLogEntry("[6/6] Executing Network Settings.bat...", "#F38BA8");
        bool step6 = await _spooferManager.RunNetworkSettingsAsync(UpdateStatusText);
        AddLogEntry(step6 ? "✓ [6/6] Network Settings completed." : "⚠ [6/6] Network Settings finished with warnings.", step6 ? "#A6E3A1" : "#FAB387");

        AddLogEntry("✔ Auto Spoof sequence execution completed.", "#A6E3A1");
        UpdateStatusText("✔ Auto Spoof completed.");

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
            txtSpooferBadgeText.Text = "RED WARNING MODE";

            cardAutoSpoofBanner.BorderBrush = redBrush;
            cardModuleControls.BorderBrush = redBrush;
            cardLogConsole.BorderBrush = redBrush;
            txtLogHeader.Foreground = redBrush;
        });
    }

    private async void btnRunMainSpoofer_Click(object sender, RoutedEventArgs e)
    {
        if (_spooferManager == null) return;

        SetButtonsEnabled(false);
        AddLogEntry("⏳ Initiating Run Spoofer (PSWOA - run as admin to spoof.exe)...", "#89B4FA");

        bool success = await _spooferManager.RunMainSpooferAsync(UpdateStatusText);

        if (success)
        {
            AddLogEntry("✓ Spoofer started successfully.", "#A6E3A1");
        }
        else
        {
            AddLogEntry("⚠ Spoofer execution completed with warnings or was cancelled.", "#FAB387");
        }

        SetButtonsEnabled(true);
    }

    private async void btnRunNetFixer_Click(object sender, RoutedEventArgs e)
    {
        if (_spooferManager == null) return;

        SetButtonsEnabled(false);
        AddLogEntry("⏳ Initiating NetFixer.bat...", "#89B4FA");

        bool success = await _spooferManager.RunNetFixerAsync(UpdateStatusText);

        if (success)
        {
            AddLogEntry("✓ NetFixer completed successfully.", "#A6E3A1");
        }
        else
        {
            AddLogEntry("⚠ NetFixer completed with warnings or was cancelled.", "#FAB387");
        }

        SetButtonsEnabled(true);
    }

    private async void btnRunNetworkSettings_Click(object sender, RoutedEventArgs e)
    {
        if (_spooferManager == null) return;

        SetButtonsEnabled(false);
        AddLogEntry("⏳ Initiating Network Settings.bat...", "#89B4FA");

        bool success = await _spooferManager.RunNetworkSettingsAsync(UpdateStatusText);

        if (success)
        {
            AddLogEntry("✓ Network Settings completed.", "#A6E3A1");
        }
        else
        {
            AddLogEntry("⚠ Network Settings completed with warnings or was cancelled.", "#FAB387");
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

        AddLogEntry("ℹ Opened Dynamic IP Changer tutorial popup.", "#89B4FA");
        dialog.ShowDialog();
    }

    private async void btnCleanWarframeFiles_Click(object sender, RoutedEventArgs e)
    {
        if (_logger == null) return;

        SetButtonsEnabled(false);
        AddLogEntry("⏳ Initiating Warframe temporary cache file cleanup...", "#E78284");
        UpdateStatusText("⏳ Cleaning Warframe temporary files...");

        await Task.Run(() =>
        {
            try
            {
                WarframeCleaner.CleanWarframeFiles(_logger);
            }
            catch (Exception ex)
            {
                _logger.LogError($"Warframe cache cleanup exception: {ex.Message}");
            }
        });

        AddLogEntry("✓ Warframe temporary cache cleanup completed.", "#A6E3A1");
        UpdateStatusText("✔ Warframe cache cleaned.");
        SetButtonsEnabled(true);
    }

    private void btnSpooferTutorial_Click(object sender, RoutedEventArgs e)
    {
        Window? ownerWindow = Window.GetWindow(this);

        var dialog = new SpooferTutorialDialog
        {
            Owner = ownerWindow
        };

        AddLogEntry("ℹ Opened Spoofer System Interactive Tutorial.", "#F5C2E7");
        dialog.ShowDialog();
    }

    private void SetButtonsEnabled(bool isEnabled)
    {
        btnAutoSpoof.IsEnabled = !_autoSpoofCompleted && isEnabled;
        btnRunMainSpoofer.IsEnabled = isEnabled;
        btnRunNetFixer.IsEnabled = isEnabled;
        btnRunNetworkSettings.IsEnabled = isEnabled;
        btnRunDynamicIpChanger.IsEnabled = isEnabled;
        btnCleanWarframeFiles.IsEnabled = isEnabled;
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
