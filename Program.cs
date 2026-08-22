using System;
using System.Diagnostics;
using System.IO;
using System.Windows;
using GameLauncher.Configuration;
using GameLauncher.Core;
using GameLauncher.Logging;
using GameLauncher.UI;

namespace GameLauncher;

public static class Program
{
    [STAThread]
    public static void Main()
    {
        AppDomain.CurrentDomain.UnhandledException += (sender, e) =>
        {
            if (e.ExceptionObject is Exception ex)
            {
                WriteStartupCrashLog(ex, "AppDomain Unhandled Exception");
                MessageBox.Show($"Warframe AIO Tool Startup Crash:\n\n{ex.GetType().Name}: {ex.Message}\n\nLog saved to %LocalAppData%\\GameLauncher\\logs\\startup_crash.log", "Warframe AIO Tool Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
            }
        };

        System.Threading.Tasks.TaskScheduler.UnobservedTaskException += (sender, e) =>
        {
            WriteStartupCrashLog(e.Exception, "TaskScheduler Unobserved Exception");
            e.SetObserved();
        };

        try
        {
            RunApplication();
        }
        catch (Exception ex)
        {
            WriteStartupCrashLog(ex, "Main Application Catch");
            WriteStartupThreadDebugLog(ex, "Main Application Catch");
            MessageBox.Show($"Warframe AIO Tool Startup Crash:\n\n{ex.GetType().Name}: {ex.Message}\n\nLog saved to %LocalAppData%\\GameLauncher\\logs\\startup_crash.log", "Warframe AIO Tool Fatal Error", MessageBoxButton.OK, MessageBoxImage.Error);
        }
    }

    private static void RunApplication()
    {
        var settings = AppSettings.Load();
        var logger = new LoggerService();
        var sdkManager = new DiscordSdkManager(settings, logger);
        var epicLauncher = new EpicLauncher(settings, logger);
        var steamLauncher = new SteamLauncher(settings, logger);
        var standaloneLauncher = new StandaloneLauncher(settings, logger);
        var launcherManager = new LauncherManager(settings, logger, epicLauncher, steamLauncher, standaloneLauncher);
        var updateManager = new WarframeUpdateManager(settings, logger);
        using var monitor = new WarframeMonitor(settings, logger);

        logger.LogReceived += (sender, entry) =>
        {
            var formatted = entry.ToString();
            Debug.WriteLine(formatted);
            Trace.WriteLine(formatted);
        };

        epicLauncher.InspectEpicProcessRelationship();

        // Core Event Subscriptions
        monitor.BootstrapDetected += (sender, e) =>
        {
        };

        monitor.GameLaunchDetected += (sender, e) =>
        {
            logger.LogSuccess($"[WORKFLOW] Warframe process detected. Executing SDK Killer action...");
            System.Threading.Tasks.Task.Run(() => sdkManager.RemoveSdk());
        };

        var app = new Application();
        app.DispatcherUnhandledException += (sender, e) =>
        {
            WriteSdkKillerDebugLog(e.Exception, "WPF Dispatcher Unhandled Exception");
            WriteStartupCrashLog(e.Exception, "Dispatcher Unhandled Exception");
            WriteStartupThreadDebugLog(e.Exception, "Dispatcher Unhandled Exception");
        };

        app.Exit += (sender, e) =>
        {
            monitor.Stop();
        };

        var mainWindow = new MainWindow(settings, logger, monitor, sdkManager, launcherManager, new ExternalLauncher(settings, logger), updateManager);
        app.Run(mainWindow);
    }

    public static void WriteSdkKillerDebugLog(Exception? ex, string operationContext, string sdkKillerState = "Unknown", int processId = 0)
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(localAppData, "GameLauncher", "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "sdkkiller_debug.log");

            int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            bool isUiThread = Application.Current?.Dispatcher?.CheckAccess() ?? false;

            var report = $"[{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [Thread #{threadId} (IsUI: {isUiThread})] [{operationContext}]\n" +
                         $"SDK Killer State: {sdkKillerState} | Target PID: {processId}\n" +
                         (ex != null ? $"Exception: {ex.GetType().FullName}: {ex.Message}\nStack Trace:\n{ex.StackTrace}\n" : "") +
                         $"--------------------------------------------------\n";

            File.AppendAllText(logFile, report);
        }
        catch
        {
            // Suppress secondary crash logging failure
        }
    }

    public static void WriteStartupThreadDebugLog(Exception ex, string context = "Startup")
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(localAppData, "GameLauncher", "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "startup_thread_debug.log");

            int threadId = System.Threading.Thread.CurrentThread.ManagedThreadId;
            bool hasDispatcherAccess = Application.Current?.Dispatcher?.CheckAccess() ?? false;

            var report = $"=== Startup Thread Diagnostic Audit [{DateTime.Now:yyyy-MM-dd HH:mm:ss.fff}] [{context}] ===\n" +
                         $"Exception Type: {ex.GetType().FullName}\n" +
                         $"Message: {ex.Message}\n" +
                         $"Target Site / Method: {ex.TargetSite?.DeclaringType?.FullName}.{ex.TargetSite?.Name}\n" +
                         $"Current Thread ID: {threadId}\n" +
                         $"Has WPF Dispatcher Access: {hasDispatcherAccess}\n" +
                         $"Inner Exception: {ex.InnerException?.Message ?? "None"}\n" +
                         $"Stack Trace:\n{ex.StackTrace}\n" +
                         $"--------------------------------------------------\n\n";

            File.AppendAllText(logFile, report);
        }
        catch
        {
            // Suppress secondary crash logging failure
        }
    }

    private static void WriteStartupCrashLog(Exception ex, string context = "Startup")
    {
        try
        {
            var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
            var logDir = Path.Combine(localAppData, "GameLauncher", "logs");
            Directory.CreateDirectory(logDir);
            var logFile = Path.Combine(logDir, "startup_crash.log");

            var assembly = System.Reflection.Assembly.GetExecutingAssembly();
            string[] manifestResources = assembly.GetManifestResourceNames();
            string resourceList = string.Join("\n  - ", manifestResources);

            var crashReport = $"=== GameLauncher Startup Crash [{DateTime.Now:yyyy-MM-dd HH:mm:ss}] [{context}] ===\n" +
                              $"Exception Type: {ex.GetType().FullName}\n" +
                              $"Message: {ex.Message}\n" +
                              $"Inner Exception: {ex.InnerException?.Message ?? "None"}\n" +
                              $"Base Directory: {AppContext.BaseDirectory}\n" +
                              $"Manifest Resources ({manifestResources.Length}):\n  - {resourceList}\n" +
                              $"Stack Trace:\n{ex.StackTrace}\n" +
                              $"--------------------------------------------------\n\n";

            File.AppendAllText(logFile, crashReport);
        }
        catch
        {
            // Suppress secondary crash logging failure
        }
    }
}
