using MagicBuds.Presentation.Maestro;

namespace MagicBuds.Presentation.Headsets;

/// <summary>
/// Turns control link failures into messages for people.
/// </summary>
public static class HeadsetErrorMessages
{
    public static string Describe(Exception exception)
    {
        ArgumentNullException.ThrowIfNull(exception);

        return exception switch
        {
            TimeoutException => "Your buds didn't respond. Try again.",
            MaestroException => "Your buds didn't accept that setting.",
            IOException => "Your buds disconnected.",
            _ => "Something went wrong talking to your buds.",
        };
    }
}
