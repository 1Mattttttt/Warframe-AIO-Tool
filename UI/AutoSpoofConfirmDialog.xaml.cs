using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;
using System.Windows.Threading;

namespace GameLauncher.UI;

public partial class AutoSpoofConfirmDialog : Window
{
    public bool IsConfirmed { get; private set; }

    private DispatcherTimer? _countdownTimer;
    private int _secondsRemaining = 5;
    private bool _isClosing;

    public AutoSpoofConfirmDialog()
    {
        InitializeComponent();
        Loaded += AutoSpoofConfirmDialog_Loaded;
    }

    private void AutoSpoofConfirmDialog_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateOpen();
        StartCountdown();
    }

    private void StartCountdown()
    {
        btnConfirm.IsEnabled = false;
        btnConfirm.Content = $"Confirm ({_secondsRemaining})";

        _countdownTimer = new DispatcherTimer
        {
            Interval = TimeSpan.FromSeconds(1)
        };

        _countdownTimer.Tick += (s, e) =>
        {
            _secondsRemaining--;
            if (_secondsRemaining > 0)
            {
                btnConfirm.Content = $"Confirm ({_secondsRemaining})";
            }
            else
            {
                _countdownTimer.Stop();
                btnConfirm.Content = "Confirm";
                btnConfirm.IsEnabled = true;
            }
        };

        _countdownTimer.Start();
    }

    private void AnimateOpen()
    {
        var duration = TimeSpan.FromMilliseconds(220);
        var ease = new CubicEase { EasingMode = EasingMode.EaseOut };

        var scaleXAnim = new DoubleAnimation(0.85, 1.0, duration) { EasingFunction = ease };
        var scaleYAnim = new DoubleAnimation(0.85, 1.0, duration) { EasingFunction = ease };
        var opacityAnim = new DoubleAnimation(0.0, 1.0, duration) { EasingFunction = ease };

        scaleDialog.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnim);
        scaleDialog.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnim);
        DialogBorderContainer.BeginAnimation(OpacityProperty, opacityAnim);
    }

    private void AnimateCloseAndClose(bool confirmed)
    {
        if (_isClosing) return;
        _isClosing = true;

        _countdownTimer?.Stop();
        IsConfirmed = confirmed;

        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var scaleXAnim = new DoubleAnimation(1.0, 0.85, duration) { EasingFunction = ease };
        var scaleYAnim = new DoubleAnimation(1.0, 0.85, duration) { EasingFunction = ease };
        var opacityAnim = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

        opacityAnim.Completed += (s, e) =>
        {
            DialogResult = confirmed;
            Close();
        };

        scaleDialog.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnim);
        scaleDialog.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnim);
        DialogBorderContainer.BeginAnimation(OpacityProperty, opacityAnim);
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void btnConfirm_Click(object sender, RoutedEventArgs e)
    {
        AnimateCloseAndClose(true);
    }

    private void btnCancel_Click(object sender, RoutedEventArgs e)
    {
        AnimateCloseAndClose(false);
    }
}
