namespace LocalCoop.Protocol;

public enum BrokerTransportMessageKind
{
    Registration,
    RegistrationAccepted,
    Envelope
}

public sealed record BrokerTransportMessage(
    BrokerTransportMessageKind Kind,
    BrokerClientRegistrationDto? Registration,
    BrokerRegistrationAccepted? RegistrationAccepted,
    BrokerEnvelope? Envelope)
{
    public static BrokerTransportMessage ForRegistration(BrokerClientRegistrationDto registration)
    {
        return new BrokerTransportMessage(BrokerTransportMessageKind.Registration, registration, RegistrationAccepted: null, Envelope: null);
    }

    public static BrokerTransportMessage ForRegistrationAccepted(string clientId, string sessionId)
    {
        return new BrokerTransportMessage(
            BrokerTransportMessageKind.RegistrationAccepted,
            Registration: null,
            new BrokerRegistrationAccepted(clientId, sessionId),
            Envelope: null);
    }

    public static BrokerTransportMessage ForEnvelope(BrokerEnvelope envelope)
    {
        return new BrokerTransportMessage(BrokerTransportMessageKind.Envelope, Registration: null, RegistrationAccepted: null, envelope);
    }
}

public sealed record BrokerClientRegistrationDto(
    string ClientId,
    BrokerClientRole Role,
    int ClientIndex);

public sealed record BrokerRegistrationAccepted(string ClientId, string SessionId);
