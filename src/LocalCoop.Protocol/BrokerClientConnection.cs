using System.Net.Sockets;

namespace LocalCoop.Protocol;

public sealed class BrokerClientConnection : IAsyncDisposable
{
    private readonly TcpClient _client;
    private readonly NetworkStream _stream;

    private BrokerClientConnection(TcpClient client)
    {
        _client = client;
        _stream = client.GetStream();
    }

    public static async Task<BrokerClientConnection> ConnectAsync(
        BrokerClientConfig config,
        string clientId,
        CancellationToken cancellationToken)
    {
        if (string.IsNullOrWhiteSpace(clientId))
        {
            throw new ArgumentException("Client id must not be blank.", nameof(clientId));
        }

        var client = new TcpClient();
        await client.ConnectAsync(config.Host, config.Port, cancellationToken);
        var connection = new BrokerClientConnection(client);

        await BrokerFrameCodec.WriteAsync(
            connection._stream,
            BrokerTransportMessage.ForRegistration(
                new BrokerClientRegistrationDto(clientId, config.Role, config.ClientIndex)),
            cancellationToken);

        var accepted = await BrokerFrameCodec.ReadAsync(connection._stream, cancellationToken);
        if (accepted?.Kind != BrokerTransportMessageKind.RegistrationAccepted
            || accepted.RegistrationAccepted?.ClientId != clientId
            || accepted.RegistrationAccepted.SessionId != config.SessionId)
        {
            await connection.DisposeAsync();
            throw new InvalidDataException("Broker did not accept registration for the requested client.");
        }

        return connection;
    }

    public async Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
    {
        await BrokerFrameCodec.WriteAsync(
            _stream,
            BrokerTransportMessage.ForEnvelope(envelope),
            cancellationToken);
    }

    public async Task<BrokerEnvelope?> ReadEnvelopeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await BrokerFrameCodec.ReadAsync(_stream, cancellationToken);
            if (message is null)
            {
                return null;
            }

            if (message.Kind == BrokerTransportMessageKind.Envelope)
            {
                return message.Envelope
                    ?? throw new InvalidDataException("Broker envelope message did not include an envelope.");
            }
        }
    }

    public ValueTask DisposeAsync()
    {
        _stream.Dispose();
        _client.Dispose();
        return ValueTask.CompletedTask;
    }
}

