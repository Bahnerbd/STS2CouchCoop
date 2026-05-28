namespace LocalCoop.Mod.Runtime;

public sealed class BrokerClientEnvelopeTransport : IBrokerEnvelopeTransport
{
    private readonly BrokerClientConnection _connection;

    public BrokerClientEnvelopeTransport(BrokerClientConnection connection)
    {
        _connection = connection;
    }

    public Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
    {
        return _connection.SendEnvelopeAsync(envelope, cancellationToken);
    }

    public Task<BrokerEnvelope?> ReceiveEnvelopeAsync(CancellationToken cancellationToken)
    {
        return _connection.ReadEnvelopeAsync(cancellationToken);
    }
}
