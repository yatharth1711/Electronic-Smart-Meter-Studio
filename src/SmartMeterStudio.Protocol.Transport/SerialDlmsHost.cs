using System.IO.Ports;
using SmartMeterStudio.Protocol.Hdlc;
using SmartMeterStudio.Protocol.Server;

namespace SmartMeterStudio.Protocol.Transport;

/// <summary>Configuration for a physical or paired virtual COM port. The virtual-port driver is installed outside this application.</summary>
public sealed record SerialDlmsPortOptions(string PortName, int BaudRate = 9600, Parity Parity = Parity.None,
    int DataBits = 8, StopBits StopBits = StopBits.One, Handshake Handshake = Handshake.None,
    bool DtrEnable = false, bool RtsEnable = false)
{
    public void Validate()
    {
        if (string.IsNullOrWhiteSpace(PortName)) throw new ArgumentException("A COM port name is required.", nameof(PortName));
        if (BaudRate is < 300 or > 921600) throw new ArgumentOutOfRangeException(nameof(BaudRate), "Baud rate must be 300..921600.");
        if (DataBits is < 5 or > 8) throw new ArgumentOutOfRangeException(nameof(DataBits), "Data bits must be 5..8.");
        if (!Enum.IsDefined(Parity) || !Enum.IsDefined(StopBits) || !Enum.IsDefined(Handshake)) throw new ArgumentException("Invalid serial line setting.");
    }
}

/// <summary>Owns one COM port and runs a DLMS HDLC session over it. Use with a virtual-pair endpoint for serial-client testing.</summary>
public sealed class SerialDlmsHost(SerialDlmsPortOptions options, HdlcDlmsSession session) : IAsyncDisposable
{
    private readonly SerialDlmsPortOptions _options = options ?? throw new ArgumentNullException(nameof(options));
    private readonly HdlcDlmsSession _session = session ?? throw new ArgumentNullException(nameof(session));
    private readonly CancellationTokenSource _shutdown = new();
    private SerialPort? _port;
    private Task? _runTask;

    public bool IsRunning => _port?.IsOpen == true;
    public string PortName => _options.PortName;
    public event Action<HdlcFrame>? FrameReceived;
    public event Action<Exception>? Faulted;

    public static IReadOnlyList<string> GetAvailablePortNames() => SerialPort.GetPortNames().OrderBy(name => name, StringComparer.OrdinalIgnoreCase).ToArray();

    public Task StartAsync()
    {
        _options.Validate();
        if (_port is not null) throw new InvalidOperationException("Serial DLMS host is already running.");
        _port = new SerialPort(_options.PortName, _options.BaudRate, _options.Parity, _options.DataBits, _options.StopBits)
        {
            Handshake = _options.Handshake, DtrEnable = _options.DtrEnable, RtsEnable = _options.RtsEnable,
            ReadTimeout = SerialPort.InfiniteTimeout, WriteTimeout = SerialPort.InfiniteTimeout
        };
        _port.Open();
        var streamHost = new DlmsStreamSessionHost(_session);
        streamHost.FrameReceived += frame => FrameReceived?.Invoke(frame);
        _runTask = RunAsync(streamHost, _port.BaseStream);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _shutdown.Cancel();
        _port?.Close();
        if (_runTask is not null)
        {
            try { await _runTask.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _runTask = null; _port?.Dispose(); _port = null;
    }

    private async Task RunAsync(DlmsStreamSessionHost streamHost, Stream stream)
    {
        try { await streamHost.RunAsync(stream, _shutdown.Token).ConfigureAwait(false); }
        catch (OperationCanceledException) when (_shutdown.IsCancellationRequested) { }
        catch (Exception exception) { Faulted?.Invoke(exception); }
    }

    public async ValueTask DisposeAsync() { await StopAsync().ConfigureAwait(false); _shutdown.Dispose(); }
}
