using System.Globalization;

namespace SmartMeterStudio.Protocol.Dlms;

/// <summary>A six-byte COSEM logical name (OBIS instance identifier).</summary>
public readonly record struct DlmsLogicalName(byte A, byte B, byte C, byte D, byte E, byte F)
{
    public static DlmsLogicalName Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        var items = value.Split('.');
        if (items.Length != 6 || items.Any(item => !byte.TryParse(item, NumberStyles.None, CultureInfo.InvariantCulture, out _)))
            throw new FormatException("A logical name must contain six decimal bytes.");
        return new DlmsLogicalName(byte.Parse(items[0], CultureInfo.InvariantCulture), byte.Parse(items[1], CultureInfo.InvariantCulture),
            byte.Parse(items[2], CultureInfo.InvariantCulture), byte.Parse(items[3], CultureInfo.InvariantCulture),
            byte.Parse(items[4], CultureInfo.InvariantCulture), byte.Parse(items[5], CultureInfo.InvariantCulture));
    }

    public static DlmsLogicalName FromBytes(ReadOnlySpan<byte> value)
    {
        if (value.Length != 6) throw new DlmsProtocolException("A COSEM logical name is exactly six bytes.");
        return new(value[0], value[1], value[2], value[3], value[4], value[5]);
    }

    public byte[] ToArray() => [A, B, C, D, E, F];
    public override string ToString() => string.Create(CultureInfo.InvariantCulture, $"{A}.{B}.{C}.{D}.{E}.{F}");
}
