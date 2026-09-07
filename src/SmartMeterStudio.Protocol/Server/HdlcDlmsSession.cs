using SmartMeterStudio.Protocol.Dlms;
using SmartMeterStudio.Protocol.Hdlc;

namespace SmartMeterStudio.Protocol.Server;

public enum HdlcSessionState { Disconnected, LinkEstablished, HlsPending, Associated }

/// <summary>
/// Per-connection HDLC state machine. Its association context is intentionally injected by the host;
/// ACSE authentication/ciphering is a separate, not-yet-implemented boundary.
/// </summary>
public sealed class HdlcDlmsSession(uint serverAddress, uint clientAddress, string meterId,
    Func<DlmsAssociationContext, CosemServiceRouter> routerFactory, DlmsAssociationSecuritySettings? securitySettings = null,
    IDlmsInvocationCounterStore? invocationCounters = null)
{
    private readonly string _meterId = string.IsNullOrWhiteSpace(meterId) ? throw new ArgumentException("Meter ID is required.", nameof(meterId)) : meterId;
    private readonly Func<DlmsAssociationContext, CosemServiceRouter> _routerFactory = routerFactory ?? throw new ArgumentNullException(nameof(routerFactory));
    private readonly DlmsAssociationServer _associationServer = new(meterId, securitySettings, invocationCounters);
    private CosemServiceRouter? _router;
    private HlsGmacExchange? _hlsExchange;
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
            _hlsExchange = null;
            State = HdlcSessionState.LinkEstablished;
            return Reply(HdlcControl.UnnumberedAcknowledgement);
        }
        if (incoming.IsDisconnect)
        {
            State = HdlcSessionState.Disconnected;
            _router = null;
            _hlsExchange = null;
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
            var result = _associationServer.Open(application);
            if (result.Context is not null) { _router = _routerFactory(result.Context); State = HdlcSessionState.Associated; }
            else if (result.HlsExchange is not null) { _hlsExchange = result.HlsExchange; State = HdlcSessionState.HlsPending; }
            return Reply(InformationControl(), BuildApplicationReply(result.Aare));
        }

        if (State == HdlcSessionState.HlsPending)
        {
            DlmsActionRequest hlsRequest;
            try { hlsRequest = DlmsLnApduCodec.DecodeRequest(application) as DlmsActionRequest ?? throw new DlmsProtocolException("Expected HLS ACTION request."); }
            catch (DlmsProtocolException) { return Reply(RejectControl()); }
            if (hlsRequest.Descriptor is not { ClassId: 15, MethodId: 1 } || hlsRequest.Descriptor.LogicalName.ToString() != "0.0.40.0.0.255" ||
                !_hlsExchange!.TryVerifyAndReply(hlsRequest.Parameter, out var hlsReply))
            {
                State = HdlcSessionState.LinkEstablished; _hlsExchange = null;
                return Reply(InformationControl(), BuildApplicationReply(DlmsLnApduCodec.EncodeResponse(new DlmsActionResponse(hlsRequest.InvokeId, DlmsAccessResult.ReadWriteDenied))));
            }
            _router = _routerFactory(_hlsExchange.Context); _hlsExchange = null; State = HdlcSessionState.Associated;
            return Reply(InformationControl(), BuildApplicationReply(DlmsLnApduCodec.EncodeResponse(new DlmsActionResponse(hlsRequest.InvokeId, DlmsAccessResult.Success, hlsReply))));
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
