using SmartMeterStudio.Protocol.Dlms;
using SmartMeterStudio.Protocol.Hdlc;

namespace SmartMeterStudio.Protocol.Server;

public enum HdlcSessionState { Disconnected, LinkEstablished, Associated }

/// <summary>
/// Per-connection HDLC state machine. Its association context is intentionally injected by the host;
/// ACSE authentication/ciphering is a separate, not-yet-implemented boundary.
/// </summary>
public sealed class HdlcDlmsSession(uint serverAddress, uint clientAddress, string meterId,
    Func<DlmsAssociationContext, CosemServiceRouter> routerFactory)
{
    private readonly string _meterId = string.IsNullOrWhiteSpace(meterId) ? throw new ArgumentException("Meter ID is required.", nameof(meterId)) : meterId;
    private readonly Func<DlmsAssociationContext, CosemServiceRouter> _routerFactory = routerFactory ?? throw new ArgumentNullException(nameof(routerFactory));
    private CosemServiceRouter? _router;
    private byte _expectedClientSequence;
    private byte _nextServerSequence;

    public uint ServerAddress { get; } = serverAddress;
    public uint ClientAddress { get; } = clientAddress;
    public HdlcSessionState State { get; private set; }

    public HdlcFrame Process(HdlcFrame incoming)
    {
        ArgumentNullException.ThrowIfNull(incoming);
        if (incoming.DestinationAddress != ServerAddress || incoming.SourceAddress != ClientAddress)
            return Reply(HdlcControl.DisconnectedMode);

        if (incoming.IsSetNormalResponseMode)
        {
            _expectedClientSequence = 0;
            _nextServerSequence = 0;
            _router = null;
            State = HdlcSessionState.LinkEstablished;
            return Reply(HdlcControl.UnnumberedAcknowledgement);
        }
        if (incoming.IsDisconnect)
        {
            State = HdlcSessionState.Disconnected;
            _router = null;
            return Reply(HdlcControl.UnnumberedAcknowledgement);
        }
        if (!incoming.IsInformationFrame || State == HdlcSessionState.Disconnected)
            return Reply(HdlcControl.DisconnectedMode);

        var clientSequence = (byte)((incoming.Control >> 1) & 0x07);
        if (clientSequence != _expectedClientSequence) return Reply(RejectControl());
        _expectedClientSequence = (byte)((_expectedClientSequence + 1) & 0x07);

        if (incoming.Information.Length < 4 || incoming.Information[0] != 0xE6 || incoming.Information[1] != 0xE6 || incoming.Information[2] != 0x00)
            return Reply(RejectControl());

        var application = incoming.Information.AsSpan(3);
        if (State == HdlcSessionState.LinkEstablished)
        {
            if (!DlmsNoSecurityAssociation.TryAccept(application, out var aare)) return Reply(InformationControl(), BuildApplicationReply(aare));
            _router = _routerFactory(DlmsAssociationContext.PublicReadOnly(_meterId));
            State = HdlcSessionState.Associated;
            return Reply(InformationControl(), BuildApplicationReply(aare));
        }

        DlmsLnResponse response;
        try { response = _router!.Execute(DlmsLnApduCodec.DecodeRequest(application)); }
        catch (DlmsProtocolException) { return Reply(RejectControl()); }

        var information = BuildApplicationReply(DlmsLnApduCodec.EncodeResponse(response));
        return Reply(InformationControl(), information);
    }

    private HdlcFrame Reply(byte control, ReadOnlySpan<byte> information = default) => new(ClientAddress, ServerAddress, control, information);
    private byte InformationControl()
    {
        var control = (byte)((_expectedClientSequence << 5) | (_nextServerSequence << 1));
        _nextServerSequence = (byte)((_nextServerSequence + 1) & 0x07);
        return control;
    }
    private byte RejectControl() => (byte)(0x09 | (_expectedClientSequence << 5));
    private static byte[] BuildApplicationReply(ReadOnlySpan<byte> application) => [0xE6, 0xE7, 0x00, .. application];

    private static class HdlcControl
    {
        public const byte UnnumberedAcknowledgement = 0x73;
        public const byte DisconnectedMode = 0x1F;
    }
}
