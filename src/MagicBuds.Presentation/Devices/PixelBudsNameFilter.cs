namespace MagicBuds.Presentation.Devices;

/// <summary>
/// Decides whether a Bluetooth device name belongs to Pixel Buds.
/// </summary>
/// <remarks>
/// Windows names them "Pixel Buds Pro 2", or "Sam's Pixel Buds Pro 2" once set up on a phone.
/// Buds renamed later keep working: <see cref="HeadsetManager"/> remembers them by address.
/// </remarks>
public static class PixelBudsNameFilter
{
    public static bool IsPixelBuds(string? name) =>
        !string.IsNullOrWhiteSpace(name) && name.Contains("Pixel Buds", StringComparison.OrdinalIgnoreCase);
}
