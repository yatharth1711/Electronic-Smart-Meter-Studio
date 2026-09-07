using SmartMeterStudio.Protocol.Hdlc;
using SmartMeterStudio.Protocol.Server;

namespace SmartMeterStudio.Protocol.Transport;

/// <summary>Runs one HDLC session over any bidirectional stream, including a SerialPort BaseStream supplied by the host app.</summary>
public sealed class DlmsStreamSessionHost(HdlcDlmsSession session)
{
    private readonly HdlcDlmsSession _session = session ?? throw new ArgumentNullException(nameof(session));
    public event Action<HdlcFrame>? FrameReceived;

    public async Task RunAsync(Stream stream, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(stream);
        if (!stream.CanRead || !stream.CanWrite) throw new ArgumentException("The transport stream must support reading and writing.", nameof(stream));
        var decoder = new HdlcFrameStreamDecoder(); var buffer = new byte[4096];
        while (!cancellationToken.IsCancellationRequested)
        {
            var count = await stream.ReadAsync(buffer.AsMemory(), cancellationToken).ConfigureAwait(false);
            if (count == 0) return;
            foreach (var frame in decoder.Feed(buffer.AsSpan(0, count)))
            {
                FrameReceived?.Invoke(frame);
                await stream.WriteAsync(HdlcFrameCodec.Encode(_session.Process(frame)).AsMemory(), cancellationToken).ConfigureAwait(false);
            }
        }
    }
}
