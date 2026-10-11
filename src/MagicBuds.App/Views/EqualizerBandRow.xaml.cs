using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagicBuds.App.Views;

/// <summary>
/// One equalizer band: its name over a -6 to 6 dB slider.
/// </summary>
public sealed partial class EqualizerBandRow : UserControl
{
    public static readonly DependencyProperty LabelProperty =
        DependencyProperty.Register(nameof(Label), typeof(string), typeof(EqualizerBandRow), new PropertyMetadata(""));

    public static readonly DependencyProperty ValueProperty =
        DependencyProperty.Register(nameof(Value), typeof(double), typeof(EqualizerBandRow), new PropertyMetadata(0.0));

    public EqualizerBandRow()
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
}
