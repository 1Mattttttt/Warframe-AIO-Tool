using System;
using System.Drawing;
using System.IO;
using System.Reflection;
using System.Windows;
using System.Windows.Forms;
using GameLauncher.Logging;
using Application = System.Windows.Application;

namespace GameLauncher.Core;

public class SystemTrayManager : IDisposable
{
    private readonly Window _targetWindow;
    private readonly LoggerService _logger;
    private readonly AmbientMusicManager? _musicManager;
    private NotifyIcon? _notifyIcon;
    private bool _disposed;

    public event Action? OnExitRequested;

    public SystemTrayManager(Window targetWindow, LoggerService logger, AmbientMusicManager? musicManager = null)
    {
        _targetWindow = targetWindow ?? throw new ArgumentNullException(nameof(targetWindow));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _musicManager = musicManager;

        InitializeTrayIcon();
    }

    private void InitializeTrayIcon()
    {
        try
        {
            _notifyIcon = new NotifyIcon
            {
                Text = "Warframe AIO Tool",
                Visible = true
            };

            // Extract icon from executable or use SystemIcons.Application
            Icon? appIcon = null;
            try
            {
                string? exePath = Environment.ProcessPath;
                if (!string.IsNullOrEmpty(exePath) && File.Exists(exePath))
                {
                    appIcon = Icon.ExtractAssociatedIcon(exePath);
                }
            }
            catch { }

            _notifyIcon.Icon = appIcon ?? SystemIcons.Application;

            // Context Menu
            var contextMenu = new ContextMenuStrip();

            var itemOpen = new ToolStripMenuItem("Open Warframe AIO Tool", null, (s, e) => RestoreWindow());
            itemOpen.Font = new Font(itemOpen.Font, System.Drawing.FontStyle.Bold);
            contextMenu.Items.Add(itemOpen);

            var itemToggleMusic = new ToolStripMenuItem("Pause/Resume Music", null, (s, e) => ToggleMusic());
            contextMenu.Items.Add(itemToggleMusic);

            var itemHide = new ToolStripMenuItem("Hide Warframe AIO Tool", null, (s, e) => HideWindow());
            contextMenu.Items.Add(itemHide);

            contextMenu.Items.Add(new ToolStripSeparator());

            var itemExit = new ToolStripMenuItem("Exit Warframe AIO Tool", null, (s, e) => ExitApplication());
            contextMenu.Items.Add(itemExit);

            _notifyIcon.ContextMenuStrip = contextMenu;

            // Double Click
            _notifyIcon.DoubleClick += (s, e) => RestoreWindow();

            _logger.LogInfo("System Tray Manager initialized successfully with Music integration.");
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to initialize System Tray Icon: {ex.Message}");
        }
    }

    public void RestoreWindow()
    {
        _targetWindow.Dispatcher.Invoke(() =>
        {
            if (_targetWindow.Visibility != Visibility.Visible)
            {
                _targetWindow.Show();
            }

            if (_targetWindow.WindowState == WindowState.Minimized)
            {
                _targetWindow.WindowState = WindowState.Normal;
            }

            _targetWindow.Activate();
            _targetWindow.Focus();

            _musicManager?.Resume();
        });
    }

    public void HideWindow()
    {
        _targetWindow.Dispatcher.Invoke(() =>
        {
            _targetWindow.Hide();
            _musicManager?.Pause();
        });
    }

    public void ToggleMusic()
    {
        _musicManager?.TogglePlayPause();
    }

    public void ShowTrayNotification(string title, string message, ToolTipIcon icon = ToolTipIcon.Info)
    {
        try
        {
            if (_notifyIcon != null && _notifyIcon.Visible)
            {
                _notifyIcon.ShowBalloonTip(3000, title, message, icon);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError($"Failed to show tray notification: {ex.Message}");
        }
    }

    public void ExitApplication()
    {
        OnExitRequested?.Invoke();
        Dispose();
        Application.Current.Shutdown();
    }

    public void Dispose()
    {
        if (!_disposed)
        {
            if (_notifyIcon != null)
            {
                _notifyIcon.Visible = false;
                _notifyIcon.Dispose();
                _notifyIcon = null;
            }
            _disposed = true;
        }
    }
}
