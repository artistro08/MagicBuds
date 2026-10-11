using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagicBuds.App.Views;

/// <summary>
/// One battery level as an icon and text, like "Left 68%".
/// </summary>
public sealed partial class BatteryLabel : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(BatteryLabel), new PropertyMetadata(""));

    public static readonly DependencyProperty LevelTextProperty =
        DependencyProperty.Register(nameof(LevelText), typeof(string), typeof(BatteryLabel), new PropertyMetadata(""));

    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.Register(nameof(Glyph), typeof(string), typeof(BatteryLabel), new PropertyMetadata(""));

    public BatteryLabel()
    {
        InitializeComponent();
    }

    /// <summary>
    /// "Left", "Right" or "Case".
    /// </summary>
    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// "68%", or "—" while unknown.
    /// </summary>
    public string LevelText
    {
        get => (string)GetValue(LevelTextProperty);
        set => SetValue(LevelTextProperty, value);
    }

    /// <summary>
    /// Segoe Fluent Icons battery glyph for the level.
    /// </summary>
    public string Glyph
    {
        get => (string)GetValue(GlyphProperty);
        set => SetValue(GlyphProperty, value);
    }
}
