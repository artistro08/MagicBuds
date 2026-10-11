using Microsoft.UI.Xaml;
using MagicBuds.Presentation.Headsets;

namespace MagicBuds.App;

/// <summary>
/// x:Bind functions for the noise control tiles' command parameter and the buds list's status
/// line (caution color for an error).
/// </summary>
public static class TileStyles
{
    /// <summary>
    /// A noise mode as the text SetNoiseModeCommand takes.
    /// </summary>
    public static string ModeName(NoiseMode mode) => mode.ToString();

    public static Style PickStatus(bool error) =>
        (Style)Application.Current.Resources[error ? "CautionTextStyle" : "SecondaryTextStyle"];
}
