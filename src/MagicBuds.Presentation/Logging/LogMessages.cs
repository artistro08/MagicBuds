using Microsoft.Extensions.Logging;

namespace MagicBuds.Presentation.Logging;

/// <summary>
/// Every log message the Presentation layer writes, as source-generated LoggerMessage delegates.
/// </summary>
internal static partial class LogMessages
{
    // =========================================================================
    // HEADSET MANAGER (1xx)
    // =========================================================================

    // No Bluetooth address: logs get attached to bug reports
    [LoggerMessage(EventId = 100, Level = LogLevel.Information, Message = "Windows connected {Name}")]
    public static partial void WindowsConnected(ILogger logger, string name);

    [LoggerMessage(EventId = 101, Level = LogLevel.Information, Message = "Windows disconnected {Name}")]
    public static partial void WindowsDisconnected(ILogger logger, string name);

    [LoggerMessage(EventId = 102, Level = LogLevel.Information, Message = "Control link to {Name} dropped")]
    public static partial void LinkDropped(ILogger logger, string name);

    [LoggerMessage(EventId = 103, Level = LogLevel.Information, Message = "Control link to {Name} open")]
    public static partial void LinkOpen(ILogger logger, string name);

    [LoggerMessage(EventId = 104, Level = LogLevel.Warning, Message = "Couldn't connect to {Name} (attempt {Attempt})")]
    public static partial void ConnectFailed(ILogger logger, Exception exception, string name, int attempt);

    [LoggerMessage(EventId = 105, Level = LogLevel.Information, Message = "Reconnecting {Name}")]
    public static partial void Reconnecting(ILogger logger, string name);

    [LoggerMessage(EventId = 106, Level = LogLevel.Information, Message = "{Name} was unpaired")]
    public static partial void Unpaired(ILogger logger, string name);

    // =========================================================================
    // HEADSET CONTROLS (2xx)
    // =========================================================================

    [LoggerMessage(EventId = 200, Level = LogLevel.Warning, Message = "Couldn't change {Setting} on {Headset}")]
    public static partial void CommandFailed(ILogger logger, Exception exception, string setting, string headset);

    // =========================================================================
    // MAESTRO (3xx)
    // =========================================================================

    [LoggerMessage(EventId = 300, Level = LogLevel.Debug, Message = "Sent {Packet}")]
    public static partial void PacketSent(ILogger logger, string packet);

    [LoggerMessage(EventId = 301, Level = LogLevel.Debug, Message = "Received {Packet}")]
    public static partial void PacketReceived(ILogger logger, string packet);

    [LoggerMessage(EventId = 304, Level = LogLevel.Debug, Message = "Read {Bytes}")]
    public static partial void BytesRead(ILogger logger, string bytes);

    [LoggerMessage(EventId = 306, Level = LogLevel.Debug, Message = "Buds offer {Service}")]
    public static partial void ServiceOffered(ILogger logger, string service);

    [LoggerMessage(EventId = 307, Level = LogLevel.Warning, Message = "Couldn't handle a {Method} message from the buds")]
    public static partial void StreamMessageFailed(ILogger logger, Exception exception, string method);

    [LoggerMessage(EventId = 305, Level = LogLevel.Debug, Message = "Wrote {Bytes}")]
    public static partial void BytesWritten(ILogger logger, string bytes);

    [LoggerMessage(EventId = 302, Level = LogLevel.Debug, Message = "Buds didn't read {Setting} (status {Status})")]
    public static partial void SettingUnreadable(ILogger logger, string setting, uint status);

    [LoggerMessage(EventId = 303, Level = LogLevel.Warning, Message = "Buds sent an unreadable reply for {Setting}")]
    public static partial void SettingMalformed(ILogger logger, Exception exception, string setting);

    // =========================================================================
    // SETTINGS (4xx)
    // =========================================================================

    [LoggerMessage(EventId = 400, Level = LogLevel.Warning, Message = "Couldn't read the startup task")]
    public static partial void StartupTaskReadFailed(ILogger logger, Exception exception);

    [LoggerMessage(EventId = 401, Level = LogLevel.Warning, Message = "Couldn't change the startup task")]
    public static partial void StartupTaskChangeFailed(ILogger logger, Exception exception);
}
