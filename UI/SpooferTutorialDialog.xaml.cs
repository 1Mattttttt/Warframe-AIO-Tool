using System;
using System.Diagnostics;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace GameLauncher.UI;

public partial class SpooferTutorialDialog : Window
{
    private int _currentStep = 1;
    private const int TotalSteps = 8;
    private bool _isClosing;

    public SpooferTutorialDialog()
    {
        InitializeComponent();
        Loaded += SpooferTutorialDialog_Loaded;
    }

    private void SpooferTutorialDialog_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateOpen();
        UpdateStepView();
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

    private void AnimateCloseAndClose()
    {
        if (_isClosing) return;
        _isClosing = true;

        var duration = TimeSpan.FromMilliseconds(180);
        var ease = new CubicEase { EasingMode = EasingMode.EaseIn };

        var scaleXAnim = new DoubleAnimation(1.0, 0.85, duration) { EasingFunction = ease };
        var scaleYAnim = new DoubleAnimation(1.0, 0.85, duration) { EasingFunction = ease };
        var opacityAnim = new DoubleAnimation(1.0, 0.0, duration) { EasingFunction = ease };

        opacityAnim.Completed += (s, e) => Close();

        scaleDialog.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleXProperty, scaleXAnim);
        scaleDialog.BeginAnimation(System.Windows.Media.ScaleTransform.ScaleYProperty, scaleYAnim);
        DialogBorderContainer.BeginAnimation(OpacityProperty, opacityAnim);
    }

    private void btnBack_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep > 1)
        {
            _currentStep--;
            UpdateStepView();
        }
    }

    private void btnNext_Click(object sender, RoutedEventArgs e)
    {
        if (_currentStep < TotalSteps)
        {
            _currentStep++;
            UpdateStepView();
        }
        else
        {
            AnimateCloseAndClose();
        }
    }

    private void btnDownloadWarp_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            Process.Start(new ProcessStartInfo
            {
                FileName = "https://1.1.1.1/",
                UseShellExecute = true
            });
        }
        catch (Exception ex)
        {
            MessageBox.Show($"Could not open browser: {ex.Message}", "URL Redirect Error", MessageBoxButton.OK, MessageBoxImage.Warning);
        }
    }

    private void UpdateStepView()
    {
        txtStepBadge.Text = $"Step {_currentStep} of {TotalSteps}";
        pbTutorialProgress.Value = _currentStep;

        step1View.Visibility = _currentStep == 1 ? Visibility.Visible : Visibility.Collapsed;
        step2View.Visibility = _currentStep == 2 ? Visibility.Visible : Visibility.Collapsed;
        step3View.Visibility = _currentStep == 3 ? Visibility.Visible : Visibility.Collapsed;
        step4View.Visibility = _currentStep == 4 ? Visibility.Visible : Visibility.Collapsed;
        step5View.Visibility = _currentStep == 5 ? Visibility.Visible : Visibility.Collapsed;
        step6View.Visibility = _currentStep == 6 ? Visibility.Visible : Visibility.Collapsed;
        step7View.Visibility = _currentStep == 7 ? Visibility.Visible : Visibility.Collapsed;
        step8View.Visibility = _currentStep == 8 ? Visibility.Visible : Visibility.Collapsed;

        btnBack.IsEnabled = _currentStep > 1;
        btnNext.Content = _currentStep == TotalSteps ? "Finish  ✔" : "Next  ▶";
    }

    private void TitleBar_MouseLeftButtonDown(object sender, MouseButtonEventArgs e)
    {
        if (e.ChangedButton == MouseButton.Left)
        {
            DragMove();
        }
    }

    private void btnClose_Click(object sender, RoutedEventArgs e)
    {
        AnimateCloseAndClose();
    }
}
