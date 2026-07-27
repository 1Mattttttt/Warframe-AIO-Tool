using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using UserControl = System.Windows.Controls.UserControl;
using System.Windows.Media;
using System.Windows.Media.Effects;
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
    private LoggerService? _logger;
    private bool _autoSpoofCompleted;

    public SpooferControl()
    {
        InitializeComponent();
    }

    public void Initialize(SpooferManager spooferManager, LoggerService logger)
    {
        _spooferManager = spooferManager ?? throw new ArgumentNullException(nameof(spooferManager));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));

        AddLogEntry("✓ Spoofer module initialized successfully.", "#A6E3A1");
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

        // Stage 1: Run Spoofer (PSWOA)
        AddLogEntry("[1/5] Executing Run Spoofer (PSWOA)...", "#F38BA8");
        bool step1 = await _spooferManager.RunMainSpooferAsync(UpdateStatusText);
        AddLogEntry(step1 ? "✓ [1/5] Spoofer completed successfully." : "⚠ [1/5] Spoofer finished with warnings.", step1 ? "#A6E3A1" : "#FAB387");

        // Stage 2: NetFixer.bat
        AddLogEntry("[2/5] Executing NetFixer.bat...", "#F38BA8");
        bool step2 = await _spooferManager.RunNetFixerAsync(UpdateStatusText);
        AddLogEntry(step2 ? "✓ [2/5] NetFixer completed." : "⚠ [2/5] NetFixer finished with warnings.", step2 ? "#A6E3A1" : "#FAB387");

        // Stage 3: Standby Delay 10 seconds
        AddLogEntry("⏳ [3/5] Waiting 10 seconds before cache cleanup...", "#89B4FA");
        UpdateStatusText("⏳ Waiting 10s before cache cleanup...");
        for (int i = 10; i > 0; i--)
        {
            await Task.Delay(1000);
            UpdateStatusText($"⏳ Waiting {i}s before cache cleanup...");
        }
        AddLogEntry("✓ [3/5] Standby period finished.", "#A6E3A1");

        // Stage 4: Clean Warframe Files
        AddLogEntry("🧹 [4/5] Cleaning Warframe temporary cache files...", "#E78284");
        UpdateStatusText("🧹 Cleaning Warframe files...");
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
                _logger?.LogError($"Auto Spoof cache cleanup exception: {ex.Message}");
            }
        });
        AddLogEntry("✓ [4/5] Warframe cache files cleaned.", "#A6E3A1");

        // Stage 5: Network Settings.bat
        AddLogEntry("[5/5] Executing Network Settings.bat...", "#F38BA8");
        bool step5 = await _spooferManager.RunNetworkSettingsAsync(UpdateStatusText);
        AddLogEntry(step5 ? "✓ [5/5] Network Settings completed." : "⚠ [5/5] Network Settings finished with warnings.", step5 ? "#A6E3A1" : "#FAB387");

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
