namespace LocalCoop.Mod.Runtime;

public sealed class BrokerLobbyMessageCoordinator
{
    private static readonly string[] LobbyStateMessageTypeNames =
    [
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerChangedCharacterMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerSetReadyMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerJoinedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerLeftMessage"
    ];

    private static readonly string[] LobbyControlMessageTypeNames =
    [
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinRequestMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage"
    ];

    private static readonly string[] HandlerOptionalLobbyControlMessageTypeNames =
    [
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage"
    ];

    private readonly object _gate = new();
    private readonly List<BrokerEnvelope> _pendingFifo = [];
    private bool _isLobbyReady;

    public BrokerLobbyMessageCoordinator(Action<string>? log = null)
    {
        _ = log;
    }

    public void MarkLobbyReady()
    {
        lock (_gate)
        {
            _isLobbyReady = true;
        }
    }

    public void Enqueue(BrokerEnvelope envelope)
    {
        lock (_gate)
        {
            _pendingFifo.Add(envelope);
        }
    }

    public IReadOnlyList<BrokerEnvelope> DrainDispatchable(IReadOnlySet<string> registeredMessageTypes)
    {
        lock (_gate)
        {
            var dispatchable = new List<BrokerEnvelope>();
            DrainFifo(registeredMessageTypes, dispatchable);
            return dispatchable;
        }
    }

    private void DrainFifo(IReadOnlySet<string> registeredMessageTypes, List<BrokerEnvelope> dispatchable)
    {
        for (var index = 0; index < _pendingFifo.Count;)
        {
            var envelope = _pendingFifo[index];
            if (IsGatedLobbyMessage(envelope) && (!CanDispatchLobbyMessage(envelope, registeredMessageTypes)))
            {
                index++;
                continue;
            }

            dispatchable.Add(envelope);
            _pendingFifo.RemoveAt(index);
        }
    }

    private bool CanDispatchLobbyMessage(BrokerEnvelope envelope, IReadOnlySet<string> registeredMessageTypes)
    {
        if (!_isLobbyReady)
        {
            return false;
        }

        return MatchesAny(envelope.MessageType, HandlerOptionalLobbyControlMessageTypeNames)
            || registeredMessageTypes.Contains(envelope.MessageType);
    }

    private static bool IsGatedLobbyMessage(BrokerEnvelope envelope)
    {
        return IsLobbyStateMessage(envelope) || MatchesAny(envelope.MessageType, LobbyControlMessageTypeNames);
    }

    private static bool IsLobbyStateMessage(BrokerEnvelope envelope)
    {
        return MatchesAny(envelope.MessageType, LobbyStateMessageTypeNames);
    }

    private static bool MatchesAny(string messageType, IReadOnlyList<string> fullTypeNames)
    {
        return fullTypeNames.Any(typeName =>
            string.Equals(messageType, typeName, StringComparison.Ordinal)
            || messageType.StartsWith(typeName + ",", StringComparison.Ordinal));
    }

}
