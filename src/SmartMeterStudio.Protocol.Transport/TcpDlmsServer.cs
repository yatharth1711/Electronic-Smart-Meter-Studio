using System.Net;
using System.Net.Sockets;
using SmartMeterStudio.Protocol.Hdlc;
using SmartMeterStudio.Protocol.Server;

namespace SmartMeterStudio.Protocol.Transport;

/// <summary>TCP byte transport for HDLC/DLMS. It owns sockets only; DLMS processing stays in SmartMeterStudio.Protocol.</summary>
public sealed class TcpDlmsServer(Func<IPEndPoint?, HdlcDlmsSession> sessionFactory) : IAsyncDisposable
{
    private readonly Func<IPEndPoint?, HdlcDlmsSession> _sessionFactory = sessionFactory ?? throw new ArgumentNullException(nameof(sessionFactory));
    private readonly CancellationTokenSource _shutdown = new();
    private TcpListener? _listener;
    private Task? _acceptLoop;
    public IPEndPoint? LocalEndpoint => _listener?.LocalEndpoint as IPEndPoint;
    public event Action<Exception>? ConnectionFaulted;
    public event Action<IPEndPoint?>? ConnectionAccepted;
    public event Action<IPEndPoint?>? ConnectionReady;
    public event Action<HdlcFrame>? FrameReceived;

    /// <summary>Starts listening. Use IPAddress.Loopback until ACSE authentication and ciphering are enabled.</summary>
    public Task StartAsync(IPAddress address, int port, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(address);
        if (port is < 0 or > 65535) throw new ArgumentOutOfRangeException(nameof(port));
        if (_listener is not null) throw new InvalidOperationException("TCP DLMS server is already running.");
        _listener = new TcpListener(address, port);
        _listener.Start();
        _acceptLoop = AcceptLoopAsync(_shutdown.Token);
        return Task.CompletedTask;
    }

    public async Task StopAsync()
    {
        _shutdown.Cancel();
        _listener?.Stop();
        if (_acceptLoop is not null)
        {
            try { await _acceptLoop.ConfigureAwait(false); }
            catch (OperationCanceledException) { }
        }
        _listener = null;
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested && _listener is not null)
            {
                var client = await _listener.AcceptTcpClientAsync(cancellationToken).ConfigureAwait(false);
                client.NoDelay = true;
                ConnectionAccepted?.Invoke(client.Client.RemoteEndPoint as IPEndPoint);
                _ = HandleClientAsync(client, cancellationToken);
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
        catch (ObjectDisposedException) when (cancellationToken.IsCancellationRequested) { }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using (client)
            try
            {
                using var stream = client.GetStream();
                var session = _sessionFactory(client.Client.RemoteEndPoint as IPEndPoint);
                ConnectionReady?.Invoke(client.Client.RemoteEndPoint as IPEndPoint);
                var host = new DlmsStreamSessionHost(session);
                host.FrameReceived += frame => FrameReceived?.Invoke(frame);
                await host.RunAsync(stream, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested) { }
            catch (Exception exception) { ConnectionFaulted?.Invoke(exception); }
    }

    public async ValueTask DisposeAsync()
    {
        await StopAsync().ConfigureAwait(false);
        _shutdown.Dispose();
    }
}
