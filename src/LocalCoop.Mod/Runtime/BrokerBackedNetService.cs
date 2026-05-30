using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;

namespace LocalCoop.Mod.Runtime;

public sealed class BrokerBackedNetService
{
    private readonly string _sessionId;
    private readonly string _clientId;
    private readonly int _clientIndex;
    private readonly IBrokerEnvelopeTransport _transport;
    private readonly Action<string>? _log;
    private readonly BrokerClientRole? _role;
    private readonly Dictionary<string, List<Delegate>> _handlersByMessageType = new(StringComparer.Ordinal);
    private readonly BrokerLobbyMessageCoordinator _messageCoordinator;
    private readonly Dictionary<ulong, bool> _knownPeersById = [];
    private readonly object _knownPeerGate = new();
    private long _sequence;

    public BrokerBackedNetService(
        string sessionId,
        string clientId,
        int clientIndex,
        IBrokerEnvelopeTransport transport,
        Action<string>? log = null,
        BrokerClientRole? role = null)
    {
        _sessionId = string.IsNullOrWhiteSpace(sessionId)
            ? throw new ArgumentException("Session id must not be blank.", nameof(sessionId))
            : sessionId;
        _clientId = string.IsNullOrWhiteSpace(clientId)
            ? throw new ArgumentException("Client id must not be blank.", nameof(clientId))
            : clientId;
        _clientIndex = clientIndex;
        NetId = BrokerPlayerId.ForClientIndex(clientIndex);
        _transport = transport;
        _log = log;
        _role = role;
        _messageCoordinator = new BrokerLobbyMessageCoordinator(log);
    }

    public event Action<ulong>? PeerTracked;

    public ulong NetId { get; }

    public bool IsConnected { get; private set; } = true;

    public bool IsGameLoading { get; private set; }

    public IReadOnlyList<ulong> ConnectedPeerIds
    {
        get
        {
            lock (_knownPeerGate)
            {
                return _knownPeersById.Keys.Order().ToArray();
            }
        }
    }

    public IReadOnlyList<BrokerConnectedPeer> ConnectedPeers
    {
        get
        {
            lock (_knownPeerGate)
            {
                return _knownPeersById
                    .OrderBy(peer => peer.Key)
                    .Select(peer => new BrokerConnectedPeer(peer.Key, peer.Value))
                    .ToArray();
            }
        }
    }

    public void RegisterMessageHandler<T>(Action<T> handler)
    {
        RegisterHandler(MessageTypeKey<T>(), handler);
    }

    public void RegisterMessageHandler<T>(Action<T, ulong> handler)
    {
        RegisterHandler(MessageTypeKey<T>(), handler);
    }

    public void UnregisterMessageHandler<T>(Action<T> handler)
    {
        UnregisterHandler(MessageTypeKey<T>(), handler);
    }

    public void UnregisterMessageHandler<T>(Action<T, ulong> handler)
    {
        UnregisterHandler(MessageTypeKey<T>(), handler);
    }

    public void Disconnect()
    {
        IsConnected = false;
    }

    private void RegisterHandler(string key, Delegate handler)
    {
        if (!_handlersByMessageType.TryGetValue(key, out var handlers))
        {
            handlers = [];
            _handlersByMessageType[key] = handlers;
        }

        handlers.Add(handler);
        _log?.Invoke($"Broker handler registered: sessionId={_sessionId} messageType={key} handler={FormatHandler(handler)} handlerCount={handlers.Count}.");
    }

    private void UnregisterHandler(string key, Delegate handler)
    {
        if (!_handlersByMessageType.TryGetValue(key, out var handlers))
        {
            return;
        }

        handlers.Remove(handler);
        _log?.Invoke($"Broker handler unregistered: sessionId={_sessionId} messageType={key} handler={FormatHandler(handler)} handlerCount={handlers.Count}.");
        if (handlers.Count == 0)
        {
            _handlersByMessageType.Remove(key);
        }
    }

    public async Task SendMessageAsync<T>(T message, ulong? targetPlayerId, CancellationToken cancellationToken)
    {
        var targetClientId = targetPlayerId is null ? null : PlayerIdToClientId(targetPlayerId.Value);
        if (ShouldSuppressOutboundMessage(message, out var reason))
        {
            _log?.Invoke($"Broker suppressed outbound: sessionId={_sessionId} source={_clientId} messageType={MessageTypeKey<T>()} reason={reason}{PayloadSuffix(message)}.");
            return;
        }

        var envelope = BrokerEnvelopeMessageSerializer.ToEnvelope(
            _sessionId,
            _clientId,
            targetClientId,
            message,
            Interlocked.Increment(ref _sequence));
        TrackKnownPeers(envelope);
        _log?.Invoke($"Broker outbound: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}{PayloadSuffix(message)}.");
        LogThinTransportOutbound(envelope, message);
        await _transport.SendEnvelopeAsync(envelope, cancellationToken);
    }

    public void SendMessage<T>(T message, ulong playerId)
    {
        SendMessageAsync(message, playerId, CancellationToken.None).GetAwaiter().GetResult();
    }

    public void SendMessage<T>(T message)
    {
        SendMessageAsync(message, targetPlayerId: null, CancellationToken.None).GetAwaiter().GetResult();
    }

    public async Task RunReceiveLoopAsync(CancellationToken cancellationToken)
    {
        try
        {
            while (!cancellationToken.IsCancellationRequested)
            {
                var envelope = await _transport.ReceiveEnvelopeAsync(cancellationToken).ConfigureAwait(false);
                if (envelope is null)
                {
                    break;
                }

                try
                {
                    EnqueueInboundEnvelope(envelope);
                }
                catch (Exception exception)
                {
                    _log?.Invoke($"Broker inbound dispatch failed: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}: {exception.GetType().Name}: {exception.Message}");
                }
            }
        }
        catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
        {
        }
        finally
        {
            IsConnected = false;
        }
    }

    public void Update()
    {
        foreach (var envelope in DrainInboundEnvelopes())
        {
            try
            {
                _log?.Invoke($"Broker inbound flushed: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}{PayloadSuffix(envelope)}.");
                var handlerCount = DispatchEnvelopeAsync(envelope, CancellationToken.None).GetAwaiter().GetResult();
                if (handlerCount == 0)
                {
                    _log?.Invoke($"Broker inbound skipped: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence} reason=no registered handler{PayloadSuffix(envelope)}.");
                }
                else
                {
                    _log?.Invoke($"Broker inbound dispatched: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence} handlerCount={handlerCount}{PayloadSuffix(envelope)}.");
                }
            }
            catch (Exception exception)
            {
                _log?.Invoke($"Broker inbound dispatch failed: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}: {exception.GetType().Name}: {exception.Message}");
            }
        }
    }

    public void SetGameLoading(bool isGameLoading)
    {
        IsGameLoading = isGameLoading;
        _log?.Invoke($"Broker game loading changed: sessionId={_sessionId} client={_clientId} isGameLoading={isGameLoading}.");
    }

    public void SetPeerReadyForBroadcasting(ulong peerId)
    {
        if (peerId == 0 || peerId == NetId)
        {
            return;
        }

        var added = false;
        lock (_knownPeerGate)
        {
            added = !_knownPeersById.ContainsKey(peerId);
            _knownPeersById[peerId] = true;
        }

        if (added)
        {
            _log?.Invoke($"Broker peer tracked: sessionId={_sessionId} peerId={peerId}.");
            PeerTracked?.Invoke(peerId);
        }

        _log?.Invoke($"Broker peer ready for broadcasting: sessionId={_sessionId} peerId={peerId}.");
    }

    public void MarkLobbyReady()
    {
        _messageCoordinator.MarkLobbyReady();
    }

    public string GetRawLobbyIdentifier()
    {
        return _sessionId;
    }

    private bool ShouldSuppressOutboundMessage<T>(T message, out string reason)
    {
        var messageType = typeof(T);
        var isLobbyCharacterChange = IsLobbyPlayerChangedCharacter(messageType);
        if (isLobbyCharacterChange && IsNullCharacterChange(message))
        {
            reason = "null character change is an initialization artifact";
            return true;
        }

        if (_role == BrokerClientRole.Client
            && IsSyncPlayerDataMessage(messageType)
            && TryReadSyncPlayerDataNetId(message, out var playerNetId)
            && playerNetId != NetId)
        {
            reason = $"client attempted to publish non-local player data playerNetId={playerNetId} localNetId={NetId}";
            return true;
        }

        reason = string.Empty;
        return false;
    }

    private static bool IsNullCharacterChange<T>(T message)
    {
        if (message is null)
        {
            return true;
        }

        var characterField = typeof(T).GetField(
            "character",
            System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance);
        return characterField is not null && characterField.GetValue(message) is null;
    }

    public async Task<int> DispatchEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        TrackKnownPeers(envelope);

        _log?.Invoke($"Broker inbound: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}{PayloadSuffix(envelope)}.");
        LogThinTransportInbound(envelope);

        if (!_handlersByMessageType.TryGetValue(envelope.MessageType, out var handlers))
        {
            return 0;
        }

        var dispatchHandlers = handlers.ToArray();
        foreach (var handler in dispatchHandlers)
        {
            var parameters = handler.Method.GetParameters();
            var parameterType = parameters[0].ParameterType;
            var message = BrokerEnvelopeMessageSerializer.Deserialize(envelope, parameterType);
            NormalizeInboundMessageForLocalClient(message);
            if (parameters.Length == 1)
            {
                InvokeHandler(handler, message);
            }
            else
            {
                InvokeHandler(handler, message, ClientIdToPlayerId(envelope.SourceClientId));
            }
        }

        await Task.CompletedTask;
        return dispatchHandlers.Length;
    }

    private static bool IsClientLobbyJoinResponse(BrokerEnvelope envelope)
    {
        return envelope.MessageType.StartsWith(
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage,",
            StringComparison.Ordinal);
    }

    private static bool IsClientLobbyJoinRequest(BrokerEnvelope envelope)
    {
        return MatchesMessageType(
            envelope.MessageType,
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinRequestMessage");
    }

    private void TrackKnownPeers(BrokerEnvelope envelope)
    {
        if (IsClientLobbyJoinRequest(envelope))
        {
            AddKnownPeer(ClientIdToPlayerId(envelope.SourceClientId));
            return;
        }

        if (!IsClientLobbyJoinResponse(envelope))
        {
            return;
        }

        AddKnownPeer(ClientIdToPlayerId(envelope.SourceClientId));
        try
        {
            var response = (ClientLobbyJoinResponseMessage)BrokerEnvelopeMessageSerializer.Deserialize(
                envelope,
                typeof(ClientLobbyJoinResponseMessage));
            if (response.playersInLobby is null)
            {
                return;
            }

            foreach (var player in response.playersInLobby)
            {
                AddKnownPeer(player.id);
            }
        }
        catch (Exception exception)
        {
            _log?.Invoke($"Broker peer tracking failed: messageType={envelope.MessageType} sequence={envelope.Sequence}: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private void AddKnownPeer(ulong peerId)
    {
        if (peerId == 0 || peerId == NetId)
        {
            return;
        }

        var added = false;
        lock (_knownPeerGate)
        {
            if (!_knownPeersById.ContainsKey(peerId))
            {
                _knownPeersById.Add(peerId, false);
                added = true;
            }
        }

        if (!added)
        {
            return;
        }

        _log?.Invoke($"Broker peer tracked: sessionId={_sessionId} peerId={peerId}.");
        PeerTracked?.Invoke(peerId);
    }

    private void EnqueueInboundEnvelope(BrokerEnvelope envelope)
    {
        _messageCoordinator.Enqueue(envelope);
        _log?.Invoke($"Broker inbound queued: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}{PayloadSuffix(envelope)}.");
    }

    private BrokerEnvelope[] DrainInboundEnvelopes()
    {
        return _messageCoordinator
            .DrainDispatchable(_handlersByMessageType.Keys.ToHashSet(StringComparer.Ordinal))
            .ToArray();
    }

    private void LogThinTransportOutbound<T>(BrokerEnvelope envelope, T message)
    {
        if (!IsThinTransportLobbyDiagnosticMessage(envelope))
        {
            return;
        }

        _log?.Invoke($"Broker thin transport outbound lobby message: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}{PayloadSuffix(message)}.");
    }

    private void LogThinTransportInbound(BrokerEnvelope envelope)
    {
        if (!IsThinTransportLobbyDiagnosticMessage(envelope))
        {
            return;
        }

        _log?.Invoke($"Broker thin transport inbound lobby message: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}{PayloadSuffix(envelope)}.");
    }

    private static bool IsThinTransportLobbyDiagnosticMessage(BrokerEnvelope envelope)
    {
        return MatchesMessageType(envelope.MessageType, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerChangedCharacterMessage")
            || MatchesMessageType(envelope.MessageType, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerSetReadyMessage")
            || MatchesMessageType(envelope.MessageType, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage")
            || MatchesMessageType(envelope.MessageType, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginRunMessage");
    }

    private static bool IsLobbyPlayerChangedCharacter(Type messageType)
    {
        return string.Equals(
            messageType.FullName,
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerChangedCharacterMessage",
            StringComparison.Ordinal);
    }

    private void NormalizeInboundMessageForLocalClient(object message)
    {
        if (!string.Equals(
            message.GetType().FullName,
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginRunMessage",
            StringComparison.Ordinal))
        {
            return;
        }

        if (ReadMember(message, "playersInLobby") is not System.Collections.IList players
            || players.IsReadOnly
            || players.IsFixedSize
            || players.Count < 2)
        {
            return;
        }

        for (var index = 0; index < players.Count; index++)
        {
            var player = players[index];
            if (player is null
                || !TryReadUInt64(ReadMember(player, "id"), out var playerId)
                || playerId != NetId)
            {
                continue;
            }

            if (index == 0)
            {
                return;
            }

            players.RemoveAt(index);
            players.Insert(0, player);
            _log?.Invoke($"Broker normalized inbound begin run player order: sessionId={_sessionId} client={_clientId} localNetId={NetId} originalIndex={index}.");
            return;
        }
    }

    private static bool IsSyncPlayerDataMessage(Type messageType)
    {
        return string.Equals(
            messageType.FullName,
            "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.SyncPlayerDataMessage",
            StringComparison.Ordinal);
    }

    private static bool TryReadSyncPlayerDataNetId<T>(T message, out ulong playerNetId)
    {
        playerNetId = 0;
        if (message is null)
        {
            return false;
        }

        var player = ReadMember(message, "player");
        if (player is null)
        {
            return false;
        }

        return TryReadUInt64(
            ReadFirstMember(player, "NetId", "netId", "id", "playerId"),
            out playerNetId);
    }

    private static object? ReadFirstMember(object instance, params string[] names)
    {
        foreach (var name in names)
        {
            var value = ReadMember(instance, name);
            if (value is not null)
            {
                return value;
            }
        }

        return null;
    }

    private static object? ReadMember(object instance, string name)
    {
        const System.Reflection.BindingFlags flags =
            System.Reflection.BindingFlags.Instance
            | System.Reflection.BindingFlags.Public
            | System.Reflection.BindingFlags.NonPublic;
        var type = instance.GetType();
        var field = type.GetField(name, flags);
        if (field is not null)
        {
            return field.GetValue(instance);
        }

        var property = type.GetProperty(name, flags);
        return property?.GetValue(instance);
    }

    private static bool TryReadUInt64(object? value, out ulong result)
    {
        switch (value)
        {
            case ulong ulongValue:
                result = ulongValue;
                return true;
            case long longValue when longValue >= 0:
                result = (ulong)longValue;
                return true;
            case uint uintValue:
                result = uintValue;
                return true;
            case int intValue when intValue >= 0:
                result = (ulong)intValue;
                return true;
            case string text when ulong.TryParse(text, out var parsed):
                result = parsed;
                return true;
            default:
                result = 0;
                return false;
        }
    }

    private static bool MatchesMessageType(string messageType, string fullName)
    {
        return string.Equals(messageType, fullName, StringComparison.Ordinal)
            || messageType.StartsWith(fullName + ",", StringComparison.Ordinal);
    }

    private void InvokeHandler(Delegate handler, params object?[] args)
    {
        try
        {
            handler.DynamicInvoke(args);
        }
        catch (Exception exception)
        {
            var actualException = exception.InnerException ?? exception;
            _log?.Invoke($"Broker inbound handler failed: handler={handler.Method.DeclaringType?.FullName}.{handler.Method.Name}: {actualException.GetType().Name}: {actualException.Message}");
        }
    }

    private static string MessageTypeKey<T>()
    {
        return typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name;
    }

    private static string FormatHandler(Delegate handler)
    {
        return $"{handler.Method.DeclaringType?.FullName ?? "<unknown>"}.{handler.Method.Name}";
    }

    private static string PayloadSuffix(object? message)
    {
        var summary = BrokerLobbyPayloadDiagnostics.Summarize(message);
        return string.IsNullOrWhiteSpace(summary) ? string.Empty : $" payload={summary}";
    }

    private static string PayloadSuffix(BrokerEnvelope envelope)
    {
        var summary = BrokerLobbyPayloadDiagnostics.Summarize(envelope);
        return string.IsNullOrWhiteSpace(summary) ? string.Empty : $" payload={summary}";
    }

    private static string PlayerIdToClientId(ulong playerId)
    {
        var clientIndex = BrokerPlayerId.ToClientIndex(playerId);
        if (clientIndex < 0)
        {
            throw new InvalidOperationException($"Player id {playerId} is not a broker player id.");
        }

        return $"client-{clientIndex}";
    }

    private static ulong ClientIdToPlayerId(string clientId)
    {
        const string prefix = "client-";
        return clientId.StartsWith(prefix, StringComparison.Ordinal)
            && int.TryParse(clientId[prefix.Length..], out var clientIndex)
            ? BrokerPlayerId.ForClientIndex(clientIndex)
            : 0;
    }

}
