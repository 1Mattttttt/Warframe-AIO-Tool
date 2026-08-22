using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Windows;
using System.Windows.Controls;
using GameLauncher.Configuration;
using GameLauncher.Core;
using GameLauncher.Logging;
using GameLauncher.Models;

namespace GameLauncher.UI;

public partial class WarframeUpdaterView : UserControl
{
    private AppSettings? _settings;
    private LoggerService? _logger;
    private WarframeUpdateManager? _updateManager;

    public WarframeUpdaterView()
    {
        InitializeComponent();
    }

    public void Initialize(AppSettings settings, LoggerService logger, WarframeUpdateManager updateManager)
    {
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));

        txtGameFolderPath.Text = _settings.WarframeInstallFolder;
        SelectComboByTag(cboLanguage, _settings.WarframeLanguage);
        SelectComboByTag(cboGraphicsApi, _settings.WarframeGraphicsApi);

        lblStatus.Text = "Manifest not loaded";
        lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinPeach") ?? System.Windows.Media.Brushes.Orange;

        _updateManager.ManifestLoaded += OnManifestLoaded;
        _updateManager.VerificationCompleted += OnVerificationCompleted;
        _updateManager.ProgressUpdated += OnProgressUpdated;

        _logger.LogReceived += (sender, entry) =>
        {
            Dispatcher.Invoke(() =>
            {
                if (txtLogConsole != null)
                {
                    txtLogConsole.AppendText(entry.ToString() + Environment.NewLine);
                    if (txtLogConsole.Text.Length > 200000)
                    {
                        txtLogConsole.Text = txtLogConsole.Text.Substring(txtLogConsole.Text.Length - 100000);
                    }
                    txtLogConsole.ScrollToEnd();
                }
            });
        };

        if (_settings.AutoCheckForUpdates)
        {
            Dispatcher.InvokeAsync(async () =>
            {
                await _updateManager.LoadManifestAsync();
                await _updateManager.InspectLocalInstallationAsync(verifyHashes: false);
            });
        }
    }

    private void btnCopyLogs_Click(object sender, RoutedEventArgs e)
    {
        if (txtLogConsole != null && !string.IsNullOrEmpty(txtLogConsole.Text))
        {
            Clipboard.SetText(txtLogConsole.Text);
            _logger?.LogSuccess("[LOGS] Execution logs copied to clipboard.");
        }
    }

    private void btnClearLogs_Click(object sender, RoutedEventArgs e)
    {
        if (txtLogConsole != null)
        {
            txtLogConsole.Clear();
            _logger?.LogInfo("[LOGS] Log console cleared.");
        }
    }

    private void SelectComboByTag(ComboBox combo, string value)
    {
        foreach (ComboBoxItem item in combo.Items)
        {
            if (string.Equals(item.Tag?.ToString(), value, StringComparison.OrdinalIgnoreCase))
            {
                combo.SelectedItem = item;
                break;
            }
        }
    }

    private void cboLanguage_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings != null && cboLanguage.SelectedItem is ComboBoxItem item && item.Tag is string lang)
        {
            _settings.WarframeLanguage = lang;
            _settings.Save();
            _logger?.LogInfo($"[SETTINGS] Language changed to: {lang}");
        }
    }

    private void cboGraphicsApi_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_settings != null && cboGraphicsApi.SelectedItem is ComboBoxItem item && item.Tag is string api)
        {
            _settings.WarframeGraphicsApi = api;
            _settings.Save();
            _logger?.LogInfo($"[SETTINGS] Graphics API changed to: {api}");
        }
    }

    private bool CheckSafetyLock()
    {
        if (_updateManager != null && _updateManager.SafetyAuditLockEnabled)
        {
            lblStatus.Text = "UPDATE DISABLED — SAFETY AUDIT REQUIRED";
            lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinRed") ?? System.Windows.Media.Brushes.Red;
            _logger?.LogWarning("[SAFETY LOCK] UPDATE DISABLED — SAFETY AUDIT REQUIRED");
            return true;
        }
        return false;
    }

    private void btnBrowseFolder_Click(object sender, RoutedEventArgs e)
    {
        var dialog = new Microsoft.Win32.OpenFolderDialog
        {
            Title = "Select Warframe Installation Folder",
            InitialDirectory = _settings?.WarframeInstallFolder ?? @"C:\Program Files\Epic Games\Warframe"
        };

        if (dialog.ShowDialog() == true)
        {
            string selectedFolder = dialog.FolderName;
            txtGameFolderPath.Text = selectedFolder;
            if (_settings != null)
            {
                _settings.WarframeInstallFolder = selectedFolder;
                _settings.Save();
            }

            _logger?.LogInfo($"[SETTINGS] Install path updated: {selectedFolder}");
            btnLoadManifest_Click(sender, e);
        }
    }

    private async void btnLoadManifest_Click(object sender, RoutedEventArgs e)
    {
        if (_updateManager == null) return;

        lblStatus.Text = "Loading manifest...";
        lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinBlue") ?? System.Windows.Media.Brushes.Cyan;

        SetBusyState(true);
        var manifest = await _updateManager.LoadManifestAsync();
        SetBusyState(false);

        if (manifest != null && manifest.TotalFiles > 0)
        {
            lblStatus.Text = $"Manifest loaded ({manifest.TotalFiles} files)";
            lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinGreen") ?? System.Windows.Media.Brushes.LightGreen;
            await _updateManager.InspectLocalInstallationAsync(verifyHashes: false);
        }
        else
        {
            lblStatus.Text = "Manifest unavailable";
            lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinPeach") ?? System.Windows.Media.Brushes.Orange;
        }
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        _updateManager?.CancelActiveOperation();
        lblStatus.Text = "Operation cancelled";
    }

    private async void btnUpdateExe_Click(object sender, RoutedEventArgs e)
    {
        if (CheckSafetyLock()) return;
        if (_updateManager == null) return;

        SetBusyState(true);
        await _updateManager.UpdateExeAsync();
        SetBusyState(false);
    }

    private async void btnDownloadTools_Click(object sender, RoutedEventArgs e)
    {
        if (CheckSafetyLock()) return;
        if (_updateManager == null) return;

        SetBusyState(true);
        await _updateManager.DownloadToolsAsync();
        SetBusyState(false);
    }

    private async void btnDownloadCache_Click(object sender, RoutedEventArgs e)
    {
        if (CheckSafetyLock()) return;
        if (_updateManager == null) return;

        var result = MessageBox.Show(
            "Downloading Cache files may take significant time and bandwidth (~50+ GB). Continue?",
            "Download Warframe Cache",
            MessageBoxButton.YesNo,
            MessageBoxImage.Question);

        if (result == MessageBoxResult.Yes)
        {
            SetBusyState(true);
            await _updateManager.DownloadCacheAsync();
            SetBusyState(false);
        }
    }

    private async void btnContentUpdate_Click(object sender, RoutedEventArgs e)
    {
        if (CheckSafetyLock()) return;
        if (_updateManager == null) return;

        SetBusyState(true);
        await _updateManager.PerformContentUpdateAsync();
        SetBusyState(false);
    }

    private async void btnRepairCache_Click(object sender, RoutedEventArgs e)
    {
        if (CheckSafetyLock()) return;
        if (_updateManager == null) return;

        SetBusyState(true);
        await _updateManager.PerformRepairCacheAsync();
        SetBusyState(false);
    }

    private void btnOptimize_Click(object sender, RoutedEventArgs e)
    {
        if (CheckSafetyLock()) return;
        if (_updateManager == null) return;

        var driveType = _updateManager.GetStorageDriveType();
        string driveExplanation = driveType switch
        {
            DriveTypeInfo.SSD => "Target drive is an SSD. Safe game-file optimization without disk defragmentation.",
            DriveTypeInfo.HDD => "Target drive is an HDD. Cache defragmentation via Warframe engine applet.",
            _ => "Executing Warframe cache optimization."
        };

        var result = MessageBox.Show(
            $"{driveExplanation}\n\nDo you want to proceed with cache optimization?",
            "Warframe Cache Optimization",
            MessageBoxButton.YesNo,
            MessageBoxImage.Information);

        if (result == MessageBoxResult.Yes)
        {
            _updateManager.LaunchAppletDefrag();
        }
    }

    private void btnLaunchGame_Click(object sender, RoutedEventArgs e)
    {
        _updateManager?.LaunchGame();
    }

    private async void btnAnalyzeInstallation_Click(object sender, RoutedEventArgs e)
    {
        if (_updateManager == null) return;

        lblStatus.Text = "Analyzing installation (Read-only)...";
        lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinMauve") ?? System.Windows.Media.Brushes.Magenta;

        SetBusyState(true);
        string reportPath = await _updateManager.AnalyzeInstallationAsync();
        SetBusyState(false);

        lblStatus.Text = "Analysis complete. Report saved to file.";
        lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinGreen") ?? System.Windows.Media.Brushes.LightGreen;

        if (!string.IsNullOrEmpty(reportPath) && File.Exists(reportPath))
        {
            MessageBox.Show($"Pre-flight audit report generated successfully:\n\n{reportPath}", "Analysis Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
    }

    private async void btnTestSingleFile_Click(object sender, RoutedEventArgs e)
    {
        if (CheckSafetyLock()) return;

        if (_updateManager?.CurrentManifest == null || _settings == null || _logger == null)
        {
            MessageBox.Show("Load manifest prior to executing tests.", "Manifest Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var entry = _updateManager.CurrentManifest.Entries.FirstOrDefault(x => x.RelativePath.EndsWith("Warframe.x64.exe", StringComparison.OrdinalIgnoreCase))
                    ?? _updateManager.CurrentManifest.Entries.FirstOrDefault(x => !x.RelativePath.EndsWith(".toc", StringComparison.OrdinalIgnoreCase));

        if (entry == null)
        {
            MessageBox.Show("No valid entry found in manifest for testing.", "Test Aborted", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetBusyState(true);
        var tester = new WarframeSingleFileTester(_settings, _logger);
        var result = await tester.ExecuteSingleFileTestAsync(entry);
        SetBusyState(false);

        if (result.Success)
        {
            MessageBox.Show($"SINGLE FILE UPDATE TEST PASSED!\n\nTarget: {result.TargetRelativePath}\nReport saved to file:\n{result.LogPath}", "Test Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"SINGLE FILE UPDATE TEST FAILED:\n\n{result.Message}", "Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private async void btnTestMultiFile_Click(object sender, RoutedEventArgs e)
    {
        if (CheckSafetyLock()) return;

        if (_updateManager?.CurrentManifest == null || _settings == null || _logger == null)
        {
            MessageBox.Show("Load manifest prior to executing tests.", "Manifest Required", MessageBoxButton.OK, MessageBoxImage.Warning);
            return;
        }

        var testEntries = _updateManager.CurrentManifest.Entries
            .Where(x => !x.RelativePath.EndsWith(".toc", StringComparison.OrdinalIgnoreCase) && x.Status != FileStatus.DynamicRebuildable)
            .Take(2)
            .ToList();

        if (testEntries.Count == 0)
        {
            MessageBox.Show("No valid entries found for multi-file test.", "Test Aborted", MessageBoxButton.OK, MessageBoxImage.Error);
            return;
        }

        SetBusyState(true);
        WarframeMultiFileTester.SetManifestForAudit(_updateManager.CurrentManifest);
        var tester = new WarframeMultiFileTester(_settings, _logger);
        var session = await tester.ExecuteMultiFileTestAsync(testEntries);
        SetBusyState(false);

        if (session.Success)
        {
            MessageBox.Show($"MULTI-FILE UPDATE TEST PASSED!\n\nFiles: {session.Items.Count}\nReport saved to file:\n{session.LogPath}", "Test Complete", MessageBoxButton.OK, MessageBoxImage.Information);
        }
        else
        {
            MessageBox.Show($"MULTI-FILE UPDATE TEST FAILED:\n\n{session.Message}", "Test Failed", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private void OnManifestLoaded(object? sender, WarframeManifest? manifest)
    {
        Dispatcher.Invoke(() =>
        {
            if (manifest != null && manifest.TotalFiles > 0)
            {
                lblStatus.Text = $"Manifest loaded ({manifest.TotalFiles} files)";
                lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinGreen") ?? System.Windows.Media.Brushes.LightGreen;
            }
            else
            {
                lblStatus.Text = "Manifest unavailable";
                lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinPeach") ?? System.Windows.Media.Brushes.Orange;
            }
        });
    }

    private void OnVerificationCompleted(object? sender, VerificationSummary? summary)
    {
        Dispatcher.Invoke(() =>
        {
            if (summary != null)
            {
                int requireUpdate = summary.MissingFiles + summary.OutdatedFiles + summary.CorruptedFiles;
                if (requireUpdate == 0)
                {
                    lblStatus.Text = "Installation verified: Ready to launch";
                    lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinGreen") ?? System.Windows.Media.Brushes.LightGreen;
                }
                else
                {
                    lblStatus.Text = $"Installation verified: {requireUpdate} files require update";
                    lblStatus.Foreground = (System.Windows.Media.Brush?)TryFindResource("CatppuccinPeach") ?? System.Windows.Media.Brushes.Orange;
                }
            }
        });
    }

    private void OnProgressUpdated(object? sender, DownloadProgressEventArgs e)
    {
        Dispatcher.Invoke(() =>
        {
            if (borderProgressArea.Visibility != Visibility.Visible)
            {
                borderProgressArea.Visibility = Visibility.Visible;
            }

            txtProgressMessage.Text = e.Message;
            progressBar.Value = Math.Clamp(e.Percent, 0, 100);

            if (e.TotalBytes > 0)
            {
                double speedKb = e.SpeedBytesPerSec / 1024.0;
                string speedText = speedKb > 1024 ? $"{speedKb / 1024.0:F2} MB/s" : $"{speedKb:F1} KB/s";
                txtProgressDetails.Text = $"{WarframeManifestEntry.FormatBytes(e.BytesReceived)} / {WarframeManifestEntry.FormatBytes(e.TotalBytes)} ({speedText})";
            }
            else
            {
                txtProgressDetails.Text = $"{WarframeManifestEntry.FormatBytes(e.BytesReceived)} downloaded";
            }
        });
    }

    private void SetBusyState(bool busy)
    {
        btnCancel.IsEnabled = busy;
        btnLoadManifest.IsEnabled = !busy;
        btnUpdateExe.IsEnabled = !busy;
        btnDownloadTools.IsEnabled = !busy;
        btnDownloadCache.IsEnabled = !busy;
        btnContentUpdate.IsEnabled = !busy;
        btnRepairCache.IsEnabled = !busy;
        btnOptimize.IsEnabled = !busy;
        btnBrowseFolder.IsEnabled = !busy;
        btnAnalyzeInstallation.IsEnabled = !busy;
        btnTestSingleFile.IsEnabled = !busy;
        btnTestMultiFile.IsEnabled = !busy;

        if (!busy)
        {
            borderProgressArea.Visibility = Visibility.Collapsed;
        }
    }
}
