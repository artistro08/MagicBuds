using System.Runtime.InteropServices;

namespace MagicBuds.Presentation.Maestro;

/// <summary>
/// One HDLC frame as the buds send and receive it: an address, the control byte and the data.
/// </summary>
/// <remarks>
/// On the wire a frame is <c>7E address control data crc32 7E</c>. The address is a varint
/// whose bytes end with a set low bit, <c>7E</c> and <c>7D</c> inside the frame are escaped as
/// <c>7D</c> plus the byte XOR 0x20, and the CRC-32 (little endian) covers address, control
/// and data. Same framing as Pigweed's pw_hdlc: https://pigweed.dev/pw_hdlc/
/// </remarks>
internal sealed record HdlcFrame(uint Address, byte Control, byte[] Data)
{
    /// <summary>
    /// Control byte of an unnumbered information frame, the only kind Maestro uses.
    /// </summary>
    public const byte UnnumberedInformation = 0x03;

    private const byte Flag   = 0x7E;
    private const byte Escape = 0x7D;

    /// <summary>
    /// Encodes the frame with its CRC, escaping and both flag bytes.
    /// </summary>
    public byte[] Encode()
    {
        // Address, Control and Data
        var body = new List<byte>(Data.Length + 10);
        var address = Address;
        while (address >> 7 != 0)
        {
            body.Add((byte)((address & 0x7F) << 1));
            address >>= 7;
        }
        body.Add((byte)(((address & 0x7F) << 1) | 1));
        body.Add(Control);
        body.AddRange(Data);

        // CRC
        var crc = Crc32.Compute(CollectionsMarshal.AsSpan(body));
        body.AddRange(BitConverter.GetBytes(crc));

        // Escaping
        var frame = new List<byte>(body.Count + 4) { Flag };
        foreach (var value in body)
        {
            if (value is Flag or Escape)
            {
                frame.Add(Escape);
                frame.Add((byte)(value ^ 0x20));
            }
            else
            {
                frame.Add(value);
            }
        }
        frame.Add(Flag);
        return [.. frame];
    }

    /// <summary>
    /// Decodes the unescaped bytes between two flags. Returns null for anything that isn't a
    /// whole frame with a matching CRC; the buds' line noise is dropped, not thrown.
    /// </summary>
    public static HdlcFrame? Decode(ReadOnlySpan<byte> content)
    {
        if (content.Length < 6)
        {
            return null;
        }

        // CRC
        var body = content[..^4];
        var crc  = BitConverter.ToUInt32(content[^4..]);
        if (Crc32.Compute(body) != crc)
        {
            return null;
        }

        // Address
        uint address = 0;
        var index = 0;
        while (true)
        {
            if (index >= body.Length || index > 4)
            {
                return null;
            }
            var value = body[index];
            address |= (uint)(value >> 1) << (7 * index);
            index++;
            if ((value & 1) == 1)
            {
                break;
            }
        }

        if (index >= body.Length)
        {
            return null;
        }
        return new HdlcFrame(address, body[index], body[(index + 1)..].ToArray());
    }

    /// <summary>
    /// Splits a byte stream into frames, keeping a partial frame until the rest arrives.
    /// </summary>
    internal sealed class Reader
    {
        // Far above any Maestro message
        private const int MaxFrameBytes = 64 * 1024;

        private readonly List<byte> _content = [];
        private bool _inFrame;
        private bool _escaped;

        /// <summary>
        /// Feeds received bytes and returns every frame they complete.
        /// </summary>
        public IReadOnlyList<HdlcFrame> Push(ReadOnlySpan<byte> bytes)
        {
            var frames = new List<HdlcFrame>();
            foreach (var value in bytes)
            {
                if (value == Flag)
                {
                    // A flag ends the current frame and starts the next
                    if (_inFrame && _content.Count > 0 && Decode(CollectionsMarshal.AsSpan(_content)) is { } frame)
                    {
                        frames.Add(frame);
                    }
                    _content.Clear();
                    _inFrame = true;
                    _escaped = false;
                    continue;
                }
                if (!_inFrame)
                {
                    continue;
                }
                if (value == Escape)
                {
                    _escaped = true;
                    continue;
                }
                _content.Add(_escaped ? (byte)(value ^ 0x20) : value);
                _escaped = false;

                // A garbled stream that never sends the closing flag: drop it and wait for the next frame
                if (_content.Count > MaxFrameBytes)
                {
                    _content.Clear();
                    _inFrame = false;
                }
            }
            return frames;
        }
    }
}

/// <summary>
/// CRC-32 (IEEE 802.3), the checksum HDLC frames end with.
/// </summary>
internal static class Crc32
{
    private static readonly uint[] Table = CreateTable();

    public static uint Compute(ReadOnlySpan<byte> bytes)
    {
        var crc = 0xFFFFFFFFu;
        foreach (var value in bytes)
        {
            crc = Table[(crc ^ value) & 0xFF] ^ (crc >> 8);
        }
        return ~crc;
    }

    private static uint[] CreateTable()
    {
        var table = new uint[256];
        for (uint i = 0; i < 256; i++)
        {
            var value = i;
            for (var bit = 0; bit < 8; bit++)
            {
                value = (value & 1) == 1 ? 0xEDB88320 ^ (value >> 1) : value >> 1;
            }
            table[i] = value;
        }
        return table;
    }
}
