using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;
using Microsoft.UI.Xaml.Media;
using Microsoft.UI.Xaml.Media.Animation;

namespace MagicBuds.App.Views;

/// <summary>
/// One bud's (or the case's) picture with its battery level as a ring around it.
/// </summary>
public sealed partial class BudBatteryRing : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(BudBatteryRing), new PropertyMetadata("", OnTextChanged));

    public static readonly DependencyProperty LevelProperty =
        DependencyProperty.Register(nameof(Level), typeof(double), typeof(BudBatteryRing), new PropertyMetadata(0.0, (ring, _) => ((BudBatteryRing)ring).AnimateLevel()));

    // Quick, then easing into the new level
    private static readonly TimeSpan LevelDuration = TimeSpan.FromMilliseconds(350);

    private Storyboard? _levelAnimation;

    public static readonly DependencyProperty LevelTextProperty =
        DependencyProperty.Register(nameof(LevelText), typeof(string), typeof(BudBatteryRing), new PropertyMetadata("", OnTextChanged));

    public static readonly DependencyProperty PictureProperty =
        DependencyProperty.Register(nameof(Picture), typeof(ImageSource), typeof(BudBatteryRing), new PropertyMetadata(null, (ring, _) => ((BudBatteryRing)ring).Bindings.Update()));

    public BudBatteryRing()
    {
        InitializeComponent();
    }

    /// <summary>
    /// "Left", "Case" or "Right".
    /// </summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Battery level, 0 to 100. The ring shows empty while it's unknown.
    /// </summary>
    public double Level
    {
        get => (double)GetValue(LevelProperty);
        set => SetValue(LevelProperty, value);
    }

    /// <summary>
    /// The level as shown under the ring, like "69%", or "—" while unknown.
    /// </summary>
    public string LevelText
    {
        get => (string)GetValue(LevelTextProperty);
        set => SetValue(LevelTextProperty, value);
    }

    public ImageSource? Picture
    {
        get => (ImageSource?)GetValue(PictureProperty);
        set => SetValue(PictureProperty, value);
    }

    public Visibility PlaceholderVisibility => Picture is null ? Visibility.Visible : Visibility.Collapsed;

    public string AccessibleText => $"{Label} battery {LevelText}";

    // Runs the ring from wherever it is now to the new level, so a change mid-animation
    // carries on smoothly instead of jumping
    private void AnimateLevel()
    {
        _levelAnimation?.Pause();
        var from = Ring.Value;
        _levelAnimation?.Stop();
        Ring.Value = from;

        var animation = new DoubleAnimation
        {
            From                     = from,
            To                       = Level,
            Duration                 = LevelDuration,
            EasingFunction           = new CubicEase { EasingMode = EasingMode.EaseOut },
            EnableDependentAnimation = true,
        };
        Storyboard.SetTarget(animation, Ring);
        Storyboard.SetTargetProperty(animation, nameof(ProgressRing.Value));

        _levelAnimation = new Storyboard { Children = { animation } };
        _levelAnimation.Begin();
    }

    private static void OnTextChanged(DependencyObject ring, DependencyPropertyChangedEventArgs e) => ((BudBatteryRing)ring).Bindings.Update();
}
