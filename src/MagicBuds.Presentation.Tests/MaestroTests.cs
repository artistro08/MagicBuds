using System.IO.Pipes;
using Google.Protobuf;
using Microsoft.Extensions.Logging.Abstractions;
using MagicBuds.Presentation.Headsets;
using MagicBuds.Presentation.Maestro;
using MagicBuds.Presentation.Maestro.Proto;

namespace MagicBuds.Presentation.Tests;

/// <summary>
/// The protocol layer against frames captured from mypixelbuds.google.com (docs/protocol/capture.log).
/// </summary>
[TestClass]
public sealed class MaestroTests
{
    // Captured writes: Noise Cancellation (call 41), EQ Balanced (call 61), head gestures on
    // (call 69), left press and hold to Digital assistant (call 70)
    private const string NoiseCancellationFrame = "7e80310310181dea71de7d5e251d9a8c9e2a0422026802382910f2c3687e";
    private const string BalancedFrame          = "7e80310310181dea71de7d5e251d9a8c9e2a1e221c8201190d000060c0150000003f1d0000803f25000080bf2d00002040383db88557307e";
    private const string HeadGesturesOnFrame    = "7e80310310181dea71de7d5e251d9a8c9e2a052203e8010238451506eb177e";
    private const string LeftAssistantFrame     = "7e80310310181dea71de7d5e251d9a8c9e2a0a22083a060a042202080638463adfa74a7e";

    [TestMethod]
    public void HashesMatchPigweed()
    {
        Assert.AreEqual(0x7ede71eau, MaestroClient.Hash("maestro_pw.Maestro"));
        Assert.AreEqual(0x7199fa44u, MaestroClient.Hash("GetSoftwareInfo"));
        Assert.AreEqual(0x2821adf5u, MaestroClient.Hash("SubscribeToSettingsChanges"));
    }

    [TestMethod]
    public void NoiseModeWriteMatchesTheWebApp() =>
        AssertWrite(NoiseCancellationFrame, 41, new SettingValue { CurrentAncrState = AncState.Active });

    [TestMethod]
    public void EqualizerWriteMatchesTheWebApp() =>
        AssertWrite(BalancedFrame, 61, new SettingValue
        {
            CurrentUserEq = new EqBands { LowBass = -3.5f, Bass = 0.5f, Mid = 1, Treble = -1, UpperTreble = 2.5f },
        });

    [TestMethod]
    public void HeadGesturesWriteMatchesTheWebApp() =>
        AssertWrite(HeadGesturesOnFrame, 69, new SettingValue { HeadGestures = HeadGestureState.HeadGesturesOn });

    [TestMethod]
    public void HoldActionWriteMatchesTheWebApp() =>
        AssertWrite(LeftAssistantFrame, 70, new SettingValue
        {
            GestureControl = new GestureControl
            {
                Left = new DeviceGestureControl { Type = new GestureControlType { Value = RegularActionTarget.ActionTargetAssistantQuery } },
            },
        });

    [TestMethod]
    public void ReaderDecodesFramesSplitAcrossReads()
    {
        var bytes = Convert.FromHexString(NoiseCancellationFrame + BalancedFrame);
        var reader = new HdlcFrame.Reader();

        var frames = new List<HdlcFrame>();
        frames.AddRange(reader.Push(bytes.AsSpan(0, 17)));
        frames.AddRange(reader.Push(bytes.AsSpan(17, 40)));
        frames.AddRange(reader.Push(bytes.AsSpan(57)));

        Assert.AreEqual(2, frames.Count);
        var packet = RpcPacket.Parser.ParseFrom(frames[1].Data);
        Assert.AreEqual(61u, packet.CallId);
        Assert.AreEqual(MaestroClient.LeftChannel, packet.ChannelId);
        Assert.AreEqual(MaestroClient.Hash("WriteSetting"), packet.MethodId);
    }

    [TestMethod]
    public void ReaderDropsFramesWithABadChecksum()
    {
        var bytes = Convert.FromHexString(NoiseCancellationFrame);
        bytes[^3] ^= 0xFF;

        Assert.AreEqual(0, new HdlcFrame.Reader().Push(bytes).Count);
    }

    [TestMethod]
    public void ReaderRecoversFromAFrameThatNeverEnds()
    {
        var reader = new HdlcFrame.Reader();
        var garbage = new byte[100_000];
        garbage[0] = 0x7E;
        Array.Fill(garbage, (byte)0x11, 1, garbage.Length - 1);

        Assert.AreEqual(0, reader.Push(garbage).Count);
        Assert.AreEqual(1, reader.Push(Convert.FromHexString(NoiseCancellationFrame)).Count);
    }

    [TestMethod]
    public void SettingsReplyUpdatesTheSnapshot()
    {
        var state = HeadsetSnapshot.Empty;

        state = PixelBudsHeadset.Apply(state, new SettingValue { CurrentAncrState = AncState.Adaptive });
        state = PixelBudsHeadset.Apply(state, new SettingValue { SpeechDetection = true });
        state = PixelBudsHeadset.Apply(state, new SettingValue { HeadGestures = HeadGestureState.HeadGesturesOn });
        state = PixelBudsHeadset.Apply(state, new SettingValue
        {
            GestureControl = new GestureControl
            {
                Right = new DeviceGestureControl { Type = new GestureControlType { Value = RegularActionTarget.ActionTargetAssistantQuery } },
            },
        });
        state = PixelBudsHeadset.Apply(state, new SettingValue
        {
            AncrGestureLoop = new AncrGestureLoop { Active = true, Off = true, Aware = false, Adaptive = true },
        });
        state = PixelBudsHeadset.Apply(state, new SettingValue { CurrentUserEq = new EqBands { LowBass = 5, Bass = 3 } });

        Assert.AreEqual(NoiseMode.Adaptive, state.NoiseMode);
        Assert.IsTrue(state.ConversationDetection);
        Assert.IsTrue(state.HeadGestures);
        Assert.AreEqual(HoldAction.ToggleNoiseControl, state.LeftHold);
        Assert.AreEqual(HoldAction.DigitalAssistant, state.RightHold);
        Assert.AreEqual(new NoiseModeCycle(true, true, true, false), state.NoiseCycle);
        Assert.AreEqual(EqualizerPreset.All[1].Bands, state.Equalizer);
    }

    [TestMethod]
    public void UnknownCallErrorMatchesTheWebApp()
    {
        // The web app's answer to the buds' second copy of the reply to call 41
        var copy = new RpcPacket
        {
            Type      = PacketType.Response,
            ChannelId = MaestroClient.LeftChannel,
            ServiceId = MaestroClient.Hash(MaestroClient.Service),
            MethodId  = MaestroClient.Hash("WriteSetting"),
            CallId    = 41,
        };

        Assert.AreEqual("7e803103080410181dea71de7d5e251d9a8c9e3009382926f412c07e", Convert.ToHexStringLower(MaestroClient.EncodeUnknownCallError(copy)));
    }

    [TestMethod]
    public void CancelIsAClientErrorWithStatusCancelled()
    {
        var packet = RpcPacket.Parser.ParseFrom(new HdlcFrame.Reader().Push(MaestroClient.EncodeCancel(MaestroClient.RightChannelA, "SubscribeToSettingsChanges", 7))[0].Data);

        Assert.AreEqual(PacketType.ClientError, packet.Type);
        Assert.AreEqual(1u, packet.Status);
        Assert.AreEqual(7u, packet.CallId);
        Assert.AreEqual(MaestroClient.RightChannelA, packet.ChannelId);
        Assert.AreEqual(MaestroClient.Hash("SubscribeToSettingsChanges"), packet.MethodId);
    }

    [TestMethod]
    public async Task ClosingCancelsOpenStreamsOnTheBuds()
    {
        using var fromClient = new AnonymousPipeServerStream(PipeDirection.In);
        using var toBuds = new AnonymousPipeClientStream(PipeDirection.Out, fromClient.ClientSafePipeHandle);
        using var silentBuds = new AnonymousPipeServerStream(PipeDirection.Out);
        using var fromBuds = new AnonymousPipeClientStream(PipeDirection.In, silentBuds.ClientSafePipeHandle);
        var client = new MaestroClient(fromBuds, toBuds, NullLogger.Instance);

        await client.SubscribeAsync(MaestroClient.RightChannelA, "SubscribeToSettingsChanges", _ => { });
        var dispose = client.DisposeAsync().AsTask();

        var reader = new HdlcFrame.Reader();
        var packets = new List<RpcPacket>();
        var buffer = new byte[1024];
        while (packets.Count < 2)
        {
            var count = await fromClient.ReadAsync(buffer);
            packets.AddRange(reader.Push(buffer.AsSpan(0, count)).Select(frame => RpcPacket.Parser.ParseFrom(frame.Data)));
        }
        await dispose;

        Assert.AreEqual(PacketType.Request, packets[0].Type);
        Assert.AreEqual(PacketType.ClientError, packets[1].Type);
        Assert.AreEqual(packets[0].CallId, packets[1].CallId);
    }

    [TestMethod]
    public void UnknownNoiseModeShowsNoMode()
    {
        // Real buds out of the ears answer 22 02 68 00
        var state = PixelBudsHeadset.Apply(HeadsetSnapshot.Empty with { NoiseMode = NoiseMode.Transparency }, SettingsRsp.Parser.ParseFrom(Convert.FromHexString("22026800")).Value);

        Assert.IsNull(state.NoiseMode);
    }

    [TestMethod]
    public void BatteryLeavesOutCellsThatDidNotReport()
    {
        var info = new RuntimeInfo
        {
            BatteryInfo = new BatteryInfo
            {
                Left  = new DeviceBatteryInfo { Level = 75, State = BatteryState.BatteryNotCharging },
                Right = new DeviceBatteryInfo { Level = 70, State = BatteryState.BatteryCharging },
            },
        };

        Assert.AreEqual(new BatteryLevels(null, 75, 70, null, true), PixelBudsHeadset.ToBattery(info));
    }

    [TestMethod]
    public async Task ReplyGoesToItsCallAndTheOpeningAnnouncementIsIgnored()
    {
        // Real Pro 2 buds announce their software info with call ID 0xFFFFFFFF when the link
        // opens, then answer each request with its own call ID (seen from the web app)
        using var toClient = new AnonymousPipeServerStream(PipeDirection.Out);
        using var fromBuds = new AnonymousPipeClientStream(PipeDirection.In, toClient.ClientSafePipeHandle);
        using var fromClient = new AnonymousPipeServerStream(PipeDirection.In);
        using var toBuds = new AnonymousPipeClientStream(PipeDirection.Out, fromClient.ClientSafePipeHandle);
        var client = new MaestroClient(fromBuds, toBuds, NullLogger.Instance);

        var call = client.CallAsync(MaestroClient.RightChannelA, "GetSoftwareInfo");
        var buffer = new byte[1024];
        var count = await fromClient.ReadAsync(buffer);
        var request = RpcPacket.Parser.ParseFrom(new HdlcFrame.Reader().Push(buffer.AsSpan(0, count))[0].Data);
        await toClient.WriteAsync(SoftwareInfoFrame(uint.MaxValue, "announced"));
        await toClient.WriteAsync(SoftwareInfoFrame(request.CallId, "release_6.144"));

        var info = SoftwareInfo.Parser.ParseFrom(await call);
        Assert.AreEqual("release_6.144", info.Firmware.Left.VersionString);
    }

    private static byte[] SoftwareInfoFrame(uint callId, string version)
    {
        var reply = new RpcPacket
        {
            Type      = PacketType.Response,
            ChannelId = MaestroClient.RightChannelA,
            ServiceId = MaestroClient.Hash(MaestroClient.Service),
            MethodId  = MaestroClient.Hash("GetSoftwareInfo"),
            Payload   = new SoftwareInfo { Firmware = new FirmwareInfo { Left = new FirmwareVersion { VersionString = version } } }.ToByteString(),
            CallId    = callId,
        };
        return new HdlcFrame(0x2900, HdlcFrame.UnnumberedInformation, reply.ToByteArray()).Encode();
    }

    private static void AssertWrite(string capturedHex, uint callId, SettingValue value)
    {
        var bytes = MaestroClient.EncodeRequest(MaestroClient.LeftChannel, "WriteSetting", callId, new WriteSettingMsg { Setting = value });

        Assert.AreEqual(capturedHex, Convert.ToHexStringLower(bytes));
    }
}
