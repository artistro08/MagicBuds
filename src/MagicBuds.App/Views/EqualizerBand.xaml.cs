using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagicBuds.App.Views;

/// <summary>
/// One equalizer band: its gain over an upright -6 to 6 dB slider, and its name under it.
/// </summary>
public sealed partial class EqualizerBand : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(EqualizerBand), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(EqualizerBand), new PropertyMetadata(0.0));

    public EqualizerBand()
    {
        InitializeComponent();
    }

    public string Label
    {
        get => (string)GetValue(LabelProperty);
        set => SetValue(LabelProperty, value);
    }

    /// <summary>
    /// Gain in dB.
    /// </summary>
    public double Value
    {
        get => (double)GetValue(ValueProperty);
        set => SetValue(ValueProperty, value);
    }

    // "+1.5 dB", "0 dB", "-3 dB"
    public static string FormatGain(double gain) => $"{(gain > 0 ? "+" : "")}{gain:0.#} dB";
}
