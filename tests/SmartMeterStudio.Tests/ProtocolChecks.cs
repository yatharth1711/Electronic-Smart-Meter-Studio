using System.Text.Json;
using System.Net;
using System.Net.Sockets;
using SmartMeterStudio.Core.Models;
using SmartMeterStudio.Core.Simulation;
using SmartMeterStudio.Protocol.Dlms;
using SmartMeterStudio.Protocol.Hdlc;
using SmartMeterStudio.Protocol.Server;
using SmartMeterStudio.Protocol.Transport;
using System.IO.Ports;

namespace SmartMeterStudio.Tests;

internal static class ProtocolChecks
{
    private const string Register = "0.128.1.0.0.255";
    public static (string Name, Action Run)[] All => [
        ("HDLC frame codec round trip and CRC validation", HdlcRoundTrip),
        ("A-XDR data codec round trip", AxdrRoundTrip),
        ("xDLMS normal GET decode and response", GetRoundTrip),
        ("xDLMS router protects writes and invokes test register", RouterPermissions),
        ("HDLC session establishes no-security LN association", HdlcAssociation),
        ("TCP host exchanges an HDLC link response", TcpHost),
        ("HLS-GMAC validates the Green Book test vector", HlsGmac),
        ("Virtual COM port configuration is validated", SerialPortConfiguration)
    ];

    private static void HdlcRoundTrip()
    {
        var wire = HdlcFrameCodec.Encode(new HdlcFrame(0x120, 0x10, 0x10, [0xC0, 1, 0xC1]));
        var decoded = HdlcFrameCodec.Decode(wire);
        Check(decoded.DestinationAddress == 0x120 && decoded.SourceAddress == 0x10 && decoded.Control == 0x10, "HDLC header did not round-trip.");
        Check(decoded.Information.SequenceEqual(new byte[] { 0xC0, 1, 0xC1 }), "HDLC information did not round-trip.");
        wire[^3] ^= 1; Check(!HdlcFrameCodec.TryDecode(wire, out _, out _), "Damaged HDLC FCS was accepted.");
    }

    private static void AxdrRoundTrip()
    {
        var source = DlmsDataValue.Structure(new(DlmsDataType.Long, (short)-12), DlmsDataValue.Array(new(DlmsDataType.Boolean, true), new(DlmsDataType.VisibleString, "ok")));
        var decoded = DlmsDataCodec.Decode(DlmsDataCodec.Encode(source));
        var items = (DlmsDataValue[])decoded.Value!;
        Check(decoded.Type == DlmsDataType.Structure && (short)items[0].Value! == -12, "A-XDR scalar did not round-trip.");
        Check(((DlmsDataValue[])items[1].Value!)[1].Value!.Equals("ok"), "A-XDR collection did not round-trip.");
    }

    private static void GetRoundTrip()
    {
        var request = DlmsLnApduCodec.DecodeRequest([0xC0, 1, 0xC1, 0, 3, 1, 0, 1, 8, 0, 255, 2, 0]) as DlmsGetRequest;
        Check(request is not null && request.Descriptor.ClassId == 3 && request.Descriptor.LogicalName.ToString() == "1.0.1.8.0.255", "GET request did not decode.");
        var response = DlmsLnApduCodec.EncodeResponse(new DlmsGetResponse(0xC1, DlmsAccessResult.Success, new(DlmsDataType.DoubleLong, 42)));
        Check(response.SequenceEqual(new byte[] { 0xC4, 1, 0xC1, 0, 5, 0, 0, 0, 42 }), "GET response did not encode.");
    }

    private static void RouterPermissions()
    {
        var fleet = new SmartMeterFleet(seedDefaults: false);
        var meterId = fleet.Create(new CreateMeterRequest { Name = "Protocol meter", SerialNumber = "PROT01", PhaseMode = MeterPhaseMode.SinglePhase, BaseLoadKw = 1, NominalVoltage = 230, NominalPowerFactor = 1 }).Definition.Id;
        fleet.CreateCosem(meterId, new CreateCosemObject(3, Register, "Protocol test register", JsonSerializer.SerializeToElement(9)));
        var descriptor = new DlmsAttributeDescriptor(3, DlmsLogicalName.Parse(Register), 2);
        var denied = new CosemServiceRouter(fleet, DlmsAssociationContext.PublicReadOnly(meterId)).Execute(new DlmsSetRequest(1, descriptor, new(DlmsDataType.DoubleLong, 7)));
        Check(denied.Result == DlmsAccessResult.ReadWriteDenied, "Public association changed a register.");
        var router = new CosemServiceRouter(fleet, new DlmsAssociationContext(meterId, "utility", true, true));
        Check(router.Execute(new DlmsSetRequest(2, descriptor, new(DlmsDataType.DoubleLong, 7))).Result == DlmsAccessResult.Success, "Authorized SET failed.");
        Check(router.Execute(new DlmsActionRequest(3, new DlmsMethodDescriptor(3, DlmsLogicalName.Parse(Register), 1), null)).Result == DlmsAccessResult.Success, "Authorized ACTION failed.");
        var get = (DlmsGetResponse)router.Execute(new DlmsGetRequest(4, descriptor));
        Check(get.Result == DlmsAccessResult.Success && (int)get.Value!.Value! == 0, "Register reset did not reach the simulator.");
    }

    private static void HdlcAssociation()
    {
        var fleet = new SmartMeterFleet(seedDefaults: false);
        var meterId = fleet.Create(new CreateMeterRequest { Name = "HDLC meter", SerialNumber = "HDLC01", PhaseMode = MeterPhaseMode.SinglePhase, BaseLoadKw = 1, NominalVoltage = 230, NominalPowerFactor = 1 }).Definition.Id;
        var session = new HdlcDlmsSession(1, 16, meterId, context => new CosemServiceRouter(fleet, context));
        var snrm = HdlcFrameCodec.Encode(new HdlcFrame(1, 16, 0x93));
        var chunks = new HdlcFrameStreamDecoder();
        Check(chunks.Feed(snrm.AsSpan(0, 4)).Count == 0 && chunks.Feed(snrm.AsSpan(4)).Single().IsSetNormalResponseMode, "Segmented SNRM was not reassembled.");
        Check(session.Process(HdlcFrameCodec.Decode(snrm)).IsUnnumberedAcknowledgement && session.State == HdlcSessionState.LinkEstablished, "SNRM did not establish HDLC link.");
        var aarq = new byte[] { 0x60, 0x1D, 0xA1, 0x09, 0x06, 0x07, 0x60, 0x85, 0x74, 0x05, 0x08, 0x01, 0x01, 0xBE, 0x10, 0x04, 0x0E, 0x01, 0x00, 0x00, 0x00, 0x06, 0x5F, 0x1F, 0x04, 0x00, 0x00, 0x7E, 0x1F, 0x04, 0xB0 };
        var association = session.Process(new HdlcFrame(1, 16, 0, [0xE6, 0xE6, 0x00, .. aarq]));
        Check(session.State == HdlcSessionState.Associated && association.Information.AsSpan(0, 5).SequenceEqual(new byte[] { 0xE6, 0xE7, 0x00, 0x61, 0x29 }), "AARQ did not produce accepted AARE.");
        var get = session.Process(new HdlcFrame(1, 16, 0x02, [0xE6, 0xE6, 0x00, 0xC0, 1, 0xC1, 0, 3, 1, 0, 1, 8, 0, 255, 2, 0]));
        Check(get.Information.AsSpan(0, 6).SequenceEqual(new byte[] { 0xE6, 0xE7, 0x00, 0xC4, 1, 0xC1 }), "Associated GET did not return a DLMS response.");
    }

    private static void TcpHost()
    {
        ThreadPool.GetMinThreads(out var workers, out var completionPorts);
        ThreadPool.SetMinThreads(Math.Max(workers, 4), Math.Max(completionPorts, 4));
        var fleet = new SmartMeterFleet(seedDefaults: false);
        var meterId = fleet.Create(new CreateMeterRequest { Name = "TCP meter", SerialNumber = "TCP01", PhaseMode = MeterPhaseMode.SinglePhase, BaseLoadKw = 1, NominalVoltage = 230, NominalPowerFactor = 1 }).Definition.Id;
        var server = new TcpDlmsServer(_ => new HdlcDlmsSession(1, 16, meterId, context => new CosemServiceRouter(fleet, context)));
        Exception? connectionFault = null;
        var receivedFrames = 0;
        var acceptedConnections = 0;
        var readyConnections = 0;
        server.ConnectionFaulted += exception => connectionFault = exception;
        server.ConnectionAccepted += _ => Interlocked.Increment(ref acceptedConnections);
        server.ConnectionReady += _ => Interlocked.Increment(ref readyConnections);
        server.FrameReceived += _ => Interlocked.Increment(ref receivedFrames);
        try
        {
            server.StartAsync(IPAddress.Loopback, 0).GetAwaiter().GetResult();
            using var client = new TcpClient { NoDelay = true }; client.ConnectAsync(IPAddress.Loopback, server.LocalEndpoint!.Port).GetAwaiter().GetResult();
            Check(SpinWait.SpinUntil(() => Volatile.Read(ref readyConnections) == 1, TimeSpan.FromSeconds(1)), "TCP host did not initialize the client session.");
            using var stream = client.GetStream();
            var request = HdlcFrameCodec.Encode(new HdlcFrame(1, 16, 0x93));
            var sent = client.Client.Send(request);
            Check(sent == request.Length, "TCP client did not send the complete HDLC frame.");
            var reply = new byte[64];
            var decoder = new HdlcFrameStreamDecoder();
            using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            HdlcFrame? response = null;
            while (response is null)
            {
                var count = stream.ReadAsync(reply.AsMemory(), timeout.Token).GetAwaiter().GetResult();
                if (count == 0) break;
                response = decoder.Feed(reply.AsSpan(0, count)).SingleOrDefault();
            }
            Check(response?.IsUnnumberedAcknowledgement == true, $"TCP host did not return UA. Connections: {acceptedConnections}; Ready: {readyConnections}; Server frames: {receivedFrames}; Control: {response?.Control:X2}; {connectionFault?.Message}");
        }
        finally { server.DisposeAsync().AsTask().GetAwaiter().GetResult(); }
    }

    private static void HlsGmac()
    {
        var fleet = new SmartMeterFleet(seedDefaults: false);
        var meterId = fleet.Create(new CreateMeterRequest { Name = "HLS meter", SerialNumber = "HLS01", PhaseMode = MeterPhaseMode.SinglePhase, BaseLoadKw = 1, NominalVoltage = 230, NominalPowerFactor = 1 }).Definition.Id;
        var hls = new DlmsHlsGmacSettings(Hex("4D4D4D0000000001"), Hex("4D4D4D0000BC614E"), Hex("000102030405060708090A0B0C0D0E0F"), Hex("D0D1D2D3D4D5D6D7D8D9DADBDCDDDEDF"), Hex("503677524A323146"));
        var session = new HdlcDlmsSession(1, 16, meterId, context => new CosemServiceRouter(fleet, context), new DlmsAssociationSecuritySettings(HlsGmac: hls), new InMemoryInvocationCounterStore(0x01234567));
        session.Process(new HdlcFrame(1, 16, 0x93));
        var aarq = new byte[] { 0x60, 0x36, 0xA1, 0x09, 0x06, 0x07, 0x60, 0x85, 0x74, 0x05, 0x08, 0x01, 0x01, 0x8A, 0x02, 0x07, 0x80, 0x8B, 0x07, 0x60, 0x85, 0x74, 0x05, 0x08, 0x02, 0x05, 0xAC, 0x0A, 0x80, 0x08, 0x4B, 0x35, 0x36, 0x69, 0x56, 0x61, 0x67, 0x59, 0xBE, 0x10, 0x04, 0x0E, 0x01, 0x00, 0x00, 0x00, 0x06, 0x5F, 0x1F, 0x04, 0x00, 0x00, 0x7E, 0x1F, 0x04, 0xB0 };
        var aare = session.Process(new HdlcFrame(1, 16, 0, [0xE6, 0xE6, 0, .. aarq]));
        Check(session.State == HdlcSessionState.HlsPending && aare.Information.AsSpan().IndexOf(Hex("503677524A323146")) >= 0, "HLS AARE did not include the server challenge.");
        var clientProof = Hex("10000000011A52FE7DD3E72748973C1E28");
        byte[] action = [0xC3, 1, 0xC3, 0, 15, 0, 0, 40, 0, 0, 255, 1, 1, 9, 17, .. clientProof];
        var response = session.Process(new HdlcFrame(1, 16, 2, [0xE6, 0xE6, 0, .. action]));
        Check(session.State == HdlcSessionState.Associated && response.Information.AsSpan().IndexOf(Hex("1001234567FE1466AFB3DBCD4F9389E2B7")) >= 0, "HLS-GMAC response does not match the Green Book test vector.");
    }

    private static byte[] Hex(string value) => Convert.FromHexString(value);

    private static void SerialPortConfiguration()
    {
        new SerialDlmsPortOptions("COM21", 9600, Parity.None, 8, StopBits.One, Handshake.None).Validate();
        try { new SerialDlmsPortOptions("", 9600).Validate(); }
        catch (ArgumentException) { return; }
        throw new InvalidOperationException("Invalid virtual COM configuration was accepted.");
    }

    private static void Check(bool condition, string text) { if (!condition) throw new InvalidOperationException(text); }
}
