namespace SmartMeterStudio.Protocol.Server;

/// <summary>Edition 11 no-ciphering, no-authentication LN association subset (AARQ/AARE).</summary>
public static class DlmsNoSecurityAssociation
{
    private static readonly byte[] LnNoCipheringContext = [0x60, 0x85, 0x74, 0x05, 0x08, 0x01, 0x01];
    private static readonly byte[] InitiateResponse = [0x08, 0x00, 0x06, 0x5F, 0x1F, 0x04, 0x00, 0x00, 0x50, 0x1F, 0x01, 0xF4, 0x00, 0x07];

    public static bool TryAccept(ReadOnlySpan<byte> aarq, out byte[] aare)
    {
        if (!TryReadTlv(aarq, 0x60, out var body) || !TryReadChildren(body, out var fields) ||
            !fields.TryGetValue(0xA1, out var context) || !IsLnNoCiphering(context) ||
            fields.ContainsKey(0x8A) || fields.ContainsKey(0xAC) || !fields.TryGetValue(0xBE, out var userInformation) ||
            !IsSupportedInitiateRequest(userInformation))
        {
            aare = BuildAare(1, 2); // permanent reject: application context not supported / invalid basic proposal
            return false;
        }
        aare = BuildAare(0, 0);
        return true;
    }

    private static bool IsLnNoCiphering(byte[] context) => context.Length == 9 && context[0] == 0x06 && context[1] == 0x07 && context.AsSpan(2).SequenceEqual(LnNoCipheringContext);
    private static bool IsSupportedInitiateRequest(byte[] userInformation) => userInformation.Length == 16 && userInformation[0] == 0x04 && userInformation[1] == 0x0E &&
        userInformation[2] == 0x01 && userInformation[6] == 0x06;

    private static byte[] BuildAare(byte result, byte diagnostic) => [
        0x61, 0x29, 0xA1, 0x09, 0x06, 0x07, .. LnNoCipheringContext,
        0xA2, 0x03, 0x02, 0x01, result,
        0xA3, 0x05, 0xA1, 0x03, 0x02, 0x01, diagnostic,
        0xBE, 0x10, 0x04, 0x0E, .. InitiateResponse];

    private static bool TryReadTlv(ReadOnlySpan<byte> source, byte expectedTag, out ReadOnlySpan<byte> value)
    {
        value = default;
        if (source.Length < 2 || source[0] != expectedTag || source[1] >= 0x80 || source.Length != source[1] + 2) return false;
        value = source.Slice(2); return true;
    }
    private static bool TryReadChildren(ReadOnlySpan<byte> body, out Dictionary<byte, byte[]> fields)
    {
        fields = [];
        var offset = 0;
        while (offset < body.Length)
        {
            if (offset + 2 > body.Length) return false;
            var tag = body[offset++]; var length = body[offset++];
            if (length >= 0x80 || offset + length > body.Length || !fields.TryAdd(tag, body.Slice(offset, length).ToArray())) return false;
            offset += length;
        }
        return true;
    }
}
