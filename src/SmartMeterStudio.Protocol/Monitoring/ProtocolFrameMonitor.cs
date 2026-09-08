using SmartMeterStudio.Protocol.Hdlc;

namespace SmartMeterStudio.Protocol.Monitoring;

/// <summary>Bounded, in-memory diagnostic trace. It is protocol infrastructure, not a UI dependency.</summary>
public interface IProtocolFrameMonitor
{
    void Record(string meterId, string direction, string transport, HdlcFrame frame);
    IReadOnlyList<ProtocolFrameRecord> Recent(string? meterId = null, int limit = 200);
}

public sealed record ProtocolFrameRecord(long Sequence, DateTimeOffset Timestamp, string MeterId, string Direction,
    string Transport, string HdlcHex, string? ApduHex, string Summary);

public sealed class ProtocolFrameMonitor(int capacity = 500) : IProtocolFrameMonitor
{
    private readonly object _gate = new();
    private readonly Queue<ProtocolFrameRecord> _records = new();
    private readonly int _capacity = capacity is >= 1 and <= 10000 ? capacity : throw new ArgumentOutOfRangeException(nameof(capacity));
    private long _sequence;

    public void Record(string meterId, string direction, string transport, HdlcFrame frame)
    {
        var apdu = ExtractApdu(frame.Information);
        var record = new ProtocolFrameRecord(Interlocked.Increment(ref _sequence), DateTimeOffset.UtcNow, meterId, direction,
            transport, Convert.ToHexString(HdlcFrameCodec.Encode(frame)), apdu is null ? null : Convert.ToHexString(apdu), Summary(frame, apdu));
        lock (_gate) { _records.Enqueue(record); while (_records.Count > _capacity) _records.Dequeue(); }
    }

    public IReadOnlyList<ProtocolFrameRecord> Recent(string? meterId = null, int limit = 200)
    {
        limit = Math.Clamp(limit, 1, _capacity);
        lock (_gate) return _records.Where(x => meterId is null || x.MeterId == meterId).TakeLast(limit).Reverse().ToArray();
    }

    private static byte[]? ExtractApdu(byte[] information) => information.Length >= 3 && information[0] == 0xE6 &&
        (information[1] == 0xE6 || information[1] == 0xE7) && information[2] == 0x00 ? information[3..] : null;
    private static string Summary(HdlcFrame frame, byte[]? apdu) => apdu is { Length: > 0 } ? apdu[0] switch
    {
        0x60 => "AARQ association request", 0x61 => "AARE association response", 0xC0 => "GET request", 0xC4 => "GET response",
        0xC1 => "SET request", 0xC5 => "SET response", 0xC3 => "ACTION request", 0xC7 => "ACTION response", _ => $"APDU 0x{apdu[0]:X2}"
    } : frame.IsSetNormalResponseMode ? "SNRM link request" : frame.IsDisconnect ? "DISC link request" : frame.Control switch
    { 0x73 => "UA link response", 0x1F => "DM disconnected mode", _ => $"HDLC control 0x{frame.Control:X2}" };
}
