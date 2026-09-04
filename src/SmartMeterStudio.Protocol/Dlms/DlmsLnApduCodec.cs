using System.Buffers.Binary;

namespace SmartMeterStudio.Protocol.Dlms;

public enum DlmsAccessResult : byte
{
    Success = 0, HardwareFault = 1, TemporaryFailure = 2, ReadWriteDenied = 3, ObjectUndefined = 4,
    ObjectClassInconsistent = 9, ObjectUnavailable = 11, TypeUnmatched = 12, ScopeOfAccessViolated = 13, DataBlockUnavailable = 14, OtherReason = 250
}

public readonly record struct DlmsAttributeDescriptor(ushort ClassId, DlmsLogicalName LogicalName, byte AttributeId);
public readonly record struct DlmsMethodDescriptor(ushort ClassId, DlmsLogicalName LogicalName, byte MethodId);
public abstract record DlmsLnRequest(byte InvokeId);
public sealed record DlmsGetRequest(byte InvokeId, DlmsAttributeDescriptor Descriptor) : DlmsLnRequest(InvokeId);
public sealed record DlmsSetRequest(byte InvokeId, DlmsAttributeDescriptor Descriptor, DlmsDataValue Value) : DlmsLnRequest(InvokeId);
public sealed record DlmsActionRequest(byte InvokeId, DlmsMethodDescriptor Descriptor, DlmsDataValue? Parameter) : DlmsLnRequest(InvokeId);
public abstract record DlmsLnResponse(byte InvokeId, DlmsAccessResult Result);
public sealed record DlmsGetResponse(byte InvokeId, DlmsAccessResult Result, DlmsDataValue? Value = null) : DlmsLnResponse(InvokeId, Result);
public sealed record DlmsSetResponse(byte InvokeId, DlmsAccessResult Result) : DlmsLnResponse(InvokeId, Result);
public sealed record DlmsActionResponse(byte InvokeId, DlmsAccessResult Result) : DlmsLnResponse(InvokeId, Result);

/// <summary>Decodes the xDLMS logical-name normal GET, SET and ACTION service forms.</summary>
public static class DlmsLnApduCodec
{
    private const byte GetRequest = 0xC0, SetRequest = 0xC1, ActionRequest = 0xC3;
    private const byte GetResponse = 0xC4, SetResponse = 0xC5, ActionResponse = 0xC7;
    private const byte Normal = 1;

    public static DlmsLnRequest DecodeRequest(ReadOnlySpan<byte> apdu)
    {
        if (apdu.Length < 3) throw new DlmsProtocolException("xDLMS APDU is incomplete.");
        var tag = apdu[0];
        if (apdu[1] != Normal) throw new DlmsProtocolException("Only normal xDLMS service requests are supported.");
        var invokeId = apdu[2];
        var offset = 3;
        return tag switch
        {
            GetRequest => DecodeGet(apdu, ref offset, invokeId),
            SetRequest => DecodeSet(apdu, ref offset, invokeId),
            ActionRequest => DecodeAction(apdu, ref offset, invokeId),
            _ => throw new DlmsProtocolException($"Unsupported xDLMS request tag 0x{tag:X2}.")
        };
    }

    public static byte[] EncodeResponse(DlmsLnResponse response)
    {
        ArgumentNullException.ThrowIfNull(response);
        var output = new List<byte> { response switch { DlmsGetResponse => GetResponse, DlmsSetResponse => SetResponse, DlmsActionResponse => ActionResponse, _ => throw new DlmsProtocolException("Unsupported response type.") }, Normal, response.InvokeId };
        switch (response)
        {
            case DlmsGetResponse get:
                if (get.Result == DlmsAccessResult.Success) { output.Add(0); output.AddRange(DlmsDataCodec.Encode(get.Value ?? DlmsDataValue.Null())); }
                else { output.Add(1); output.Add((byte)get.Result); }
                break;
            case DlmsSetResponse set: output.Add((byte)set.Result); break;
            case DlmsActionResponse action:
                output.Add((byte)action.Result);
                output.Add(0); // return-parameters: omitted
                break;
        }
        return output.ToArray();
    }

    private static DlmsGetRequest DecodeGet(ReadOnlySpan<byte> apdu, ref int offset, byte invokeId)
    {
        var descriptor = ReadAttributeDescriptor(apdu, ref offset);
        RequireByte(apdu, ref offset, 0, "Only GET without selective access is supported.");
        RequireEnd(apdu, offset); return new(invokeId, descriptor);
    }
    private static DlmsSetRequest DecodeSet(ReadOnlySpan<byte> apdu, ref int offset, byte invokeId)
    {
        var descriptor = ReadAttributeDescriptor(apdu, ref offset);
        RequireByte(apdu, ref offset, 0, "Only SET without selective access is supported.");
        var value = DlmsDataCodec.Decode(apdu[offset..]); return new(invokeId, descriptor, value);
    }
    private static DlmsActionRequest DecodeAction(ReadOnlySpan<byte> apdu, ref int offset, byte invokeId)
    {
        var descriptor = ReadMethodDescriptor(apdu, ref offset);
        if (offset >= apdu.Length) throw new DlmsProtocolException("ACTION request parameter flag is missing.");
        var hasParameter = apdu[offset++];
        if (hasParameter is not (0 or 1)) throw new DlmsProtocolException("ACTION request parameter flag is invalid.");
        var parameter = hasParameter == 0 ? null : DlmsDataCodec.Decode(apdu[offset..]);
        if (hasParameter == 0) RequireEnd(apdu, offset);
        return new(invokeId, descriptor, parameter);
    }
    private static DlmsAttributeDescriptor ReadAttributeDescriptor(ReadOnlySpan<byte> apdu, ref int offset) => new(ReadUInt16(apdu, ref offset), ReadLogicalName(apdu, ref offset), ReadByte(apdu, ref offset));
    private static DlmsMethodDescriptor ReadMethodDescriptor(ReadOnlySpan<byte> apdu, ref int offset) => new(ReadUInt16(apdu, ref offset), ReadLogicalName(apdu, ref offset), ReadByte(apdu, ref offset));
    private static DlmsLogicalName ReadLogicalName(ReadOnlySpan<byte> apdu, ref int offset) { if (offset + 6 > apdu.Length) throw new DlmsProtocolException("xDLMS logical name is incomplete."); var value = DlmsLogicalName.FromBytes(apdu.Slice(offset, 6)); offset += 6; return value; }
    private static ushort ReadUInt16(ReadOnlySpan<byte> apdu, ref int offset) { if (offset + 2 > apdu.Length) throw new DlmsProtocolException("xDLMS class ID is incomplete."); var value = BinaryPrimitives.ReadUInt16BigEndian(apdu[offset..]); offset += 2; return value; }
    private static byte ReadByte(ReadOnlySpan<byte> apdu, ref int offset) => offset < apdu.Length ? apdu[offset++] : throw new DlmsProtocolException("xDLMS descriptor is incomplete.");
    private static void RequireByte(ReadOnlySpan<byte> apdu, ref int offset, byte value, string error) { if (ReadByte(apdu, ref offset) != value) throw new DlmsProtocolException(error); }
    private static void RequireEnd(ReadOnlySpan<byte> apdu, int offset) { if (offset != apdu.Length) throw new DlmsProtocolException("Unexpected trailing xDLMS request data."); }
}
