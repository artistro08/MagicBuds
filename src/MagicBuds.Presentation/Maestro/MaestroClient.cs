using System.Collections.Concurrent;
using Google.Protobuf;
using Microsoft.Extensions.Logging;
using MagicBuds.Presentation.Logging;
using MagicBuds.Presentation.Maestro.Proto;

namespace MagicBuds.Presentation.Maestro;

/// <summary>
/// The buds refused or failed an RPC.
/// </summary>
/// <param name="status">Pigweed status code the buds answered with (2 unknown, 9 failed precondition, …).</param>
public sealed class MaestroException(uint status, string method)
    : IOException($"The buds answered {method} with status {status}.")
{
    public uint Status { get; } = status;
}

/// <summary>
/// Pigweed RPC client for the buds' Maestro service, over the RFCOMM byte stream.
/// </summary>
/// <remarks>
/// Every request carries a fresh call ID, and replies are matched back to their caller by it.
/// Unary calls complete on the RESPONSE packet; streams stay open and deliver each
/// SERVER_STREAM packet until disposed. Frames are addressed from the host to the bud that
/// owns the channel, the same way mypixelbuds.google.com addresses them (docs/protocol).
/// </remarks>
internal sealed class MaestroClient : IAsyncDisposable
{
    /// <summary>
    /// Channels that reach the left and right bud. The buds serve one pair per session (19/21
    /// or 24/26, seen in different captures), so the client asks all of them.
    /// </summary>
    public const uint LeftChannel   = 24;
    public const uint RightChannel  = 26;
    public const uint LeftChannelA  = 19;
    public const uint RightChannelA = 21;

    public static readonly uint[] Channels = [LeftChannelA, RightChannelA, LeftChannel, RightChannel];

    public const string Service = "maestro_pw.Maestro";

    public static readonly TimeSpan CallTimeout = TimeSpan.FromSeconds(5);

    // Pigweed's call ID for packets that don't belong to one particular call
    private const uint OpenCallId = uint.MaxValue;

    // Pigweed statuses: a cancelled call, and the answer to a packet for an unknown call
    private const uint Cancelled          = 1;
    private const uint FailedPrecondition = 9;

    // Frame address peers
    private const uint HostPeer  = 1;
    private const uint LeftPeer  = 3;
    private const uint RightPeer = 4;

    private readonly Stream _input;
    private readonly Stream _output;
    private readonly ILogger _logger;
    private readonly SemaphoreSlim _writeLock = new(1, 1);
    private readonly ConcurrentDictionary<uint, Call> _calls = [];
    private readonly CancellationTokenSource _lifetime = new();
    private readonly Task _readLoop;
    private readonly TaskCompletionSource<uint> _announcedChannel = new(TaskCreationOptions.RunContinuationsAsynchronously);

    // Random per session, so a call left open on the buds by an earlier session can't share an
    // ID with one of this session's: its packets then count as unknown and get turned away
    private int _nextCallId = Random.Shared.Next(1, 1 << 24);

    /// <param name="input">Bytes from the buds.</param>
    /// <param name="output">Bytes to the buds.</param>
    /// <param name="logger">Gets every packet at Debug level.</param>
    public MaestroClient(Stream input, Stream output, ILogger logger)
    {
        _input    = input;
        _output   = output;
        _logger   = logger;
        _readLoop = Task.Run(ReadLoopAsync);
    }

    /// <summary>
    /// The channel the buds announce their software info on right after the link opens: the
    /// one this session should use.
    /// </summary>
    public Task<uint> AnnouncedChannel => _announcedChannel.Task;

    /// <summary>
    /// Raised once when the byte stream ends or fails. Pending calls fail with it.
    /// </summary>
    public event EventHandler? Closed;

    /// <summary>
    /// Pigweed's 65599 hash of a service or method name, which packets carry instead of names.
    /// </summary>
    public static uint Hash(string name)
    {
        var hash = (uint)name.Length;
        var coefficient = 65599u;
        foreach (var character in name)
        {
            hash += coefficient * character;
            coefficient *= 65599u;
        }
        return hash;
    }

    /// <summary>
    /// Calls a unary method and returns the reply payload.
    /// </summary>
    /// <exception cref="MaestroException">The buds answered with a non-zero status.</exception>
    /// <exception cref="TimeoutException">No reply within <see cref="CallTimeout"/>.</exception>
    /// <exception cref="IOException">The link closed.</exception>
    public async Task<ByteString> CallAsync(uint channel, string method, IMessage? request = null, TimeSpan? timeout = null, CancellationToken cancellationToken = default)
    {
        var call = new Call(method, channel, null);
        var callId = Register(call);
        try
        {
            await SendAsync(channel, method, callId, request, cancellationToken).ConfigureAwait(false);
            return await call.Completion.Task.WaitAsync(timeout ?? CallTimeout, cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _calls.TryRemove(callId, out _);
        }
    }

    /// <summary>
    /// Opens a server stream. <paramref name="onMessage"/> runs on the read thread for each
    /// message until the returned handle is disposed or the link closes. Disposing the handle
    /// also cancels the stream on the buds.
    /// </summary>
    public async Task<IDisposable> SubscribeAsync(uint channel, string method, Action<ByteString> onMessage, CancellationToken cancellationToken = default)
    {
        var call = new Call(method, channel, onMessage);
        var callId = Register(call);
        try
        {
            await SendAsync(channel, method, callId, null, cancellationToken).ConfigureAwait(false);
        }
        catch
        {
            _calls.TryRemove(callId, out _);
            throw;
        }
        return new Subscription(() =>
        {
            if (_calls.TryRemove(callId, out var removed))
            {
                _ = CancelOnBudsAsync(callId, removed);
            }
        });
    }

    /// <summary>
    /// Cancels every open stream on the buds, then closes the link.
    /// </summary>
    /// <remarks>
    /// The buds keep a stream open after the link closes, through reconnects and trips to the
    /// case, until Bluetooth itself restarts. Left-over streams pile up and make the buds play
    /// their mode-change tone twice, for any app, so each one is cancelled here first.
    /// </remarks>
    public async ValueTask DisposeAsync()
    {
        var streams = _calls.Where(pair => pair.Value.OnMessage is not null).ToList();
        foreach (var (callId, call) in streams)
        {
            _calls.TryRemove(callId, out _);
            await CancelOnBudsAsync(callId, call).ConfigureAwait(false);
        }

        await _lifetime.CancelAsync().ConfigureAwait(false);
        _input.Dispose();
        _output.Dispose();
        try
        {
            await _readLoop.ConfigureAwait(false);
        }
        catch (Exception)
        {
            // Already reported through Closed
        }
        _lifetime.Dispose();
        _writeLock.Dispose();
    }

    // A Maestro method name, or "service/method" for another service
    private static (uint ServiceId, uint MethodId) Ids(string method)
    {
        var slash = method.LastIndexOf('/');
        return (Hash(slash < 0 ? Service : method[..slash]), Hash(method[(slash + 1)..]));
    }

    private static uint AddressFor(uint channel) =>
        (HostPeer << 6) | ((channel is RightChannel or RightChannelA ? RightPeer : LeftPeer) << 10);

    private uint Register(Call call)
    {
        var callId = (uint)Interlocked.Increment(ref _nextCallId);
        _calls[callId] = call;
        return callId;
    }

    /// <summary>
    /// The bytes of one request, framed and ready to send.
    /// </summary>
    internal static byte[] EncodeRequest(uint channel, string method, uint callId, IMessage? request)
    {
        var (serviceId, methodId) = Ids(method);
        var packet = new RpcPacket
        {
            Type      = PacketType.Request,
            ChannelId = channel,
            ServiceId = serviceId,
            MethodId  = methodId,
            Payload   = request?.ToByteString() ?? ByteString.Empty,
            CallId    = callId,
        };
        return new HdlcFrame(AddressFor(channel), HdlcFrame.UnnumberedInformation, packet.ToByteArray()).Encode();
    }

    private async Task SendAsync(uint channel, string method, uint callId, IMessage? request, CancellationToken cancellationToken)
    {
        var bytes = EncodeRequest(channel, method, callId, request);
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var sent = $"{method} channel={channel} call={callId} payload={Convert.ToHexStringLower(request?.ToByteArray() ?? [])}";
            LogMessages.PacketSent(_logger, sent);
        }
        await WriteFrameAsync(bytes, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>
    /// The CLIENT_ERROR a Pigweed client answers a packet for a call it doesn't have with
    /// (FAILED_PRECONDITION). Echoes the packet's channel, method and call ID.
    /// </summary>
    internal static byte[] EncodeUnknownCallError(RpcPacket packet)
    {
        var error = new RpcPacket
        {
            Type      = PacketType.ClientError,
            ChannelId = packet.ChannelId,
            ServiceId = packet.ServiceId,
            MethodId  = packet.MethodId,
            CallId    = packet.CallId,
            Status    = FailedPrecondition,
        };
        return new HdlcFrame(AddressFor(packet.ChannelId), HdlcFrame.UnnumberedInformation, error.ToByteArray()).Encode();
    }

    /// <summary>
    /// The CLIENT_ERROR a Pigweed client cancels one of its own calls with (CANCELLED).
    /// </summary>
    internal static byte[] EncodeCancel(uint channel, string method, uint callId)
    {
        var (serviceId, methodId) = Ids(method);
        var cancel = new RpcPacket
        {
            Type      = PacketType.ClientError,
            ChannelId = channel,
            ServiceId = serviceId,
            MethodId  = methodId,
            CallId    = callId,
            Status    = Cancelled,
        };
        return new HdlcFrame(AddressFor(channel), HdlcFrame.UnnumberedInformation, cancel.ToByteArray()).Encode();
    }

    // The buds send every reply twice, and keep streams for calls the client no longer has.
    // A Pigweed client answers those packets with this error, which also makes the buds drop
    // a stream left over from an earlier session.
    private Task RejectUnknownCallAsync(RpcPacket packet) => TryWriteAsync(EncodeUnknownCallError(packet));

    private Task CancelOnBudsAsync(uint callId, Call call) => TryWriteAsync(EncodeCancel(call.Channel, call.Method, callId));

    private async Task TryWriteAsync(byte[] frame)
    {
        try
        {
            await WriteFrameAsync(frame, _lifetime.Token).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or OperationCanceledException or ObjectDisposedException)
        {
            // The link is closing; nothing left to tell the buds
        }
    }

    private async Task WriteFrameAsync(byte[] bytes, CancellationToken cancellationToken)
    {
        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var written = Convert.ToHexStringLower(bytes);
            LogMessages.BytesWritten(_logger, written);
        }
        await _writeLock.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            await _output.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
            await _output.FlushAsync(cancellationToken).ConfigureAwait(false);
        }
        finally
        {
            _writeLock.Release();
        }
    }

    private async Task ReadLoopAsync()
    {
        var reader = new HdlcFrame.Reader();
        var buffer = new byte[4096];
        Exception? error = null;
        try
        {
            while (true)
            {
                var count = await _input.ReadAsync(buffer, _lifetime.Token).ConfigureAwait(false);
                if (count == 0)
                {
                    break;
                }
                if (_logger.IsEnabled(LogLevel.Debug))
                {
                    var bytes = Convert.ToHexStringLower(buffer.AsSpan(0, count));
                    LogMessages.BytesRead(_logger, bytes);
                }
                foreach (var frame in reader.Push(buffer.AsSpan(0, count)))
                {
                    Dispatch(frame);
                }
            }
        }
        catch (Exception ex)
        {
            error = ex;
        }

        // Fail everything still waiting, then report the close once
        var closed = new IOException("The buds closed the control link.", error);
        // Streams have nobody waiting on them; Closed tells their owner instead
        foreach (var call in _calls.Values.Where(call => call.OnMessage is null))
        {
            call.Completion.TrySetException(closed);
        }
        _calls.Clear();
        if (!_lifetime.IsCancellationRequested)
        {
            Closed?.Invoke(this, EventArgs.Empty);
        }
    }

    private void Dispatch(HdlcFrame frame)
    {
        if (frame.Control != HdlcFrame.UnnumberedInformation)
        {
            return;
        }

        RpcPacket packet;
        try
        {
            packet = RpcPacket.Parser.ParseFrom(frame.Data);
        }
        catch (InvalidProtocolBufferException)
        {
            return;
        }

        if (_logger.IsEnabled(LogLevel.Debug))
        {
            var received = $"{packet.Type} channel={packet.ChannelId} call={packet.CallId} status={packet.Status} payload={Convert.ToHexStringLower(packet.Payload.Span)}";
            LogMessages.PacketReceived(_logger, received);
        }

        // Packets with an open call ID (the software info the buds announce when the link
        // opens) belong to no call and are dropped. Copies of replies already handled get
        // the unknown-call error.
        if (packet.CallId == OpenCallId)
        {
            _announcedChannel.TrySetResult(packet.ChannelId);
        }

        if (!_calls.TryGetValue(packet.CallId, out var call))
        {
            if (packet.CallId != OpenCallId && packet.Type is PacketType.Response or PacketType.ServerStream)
            {
                _ = RejectUnknownCallAsync(packet);
            }
            return;
        }

        switch (packet.Type)
        {
            // One unreadable message mustn't end the read loop (and with it the link)
            case PacketType.ServerStream:
                try
                {
                    call.OnMessage?.Invoke(packet.Payload);
                }
                catch (Exception ex)
                {
                    LogMessages.StreamMessageFailed(_logger, ex, call.Method);
                }
                break;

            // Removed here, not after the caller resumes, so the buds' copy of this reply
            // already counts as unknown
            case PacketType.Response when packet.Status == 0:
                call.Completion.TrySetResult(packet.Payload);
                _calls.TryRemove(packet.CallId, out _);
                break;

            case PacketType.Response:
            case PacketType.ServerError:
                call.Completion.TrySetException(new MaestroException(packet.Status, call.Method));
                _calls.TryRemove(packet.CallId, out _);
                break;
        }
    }

    private sealed record Call(string Method, uint Channel, Action<ByteString>? OnMessage)
    {
        public TaskCompletionSource<ByteString> Completion { get; } = new(TaskCreationOptions.RunContinuationsAsynchronously);
    }

    private sealed class Subscription(Action dispose) : IDisposable
    {
        public void Dispose() => dispose();
    }
}
