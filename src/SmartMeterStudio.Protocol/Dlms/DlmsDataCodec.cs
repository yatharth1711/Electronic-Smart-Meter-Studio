using System.Buffers.Binary;
using System.Globalization;
using System.Text;
using System.Text.Json;
using SmartMeterStudio.Core.Models;

namespace SmartMeterStudio.Protocol.Dlms;

/// <summary>Small, bounded A-XDR data codec. Unsupported types fail closed rather than being guessed.</summary>
public static class DlmsDataCodec
{
    private const int MaxDepth = 16;
    private const int MaxCollectionItems = 4096;
    private const int MaxOctets = 8192;

    public static byte[] Encode(DlmsDataValue value)
    {
        ArgumentNullException.ThrowIfNull(value);
        var result = new List<byte>();
        Write(result, value, 0);
        return result.ToArray();
    }

    public static DlmsDataValue Decode(ReadOnlySpan<byte> bytes)
    {
        var offset = 0;
        var result = Read(bytes, ref offset, 0);
        if (offset != bytes.Length) throw new DlmsProtocolException("Extra bytes follow the A-XDR data value.");
        return result;
    }

    public static DlmsDataValue FromObject(object? value) => value switch
    {
        null => DlmsDataValue.Null(),
        DlmsDataValue data => data,
        bool boolean => new(DlmsDataType.Boolean, boolean),
        byte number => new(DlmsDataType.Unsigned, number),
        sbyte number => new(DlmsDataType.Integer, number),
        short number => new(DlmsDataType.Long, number),
        ushort number => new(DlmsDataType.LongUnsigned, number),
        int number => new(DlmsDataType.DoubleLong, number),
        uint number => new(DlmsDataType.DoubleLongUnsigned, number),
        long number => new(DlmsDataType.Long64, number),
        ulong number => new(DlmsDataType.Long64Unsigned, number),
        float number => new(DlmsDataType.Float32, number),
        double number => new(DlmsDataType.Float64, number),
        string text => new(DlmsDataType.VisibleString, text),
        byte[] bytes => DlmsDataValue.Octets(bytes),
        DateTimeOffset time => new(DlmsDataType.DateTime, time),
        ScalerUnit scaler => DlmsDataValue.Structure(new(DlmsDataType.Integer, (sbyte)scaler.Scaler), new(DlmsDataType.Enum, (byte)scaler.Unit)),
        JsonElement json => FromJson(json),
        IEnumerable<JsonElement> items => new(DlmsDataType.Array, items.Select(FromJson).ToArray()),
        _ => throw new NotSupportedException($"Cannot encode {value.GetType().Name} as A-XDR data.")
    };

    public static JsonElement ToJson(DlmsDataValue value)
    {
        var model = ToJsonModel(value);
        return JsonSerializer.SerializeToElement(model);
    }

    private static object? ToJsonModel(DlmsDataValue value) => value.Type switch
    {
        DlmsDataType.Null => null,
        DlmsDataType.OctetString => Convert.ToHexString((byte[])value.Value!),
        DlmsDataType.DateTime => ((DateTimeOffset)value.Value!).ToString("O", CultureInfo.InvariantCulture),
        DlmsDataType.Array or DlmsDataType.Structure => ((DlmsDataValue[])value.Value!).Select(ToJsonModel).ToArray(),
        _ => value.Value
    };

    private static DlmsDataValue FromJson(JsonElement value) => value.ValueKind switch
    {
        JsonValueKind.Null => DlmsDataValue.Null(),
        JsonValueKind.True => new(DlmsDataType.Boolean, true),
        JsonValueKind.False => new(DlmsDataType.Boolean, false),
        JsonValueKind.String => new(DlmsDataType.VisibleString, value.GetString() ?? string.Empty),
        JsonValueKind.Number when value.TryGetInt32(out var integer) => new(DlmsDataType.DoubleLong, integer),
        JsonValueKind.Number => new(DlmsDataType.Float64, value.GetDouble()),
        JsonValueKind.Array => new(DlmsDataType.Array, value.EnumerateArray().Select(FromJson).ToArray()),
        JsonValueKind.Object => new(DlmsDataType.Structure, value.EnumerateObject().Select(property => FromJson(property.Value)).ToArray()),
        _ => throw new NotSupportedException("Unsupported JSON value for A-XDR conversion.")
    };

    private static void Write(List<byte> output, DlmsDataValue value, int depth)
    {
        if (depth > MaxDepth) throw new DlmsProtocolException("A-XDR nesting exceeds the supported limit.");
        output.Add((byte)value.Type);
        switch (value.Type)
        {
            case DlmsDataType.Null: return;
            case DlmsDataType.Boolean: output.Add((bool)value.Value! ? (byte)1 : (byte)0); return;
            case DlmsDataType.Integer: output.Add(unchecked((byte)(sbyte)value.Value!)); return;
            case DlmsDataType.Unsigned or DlmsDataType.Enum: output.Add((byte)value.Value!); return;
            case DlmsDataType.Long: AddInt16(output, (short)value.Value!); return;
            case DlmsDataType.LongUnsigned: AddUInt16(output, (ushort)value.Value!); return;
            case DlmsDataType.DoubleLong: AddInt32(output, (int)value.Value!); return;
            case DlmsDataType.DoubleLongUnsigned: AddUInt32(output, (uint)value.Value!); return;
            case DlmsDataType.Long64: AddInt64(output, (long)value.Value!); return;
            case DlmsDataType.Long64Unsigned: AddUInt64(output, (ulong)value.Value!); return;
            case DlmsDataType.Float32: AddUInt32(output, BitConverter.SingleToUInt32Bits((float)value.Value!)); return;
            case DlmsDataType.Float64: AddUInt64(output, BitConverter.DoubleToUInt64Bits((double)value.Value!)); return;
            case DlmsDataType.OctetString: WriteBytes(output, (byte[])value.Value!); return;
            case DlmsDataType.VisibleString: WriteBytes(output, Encoding.ASCII.GetBytes((string)value.Value!)); return;
            case DlmsDataType.Utf8String: WriteBytes(output, Encoding.UTF8.GetBytes((string)value.Value!)); return;
            case DlmsDataType.DateTime: WriteDateTime(output, (DateTimeOffset)value.Value!); return;
            case DlmsDataType.Array or DlmsDataType.Structure:
                var values = (DlmsDataValue[])value.Value!;
                if (values.Length > MaxCollectionItems) throw new DlmsProtocolException("A-XDR collection exceeds the supported limit.");
                WriteLength(output, values.Length);
                foreach (var item in values) Write(output, item, depth + 1);
                return;
            default: throw new DlmsProtocolException($"A-XDR type {(byte)value.Type} is not supported.");
        }
    }

    private static DlmsDataValue Read(ReadOnlySpan<byte> input, ref int offset, int depth)
    {
        if (depth > MaxDepth || offset >= input.Length) throw new DlmsProtocolException("A-XDR data is incomplete or too deeply nested.");
        var type = (DlmsDataType)input[offset++];
        return type switch
        {
            DlmsDataType.Null => DlmsDataValue.Null(),
            DlmsDataType.Boolean => new(type, ReadByte(input, ref offset) != 0),
            DlmsDataType.Integer => new(type, unchecked((sbyte)ReadByte(input, ref offset))),
            DlmsDataType.Unsigned or DlmsDataType.Enum => new(type, ReadByte(input, ref offset)),
            DlmsDataType.Long => new(type, ReadInt16(input, ref offset)),
            DlmsDataType.LongUnsigned => new(type, ReadUInt16(input, ref offset)),
            DlmsDataType.DoubleLong => new(type, ReadInt32(input, ref offset)),
            DlmsDataType.DoubleLongUnsigned => new(type, ReadUInt32(input, ref offset)),
            DlmsDataType.Long64 => new(type, ReadInt64(input, ref offset)),
            DlmsDataType.Long64Unsigned => new(type, ReadUInt64(input, ref offset)),
            DlmsDataType.Float32 => new(type, BitConverter.UInt32BitsToSingle(ReadUInt32(input, ref offset))),
            DlmsDataType.Float64 => new(type, BitConverter.UInt64BitsToDouble(ReadUInt64(input, ref offset))),
            DlmsDataType.OctetString => new(type, ReadBytes(input, ref offset)),
            DlmsDataType.VisibleString => new(type, Encoding.ASCII.GetString(ReadBytes(input, ref offset))),
            DlmsDataType.Utf8String => new(type, Encoding.UTF8.GetString(ReadBytes(input, ref offset))),
            DlmsDataType.DateTime => new(type, ReadDateTime(input, ref offset)),
            DlmsDataType.Array or DlmsDataType.Structure => ReadCollection(type, input, ref offset, depth),
            _ => throw new DlmsProtocolException($"A-XDR data type {(byte)type} is not supported.")
        };
    }

    private static DlmsDataValue ReadCollection(DlmsDataType type, ReadOnlySpan<byte> input, ref int offset, int depth)
    {
        var count = ReadLength(input, ref offset);
        if (count > MaxCollectionItems) throw new DlmsProtocolException("A-XDR collection exceeds the supported limit.");
        var values = new DlmsDataValue[count];
        for (var index = 0; index < count; index++) values[index] = Read(input, ref offset, depth + 1);
        return new(type, values);
    }

    private static void WriteBytes(List<byte> output, byte[] bytes)
    {
        if (bytes.Length > MaxOctets) throw new DlmsProtocolException("A-XDR octet string exceeds the supported limit.");
        WriteLength(output, bytes.Length); output.AddRange(bytes);
    }
    private static byte[] ReadBytes(ReadOnlySpan<byte> input, ref int offset)
    {
        var length = ReadLength(input, ref offset);
        if (length > MaxOctets || offset + length > input.Length) throw new DlmsProtocolException("A-XDR octet string is incomplete or too large.");
        var result = input.Slice(offset, length).ToArray(); offset += length; return result;
    }
    private static void WriteLength(List<byte> output, int value)

    {
        if (value < 0 || value > MaxOctets) throw new DlmsProtocolException("Unsupported A-XDR length.");
        if (value < 0x80) output.Add((byte)value);
        else if (value <= byte.MaxValue) { output.Add(0x81); output.Add((byte)value); }
        else { output.Add(0x82); AddUInt16(output, (ushort)value); }
    }
    private static int ReadLength(ReadOnlySpan<byte> input, ref int offset)
    {
        var first = ReadByte(input, ref offset);
        if ((first & 0x80) == 0) return first;
        var bytes = first & 0x7F;
        if (bytes is < 1 or > 2 || offset + bytes > input.Length) throw new DlmsProtocolException("Unsupported or incomplete A-XDR length.");
        var value = 0; for (var index = 0; index < bytes; index++) value = (value << 8) | ReadByte(input, ref offset);
        return value;
    }
    private static void WriteDateTime(List<byte> output, DateTimeOffset time)
    {
        AddUInt16(output, (ushort)time.Year); output.Add((byte)time.Month); output.Add((byte)time.Day); output.Add(0xFF);
        output.Add((byte)time.Hour); output.Add((byte)time.Minute); output.Add((byte)time.Second); output.Add(0);
        AddInt16(output, (short)-time.Offset.TotalMinutes); output.Add(0);
    }
    private static DateTimeOffset ReadDateTime(ReadOnlySpan<byte> input, ref int offset)
    {
        if (offset + 12 > input.Length) throw new DlmsProtocolException("A-XDR date-time is incomplete.");
        var year = ReadUInt16(input, ref offset); var month = ReadByte(input, ref offset); var day = ReadByte(input, ref offset); offset++;
        var hour = ReadByte(input, ref offset); var minute = ReadByte(input, ref offset); var second = ReadByte(input, ref offset); offset++;
        var deviation = ReadInt16(input, ref offset); offset++;
        try { return new DateTimeOffset(year, month, day, hour, minute, second, TimeSpan.FromMinutes(-deviation)); }
        catch (ArgumentOutOfRangeException exception) { throw new DlmsProtocolException($"Invalid A-XDR date-time: {exception.Message}"); }
    }
    private static byte ReadByte(ReadOnlySpan<byte> input, ref int offset) => offset < input.Length ? input[offset++] : throw new DlmsProtocolException("A-XDR data is incomplete.");
    private static short ReadInt16(ReadOnlySpan<byte> input, ref int offset) => unchecked((short)ReadUInt16(input, ref offset));
    private static ushort ReadUInt16(ReadOnlySpan<byte> input, ref int offset) { Require(input, offset, 2); var value = BinaryPrimitives.ReadUInt16BigEndian(input[offset..]); offset += 2; return value; }
    private static int ReadInt32(ReadOnlySpan<byte> input, ref int offset) { Require(input, offset, 4); var value = BinaryPrimitives.ReadInt32BigEndian(input[offset..]); offset += 4; return value; }
    private static uint ReadUInt32(ReadOnlySpan<byte> input, ref int offset) { Require(input, offset, 4); var value = BinaryPrimitives.ReadUInt32BigEndian(input[offset..]); offset += 4; return value; }
    private static long ReadInt64(ReadOnlySpan<byte> input, ref int offset) { Require(input, offset, 8); var value = BinaryPrimitives.ReadInt64BigEndian(input[offset..]); offset += 8; return value; }
    private static ulong ReadUInt64(ReadOnlySpan<byte> input, ref int offset) { Require(input, offset, 8); var value = BinaryPrimitives.ReadUInt64BigEndian(input[offset..]); offset += 8; return value; }
    private static void Require(ReadOnlySpan<byte> input, int offset, int length) { if (offset + length > input.Length) throw new DlmsProtocolException("A-XDR data is incomplete."); }
    private static void AddInt16(List<byte> output, short value) => AddUInt16(output, unchecked((ushort)value));
    private static void AddUInt16(List<byte> output, ushort value) { Span<byte> bytes = stackalloc byte[2]; BinaryPrimitives.WriteUInt16BigEndian(bytes, value); output.AddRange(bytes.ToArray()); }
    private static void AddInt32(List<byte> output, int value) => AddUInt32(output, unchecked((uint)value));
    private static void AddUInt32(List<byte> output, uint value) { Span<byte> bytes = stackalloc byte[4]; BinaryPrimitives.WriteUInt32BigEndian(bytes, value); output.AddRange(bytes.ToArray()); }
    private static void AddInt64(List<byte> output, long value) => AddUInt64(output, unchecked((ulong)value));
    private static void AddUInt64(List<byte> output, ulong value) { Span<byte> bytes = stackalloc byte[8]; BinaryPrimitives.WriteUInt64BigEndian(bytes, value); output.AddRange(bytes.ToArray()); }
}
