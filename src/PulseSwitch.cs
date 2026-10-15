using System.Windows;
using System.Windows.Controls;
using System.Windows.Media;
using System.Windows.Media.Animation;
using CheckBox = System.Windows.Controls.CheckBox;
using Color = System.Windows.Media.Color;

namespace XboxRapidFire;

/// <summary>A vector toggle whose visual transitions never delay the bound setting.</summary>
public sealed class PulseSwitch : CheckBox
{
    private TranslateTransform? slide;
    private SolidColorBrush? trackBrush, thumbBrush;
    private const double Travel = 24;
    private static readonly Duration Transition = new(TimeSpan.FromMilliseconds(200));

    public override void OnApplyTemplate()
    {
        base.OnApplyTemplate();
        var track = GetTemplateChild("PART_Track") as Border;
        var thumb = GetTemplateChild("PART_Thumb") as System.Windows.Shapes.Ellipse;
        slide = null;
        if (thumb is not null) thumb.RenderTransform = slide = new TranslateTransform();
        // Each control owns its brushes so rapid toggles cannot animate another button.
        if (track is not null) track.Background = trackBrush = new SolidColorBrush();
        if (thumb is not null) thumb.Fill = thumbBrush = new SolidColorBrush();
        UpdateVisual(false);
    }

    protected override void OnChecked(RoutedEventArgs e) { base.OnChecked(e); UpdateVisual(true); }
    protected override void OnUnchecked(RoutedEventArgs e) { base.OnUnchecked(e); UpdateVisual(true); }

    private void UpdateVisual(bool animate)
    {
        if (slide is null || trackBrush is null || thumbBrush is null) return;
        bool enabled = IsChecked == true;
        bool motion = animate && IsLoaded && SystemParameters.ClientAreaAnimation;
        double destination = enabled ? Travel : 0;
        double start = slide.X;
        slide.BeginAnimation(TranslateTransform.XProperty, null);
        slide.X = destination;
        if (motion)
            slide.BeginAnimation(TranslateTransform.XProperty, new DoubleAnimation(start, destination, Transition)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
        AnimateColor(trackBrush, enabled ? Color.FromRgb(165, 235, 131) : Color.FromRgb(64, 76, 75), motion);
        AnimateColor(thumbBrush, enabled ? Color.FromRgb(23, 36, 23) : Color.FromRgb(220, 229, 223), motion);
    }

    private static void AnimateColor(SolidColorBrush brush, Color destination, bool motion)
    {
        Color start = brush.Color;
        brush.BeginAnimation(SolidColorBrush.ColorProperty, null);
        brush.Color = destination;
        if (motion)
            brush.BeginAnimation(SolidColorBrush.ColorProperty, new ColorAnimation(start, destination, Transition)
            {
                EasingFunction = new CubicEase { EasingMode = EasingMode.EaseOut },
                FillBehavior = FillBehavior.Stop
            });
    }
}
