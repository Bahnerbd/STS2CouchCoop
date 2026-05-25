using LocalCoop.Protocol;

namespace LocalCoop.Mod.Runtime;

public interface IBrokerEnvelopeTransport
{
    Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken);
}

