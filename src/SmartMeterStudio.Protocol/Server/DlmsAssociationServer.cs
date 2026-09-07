using System.Security.Cryptography;

namespace SmartMeterStudio.Protocol.Server;

internal sealed record DlmsAssociationOpenResult(byte[] Aare, DlmsAssociationContext? Context, HlsGmacExchange? HlsExchange);

/// <summary>Accepts the Edition 11 LN no-security, LLS and HLS-GMAC association profiles supported by the simulator.</summary>
public sealed class DlmsAssociationServer
{
    private static readonly byte[] LnNoCipheringContext = [0x60, 0x85, 0x74, 0x05, 0x08, 0x01, 0x01];
    private static readonly byte[] LlsMechanism = [0x60, 0x85, 0x74, 0x05, 0x08, 0x02, 0x01];
    private static readonly byte[] HlsGmacMechanism = [0x60, 0x85, 0x74, 0x05, 0x08, 0x02, 0x05];
    private static readonly byte[] InitiateResponse = [0x08, 0x00, 0x06, 0x5F, 0x1F, 0x04, 0x00, 0x00, 0x50, 0x1F, 0x01, 0xF4, 0x00, 0x07];
    private readonly string _meterId;
    private readonly DlmsAssociationSecuritySettings _settings;
    private readonly IDlmsInvocationCounterStore _counters;

    public DlmsAssociationServer(string meterId, DlmsAssociationSecuritySettings? settings = null, IDlmsInvocationCounterStore? counters = null)
    {
        _meterId = string.IsNullOrWhiteSpace(meterId) ? throw new ArgumentException("Meter ID is required.", nameof(meterId)) : meterId;
        _settings = settings ?? new(); _settings.Validate(); _counters = counters ?? new InMemoryInvocationCounterStore();
    }

    internal DlmsAssociationOpenResult Open(ReadOnlySpan<byte> aarq)
    {
        if (!TryReadTlv(aarq, 0x60, out var body) || !TryReadChildren(body, out var fields) || !fields.TryGetValue(0xA1, out var context) ||
            !IsOid(context, LnNoCipheringContext) || !fields.TryGetValue(0xBE, out var userInfo) || !IsSupportedInitiateRequest(userInfo))
            return new(BuildAare(1, 2), null, null);

        if (!fields.TryGetValue(0x8B, out var mechanism))
            return new(BuildAare(0, 0), DlmsAssociationContext.PublicReadOnly(_meterId), null);
        if (!fields.TryGetValue(0x8A, out var requirements) || !requirements.AsSpan().SequenceEqual(new byte[] { 0x07, 0x80 }) ||
            !fields.TryGetValue(0xAC, out var authValue) || !TryReadAuthenticationValue(authValue, out var challengeOrPassword))
            return new(BuildAare(1, 11), null, null);

        if (mechanism.AsSpan().SequenceEqual(LlsMechanism))
        {
            if (_settings.LlsPassword is null || !CryptographicOperations.FixedTimeEquals(challengeOrPassword, _settings.LlsPassword)) return new(BuildAare(1, 13), null, null);
            return new(BuildAare(0, 0), AuthenticatedContext(DlmsAssociationAuthentication.LowLevelSecurity), null);
        }
        if (mechanism.AsSpan().SequenceEqual(HlsGmacMechanism) && _settings.HlsGmac is { } hls)
        {
            if (challengeOrPassword.Length is < 8 or > 64) return new(BuildAare(1, 13), null, null);
            var serverChallenge = hls.ServerChallenge?.ToArray() ?? RandomNumberGenerator.GetBytes(8);
            var exchange = new HlsGmacExchange(hls, _counters, _meterId, challengeOrPassword, serverChallenge, AuthenticatedContext(DlmsAssociationAuthentication.HlsGmac));
            return new(BuildAare(0, 0, HlsGmacMechanism, serverChallenge), null, exchange);
        }
        return new(BuildAare(1, 11), null, null);
    }

    private DlmsAssociationContext AuthenticatedContext(DlmsAssociationAuthentication authentication) => new(_meterId, "authenticated", _settings.AuthenticatedMayWrite, _settings.AuthenticatedMayAction, Authentication: authentication);
    private static bool IsOid(byte[] encoded, byte[] oid) => encoded.Length == oid.Length + 2 && encoded[0] == 0x06 && encoded[1] == oid.Length && encoded.AsSpan(2).SequenceEqual(oid);
    private static bool IsSupportedInitiateRequest(byte[] userInformation) => userInformation.Length == 16 && userInformation[0] == 0x04 && userInformation[1] == 0x0E && userInformation[2] == 0x01 && userInformation[6] == 0x06;
    private static bool TryReadAuthenticationValue(byte[] source, out byte[] value)
    {
        value = []; if (source.Length < 2 || source[0] != 0x80 || source[1] >= 0x80 || source.Length != source[1] + 2) return false;
        value = source[2..]; return true;
    }
    private static byte[] BuildAare(byte result, byte diagnostic, byte[]? mechanism = null, byte[]? serverChallenge = null)
    {
        var fields = new List<byte> { 0xA1, 0x09, 0x06, 0x07 };
        fields.AddRange(LnNoCipheringContext);
        fields.AddRange([0xA2, 0x03, 0x02, 0x01, result, 0xA3, 0x05, 0xA1, 0x03, 0x02, 0x01, diagnostic]);
        if (mechanism is not null && serverChallenge is not null)
        {
            fields.AddRange([0x88, 0x02, 0x07, 0x80, 0x89, 0x07]); fields.AddRange(mechanism);
            fields.Add(0xAA); fields.Add((byte)(serverChallenge.Length + 2)); fields.Add(0x80); fields.Add((byte)serverChallenge.Length); fields.AddRange(serverChallenge);
        }
        fields.AddRange([0xBE, 0x10, 0x04, 0x0E, .. InitiateResponse]);
        if (fields.Count >= 0x80) throw new InvalidOperationException("AARE exceeds this simulator's short BER length support.");
        return [0x61, (byte)fields.Count, .. fields];
    }
    private static bool TryReadTlv(ReadOnlySpan<byte> source, byte expectedTag, out ReadOnlySpan<byte> value)
    {
        value = default; if (source.Length < 2 || source[0] != expectedTag || source[1] >= 0x80 || source.Length != source[1] + 2) return false;
        value = source.Slice(2); return true;
    }
    private static bool TryReadChildren(ReadOnlySpan<byte> body, out Dictionary<byte, byte[]> fields)
    {
        fields = []; var offset = 0;
        while (offset < body.Length)
        {
            if (offset + 2 > body.Length) return false; var tag = body[offset++]; var length = body[offset++];
            if (length >= 0x80 || offset + length > body.Length || !fields.TryAdd(tag, body.Slice(offset, length).ToArray())) return false;
            offset += length;
        }
        return true;
    }
}
