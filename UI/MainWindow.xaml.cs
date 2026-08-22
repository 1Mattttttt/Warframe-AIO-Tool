using System;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Animation;
using System.Windows.Media.Effects;
using System.Windows.Threading;
using Point = System.Windows.Point;
using Color = System.Windows.Media.Color;
using MouseEventArgs = System.Windows.Input.MouseEventArgs;
using GameLauncher.Configuration;
using GameLauncher.Core;
using GameLauncher.Logging;
using GameLauncher.Models;

namespace GameLauncher.UI;

public partial class MainWindow : Window
{
    private readonly AppSettings _settings;
    private readonly LoggerService _logger;
    private readonly WarframeMonitor _monitor;
    private readonly DiscordSdkManager _sdkManager;
    private readonly LauncherManager _launcherManager;
    private readonly ExternalLauncher _externalLauncher;
    private readonly SpooferManager _spooferManager;
    private readonly GuidSpoofManager _guidSpoofManager;
    private readonly WarframeUpdateManager _updateManager;

    private readonly AmbientMusicManager _ambientMusicManager;
    private SystemTrayManager? _trayManager;
    private bool _isExitingFromTray;

    // Mouse lighting & dynamic UI state
    private Point _targetMousePos = new Point(0.5, 0.5);
    private Point _coreGlowPos = new Point(0.5, 0.5);
    private Point _ambientGlowPos = new Point(0.5, 0.5);
    private Point _trailGlowPos = new Point(0.5, 0.5);
    private DateTime _lastMouseMoveTime = DateTime.UtcNow;
    private double _idleBreathingPhase = 0.0;
    private bool _isAmbientEngineHooked = false;
    private long _lastRenderTicks = 0;
    private int _activeRefreshRateHz = 60;
    private double _targetFrameIntervalMs = 16.67;

    private DispatcherTimer? _owHelperTimer;
    private string? _wasOwHelperRunningState;
    private bool _isCheckingOwHelper = false;

    public MainWindow() : this(AppSettings.Load(), new LoggerService())
    {
    }

    public MainWindow(AppSettings settings, LoggerService logger)
        : this(
            settings,
            logger,
            new WarframeMonitor(settings, logger),
            new DiscordSdkManager(settings, logger),
            CreateLauncherManager(settings, logger))
    {
    }

    public MainWindow(AppSettings settings, LoggerService logger, WarframeMonitor monitor, DiscordSdkManager sdkManager, LauncherManager launcherManager)
        : this(settings, logger, monitor, sdkManager, launcherManager, new ExternalLauncher(settings, logger))
    {
    }

    public MainWindow(AppSettings settings, LoggerService logger, WarframeMonitor monitor, DiscordSdkManager sdkManager, LauncherManager launcherManager, ExternalLauncher externalLauncher)
        : this(settings, logger, monitor, sdkManager, launcherManager, externalLauncher, new WarframeUpdateManager(settings, logger))
    {
    }

    public MainWindow(AppSettings settings, LoggerService logger, WarframeMonitor monitor, DiscordSdkManager sdkManager, LauncherManager launcherManager, ExternalLauncher externalLauncher, WarframeUpdateManager updateManager)
    {
        InitializeComponent();

        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _monitor = monitor ?? throw new ArgumentNullException(nameof(monitor));
        _sdkManager = sdkManager ?? throw new ArgumentNullException(nameof(sdkManager));
        _launcherManager = launcherManager ?? throw new ArgumentNullException(nameof(launcherManager));
        _externalLauncher = externalLauncher ?? throw new ArgumentNullException(nameof(externalLauncher));
        _updateManager = updateManager ?? throw new ArgumentNullException(nameof(updateManager));
        _spooferManager = new SpooferManager(_settings, _logger);
        _guidSpoofManager = new GuidSpoofManager(_settings, _logger);

        _ambientMusicManager = new AmbientMusicManager(_settings, _logger);
        _trayManager = new SystemTrayManager(this, _logger, _ambientMusicManager);
        _trayManager.OnExitRequested += () => { _isExitingFromTray = true; };

        // Initialize Audio Controls UI
        sliderAudioVolume.Value = _ambientMusicManager.Volume;
        txtAudioMuteIcon.Text = _ambientMusicManager.IsMuted ? "🔇" : "🔊";
        txtAudioVolumeValue.Text = $"{(int)_ambientMusicManager.Volume}%";

        // Initialize Warframe Updater View
        updaterControlView.Initialize(_settings, _logger, _updateManager);

        // Start ambient music playback asynchronously
        _ambientMusicManager.InitializeAndStart();

        Loaded += MainWindow_Loaded;
        LocationChanged += (s, e) => UpdateDisplayRefreshRate();
        InitializeDashboard();
    }

    private void UpdateDisplayRefreshRate()
    {
        try
        {
            int currentHz = DisplayMonitorHelper.GetWindowRefreshRate(this);
            if (currentHz > 0 && currentHz != _activeRefreshRateHz)
            {
                _activeRefreshRateHz = currentHz;
                _targetFrameIntervalMs = 1000.0 / _activeRefreshRateHz;
                _logger?.LogInfo($"[Display Engine] Active monitor refresh rate detected: {_activeRefreshRateHz}Hz (Target frame interval: {_targetFrameIntervalMs:F2}ms)");
            }
        }
        catch
        {
            // Suppress query failures
        }
    }

    private async void MainWindow_Loaded(object sender, RoutedEventArgs e)
    {
        try
        {
            UpdateDisplayRefreshRate();
            AnimateWindowMaterialize();

            if (_settings.EnableStartupAnimation)
            {
                await ExecuteBootSequenceAsync();
            }
            else
            {
                StartupOverlayGrid.Visibility = Visibility.Collapsed;
                StartupOverlayGrid.IsHitTestVisible = false;
                double animDuration = _settings.StartupWindowMode.Equals("Windowed", StringComparison.OrdinalIgnoreCase) ? 1000 : 600;
                AnimateStaggeredEntrance(animDuration);
            }
        }
        catch
        {
            StartupOverlayGrid.Visibility = Visibility.Collapsed;
            StartupOverlayGrid.IsHitTestVisible = false;
            animHeader.Opacity = 1.0;
            animCardStatus.Opacity = 1.0;
            animCardCounter.Opacity = 1.0;
            animCardSdk.Opacity = 1.0;
            animLauncherPanel.Opacity = 1.0;
            animActionButtons.Opacity = 1.0;
            animLogConsole.Opacity = 1.0;
        }
    }

    private void AnimateWindowMaterialize()
    {
        try
        {
            Opacity = 0.0;
            scaleWindowMaterialize.ScaleX = 0.82;
            scaleWindowMaterialize.ScaleY = 0.82;

            var fadeAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(550))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            };

            var scaleKeyFramesX = new DoubleAnimationUsingKeyFrames();
            scaleKeyFramesX.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.82, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            scaleKeyFramesX.KeyFrames.Add(new SplineDoubleKeyFrame(1.025, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)), new KeySpline(0.1, 0.9, 0.2, 1.0)));
            scaleKeyFramesX.KeyFrames.Add(new SplineDoubleKeyFrame(1.000, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550)), new KeySpline(0.25, 1.0, 0.5, 1.0)));

            var scaleKeyFramesY = new DoubleAnimationUsingKeyFrames();
            scaleKeyFramesY.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.82, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(0))));
            scaleKeyFramesY.KeyFrames.Add(new SplineDoubleKeyFrame(1.025, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(380)), new KeySpline(0.1, 0.9, 0.2, 1.0)));
            scaleKeyFramesY.KeyFrames.Add(new SplineDoubleKeyFrame(1.000, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(550)), new KeySpline(0.25, 1.0, 0.5, 1.0)));

            BeginAnimation(OpacityProperty, fadeAnim);
            scaleWindowMaterialize.BeginAnimation(ScaleTransform.ScaleXProperty, scaleKeyFramesX);
            scaleWindowMaterialize.BeginAnimation(ScaleTransform.ScaleYProperty, scaleKeyFramesY);
        }
        catch
        {
            Opacity = 1.0;
            scaleWindowMaterialize.ScaleX = 1.0;
            scaleWindowMaterialize.ScaleY = 1.0;
        }
    }

    private Storyboard? _spinnerStoryboard;

    private void StartSpinnerAnimation()
    {
        var anim = new DoubleAnimation
        {
            From = 0,
            To = 360,
            Duration = TimeSpan.FromMilliseconds(900),
            RepeatBehavior = RepeatBehavior.Forever
        };
        Storyboard.SetTarget(anim, spinnerRotate);
        Storyboard.SetTargetProperty(anim, new PropertyPath(RotateTransform.AngleProperty));

        _spinnerStoryboard = new Storyboard();
        _spinnerStoryboard.Children.Add(anim);
        _spinnerStoryboard.Begin();
    }

    private void StopSpinnerAnimation()
    {
        _spinnerStoryboard?.Stop();
        _spinnerStoryboard = null;
    }

    private void AnimateStartupMaterialize(double durationMs)
    {
        var duration = TimeSpan.FromMilliseconds(durationMs);
        var easing = new CubicEase { EasingMode = EasingMode.EaseOut };

        // 1. Fade in Opacity (0 -> 1.0)
        var opacityAnim = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(durationMs * 0.4))
        {
            EasingFunction = easing
        };
        StartupOverlayGrid.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

        // 2. ScaleX Animation with Subtle Overshoot (0.85 -> 1.03 -> 1.00)
        var scaleXFrames = new DoubleAnimationUsingKeyFrames();
        scaleXFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.85, KeyTime.FromPercent(0)));
        scaleXFrames.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.75))) { EasingFunction = easing });
        scaleXFrames.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(duration)) { EasingFunction = easing });

        // 3. ScaleY Animation with Subtle Overshoot (0.85 -> 1.03 -> 1.00)
        var scaleYFrames = new DoubleAnimationUsingKeyFrames();
        scaleYFrames.KeyFrames.Add(new DiscreteDoubleKeyFrame(0.85, KeyTime.FromPercent(0)));
        scaleYFrames.KeyFrames.Add(new EasingDoubleKeyFrame(1.03, KeyTime.FromTimeSpan(TimeSpan.FromMilliseconds(durationMs * 0.75))) { EasingFunction = easing });
        scaleYFrames.KeyFrames.Add(new EasingDoubleKeyFrame(1.00, KeyTime.FromTimeSpan(duration)) { EasingFunction = easing });

        scaleStartupContent.BeginAnimation(ScaleTransform.ScaleXProperty, scaleXFrames);
        scaleStartupContent.BeginAnimation(ScaleTransform.ScaleYProperty, scaleYFrames);
    }

    private Storyboard? _logoPulseStoryboard;

    private void StartLogoPulseAnimation()
    {
        var anim = new DoubleAnimation
        {
            From = 0.3,
            To = 0.8,
            Duration = TimeSpan.FromMilliseconds(1200),
            AutoReverse = true,
            RepeatBehavior = RepeatBehavior.Forever,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseInOut }
        };
        Storyboard.SetTarget(anim, shadowStartupLogo);
        Storyboard.SetTargetProperty(anim, new PropertyPath(DropShadowEffect.OpacityProperty));

        _logoPulseStoryboard = new Storyboard();
        _logoPulseStoryboard.Children.Add(anim);
        _logoPulseStoryboard.Begin();
    }

    private void StopLogoPulseAnimation()
    {
        _logoPulseStoryboard?.Stop();
        _logoPulseStoryboard = null;
    }

    private async Task ExecuteBootSequenceAsync()
    {
        bool isOwHelperActive = _externalLauncher.IsOwHelperActive();
        bool isOwHelperInjected = _owHelperWasLaunchedBeforeWarframe && _warframeDetectedAfterOwHelper;
        var theme = LauncherThemeManager.GetTheme(_launcherManager.SelectedLauncher, isOwHelperActive, isOwHelperInjected);

        double bootAnimMs = _settings.StartupWindowMode.Equals("Windowed", StringComparison.OrdinalIgnoreCase) ? 1200 : 800;

        // Activate Monochrome Saturn Loading Environment
        cinematicBgEngine.IsMonochromeMode = true;

        // Theme integration for startup screen overlay
        StartupOverlayGrid.Background = new SolidColorBrush(theme.BaseColor);
        spinStopAccent.Color = theme.AccentColor;
        txtStartupLogoTitle.Foreground = new SolidColorBrush(theme.AccentColor);
        shadowStartupLogo.Color = theme.AccentColor;

        StartupOverlayGrid.Visibility = Visibility.Visible;
        StartupOverlayGrid.IsHitTestVisible = true;

        AnimateStartupMaterialize(bootAnimMs);
        StartSpinnerAnimation();
        StartLogoPulseAnimation();

        panelBootLogs.Children.Clear();

        // Initialization Steps
        txtBootStatus.Text = "> Initializing Warframe AIO Tool...";
        txtBootProgress.Text = "[○○○○○○○○] 0%";
        AddBootLogLine("> Initializing Warframe AIO Tool...", theme.AccentColor);
        await Task.Delay(400);

        txtBootProgress.Text = "[●○○○○○○○] 12%";
        AddBootLogLine("> Loading configuration system...", (Color)ColorConverter.ConvertFromString("#CDD6F4"));
        await Task.Delay(400);

        txtBootStatus.Text = "> Loading cinematic environment...";
        txtBootProgress.Text = "[●●○○○○○○] 25%";
        AddBootLogLine("> Loading cinematic environment...", theme.AccentColor);
        await Task.Delay(400);

        txtBootProgress.Text = "[●●●○○○○○] 37%";
        AddBootLogLine("> Initializing Saturn ring particle clouds...", (Color)ColorConverter.ConvertFromString("#CDD6F4"));
        await Task.Delay(400);

        txtBootProgress.Text = "[●●●●○○○○] 50%";
        AddBootLogLine("> Loading Discord SDK Manager...", (Color)ColorConverter.ConvertFromString("#CDD6F4"));
        await Task.Delay(400);

        txtBootProgress.Text = "[●●●●●○○○] 62%";
        AddBootLogLine("> Loading Warframe Monitor...", (Color)ColorConverter.ConvertFromString("#CDD6F4"));
        await Task.Delay(400);

        txtBootProgress.Text = "[●●●●●●○○] 75%";
        AddBootLogLine("> Checking external tools (OwHelper)...", (Color)ColorConverter.ConvertFromString("#CDD6F4"));
        await Task.Delay(400);

        txtBootProgress.Text = "[●●●●●●●○] 87%";
        AddBootLogLine("> Materializing spacecraft interface...", (Color)ColorConverter.ConvertFromString("#CDD6F4"));
        await Task.Delay(400);

        txtBootStatus.Text = "> System Ready.";
        txtBootProgress.Text = "[●●●●●●●●] 100% Complete";
        AddBootLogLine("> System Ready.", (Color)ColorConverter.ConvertFromString("#A6E3A1"));

        // Logo flash / brighten effect on completion
        var flashAnim = new DoubleAnimation(0.4, 1.0, TimeSpan.FromMilliseconds(300))
        {
            AutoReverse = true,
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        shadowStartupLogo.BeginAnimation(DropShadowEffect.OpacityProperty, flashAnim);

        await Task.Delay(400);

        // Smooth transition out into main dashboard & dissolve monochrome to full color
        cinematicBgEngine.IsMonochromeMode = false;
        double mainEntranceMs = _settings.StartupWindowMode.Equals("Windowed", StringComparison.OrdinalIgnoreCase) ? 1000 : 800;
        AnimateStaggeredEntrance(mainEntranceMs);

        var fadeOutAnim = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(800))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };

        fadeOutAnim.Completed += (s, e) =>
        {
            StartupOverlayGrid.Visibility = Visibility.Collapsed;
            StartupOverlayGrid.IsHitTestVisible = false;
            StopSpinnerAnimation();
            StopLogoPulseAnimation();
            _ = StartupJingleManager.PlayJingleAsync(_settings.EnableStartupJingle, _logger);
        };

        StartupOverlayGrid.BeginAnimation(UIElement.OpacityProperty, fadeOutAnim);
    }

    private void AddBootLogLine(string text, Color color)
    {
        var tb = new TextBlock
        {
            Text = text,
            FontSize = 12,
            FontFamily = new FontFamily("Consolas, Cascadia Code, Courier New"),
            Foreground = new SolidColorBrush(color),
            Margin = new Thickness(0, 2, 0, 2),
            Opacity = 0.0
        };
        panelBootLogs.Children.Add(tb);

        var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(200))
        {
            EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
        };
        tb.BeginAnimation(OpacityProperty, fadeIn);
        scrollBootLogs.ScrollToBottom();
    }

    private void AnimateStaggeredEntrance(double totalDurationMs)
    {
        var elements = new FrameworkElement[]
        {
            animHeader,
            animCardStatus,
            animCardCounter,
            animCardSdk,
            animLauncherPanel,
            animActionButtons,
            animLogConsole
        };

        double animTime = Math.Max(350, totalDurationMs * 0.5);
        double stepDelay = 70; // 70ms stagger delay

        for (int i = 0; i < elements.Length; i++)
        {
            var element = elements[i];
            if (element == null) continue;

            TimeSpan delay = TimeSpan.FromMilliseconds(i * stepDelay);

            var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(animTime))
            {
                BeginTime = delay,
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
            };
            element.BeginAnimation(OpacityProperty, fadeIn);

            if (element.RenderTransform is TranslateTransform tt)
            {
                var slideIn = new DoubleAnimation(18.0, 0.0, TimeSpan.FromMilliseconds(animTime))
                {
                    BeginTime = delay,
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                };
                tt.BeginAnimation(TranslateTransform.YProperty, slideIn);
            }
        }
    }

    // CUSTOM WINDOW CHROME HANDLERS
    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ClickCount == 2)
        {
            ToggleMaximizeRestore();
        }
        else if (e.LeftButton == MouseButtonState.Pressed)
        {
            DragMove();
        }
    }

    private void btnWindowMinimize_Click(object sender, RoutedEventArgs e)
    {
        WindowState = WindowState.Minimized;
    }

    private void btnWindowMaximize_Click(object sender, RoutedEventArgs e)
    {
        ToggleMaximizeRestore();
    }

    private void btnAudioMute_Click(object sender, RoutedEventArgs e)
    {
        _ambientMusicManager.ToggleMute();
        txtAudioMuteIcon.Text = _ambientMusicManager.IsMuted ? "🔇" : "🔊";
    }

    private void sliderAudioVolume_ValueChanged(object sender, RoutedPropertyChangedEventArgs<double> e)
    {
        if (_ambientMusicManager != null)
        {
            _ambientMusicManager.Volume = e.NewValue;
            if (txtAudioVolumeValue != null)
            {
                txtAudioVolumeValue.Text = $"{(int)e.NewValue}%";
            }
        }
    }

    protected override void OnStateChanged(EventArgs e)
    {
        base.OnStateChanged(e);
        if (WindowState == WindowState.Minimized)
        {
            if (_settings.CloseToTray || _settings.StartMinimizedToTray)
            {
                _trayManager?.HideWindow();
            }
            else
            {
                _ambientMusicManager?.Pause();
            }
        }
        else if (WindowState == WindowState.Normal || WindowState == WindowState.Maximized)
        {
            _ambientMusicManager?.Resume();
        }
    }

    private async void btnWindowClose_Click(object sender, RoutedEventArgs e)
    {
        await _ambientMusicManager.FadeOutAsync(400);
        Close();
    }

    private void ToggleMaximizeRestore()
    {
        bool targetMaximized = WindowState != WindowState.Maximized;

        OuterWindowBorder.CornerRadius = targetMaximized ? new CornerRadius(0) : new CornerRadius(16);
        OuterWindowBorder.BorderThickness = targetMaximized ? new Thickness(0) : new Thickness(1);

        if (targetMaximized)
        {
            WindowState = WindowState.Maximized;
            txtWindowMaximizeIcon.Text = "🗗";
            OuterWindowBorder.Effect = null;
        }
        else
        {
            WindowState = WindowState.Normal;
            txtWindowMaximizeIcon.Text = "🗖";
            OuterWindowBorder.Effect = new DropShadowEffect
            {
                Color = (Color)ColorConverter.ConvertFromString("#000000"),
                BlurRadius = 28,
                ShadowDepth = 4,
                Opacity = 0.45
            };
        }
    }



    private static LauncherManager CreateLauncherManager(AppSettings settings, LoggerService logger)
    {
        var epicLauncher = new EpicLauncher(settings, logger);
        var steamLauncher = new SteamLauncher(settings, logger);
        var standaloneLauncher = new StandaloneLauncher(settings, logger);
        return new LauncherManager(settings, logger, epicLauncher, steamLauncher, standaloneLauncher);
    }

    private bool _owHelperWasLaunchedBeforeWarframe;
    private bool _warframeDetectedAfterOwHelper;
    private bool _warframeLaunchedWithoutOwHelper;
    private string? _lastOwHelperCardState;

    private void InitializeDashboard()
    {
        _logger.EnableFileLogging = _settings.EnableFileLogging;

        _logger.LogReceived += OnLogReceived;

        _monitor.ProcessDetected += OnProcessDetected;
        _monitor.BootstrapDetected += OnBootstrapDetected;
        _monitor.GameLaunchDetected += OnGameLaunchDetected;

        UpdateLauncherSelectionVisuals(_launcherManager.SelectedLauncher);
        UpdateLauncherDisplay();
        UpdateThemeState(animate: false);

        // Respect user preference for SDK Killer auto-start
        if (_settings.StartMonitoringOnLaunch)
        {
            _monitor.Start();
        }
        UpdateUiState();

        StartOwHelperMonitoringTimer();

        // Initialize Spoofer module view
        spooferControlView.Initialize(_spooferManager, _guidSpoofManager, _logger);

        // Load and validate launcher logo & profile images
        var profileImage = ImageAssetManager.Load("Assets/profile.png", _logger);
        if (profileImage != null)
        {
            imgHeaderProfile.Source = profileImage;
            imgStartupProfile.Source = profileImage;
        }

        imgEpicLogo.Source = ImageAssetManager.Load("Assets/epic_games.png", _logger);
        imgSteamLogo.Source = ImageAssetManager.Load("Assets/steam.png", _logger);
        imgStandaloneLogo.Source = ImageAssetManager.Load("Assets/warframe.png", _logger);

        if (!_isAmbientEngineHooked)
        {
            CompositionTarget.Rendering += OnAmbientEngineRendering;
            _isAmbientEngineHooked = true;
        }

        bool argsContainMinimized = Array.Exists(Environment.GetCommandLineArgs(), arg => arg.Equals("--minimized", StringComparison.OrdinalIgnoreCase));
        if (argsContainMinimized || _settings.StartMinimizedToTray || _settings.LaunchMinimizedOnSystemStartup)
        {
            Hide();
        }
        else if (_settings.StartMinimized)
        {
            WindowState = WindowState.Minimized;
        }
        else if (_settings.StartupWindowMode.Equals("Windowed", StringComparison.OrdinalIgnoreCase))
        {
            WindowState = WindowState.Normal;
            WindowStartupLocation = WindowStartupLocation.CenterScreen;
            txtWindowMaximizeIcon.Text = "🗖";
        }
        else
        {
            WindowState = WindowState.Maximized;
            txtWindowMaximizeIcon.Text = "🗗";
        }

        if (_settings.AutoLaunchWarframe)
        {
            _launcherManager.LaunchWarframe();
        }
    }

    private void StartOwHelperMonitoringTimer()
    {
        _owHelperTimer = new DispatcherTimer(System.Windows.Threading.DispatcherPriority.Background)
        {
            Interval = TimeSpan.FromMilliseconds(1500)
        };
        _owHelperTimer.Tick += OnOwHelperTimerTick;
        _owHelperTimer.Start();
        CheckOwHelperStatus(animate: false);
    }

    private void OnOwHelperTimerTick(object? sender, EventArgs e)
    {
        CheckOwHelperStatus(animate: true);
    }

    private async void CheckOwHelperStatus(bool animate)
    {
        if (_isCheckingOwHelper) return;
        _isCheckingOwHelper = true;

        try
        {
            string details = string.Empty;
            bool isRunning = await Task.Run(() => _externalLauncher.IsOwHelperActive(out details));
            string currentStateKey = isRunning ? (string.IsNullOrEmpty(details) ? "running" : details) : "stopped";

            if (_wasOwHelperRunningState != currentStateKey)
            {
                bool isInitialCheck = _wasOwHelperRunningState == null;
                _wasOwHelperRunningState = currentStateKey;

                if (isRunning)
                {
                    if (details == "bootstrap")
                    {
                        _logger.LogSuccess("OwHelper bootstrap process detected.");
                    }
                    else if (!string.IsNullOrEmpty(details))
                    {
                        _logger.LogSuccess($"OwHelper payload detected:\n{details}");
                    }
                    else
                    {
                        _logger.LogSuccess("OwHelper process detected.");
                    }
                }
                else if (!isInitialCheck)
                {
                    _logger.LogInfo("OwHelper process exited.");
                }
                else
                {
                    _logger.LogDebug("OwHelper process not detected.");
                }

                UpdateOwHelperButtonState(isRunning, animate && !isInitialCheck);
                UpdateOwHelperCardStatus(isRunning);
                UpdateThemeState(animate: !isInitialCheck);
            }
        }
        catch
        {
            // Suppress monitoring errors gracefully
        }
        finally
        {
            _isCheckingOwHelper = false;
        }
    }

    private void UpdateOwHelperCardStatus(bool isRunning)
    {
        RunOnUIThread(() =>
        {
            string newState;
            string icon;
            string statusText;
            string subtextText;
            string hexColor;

            if (isRunning)
            {
                _owHelperWasLaunchedBeforeWarframe = true;
                if (_warframeDetectedAfterOwHelper)
                {
                    // 🟢 Injected
                    newState = "injected";
                    icon = "🟢";
                    statusText = "Injected";
                    subtextText = "OwHelper confirmed active";
                    hexColor = "#A6E3A1";
                }
                else if (_externalLauncher.IsOwHelperActive(out string details) && !string.IsNullOrEmpty(details) && details != "bootstrap")
                {
                    // 🟠 OwHelper Running (payload detected)
                    newState = "running";
                    icon = "🟤";
                    statusText = "OwHelper Running";
                    subtextText = "Payload detected";
                    hexColor = "#89B4FA";
                }
                else
                {
                    // 🟡 Waiting for Warframe
                    newState = "waiting";
                    icon = "🟡";
                    statusText = "Waiting for Warframe";
                    subtextText = "OwHelper ready, launch Warframe";
                    hexColor = "#FAB387";
                }
            }
            else
            {
                if (_owHelperWasLaunchedBeforeWarframe && _warframeDetectedAfterOwHelper)
                {
                    // 🟢 Injected (historically)
                    newState = "injected";
                    icon = "🟢";
                    statusText = "Injected";
                    subtextText = "OwHelper payload was active";
                    hexColor = "#A6E3A1";
                }
                else if (_warframeLaunchedWithoutOwHelper)
                {
                    // 🔴 Injection Failed
                    newState = "failed";
                    icon = "🔴";
                    statusText = "Injection Failed";
                    subtextText = "Warframe launched without OwHelper";
                    hexColor = "#F38BA8";
                }
                else
                {
                    // ⚪ Not Started
                    newState = "not_started";
                    icon = "⚪";
                    statusText = "Not Started";
                    subtextText = "OwHelper has not been launched";
                    hexColor = "#6C7086";
                }
            }

            bool stateChanged = _lastOwHelperCardState != newState;
            _lastOwHelperCardState = newState;

            // Animate icon change with pulse
            txtOwHelperCardIcon.Text = icon;
            if (stateChanged)
            {
                PulseIconAnimation(txtOwHelperCardIcon);
                FadeTextTransition(txtOwHelperCardStatus, statusText, hexColor);
                FadeTextTransition(txtOwHelperCardSubtext, subtextText, null);
            }
            else
            {
                txtOwHelperCardStatus.Text = statusText;
                var colorBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
                colorBrush.Freeze();
                txtOwHelperCardStatus.Foreground = colorBrush;
                txtOwHelperCardSubtext.Text = subtextText;
            }
        });
    }

    private void UpdateOwHelperButtonState(bool isRunning, bool animate)
    {
        RunOnUIThread(() =>
        {
            var targetBgColor = (Color)ColorConverter.ConvertFromString(isRunning ? "#A6E3A1" : "#F38BA8");
            string targetText = isRunning ? "✔  OwHelper Running" : "☣  Launch OwHelper";

            btnLaunchOwHelper.Content = targetText;
            btnLaunchOwHelper.IsEnabled = !isRunning;
            btnLaunchOwHelper.Cursor = isRunning ? Cursors.Arrow : Cursors.Hand;

            if (animate)
            {
                var colorAnim = new ColorAnimation(targetBgColor, TimeSpan.FromMilliseconds(200))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                };

                if (btnLaunchOwHelper.Background is SolidColorBrush brush && !brush.IsFrozen)
                {
                    brush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                }
                else
                {
                    var newBrush = new SolidColorBrush(targetBgColor);
                    btnLaunchOwHelper.Background = newBrush;
                    newBrush.BeginAnimation(SolidColorBrush.ColorProperty, colorAnim);
                }
            }
            else
            {
                var bgB = new SolidColorBrush(targetBgColor);
                bgB.Freeze();
                btnLaunchOwHelper.Background = bgB;
            }
        });
    }

    private void SelectLauncher(LauncherType launcherType)
    {
        _launcherManager.SetSelectedLauncher(launcherType);
        UpdateLauncherSelectionVisuals(launcherType);
        UpdateLauncherDisplay();
        UpdateEpicSettingsVisibility();
        UpdateThemeState(animate: true);
    }

    private static readonly IEasingFunction ThemeEasing = new SineEase { EasingMode = EasingMode.EaseOut };

    private static void AnimateBrushColor(FrameworkElement element, DependencyProperty property, Color targetColor, TimeSpan duration)
    {
        if (element == null) return;

        if (!element.Dispatcher.CheckAccess())
        {
            element.Dispatcher.InvokeAsync(() => AnimateBrushColor(element, property, targetColor, duration));
            return;
        }

        var brush = element.GetValue(property) as SolidColorBrush;
        if (brush != null && !brush.IsFrozen)
        {
            if (brush.Color == targetColor) return;

            var anim = new ColorAnimation(targetColor, duration) { EasingFunction = ThemeEasing };
            brush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
        }
        else
        {
            element.BeginAnimation(property, null);
            var newBrush = new SolidColorBrush(targetColor);
            element.SetValue(property, newBrush);

            if (duration > TimeSpan.Zero)
            {
                var anim = new ColorAnimation(targetColor, duration) { EasingFunction = ThemeEasing };
                newBrush.BeginAnimation(SolidColorBrush.ColorProperty, anim);
            }
        }
    }

    private void UpdateThemeState(bool animate = true)
    {
        RunOnUIThread(() =>
        {
            try
            {
                bool isOwHelperActive = _externalLauncher.IsOwHelperActive();
                bool isOwHelperInjected = _owHelperWasLaunchedBeforeWarframe && _warframeDetectedAfterOwHelper;
                var theme = LauncherThemeManager.GetTheme(_launcherManager.SelectedLauncher, isOwHelperActive, isOwHelperInjected);

                TimeSpan duration = animate ? TimeSpan.FromMilliseconds(800) : TimeSpan.Zero;

                // 1. Outer Window Frame Base Background
                AnimateBrushColor(OuterWindowBorder, Border.BackgroundProperty, theme.BaseColor, duration);

                // Update Cinematic Background Engine Theme Colors
                cinematicBgEngine.UpdateThemeColors(theme.BaseColor, theme.AccentColor, theme.GlowColor);

                // 2. Multi-Layer Hardware-Accelerated Ambient Mouse Glow
                Color coreGlowCenter = Color.FromArgb(0x55, theme.GlowColor.R, theme.GlowColor.G, theme.GlowColor.B);
                Color coreGlowEdge = Color.FromArgb(0x00, theme.BaseColor.R, theme.BaseColor.G, theme.BaseColor.B);

                Color ambientGlowCenter = Color.FromArgb(0x28, theme.GlowColor.R, theme.GlowColor.G, theme.GlowColor.B);
                Color ambientGlowEdge = Color.FromArgb(0x00, theme.BaseColor.R, theme.BaseColor.G, theme.BaseColor.B);

                Color trailGlowCenter = Color.FromArgb(0x35, theme.AccentColor.R, theme.AccentColor.G, theme.AccentColor.B);
                Color trailGlowEdge = Color.FromArgb(0x00, theme.BaseColor.R, theme.BaseColor.G, theme.BaseColor.B);

                if (duration > TimeSpan.Zero)
                {
                    if (!stopCoreCenter.IsFrozen) stopCoreCenter.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(coreGlowCenter, duration) { EasingFunction = ThemeEasing });
                    if (!stopCoreEdge.IsFrozen) stopCoreEdge.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(coreGlowEdge, duration) { EasingFunction = ThemeEasing });

                    if (!stopAmbientCenter.IsFrozen) stopAmbientCenter.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(ambientGlowCenter, duration) { EasingFunction = ThemeEasing });
                    if (!stopAmbientEdge.IsFrozen) stopAmbientEdge.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(ambientGlowEdge, duration) { EasingFunction = ThemeEasing });

                    if (!stopTrailCenter.IsFrozen) stopTrailCenter.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(trailGlowCenter, duration) { EasingFunction = ThemeEasing });
                    if (!stopTrailEdge.IsFrozen) stopTrailEdge.BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(trailGlowEdge, duration) { EasingFunction = ThemeEasing });
                }
                else
                {
                    stopCoreCenter.Color = coreGlowCenter;
                    stopCoreEdge.Color = coreGlowEdge;
                    stopAmbientCenter.Color = ambientGlowCenter;
                    stopAmbientEdge.Color = ambientGlowEdge;
                    stopTrailCenter.Color = trailGlowCenter;
                    stopTrailEdge.Color = trailGlowEdge;
                }

                // 3. Header Accent Text & Branding & Audio Controls
                AnimateBrushColor(txtLauncherType, TextBlock.ForegroundProperty, theme.AccentColor, duration);
                AnimateBrushColor(txtAudioVolumeValue, TextBlock.ForegroundProperty, theme.AccentColor, duration);
                AnimateBrushColor(borderAudioControl, Border.BackgroundProperty, theme.CardBackground, duration);
                AnimateBrushColor(borderAudioControl, Border.BorderBrushProperty, theme.CardBorder, duration);

                // 4. Dashboard Widgets & Cards Background / Border
                var dashboardCards = new[] { animCardStatus, animCardCounter, animCardSdk };
                foreach (var card in dashboardCards)
                {
                    if (card == null) continue;
                    AnimateBrushColor(card, Border.BackgroundProperty, theme.CardBackground, duration);
                    AnimateBrushColor(card, Border.BorderBrushProperty, theme.CardBorder, duration);
                }

                // 5. Action Buttons & Settings Background / Border (btnToggleMonitoring managed separately)
                var secondaryButtons = new[] { btnRestore, btnSettings };
                foreach (var btn in secondaryButtons)
                {
                    if (btn == null) continue;
                    AnimateBrushColor(btn, Button.BackgroundProperty, theme.ButtonBackground, duration);
                    AnimateBrushColor(btn, Button.BorderBrushProperty, theme.ButtonBorder, duration);
                }

                // 6. Primary Launch Button Gradient & Text Color
                var fgB = new SolidColorBrush(theme.PrimaryForeground);
                fgB.Freeze();
                btnLaunchWarframe.Foreground = fgB;

                if (btnLaunchWarframe.Background is LinearGradientBrush lgb && lgb.GradientStops.Count >= 2 && !lgb.IsFrozen)
                {
                    lgb.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.PrimaryGradientStart, duration) { EasingFunction = ThemeEasing });
                    lgb.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.PrimaryGradientEnd, duration) { EasingFunction = ThemeEasing });
                }
                else
                {
                    var newLgb = new LinearGradientBrush
                    {
                        StartPoint = new Point(0, 0),
                        EndPoint = new Point(1, 1),
                        GradientStops = new GradientStopCollection
                        {
                            new GradientStop(theme.PrimaryGradientStart, 0),
                            new GradientStop(theme.PrimaryGradientEnd, 1)
                        }
                    };
                    btnLaunchWarframe.Background = newLgb;
                    if (duration > TimeSpan.Zero)
                    {
                        newLgb.GradientStops[0].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.PrimaryGradientStart, duration) { EasingFunction = ThemeEasing });
                        newLgb.GradientStops[1].BeginAnimation(GradientStop.ColorProperty, new ColorAnimation(theme.PrimaryGradientEnd, duration) { EasingFunction = ThemeEasing });
                    }
                }

                // 7. Log Console Background, Border, Header & ListBox Background
                Color consoleAcrylicBg = Color.FromArgb(0xD8, theme.ConsoleBackground.R, theme.ConsoleBackground.G, theme.ConsoleBackground.B);
                AnimateBrushColor(animLogConsole, Border.BackgroundProperty, consoleAcrylicBg, duration);
                AnimateBrushColor(animLogConsole, Border.BorderBrushProperty, theme.ConsoleBorder, duration);
                AnimateBrushColor(txtLogConsoleHeader, TextBlock.ForegroundProperty, theme.AccentColor, duration);

                // 8. Refresh Launcher Cards Visuals
                UpdateLauncherSelectionVisuals(_launcherManager.SelectedLauncher);

                // 9. Status Card Accent
                if (!_monitor.IsMonitoring)
                {
                    var accentB = new SolidColorBrush(theme.AccentColor);
                    accentB.Freeze();
                    txtCardStatus.Foreground = accentB;
                }

                // 10. Ensure SDK Killer button state persistence across theme changes
                UpdateMonitoringButtonState(animate: animate);
            }
            catch (Exception ex)
            {
                Program.WriteSdkKillerDebugLog(ex, "UpdateThemeState Error");
            }
        });
    }

    private void UpdateLauncherSelectionVisuals(LauncherType selected)
    {
        RunOnUIThread(() =>
        {
            AnimateLauncherCard(cardEpic, LauncherType.Epic, selected == LauncherType.Epic, badgeEpicSelected);
            AnimateLauncherCard(cardSteam, LauncherType.Steam, selected == LauncherType.Steam, badgeSteamSelected);
            AnimateLauncherCard(cardStandalone, LauncherType.Standalone, selected == LauncherType.Standalone, badgeStandaloneSelected);
        });
    }

    private void AnimateLauncherCard(Border card, LauncherType platformType, bool isSelected, TextBlock selectedBadge)
    {
        RunOnUIThread(() =>
        {
            bool isOwHelperActive = _externalLauncher.IsOwHelperActive();
            var platformTheme = LauncherThemeManager.GetTheme(platformType, isOwHelperActive);

            var targetBorderColor = isSelected ? platformTheme.AccentColor : platformTheme.CardBorder;
            var targetBgColor = isSelected ? platformTheme.CardBackground : Color.FromArgb(0x66, platformTheme.CardBackground.R, platformTheme.CardBackground.G, platformTheme.CardBackground.B);
            double targetScale = isSelected ? 1.03 : 1.00;
            double targetOpacity = isSelected ? 1.0 : 0.55;

            card.BorderThickness = new Thickness(isSelected ? 2 : 1);
            selectedBadge.Visibility = isSelected ? Visibility.Visible : Visibility.Collapsed;

            var accentB = new SolidColorBrush(platformTheme.AccentColor);
            accentB.Freeze();
            selectedBadge.Foreground = accentB;

            var duration = TimeSpan.FromMilliseconds(400);
            var easing = new SineEase { EasingMode = EasingMode.EaseOut };

            var borderAnim = new ColorAnimation(targetBorderColor, duration) { EasingFunction = easing };
            var bgAnim = new ColorAnimation(targetBgColor, duration) { EasingFunction = easing };
            var opacityAnim = new DoubleAnimation(targetOpacity, duration) { EasingFunction = easing };

            if (card.BorderBrush is SolidColorBrush borderBrush && !borderBrush.IsFrozen)
            {
                borderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
            }
            else
            {
                var newBorderBrush = new SolidColorBrush(targetBorderColor);
                card.BorderBrush = newBorderBrush;
                newBorderBrush.BeginAnimation(SolidColorBrush.ColorProperty, borderAnim);
            }

            if (card.Background is SolidColorBrush bgBrush && !bgBrush.IsFrozen)
            {
                bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
            }
            else
            {
                var newBgBrush = new SolidColorBrush(targetBgColor);
                card.Background = newBgBrush;
                newBgBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
            }

            card.BeginAnimation(UIElement.OpacityProperty, opacityAnim);

            if (card.RenderTransform is ScaleTransform st)
            {
                st.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
                st.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
            }
            else
            {
                var stNew = new ScaleTransform(1.0, 1.0);
                card.RenderTransformOrigin = new Point(0.5, 0.5);
                card.RenderTransform = stNew;
                stNew.BeginAnimation(ScaleTransform.ScaleXProperty, new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
                stNew.BeginAnimation(ScaleTransform.ScaleYProperty, new DoubleAnimation(targetScale, duration) { EasingFunction = easing });
            }

            card.Effect = isSelected ? new DropShadowEffect
            {
                Color = platformTheme.AccentColor,
                BlurRadius = 16,
                ShadowDepth = 0,
                Opacity = 0.4
            } : null;
        });
    }

    private void cardEpic_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SelectLauncher(LauncherType.Epic);
    }

    private void cardSteam_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SelectLauncher(LauncherType.Steam);
    }

    private void cardStandalone_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        SelectLauncher(LauncherType.Standalone);
    }

    private void UpdateLauncherDisplay()
    {
        txtLauncherType.Text = $"Launcher: {LauncherManager.GetDisplayName(_launcherManager.SelectedLauncher)}";
    }

    private void UpdateEpicSettingsVisibility()
    {
        // Settings button is always visible regardless of selected launcher
        btnSettings.Visibility = Visibility.Visible;
    }

    protected override void OnClosing(System.ComponentModel.CancelEventArgs e)
    {
        if (!_isExitingFromTray && _settings.CloseToTray)
        {
            e.Cancel = true;
            _trayManager?.HideWindow();
            _trayManager?.ShowTrayNotification("GameLauncher", "Application minimized to system tray.");
            return;
        }

        _trayManager?.Dispose();
        base.OnClosing(e);
    }

    private void btnSettings_Click(object sender, RoutedEventArgs e)
    {
        var settingsWindow = new SettingsWindow(_settings, _logger)
        {
            Owner = this
        };

        if (settingsWindow.ShowDialog() == true)
        {
            _logger.EnableFileLogging = _settings.EnableFileLogging;
            UpdateLauncherSelectionVisuals(_launcherManager.SelectedLauncher);
            UpdateLauncherDisplay();
            UpdateUiState();
        }
    }

    private bool _isTabTransitionRunning;

    private void btnTabDashboard_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab("Dashboard");
    }

    private void btnTabUpdater_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab("Updater");
    }

    private void btnTabSpoofer_Click(object sender, RoutedEventArgs e)
    {
        SwitchTab("Spoofer");
    }

    private void SwitchTab(string targetTab)
    {
        if (_isTabTransitionRunning) return;

        bool isDashboard = string.Equals(targetTab, "Dashboard", StringComparison.OrdinalIgnoreCase);
        bool isUpdater = string.Equals(targetTab, "Updater", StringComparison.OrdinalIgnoreCase);
        bool isSpoofer = string.Equals(targetTab, "Spoofer", StringComparison.OrdinalIgnoreCase);

        // Check if already active
        if (isDashboard && gridDashboardView.Visibility == Visibility.Visible) return;
        if (isUpdater && gridUpdaterView.Visibility == Visibility.Visible) return;
        if (isSpoofer && gridSpooferView.Visibility == Visibility.Visible) return;

        _isTabTransitionRunning = true;

        var fadeOutDuration = TimeSpan.FromMilliseconds(140);
        var fadeInDuration = TimeSpan.FromMilliseconds(200);
        var easeOut = new CubicEase { EasingMode = EasingMode.EaseOut };
        var easeIn = new CubicEase { EasingMode = EasingMode.EaseIn };

        Grid showGrid = isDashboard ? gridDashboardView : (isUpdater ? gridUpdaterView : gridSpooferView);
        TranslateTransform showTrans = isDashboard ? transDashboard : (isUpdater ? transUpdater : transSpoofer);

        List<Grid> hideGrids = new();
        List<TranslateTransform> hideTransforms = new();

        if (!isDashboard && gridDashboardView.Visibility == Visibility.Visible)
        {
            hideGrids.Add(gridDashboardView);
            hideTransforms.Add(transDashboard);
        }
        if (!isUpdater && gridUpdaterView.Visibility == Visibility.Visible)
        {
            hideGrids.Add(gridUpdaterView);
            hideTransforms.Add(transUpdater);
        }
        if (!isSpoofer && gridSpooferView.Visibility == Visibility.Visible)
        {
            hideGrids.Add(gridSpooferView);
            hideTransforms.Add(transSpoofer);
        }

        btnTabDashboard.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isDashboard ? "#89B4FA" : "#A6ADC8"));
        btnTabUpdater.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isUpdater ? "#89B4FA" : "#A6ADC8"));
        btnTabSpoofer.Foreground = new SolidColorBrush((Color)ColorConverter.ConvertFromString(isSpoofer ? "#89B4FA" : "#A6ADC8"));

        // Fade out current grids
        var fadeOutAnim = new DoubleAnimation(1.0, 0.0, fadeOutDuration) { EasingFunction = easeIn };
        var slideOutAnim = new DoubleAnimation(0.0, -10.0, fadeOutDuration) { EasingFunction = easeIn };

        fadeOutAnim.Completed += (s, e) =>
        {
            foreach (var g in hideGrids) g.Visibility = Visibility.Collapsed;

            showGrid.Visibility = Visibility.Visible;
            showGrid.Opacity = 0.0;
            showTrans.Y = 12.0;

            var fadeInAnim = new DoubleAnimation(0.0, 1.0, fadeInDuration) { EasingFunction = easeOut };
            var slideInAnim = new DoubleAnimation(12.0, 0.0, fadeInDuration) { EasingFunction = easeOut };

            fadeInAnim.Completed += (s2, e2) =>
            {
                _isTabTransitionRunning = false;
            };

            showTrans.BeginAnimation(TranslateTransform.YProperty, slideInAnim);
            showGrid.BeginAnimation(OpacityProperty, fadeInAnim);
        };

        foreach (var t in hideTransforms) t.BeginAnimation(TranslateTransform.YProperty, slideOutAnim);
        foreach (var g in hideGrids) g.BeginAnimation(OpacityProperty, fadeOutAnim);
    }

    private void OnLogReceived(object? sender, LogEntry entry)
    {
        RunOnUIThread(() =>
        {
            if (Visibility != Visibility.Visible || WindowState == WindowState.Minimized)
                return;

            lstLogs.Items.Add(entry);

            int maxEntries = _settings.MaxLogEntries is > 0 and <= 200 ? _settings.MaxLogEntries : 200;
            while (lstLogs.Items.Count > maxEntries)
            {
                lstLogs.Items.RemoveAt(0);
            }

            if (lstLogs.Items.Count > 0)
            {
                lstLogs.ScrollIntoView(lstLogs.Items[lstLogs.Items.Count - 1]);
            }
        });
    }

    private void OnProcessDetected(object? sender, LaunchEvent e)
    {
        Program.WriteSdkKillerDebugLog(null, "OnProcessDetected Callback", _monitor.IsMonitoring ? "Active" : "Disabled", e.ProcessId);
        RunOnUIThread(UpdateUiState);
    }

    private void OnBootstrapDetected(object? sender, LaunchEvent e)
    {
        Program.WriteSdkKillerDebugLog(null, "OnBootstrapDetected Callback", _monitor.IsMonitoring ? "Active" : "Disabled", e.ProcessId);
        RunOnUIThread(() =>
        {
            SetStatusWidget("Bootstrap Detected", $"Waiting for real game launch. (PID: {e.ProcessId})", "#FAB387", "⚠");
            UpdateMonitoringButtonState(animate: true, overrideState: "waiting");
        });
    }

    private void OnGameLaunchDetected(object? sender, LaunchEvent e)
    {
        Program.WriteSdkKillerDebugLog(null, "OnGameLaunchDetected Callback", _monitor.IsMonitoring ? "Active" : "Disabled", e.ProcessId);
        RunOnUIThread(() =>
        {
            SetStatusWidget("Warframe Detected", $"Discord SDK optimization executed. (PID: {e.ProcessId})", "#89B4FA", "🎮");

            // If OwHelper was running before Warframe launched, mark as injected
            if (_owHelperWasLaunchedBeforeWarframe)
            {
                _warframeDetectedAfterOwHelper = true;
                UpdateOwHelperCardStatus(true);
                UpdateThemeState(animate: true);
            }
            else
            {
                // Warframe launched but OwHelper was never active
                _warframeLaunchedWithoutOwHelper = true;
                UpdateOwHelperCardStatus(false);
            }
        });
    }

    private void btnLaunchWarframe_Click(object sender, RoutedEventArgs e)
    {
        if (!_launcherManager.LaunchWarframe())
        {
            _logger.LogError("Launch attempt failed. Check settings and activity logs for details.");
        }
    }

    private async void btnAioCheat_Click(object sender, RoutedEventArgs e)
    {
        btnAioCheat.IsEnabled = false;
        _logger.LogInfo("⚡ Starting AIO Cheat workflow sequence...");
        SetStatusWidget("AIO Cheat Active", "Executing Warframe & awaiting process initialization...", "#CBA6F7", "⚡");

        bool launched = _launcherManager.LaunchWarframe();
        if (!launched)
        {
            _logger.LogError("❌ Failed to launch Warframe. AIO Cheat sequence cancelled.");
            btnAioCheat.IsEnabled = true;
            return;
        }

        _logger.LogInfo("⏳ Waiting for Warframe.x64.exe process to initialize (ignoring launcher bootstrapper)...");

        bool processFound = false;
        for (int i = 0; i < 60; i++)
        {
            await Task.Delay(1000);
            var procs = System.Diagnostics.Process.GetProcessesByName("Warframe.x64");
            if (procs.Length > 0)
            {
                foreach (var p in procs) p.Dispose();
                processFound = true;
                break;
            }
        }

        if (!processFound)
        {
            _logger.LogWarning("⚠ Warframe.x64 process was not detected within 60s timeout. Sequence cancelled.");
            btnAioCheat.IsEnabled = true;
            return;
        }

        _logger.LogSuccess("✓ Warframe.x64.exe process detected! Holding for 5 seconds stabilization delay...");
        for (int sec = 5; sec > 0; sec--)
        {
            _logger.LogInfo($"⏳ Launching OwHelper in {sec}s...");
            SetStatusWidget("AIO Cheat Active", $"Launching OwHelper in {sec}s...", "#FAB387", "⏳");
            await Task.Delay(1000);
        }

        _logger.LogInfo("☣ Auto-executing OwHelper...");
        bool helperLaunched = _externalLauncher.LaunchOwHelper();
        if (helperLaunched)
        {
            _logger.LogSuccess("✔ AIO Cheat sequence executed successfully!");
            SetStatusWidget("AIO Cheat Active", "Warframe.x64 & OwHelper running", "#A6E3A1", "✔");
        }
        else
        {
            _logger.LogError("❌ Failed to launch OwHelper during AIO Cheat execution.");
        }

        btnAioCheat.IsEnabled = true;
    }

    private void btnToggleMonitoring_Click(object sender, RoutedEventArgs e)
    {
        if (_monitor.IsMonitoring)
        {
            _monitor.Stop();
        }
        else
        {
            _monitor.Start();
        }
        UpdateMonitoringButtonState(animate: true);
        UpdateUiState();
    }

    private void UpdateMonitoringButtonState(bool animate, string? overrideState = null)
    {
        RunOnUIThread(() =>
        {
            string state = overrideState ?? (_monitor.IsMonitoring ? "active" : "disabled");

            Color targetBgColor;
            Color targetFgColor;
            string targetText;
            Color glowColor;

            switch (state)
            {
                case "waiting":
                    targetBgColor = (Color)ColorConverter.ConvertFromString("#FAB387");
                    targetFgColor = (Color)ColorConverter.ConvertFromString("#11111B");
                    targetText = "⚠  Waiting for Game";
                    glowColor = (Color)ColorConverter.ConvertFromString("#FAB387");
                    break;
                case "disabled":
                    targetBgColor = (Color)ColorConverter.ConvertFromString("#F38BA8");
                    targetFgColor = (Color)ColorConverter.ConvertFromString("#FFFFFF");
                    targetText = "✖  SDK Killer Disabled";
                    glowColor = (Color)ColorConverter.ConvertFromString("#F38BA8");
                    break;
                default: // "active"
                    targetBgColor = (Color)ColorConverter.ConvertFromString("#A6E3A1");
                    targetFgColor = (Color)ColorConverter.ConvertFromString("#11111B");
                    targetText = "✔  SDK Killer Active";
                    glowColor = (Color)ColorConverter.ConvertFromString("#A6E3A1");
                    break;
            }

            btnToggleMonitoring.Content = targetText;

            // Apply glow effect
            var glowEffect = new DropShadowEffect
            {
                Color = glowColor,
                BlurRadius = 12,
                ShadowDepth = 0,
                Opacity = 0.35
            };
            btnToggleMonitoring.Effect = glowEffect;

            if (animate)
            {
                var bgAnim = new ColorAnimation(targetBgColor, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = ThemeEasing
                };
                var fgAnim = new ColorAnimation(targetFgColor, TimeSpan.FromMilliseconds(300))
                {
                    EasingFunction = ThemeEasing
                };

                if (btnToggleMonitoring.Background is SolidColorBrush bgBrush && !bgBrush.IsFrozen)
                {
                    bgBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
                }
                else
                {
                    var newBrush = new SolidColorBrush(targetBgColor);
                    btnToggleMonitoring.Background = newBrush;
                    newBrush.BeginAnimation(SolidColorBrush.ColorProperty, bgAnim);
                }

                if (btnToggleMonitoring.Foreground is SolidColorBrush fgBrush && !fgBrush.IsFrozen)
                {
                    fgBrush.BeginAnimation(SolidColorBrush.ColorProperty, fgAnim);
                }
                else
                {
                    var newFgBrush = new SolidColorBrush(targetFgColor);
                    btnToggleMonitoring.Foreground = newFgBrush;
                    newFgBrush.BeginAnimation(SolidColorBrush.ColorProperty, fgAnim);
                }
            }
            else
            {
                var bgB = new SolidColorBrush(targetBgColor);
                bgB.Freeze();
                btnToggleMonitoring.Background = bgB;

                var fgB = new SolidColorBrush(targetFgColor);
                fgB.Freeze();
                btnToggleMonitoring.Foreground = fgB;
            }
        });
    }

    private void RunOnUIThread(Action action)
    {
        if (action == null) return;
        if (Dispatcher.CheckAccess())
        {
            action();
        }
        else
        {
            Dispatcher.InvokeAsync(action, System.Windows.Threading.DispatcherPriority.Normal);
        }
    }

    private async void btnRestore_Click(object sender, RoutedEventArgs e)
    {
        SetStatusWidget("Restoring SDK", "Restoring Discord SDK file...", "#CBA6F7", "🔄");
        await Task.Run(() => _sdkManager.RestoreSdk());
        await Task.Delay(1200);
        UpdateUiState();
    }

    private async void btnClean_Click(object sender, RoutedEventArgs e)
    {
        if (!_settings.EnableCacheCleaner)
        {
            _logger.LogWarning("Cache cleaner is disabled in Settings.");
            return;
        }

        SetStatusWidget("Cleaning Cache", "Removing temporary Warframe files...", "#FAB387", "🧹");
        await Task.Run(() => WarframeCleaner.CleanWarframeFiles(_logger, _settings));
        await Task.Delay(1500);
        UpdateUiState();
    }

    private void btnLaunchOwHelper_Click(object sender, RoutedEventArgs e)
    {
        _externalLauncher.LaunchOwHelper();
    }

    private void SetStatusWidget(string statusTitle, string subtext, string hexColor, string iconSymbol)
    {
        RunOnUIThread(() =>
        {
            bool iconChanged = txtCardStatusIcon.Text != iconSymbol;
            txtCardStatusIcon.Text = iconSymbol;

            if (iconChanged)
            {
                PulseIconAnimation(txtCardStatusIcon);
            }

            FadeTextTransition(txtCardStatus, statusTitle, hexColor);
            FadeTextTransition(txtStatusSubtext, subtext, null);
        });
    }

    // UI animation helpers

    private void PulseIconAnimation(TextBlock icon)
    {
        RunOnUIThread(() =>
        {
            if (icon.RenderTransform is ScaleTransform st)
            {
                var pulseUp = new DoubleAnimation(1.0, 1.35, TimeSpan.FromMilliseconds(120))
                {
                    EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut },
                    AutoReverse = true
                };
                st.BeginAnimation(ScaleTransform.ScaleXProperty, pulseUp);
                st.BeginAnimation(ScaleTransform.ScaleYProperty, pulseUp);
            }
        });
    }

    private void FadeTextTransition(TextBlock textBlock, string newText, string? hexColor)
    {
        RunOnUIThread(() =>
        {
            if (textBlock.Text == newText && hexColor == null) return;

            var fadeOut = new DoubleAnimation(1.0, 0.0, TimeSpan.FromMilliseconds(80))
            {
                EasingFunction = new SineEase { EasingMode = EasingMode.EaseIn }
            };

            fadeOut.Completed += (_, _) =>
            {
                RunOnUIThread(() =>
                {
                    textBlock.Text = newText;
                    if (hexColor != null)
                    {
                        var brush = new SolidColorBrush((Color)ColorConverter.ConvertFromString(hexColor));
                        brush.Freeze();
                        textBlock.Foreground = brush;
                    }

                    var fadeIn = new DoubleAnimation(0.0, 1.0, TimeSpan.FromMilliseconds(150))
                    {
                        EasingFunction = new SineEase { EasingMode = EasingMode.EaseOut }
                    };
                    textBlock.BeginAnimation(OpacityProperty, fadeIn);
                });
            };

            textBlock.BeginAnimation(OpacityProperty, fadeOut);
        });
    }

    private void UpdateUiState()
    {
        RunOnUIThread(() =>
        {
            bool isOwHelperInjected = _owHelperWasLaunchedBeforeWarframe && _warframeDetectedAfterOwHelper;
            var theme = LauncherThemeManager.GetTheme(_launcherManager.SelectedLauncher, _externalLauncher.IsOwHelperActive(), isOwHelperInjected);

            // SDK Killer Status Card
            if (_monitor.IsMonitoring)
            {
                SetStatusWidget("SDK Killer Active", "Watching Warframe launch", "#A6E3A1", "✔");
            }
            else
            {
                SetStatusWidget("SDK Killer Disabled", "Waiting for activation", "#F38BA8", "✖");
            }

            // Monitoring Toggle Button
            UpdateMonitoringButtonState(animate: false);

            // Discord SDK Status
            var sdkStatus = _sdkManager.GetStatusMessage();
            txtSdkStatus.Text = sdkStatus;

            SolidColorBrush sdkBrush;
            if (_sdkManager.IsRemoved)
            {
                sdkBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#A6E3A1"));
            }
            else if (_sdkManager.BackupExists)
            {
                sdkBrush = new SolidColorBrush((Color)ColorConverter.ConvertFromString("#FAB387"));
            }
            else
            {
                sdkBrush = new SolidColorBrush(theme.AccentColor);
            }
            sdkBrush.Freeze();
            txtSdkStatus.Foreground = sdkBrush;
        });
    }

    // Ambient lighting rendering loop

    private void Window_MouseMove(object sender, MouseEventArgs e)
    {
        if (ActualWidth > 0 && ActualHeight > 0)
        {
            Point pos = e.GetPosition(this);
            _targetMousePos = new Point(
                Math.Clamp(pos.X / ActualWidth, 0.0, 1.0),
                Math.Clamp(pos.Y / ActualHeight, 0.0, 1.0)
            );
            _lastMouseMoveTime = DateTime.UtcNow;
        }
    }

    private void OnAmbientEngineRendering(object? sender, EventArgs e)
    {
        if (Visibility != Visibility.Visible || WindowState == WindowState.Minimized || !IsLoaded || ActualWidth <= 0 || ActualHeight <= 0)
            return;

        long currentTicks = DateTime.UtcNow.Ticks;
        double elapsedMs = (currentTicks - _lastRenderTicks) / (double)TimeSpan.TicksPerMillisecond;
        if (elapsedMs < (_targetFrameIntervalMs - 0.5)) return; // Dynamic refresh rate frame throttling (60, 100, 144, 165, 240+ Hz)
        _lastRenderTicks = currentTicks;

        double dt = Math.Min(0.05, Math.Max(0.003, elapsedMs / 1000.0));

        // Render 60 FPS Cinematic Background Engine Frame (3D Particles, Fog, Energy Core & Parallax)
        cinematicBgEngine.UpdateEngineFrame(_targetMousePos.X, _targetMousePos.Y, dt);

        // 1. Multi-Layer Position Interpolation (Fast Core, Medium Ambient, Slow Cinematic Trail)
        _coreGlowPos.X += (_targetMousePos.X - _coreGlowPos.X) * 0.18;
        _coreGlowPos.Y += (_targetMousePos.Y - _coreGlowPos.Y) * 0.18;

        _ambientGlowPos.X += (_targetMousePos.X - _ambientGlowPos.X) * 0.09;
        _ambientGlowPos.Y += (_targetMousePos.Y - _ambientGlowPos.Y) * 0.09;

        _trailGlowPos.X += (_targetMousePos.X - _trailGlowPos.X) * 0.045;
        _trailGlowPos.Y += (_targetMousePos.Y - _trailGlowPos.Y) * 0.045;

        // Update Brush Positions
        BackgroundGlowBrushCore.Center = _coreGlowPos;
        BackgroundGlowBrushCore.GradientOrigin = _coreGlowPos;

        BackgroundGlowBrushAmbient.Center = _ambientGlowPos;
        BackgroundGlowBrushAmbient.GradientOrigin = _ambientGlowPos;

        BackgroundGlowBrushTrail.Center = _trailGlowPos;
        BackgroundGlowBrushTrail.GradientOrigin = _trailGlowPos;

        // 2. Idle Ambient Light Breathing Atmosphere
        double idleSeconds = (DateTime.UtcNow - _lastMouseMoveTime).TotalSeconds;
        if (idleSeconds > 1.2)
        {
            _idleBreathingPhase += 0.03;
            double breath = Math.Sin(_idleBreathingPhase) * 0.04;
            BackgroundGlowBrushCore.RadiusX = 0.45 + breath;
            BackgroundGlowBrushCore.RadiusY = 0.45 + breath;
            BackgroundGlowBrushAmbient.RadiusX = 0.85 + (breath * 1.5);
            BackgroundGlowBrushAmbient.RadiusY = 0.85 + (breath * 1.5);
        }
        else
        {
            BackgroundGlowBrushCore.RadiusX = 0.45;
            BackgroundGlowBrushCore.RadiusY = 0.45;
            BackgroundGlowBrushAmbient.RadiusX = 0.85;
            BackgroundGlowBrushAmbient.RadiusY = 0.85;
        }

        // 3. Proximity Lighting Interaction with Dashboard Cards
        Point mousePx = new Point(_coreGlowPos.X * ActualWidth, _coreGlowPos.Y * ActualHeight);
        UpdateCardProximityGlow(cardEpic, mousePx);
        UpdateCardProximityGlow(cardSteam, mousePx);
        UpdateCardProximityGlow(cardStandalone, mousePx);
        UpdateCardProximityGlow(animCardStatus, mousePx);
        UpdateCardProximityGlow(animCardCounter, mousePx);
        UpdateCardProximityGlow(animCardSdk, mousePx);
        UpdateCardProximityGlow(animLogConsole, mousePx);
    }

    private void UpdateCardProximityGlow(FrameworkElement? element, Point mousePx)
    {
        if (element == null || !element.IsVisible) return;

        try
        {
            GeneralTransform transform = element.TransformToAncestor(this);
            Point cardPos = transform.Transform(new Point(element.ActualWidth / 2.0, element.ActualHeight / 2.0));

            double dx = mousePx.X - cardPos.X;
            double dy = mousePx.Y - cardPos.Y;
            double distance = Math.Sqrt((dx * dx) + (dy * dy));

            const double maxDistance = 280.0;
            if (distance < maxDistance)
            {
                double proximity = 1.0 - (distance / maxDistance);
                if (element.Effect is DropShadowEffect shadow && !shadow.IsFrozen)
                {
                    shadow.Opacity = 0.25 + (proximity * 0.35);
                }
            }
        }
        catch
        {
            // Ignore layout transform errors during window resize or tab transitions
        }
    }
}
