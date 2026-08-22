using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using GameLauncher.Configuration;
using GameLauncher.Core;
using GameLauncher.Logging;
using GameLauncher.UI;

namespace GameLauncher;

public static class BetaProgram
{
    [STAThread]
    public static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                WriteBetaCrashLog(ex, "AppDomain Unhandled Exception");
                MessageBox.Show($"Warframe Updater Beta Fatal Crash:\n\n{ex.GetType().Name}: {ex.Message}", "Warframe Updater Beta Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            WriteBetaCrashLog(e.Exception, "TaskScheduler Unobserved Exception");
            e.SetObserved();
        };

        try
        {
            RunBetaApplication();
        }
        catch (Exception ex)
        {
            WriteBetaCrashLog(ex, "Main Application Catch");
            MessageBox.Show($"Warframe Updater Beta Startup Crash:\n\n{ex.GetType().Name}: {ex.Message}", "Warframe Updater Beta Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void RunBetaApplication()
    {
        var settings = AppSettings.Load();
        var logger = new LoggerService();
        var updateManager = new WarframeUpdateManager(settings, logger);

        logger.LogReceived += (sender, entry) =>
        {
            var formatted = entry.ToString();
            Debug.WriteLine(formatted);
            Trace.WriteLine(formatted);
        };

        var app = new Application();
        app.DispatcherUnhandledException += (sender, e) =>
        {
            WriteBetaCrashLog(e.Exception, "WPF Dispatcher Unhandled Exception");
        };

        var betaWindow = new BetaWindow(settings, logger, updateManager);
        app.Run(betaWindow);
    }

    private static void WriteBetaCrashLog(Exception ex, string context = "Startup")
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(localAppData, "GameLauncher", "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "beta_crash.log");

            var crashReport = $"=== Warframe Updater Beta Crash [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] ===\n" +
                              $"Exception Type: {ex.GetType().FullName}\n" +
                              $"Message: {ex.Message}\n" +
                              $"Stack Trace:\n{ex.StackTrace}\n" +
                              $"--------------------------------------------------\n\n";

            File.AppendAllText(logFile, crashReport);
        }
        catch
        {
            // Suppress crash log write failure
        }
    }
}
