using LocalCoop.Protocol;
using System.Net;
using System.Net.Sockets;

namespace LocalCoop.Broker;

public sealed class BrokerTcpServer : IAsyncDisposable
{
    private readonly InMemoryBrokerSession _session;
    private readonly TcpListener _listener;
    private readonly CancellationTokenSource _shutdown = new();
    private readonly Dictionary<string, NetworkStream> _streamsByClientId = new(StringComparer.Ordinal);
    private readonly object _streamsLock = new();
    private Task? _acceptLoop;

    public BrokerTcpServer(string sessionId, IPAddress address, int port)
    {
        _session = new InMemoryBrokerSession(sessionId);
        _listener = new TcpListener(address, port);
    }

    public int Port
    {
        get
        {
            if (_listener.LocalEndpoint is not IPEndPoint endpoint)
            {
                throw new InvalidOperationException("Broker TCP server has not started.");
            }

            return endpoint.Port;
        }
    }

    public Task StartAsync(CancellationToken cancellationToken)
    {
        _listener.Start();
        _acceptLoop = Task.Run(() => AcceptLoopAsync(_shutdown.Token), cancellationToken);
        return Task.CompletedTask;
    }

    public async ValueTask DisposeAsync()
    {
        await _shutdown.CancelAsync();
        _listener.Stop();

        lock (_streamsLock)
        {
            foreach (var stream in _streamsByClientId.Values)
            {
                stream.Dispose();
            }

            _streamsByClientId.Clear();
        }

        if (_acceptLoop is not null)
        {
            try
            {
                await _acceptLoop;
            }
            catch (OperationCanceledException)
            {
            }
            catch (SocketException)
            {
            }
            catch (ObjectDisposedException)
            {
            }
        }

        _shutdown.Dispose();
    }

    private async Task AcceptLoopAsync(CancellationToken cancellationToken)
    {
        while (!cancellationToken.IsCancellationRequested)
        {
            var client = await _listener.AcceptTcpClientAsync(cancellationToken);
            _ = Task.Run(() => HandleClientAsync(client, cancellationToken), cancellationToken);
        }
    }

    private async Task HandleClientAsync(TcpClient client, CancellationToken cancellationToken)
    {
        using var clientLease = client;
        var stream = client.GetStream();
        string? clientId = null;

        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var message = await BrokerFrameCodec.ReadAsync(stream, cancellationToken);
                if (message is null)
                {
                    return;
                }

                if (message.Kind == BrokerTransportMessageKind.Registration)
                {
                    if (message.Registration is null)
                    {
                        throw new InvalidDataException("Registration message did not include registration data.");
                    }

                    var registration = BrokerClientRegistration.FromDto(message.Registration);
                    _session.Register(registration);
                    clientId = registration.ClientId;
                    lock (_streamsLock)
                    {
                        _streamsByClientId[clientId] = stream;
                    }

                    await BrokerFrameCodec.WriteAsync(
                        stream,
                        BrokerTransportMessage.ForRegistrationAccepted(clientId, _session.SessionId),
                        cancellationToken);

                    continue;
                }

                if (message.Envelope is null)
                {
                    throw new InvalidDataException("Envelope message did not include an envelope.");
                }

                foreach (var route in _session.Route(message.Envelope))
                {
                    NetworkStream? targetStream;
                    lock (_streamsLock)
                    {
                        _streamsByClientId.TryGetValue(route.TargetClientId, out targetStream);
                    }

                    if (targetStream is not null)
                    {
                        await BrokerFrameCodec.WriteAsync(
                            targetStream,
                            BrokerTransportMessage.ForEnvelope(route.Envelope),
                            cancellationToken);
                    }
                }
            }
        }
        finally
        {
            if (clientId is not null)
            {
                lock (_streamsLock)
                {
                    _streamsByClientId.Remove(clientId);
                }

                _session.Unregister(clientId);
            }
        }
    }
}
