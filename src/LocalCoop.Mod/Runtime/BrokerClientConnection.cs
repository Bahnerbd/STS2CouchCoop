using System.Buffers.Binary;
using System.Net.Sockets;
using System.Text.Json;

namespace LocalCoop.Mod.Runtime;

public sealed class BrokerClientConnection : IAsyncDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
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
        await client.ConnectAsync(config.Host, config.Port, cancellationToken).ConfigureAwait(false);
        var connection = new BrokerClientConnection(client);

        await connection.WriteAsync(BrokerTransportMessage.ForRegistration(
            new BrokerClientRegistrationDto(clientId, config.Role, config.ClientIndex)),
            cancellationToken).ConfigureAwait(false);

        var accepted = await connection.ReadTransportMessageAsync(cancellationToken).ConfigureAwait(false);
        if (accepted?.Kind != BrokerTransportMessageKind.RegistrationAccepted
            || accepted.RegistrationAccepted?.ClientId != clientId
            || accepted.RegistrationAccepted.SessionId != config.SessionId)
        {
            await connection.DisposeAsync();
            throw new InvalidDataException("Broker did not accept registration for the requested client.");
        }

        return connection;
    }

    public Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
    {
        return WriteAsync(BrokerTransportMessage.ForEnvelope(envelope), cancellationToken);
    }

    public async Task<BrokerEnvelope?> ReadEnvelopeAsync(CancellationToken cancellationToken)
    {
        while (true)
        {
            var message = await ReadTransportMessageAsync(cancellationToken);
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

    private async Task WriteAsync(BrokerTransportMessage message, CancellationToken cancellationToken)
    {
        var payload = JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions);
        var lengthPrefix = new byte[4];
        BinaryPrimitives.WriteInt32BigEndian(lengthPrefix, payload.Length);
        await _stream.WriteAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        await _stream.WriteAsync(payload, cancellationToken).ConfigureAwait(false);
        await _stream.FlushAsync(cancellationToken).ConfigureAwait(false);
    }

    private async Task<BrokerTransportMessage?> ReadTransportMessageAsync(CancellationToken cancellationToken)
    {
        var lengthPrefix = new byte[4];
        var prefixBytes = await ReadExactlyOrEndAsync(lengthPrefix, cancellationToken).ConfigureAwait(false);
        if (prefixBytes == 0)
        {
            return null;
        }

        if (prefixBytes < lengthPrefix.Length)
        {
            throw new EndOfStreamException("Broker frame ended during length prefix.");
        }

        var length = BinaryPrimitives.ReadInt32BigEndian(lengthPrefix);
        if (length <= 0)
        {
            throw new InvalidDataException("Broker frame length must be positive.");
        }

        var payload = new byte[length];
        var payloadBytes = await ReadExactlyOrEndAsync(payload, cancellationToken).ConfigureAwait(false);
        if (payloadBytes < length)
        {
            throw new EndOfStreamException("Broker frame ended during payload.");
        }

        return JsonSerializer.Deserialize<BrokerTransportMessage>(payload, JsonOptions)
            ?? throw new InvalidDataException("Broker frame payload did not contain a message.");
    }

    private async Task<int> ReadExactlyOrEndAsync(byte[] buffer, CancellationToken cancellationToken)
    {
        var totalRead = 0;
        while (totalRead < buffer.Length)
        {
            var read = await _stream.ReadAsync(buffer.AsMemory(totalRead), cancellationToken).ConfigureAwait(false);
            if (read == 0)
            {
                return totalRead;
            }

            totalRead += read;
        }

        return totalRead;
    }

    private enum BrokerTransportMessageKind
    {
        Registration,
        RegistrationAccepted,
        Envelope
    }

    private sealed record BrokerTransportMessage(
        BrokerTransportMessageKind Kind,
        BrokerClientRegistrationDto? Registration,
        BrokerRegistrationAccepted? RegistrationAccepted,
        BrokerEnvelope? Envelope)
    {
        public static BrokerTransportMessage ForRegistration(BrokerClientRegistrationDto registration)
        {
            return new BrokerTransportMessage(BrokerTransportMessageKind.Registration, registration, RegistrationAccepted: null, Envelope: null);
        }

        public static BrokerTransportMessage ForEnvelope(BrokerEnvelope envelope)
        {
            return new BrokerTransportMessage(BrokerTransportMessageKind.Envelope, Registration: null, RegistrationAccepted: null, envelope);
        }
    }

    private sealed record BrokerClientRegistrationDto(
        string ClientId,
        BrokerClientRole Role,
        int ClientIndex);

    private sealed record BrokerRegistrationAccepted(string ClientId, string SessionId);
}
