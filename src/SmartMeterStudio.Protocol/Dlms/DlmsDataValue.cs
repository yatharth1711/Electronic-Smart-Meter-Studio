namespace SmartMeterStudio.Protocol.Dlms;

public enum DlmsDataType : byte
{
    Null = 0, Array = 1, Structure = 2, Boolean = 3, DoubleLong = 5, DoubleLongUnsigned = 6,
    OctetString = 9, VisibleString = 10, Utf8String = 12, Integer = 15, Long = 16, Unsigned = 17,
    LongUnsigned = 18, Long64 = 20, Long64Unsigned = 21, Enum = 22, Float32 = 23, Float64 = 24, DateTime = 25
}

/// <summary>A bounded A-XDR data value used by the supported LN services.</summary>
public sealed record DlmsDataValue(DlmsDataType Type, object? Value)
{
    public static DlmsDataValue Null() => new(DlmsDataType.Null, null);
    public static DlmsDataValue Octets(ReadOnlySpan<byte> value) => new(DlmsDataType.OctetString, value.ToArray());
    public static DlmsDataValue Structure(params DlmsDataValue[] values) => new(DlmsDataType.Structure, values);
    public static DlmsDataValue Array(params DlmsDataValue[] values) => new(DlmsDataType.Array, values);
}
