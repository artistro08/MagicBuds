using Microsoft.UI.Xaml;
using Microsoft.UI.Xaml.Controls;

namespace MagicBuds.App.Views;

/// <summary>
/// One touch gesture and what it does, in the Controls and gestures page's list.
/// </summary>
public sealed partial class GestureInfoRow : UserControl
{
    public static readonly DependencyProperty SymbolProperty =
        DependencyProperty.Register(nameof(Symbol), typeof(string), typeof(GestureInfoRow), new PropertyMetadata(""));

    public static readonly DependencyProperty TitleProperty =
        DependencyProperty.Register(nameof(Title), typeof(string), typeof(GestureInfoRow), new PropertyMetadata(""));

    public static readonly DependencyProperty SubtitleProperty =
        DependencyProperty.Register(nameof(Subtitle), typeof(string), typeof(GestureInfoRow), new PropertyMetadata(""));

    public GestureInfoRow()
    {
        InitializeComponent();
    }

    /// <summary>
    /// Short mark for the gesture, like "••" for a double tap.
    /// </summary>
    public string Symbol
    {
        get => (string)GetValue(SymbolProperty);
        set => SetValue(SymbolProperty, value);
    }

    public string Title
    {
        get => (string)GetValue(TitleProperty);
        set => SetValue(TitleProperty, value);
    }

    public string Subtitle
    {
        get => (string)GetValue(SubtitleProperty);
        set => SetValue(SubtitleProperty, value);
    }
}
