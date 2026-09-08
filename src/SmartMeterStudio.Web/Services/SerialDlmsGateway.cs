using System.IO.Ports;
using SmartMeterStudio.Core.Simulation;
using SmartMeterStudio.Protocol.Monitoring;
using SmartMeterStudio.Protocol.Server;
using SmartMeterStudio.Protocol.Transport;

namespace SmartMeterStudio.Web.Services;

public sealed record SerialGatewayStatus(bool Running, string? MeterId, string? PortName, int BaudRate, string? LastError);

/// <summary>Owns one serial endpoint. A Windows virtual-COM driver supplies the paired endpoint used by a client tool.</summary>
public sealed class SerialDlmsGateway(SmartMeterFleet fleet, IProtocolFrameMonitor monitor) : IAsyncDisposable
{
    private readonly object _gate = new();
    private readonly SmartMeterFleet _fleet = fleet;
    private readonly IProtocolFrameMonitor _monitor = monitor;
    private SerialDlmsHost? _host;
    private SerialGatewayStatus _status = new(false, null, null, 9600, null);

    public SerialGatewayStatus Status { get { lock (_gate) return _status; } }
    public IReadOnlyList<string> AvailablePorts => SerialDlmsHost.GetAvailablePortNames();

    public async Task StartAsync(string meterId, string portName, int baudRate)
    {
        if (_fleet.GetSnapshot(meterId) is null) throw new KeyNotFoundException("Select an existing virtual meter.");
        if (string.IsNullOrWhiteSpace(portName)) throw new ArgumentException("Select the simulator COM port.");
        await StopAsync().ConfigureAwait(false);
        var session = new HdlcDlmsSession(1, 16, meterId, context => new CosemServiceRouter(_fleet, context),
            frameMonitor: _monitor, transport: $"Serial {portName}");
        var host = new SerialDlmsHost(new SerialDlmsPortOptions(portName, baudRate, Parity.None, 8, StopBits.One, Handshake.None), session);
        host.Faulted += error => { lock (_gate) _status = new(false, meterId, portName, baudRate, error.Message); };
        try
        {
            await host.StartAsync().ConfigureAwait(false);
            lock (_gate) { _host = host; _status = new(true, meterId, portName, baudRate, null); }
        }
        catch
        {
            await host.DisposeAsync().ConfigureAwait(false);
            throw;
        }
    }

    public async Task StopAsync()
    {
        SerialDlmsHost? host;
        lock (_gate) { host = _host; _host = null; _status = new(false, null, null, 9600, null); }
        if (host is not null) await host.DisposeAsync().ConfigureAwait(false);
    }

    public ValueTask DisposeAsync() => new(StopAsync());
}
