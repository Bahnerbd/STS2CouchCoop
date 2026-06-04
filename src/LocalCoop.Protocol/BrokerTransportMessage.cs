using System.Text.Json.Serialization;

namespace LocalCoop.Protocol;

public enum BrokerTransportMessageKind
{
    Registration,
    RegistrationAccepted,
    Envelope,
    PeerRegistered
}

public sealed record BrokerTransportMessage(
    BrokerTransportMessageKind Kind,
    BrokerClientRegistrationDto? Registration,
    BrokerRegistrationAccepted? RegistrationAccepted,
    BrokerEnvelope? Envelope,
    BrokerClientRegistrationDto? PeerRegistration)
{
    public static BrokerTransportMessage ForRegistration(BrokerClientRegistrationDto registration)
    {
        return new BrokerTransportMessage(
            BrokerTransportMessageKind.Registration,
            registration,
            RegistrationAccepted: null,
            Envelope: null,
            PeerRegistration: null);
    }

    public static BrokerTransportMessage ForRegistrationAccepted(
        string clientId,
        string sessionId,
        IReadOnlyList<BrokerClientRegistrationDto>? connectedPeers = null)
    {
        return new BrokerTransportMessage(
            BrokerTransportMessageKind.RegistrationAccepted,
            Registration: null,
            new BrokerRegistrationAccepted(clientId, sessionId, connectedPeers),
            Envelope: null,
            PeerRegistration: null);
    }

    public static BrokerTransportMessage ForEnvelope(BrokerEnvelope envelope)
    {
        return new BrokerTransportMessage(
            BrokerTransportMessageKind.Envelope,
            Registration: null,
            RegistrationAccepted: null,
            envelope,
            PeerRegistration: null);
    }

    public static BrokerTransportMessage ForPeerRegistered(BrokerClientRegistrationDto peerRegistration)
    {
        return new BrokerTransportMessage(
            BrokerTransportMessageKind.PeerRegistered,
            Registration: null,
            RegistrationAccepted: null,
            Envelope: null,
            peerRegistration);
    }
}

public sealed record BrokerClientRegistrationDto(
    string ClientId,
    BrokerClientRole Role,
    int ClientIndex);

public sealed record BrokerRegistrationAccepted
{
    [JsonConstructor]
    public BrokerRegistrationAccepted(
        string clientId,
        string sessionId,
        IReadOnlyList<BrokerClientRegistrationDto>? connectedPeers = null)
    {
        ClientId = clientId;
        SessionId = sessionId;
        ConnectedPeers = connectedPeers ?? [];
    }

    public string ClientId { get; init; }

    public string SessionId { get; init; }

    public IReadOnlyList<BrokerClientRegistrationDto> ConnectedPeers { get; init; }
}
