namespace LocalCoop.Protocol;

public enum BrokerTransportMessageKind
{
    Registration,
    Envelope
}

public sealed record BrokerTransportMessage(
    BrokerTransportMessageKind Kind,
    BrokerClientRegistrationDto? Registration,
    BrokerEnvelope? Envelope)
{
    public static BrokerTransportMessage ForRegistration(BrokerClientRegistrationDto registration)
    {
        return new BrokerTransportMessage(BrokerTransportMessageKind.Registration, registration, Envelope: null);
    }

    public static BrokerTransportMessage ForEnvelope(BrokerEnvelope envelope)
    {
        return new BrokerTransportMessage(BrokerTransportMessageKind.Envelope, Registration: null, envelope);
    }
}

public sealed record BrokerClientRegistrationDto(
    string ClientId,
    BrokerClientRole Role,
    int ClientIndex);

