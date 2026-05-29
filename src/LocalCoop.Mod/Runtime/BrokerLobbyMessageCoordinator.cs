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

    private readonly Action<string>? _log;
    private readonly object _gate = new();
    private readonly List<BrokerEnvelope> _pendingFifo = [];
    private readonly Dictionary<StateKey, BrokerEnvelope> _pendingStateByKey = new();
    private readonly List<StateKey> _pendingStateOrder = [];
    private bool _isLobbyReady;

    public BrokerLobbyMessageCoordinator(Action<string>? log = null)
    {
        _log = log;
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
            if (IsLobbyStateMessage(envelope))
            {
                EnqueueState(envelope);
                return;
            }

            _pendingFifo.Add(envelope);
        }
    }

    public IReadOnlyList<BrokerEnvelope> DrainDispatchable(IReadOnlySet<string> registeredMessageTypes)
    {
        lock (_gate)
        {
            var dispatchable = new List<BrokerEnvelope>();
            DrainFifo(registeredMessageTypes, dispatchable);
            DrainState(registeredMessageTypes, dispatchable);
            return dispatchable;
        }
    }

    private void EnqueueState(BrokerEnvelope envelope)
    {
        var key = new StateKey(envelope.SourceClientId, envelope.MessageType);
        if (_pendingStateByKey.TryGetValue(key, out var existing))
        {
            if (existing.Payload.SequenceEqual(envelope.Payload))
            {
                _log?.Invoke($"Broker inbound duplicate state ignored: source={envelope.SourceClientId} messageType={envelope.MessageType} sequence={envelope.Sequence}.");
                return;
            }

            _log?.Invoke($"Broker inbound state coalesced: source={envelope.SourceClientId} messageType={envelope.MessageType} previousSequence={existing.Sequence} sequence={envelope.Sequence}.");
        }
        else
        {
            _pendingStateOrder.Add(key);
        }

        _pendingStateByKey[key] = envelope;
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

    private void DrainState(IReadOnlySet<string> registeredMessageTypes, List<BrokerEnvelope> dispatchable)
    {
        for (var index = 0; index < _pendingStateOrder.Count;)
        {
            var key = _pendingStateOrder[index];
            var envelope = _pendingStateByKey[key];
            if (!CanDispatchLobbyMessage(envelope, registeredMessageTypes))
            {
                index++;
                continue;
            }

            dispatchable.Add(envelope);
            _pendingStateByKey.Remove(key);
            _pendingStateOrder.RemoveAt(index);
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

    private readonly record struct StateKey(string SourceClientId, string MessageType);
}
