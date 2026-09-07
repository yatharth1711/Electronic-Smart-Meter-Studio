using System.Collections.Concurrent;
using System.Security.Cryptography;
using SmartMeterStudio.Protocol.Dlms;

namespace SmartMeterStudio.Protocol.Server;

public sealed record DlmsHlsGmacSettings(byte[] ClientSystemTitle, byte[] ServerSystemTitle, byte[] BlockCipherKey, byte[] AuthenticationKey,
    byte[]? ServerChallenge = null)
{
    public void Validate()
    {
        if (ClientSystemTitle.Length != 8 || ServerSystemTitle.Length != 8) throw new ArgumentException("DLMS system titles must be eight bytes.");
        if (BlockCipherKey.Length != 16 || AuthenticationKey.Length != 16) throw new ArgumentException("This simulator supports GCM-AES-128 keys only.");
        if (ServerChallenge is { Length: < 8 or > 64 }) throw new ArgumentException("HLS challenge must be 8..64 bytes.");
    }
}

public sealed record DlmsAssociationSecuritySettings(byte[]? LlsPassword = null, DlmsHlsGmacSettings? HlsGmac = null,
    bool AuthenticatedMayWrite = true, bool AuthenticatedMayAction = true)
{
    public void Validate()
    {
        if (LlsPassword is { Length: < 1 or > 64 }) throw new ArgumentException("LLS password must contain 1..64 bytes.");
        HlsGmac?.Validate();
    }
}

/// <summary>Persists the next outbound counter and rejects replayed inbound counters per association identity.</summary>
public interface IDlmsInvocationCounterStore
{
    uint NextOutbound(string identity);
    bool TryAcceptInbound(string identity, uint counter);
}

public sealed class InMemoryInvocationCounterStore : IDlmsInvocationCounterStore
{
    private readonly ConcurrentDictionary<string, uint> _outbound = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, uint> _inbound = new(StringComparer.Ordinal);
    public InMemoryInvocationCounterStore(uint firstOutbound = 1) => FirstOutbound = firstOutbound;
    public uint FirstOutbound { get; }
    public uint NextOutbound(string identity) => _outbound.AddOrUpdate(identity, FirstOutbound, (_, next) => checked(next + 1));
    public bool TryAcceptInbound(string identity, uint counter)
    {
        while (true)
        {
            if (!_inbound.TryGetValue(identity, out var current))
            {
                if (_inbound.TryAdd(identity, counter)) return true;
                continue;
            }
            if (counter <= current) return false;
            if (_inbound.TryUpdate(identity, counter, current)) return true;
        }
    }
}

/// <summary>Small local counter store for a simulator. Keep this file outside source control and restrict its ACL.</summary>
public sealed class FileInvocationCounterStore : IDlmsInvocationCounterStore
{
    private sealed record State(Dictionary<string, uint> Outbound, Dictionary<string, uint> Inbound);
    private readonly object _gate = new();
    private readonly string _path;
    private readonly uint _firstOutbound;
    private State _state;

    public FileInvocationCounterStore(string path, uint firstOutbound = 1)
    {
        if (string.IsNullOrWhiteSpace(path)) throw new ArgumentException("Counter store path is required.", nameof(path));
        _path = Path.GetFullPath(path); _firstOutbound = firstOutbound;
        _state = File.Exists(_path) ? System.Text.Json.JsonSerializer.Deserialize<State>(File.ReadAllText(_path)) ?? new([], []) : new([], []);
    }
    public uint NextOutbound(string identity)
    {
        lock (_gate)
        {
            var value = _state.Outbound.TryGetValue(identity, out var next) ? checked(next + 1) : _firstOutbound;
            _state.Outbound[identity] = value; Save(); return value;
        }
    }
    public bool TryAcceptInbound(string identity, uint counter)
    {
        lock (_gate)
        {
            if (_state.Inbound.TryGetValue(identity, out var prior) && counter <= prior) return false;
            _state.Inbound[identity] = counter; Save(); return true;
        }
    }
    private void Save()
    {
        var directory = Path.GetDirectoryName(_path)!; Directory.CreateDirectory(directory);
        var temporary = _path + ".tmp"; File.WriteAllText(temporary, System.Text.Json.JsonSerializer.Serialize(_state)); File.Move(temporary, _path, true);
    }
}

internal sealed class HlsGmacExchange(DlmsHlsGmacSettings settings, IDlmsInvocationCounterStore counters, string identity, byte[] clientChallenge,
    byte[] serverChallenge, DlmsAssociationContext context)
{
    private const byte SecurityControl = 0x10;
    public DlmsAssociationContext Context { get; } = context;
    public byte[] ServerChallenge { get; } = serverChallenge;

    public bool TryVerifyAndReply(DlmsDataValue? parameter, out DlmsDataValue? response)
    {
        response = null;
        if (parameter is not { Type: DlmsDataType.OctetString, Value: byte[] proof } || proof.Length != 17 || proof[0] != SecurityControl) return false;
        var counter = System.Buffers.Binary.BinaryPrimitives.ReadUInt32BigEndian(proof.AsSpan(1, 4));
        var expected = Gmac(settings.BlockCipherKey, settings.AuthenticationKey, settings.ClientSystemTitle, counter, ServerChallenge);
        if (!CryptographicOperations.FixedTimeEquals(proof.AsSpan(5), expected)) return false;
        if (!counters.TryAcceptInbound(identity + ":client", counter)) return false;
        var serverCounter = counters.NextOutbound(identity + ":server");
        var tag = Gmac(settings.BlockCipherKey, settings.AuthenticationKey, settings.ServerSystemTitle, serverCounter, clientChallenge);
        var reply = new byte[17]; reply[0] = SecurityControl;
        System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(reply.AsSpan(1, 4), serverCounter);
        tag.CopyTo(reply, 5); response = DlmsDataValue.Octets(reply); return true;
    }

    internal static byte[] Gmac(byte[] encryptionKey, byte[] authenticationKey, byte[] systemTitle, uint counter, byte[] challenge)
    {
        var nonce = new byte[12]; systemTitle.CopyTo(nonce, 0); System.Buffers.Binary.BinaryPrimitives.WriteUInt32BigEndian(nonce.AsSpan(8), counter);
        var associatedData = new byte[1 + authenticationKey.Length + challenge.Length]; associatedData[0] = SecurityControl;
        authenticationKey.CopyTo(associatedData, 1); challenge.CopyTo(associatedData, 1 + authenticationKey.Length);
        var tag = new byte[12]; using var cipher = new AesGcm(encryptionKey, 12);
        cipher.Encrypt(nonce, ReadOnlySpan<byte>.Empty, Span<byte>.Empty, tag, associatedData);
        return tag;
    }
}
