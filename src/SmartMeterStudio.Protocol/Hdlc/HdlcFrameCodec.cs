using System.Buffers.Binary;

namespace SmartMeterStudio.Protocol.Hdlc;

/// <summary>Validates and builds bounded HDLC frames. It does not own a serial port or TCP socket.</summary>
public static class HdlcFrameCodec
{
    public const byte Flag = 0x7E;
    private const int MinimumFrameLength = 9;
    private const int MaximumFrameLength = 2047;

    public static byte[] Encode(HdlcFrame frame)
    {
        ArgumentNullException.ThrowIfNull(frame);
        var destination = EncodeAddress(frame.DestinationAddress);
        var source = EncodeAddress(frame.SourceAddress);
        var hasInformation = frame.Information.Length > 0;
        var length = 2 + destination.Length + source.Length + 1 + (hasInformation ? 2 : 0) + frame.Information.Length + 2;
        if (length > MaximumFrameLength) throw new ArgumentOutOfRangeException(nameof(frame), "HDLC frame is larger than 2047 bytes.");

        var raw = new List<byte>(length)
        {
            (byte)(0xA0 | (length >> 8)),
            (byte)length
        };
        raw.AddRange(destination);
        raw.AddRange(source);
        raw.Add(frame.Control);
        if (hasInformation)
        {
            AddCrc(raw, Crc16(raw.ToArray()));
            raw.AddRange(frame.Information);
        }
        AddCrc(raw, Crc16(raw.ToArray()));

        var result = new byte[raw.Count + 2];
        result[0] = Flag;
        raw.CopyTo(result, 1);
        result[^1] = Flag;
        return result;
    }

    public static HdlcFrame Decode(ReadOnlySpan<byte> bytes)
    {
        if (bytes.Length < MinimumFrameLength || bytes[0] != Flag || bytes[^1] != Flag)
            throw new HdlcProtocolException("An HDLC frame must begin and end with 0x7E.");

        var raw = bytes[1..^1];
        if ((raw[0] & 0xF0) != 0xA0) throw new HdlcProtocolException("Unsupported HDLC frame format.");
        var declaredLength = ((raw[0] & 0x07) << 8) | raw[1];
        if (declaredLength != raw.Length) throw new HdlcProtocolException("HDLC frame length does not match its format field.");
        if (raw.Length < MinimumFrameLength - 2) throw new HdlcProtocolException("HDLC frame is too short.");

        var offset = 2;
        var destination = DecodeAddress(raw, ref offset);
        var source = DecodeAddress(raw, ref offset);
        if (offset + 3 > raw.Length) throw new HdlcProtocolException("HDLC frame has no complete control and FCS fields.");
        var control = raw[offset++];
        var remaining = raw.Length - offset;
        var informationLength = 0;
        if (remaining > 2)
        {
            if (remaining < 4) throw new HdlcProtocolException("HDLC frame has an incomplete HCS or FCS field.");
            ValidateCrc(raw[..offset], raw[offset], raw[offset + 1], "HCS");
            offset += 2;
            informationLength = raw.Length - offset - 2;
        }
        ValidateCrc(raw[..^2], raw[^2], raw[^1], "FCS");
        return new HdlcFrame(destination, source, control, raw.Slice(offset, informationLength));
    }

    public static bool TryDecode(ReadOnlySpan<byte> bytes, out HdlcFrame? frame, out string? error)
    {
        try { frame = Decode(bytes); error = null; return true; }
        catch (HdlcProtocolException exception) { frame = null; error = exception.Message; return false; }
    }

    private static byte[] EncodeAddress(uint address)
    {
        if (address > 0x0FFFFFFF) throw new ArgumentOutOfRangeException(nameof(address), "HDLC addresses use at most four seven-bit groups.");
        var groups = new Stack<byte>();
        do { groups.Push((byte)((address & 0x7F) << 1)); address >>= 7; } while (address != 0);
        var result = groups.ToArray();
        result[^1] |= 0x01;
        return result;
    }

    private static uint DecodeAddress(ReadOnlySpan<byte> raw, ref int offset)
    {
        uint value = 0;
        for (var count = 0; count < 4; count++)
        {
            if (offset >= raw.Length - 3) throw new HdlcProtocolException("HDLC address is incomplete.");
            var part = raw[offset++];
            value = (value << 7) | (uint)(part >> 1);
            if ((part & 0x01) != 0) return value;
        }
        throw new HdlcProtocolException("HDLC address uses more than four groups.");
    }

    private static void AddCrc(List<byte> destination, ushort crc)
    {
        destination.Add((byte)crc);
        destination.Add((byte)(crc >> 8));
    }

    private static void ValidateCrc(ReadOnlySpan<byte> data, byte low, byte high, string label)
    {
        var received = BinaryPrimitives.ReadUInt16LittleEndian([low, high]);
        if (Crc16(data) != received) throw new HdlcProtocolException($"HDLC {label} check failed.");
    }

    private static ushort Crc16(ReadOnlySpan<byte> data)
    {
        ushort value = 0xFFFF;
        foreach (var item in data)
        {
            value ^= item;
            for (var bit = 0; bit < 8; bit++) value = (value & 1) != 0 ? (ushort)((value >> 1) ^ 0x8408) : (ushort)(value >> 1);
        }
        return (ushort)~value;
    }
}
