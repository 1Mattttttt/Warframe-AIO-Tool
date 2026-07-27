using System;
using System.Windows;
using System.Windows.Input;
using System.Windows.Media.Animation;

namespace GameLauncher.UI;

public partial class DynamicIpChangerDialog : Window
{
    private bool _isClosing;

    public DynamicIpChangerDialog()
    {
        InitializeComponent();
        Loaded += DynamicIpChangerDialog_Loaded;
    }

    private void DynamicIpChangerDialog_Loaded(object sender, RoutedEventArgs e)
    {
        AnimateOpen();
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
