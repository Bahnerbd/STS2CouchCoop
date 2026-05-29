namespace LocalCoop.Mod.Runtime;

public sealed class BrokerBackedNetService
{
    private readonly string _sessionId;
    private readonly string _clientId;
    private readonly IBrokerEnvelopeTransport _transport;
    private readonly Action<string>? _log;
    private readonly Dictionary<string, List<Delegate>> _handlersByMessageType = new(StringComparer.Ordinal);
    private long _sequence;

    public BrokerBackedNetService(
        string sessionId,
        string clientId,
        int clientIndex,
        IBrokerEnvelopeTransport transport,
        Action<string>? log = null)
    {
        _sessionId = string.IsNullOrWhiteSpace(sessionId)
            ? throw new ArgumentException("Session id must not be blank.", nameof(sessionId))
            : sessionId;
        _clientId = string.IsNullOrWhiteSpace(clientId)
            ? throw new ArgumentException("Client id must not be blank.", nameof(clientId))
            : clientId;
        NetId = BrokerPlayerId.ForClientIndex(clientIndex);
        _transport = transport;
        _log = log;
    }

    public ulong NetId { get; }

    public bool IsConnected { get; private set; } = true;

    public bool IsGameLoading { get; private set; }

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
    }

    private void UnregisterHandler(string key, Delegate handler)
    {
        if (!_handlersByMessageType.TryGetValue(key, out var handlers))
        {
            return;
        }

        handlers.Remove(handler);
        if (handlers.Count == 0)
        {
            _handlersByMessageType.Remove(key);
        }
    }

    public async Task SendMessageAsync<T>(T message, ulong? targetPlayerId, CancellationToken cancellationToken)
    {
        if (ShouldSuppressOutboundMessage(message, out var reason))
        {
            _log?.Invoke($"Broker suppressed outbound: sessionId={_sessionId} source={_clientId} messageType={MessageTypeKey<T>()} reason={reason}.");
            return;
        }

        var targetClientId = targetPlayerId is null ? null : PlayerIdToClientId(targetPlayerId.Value);
        var envelope = BrokerEnvelopeMessageSerializer.ToEnvelope(
            _sessionId,
            _clientId,
            targetClientId,
            message,
            Interlocked.Increment(ref _sequence));
        _log?.Invoke($"Broker outbound: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}.");
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
                    await DispatchEnvelopeAsync(envelope, cancellationToken).ConfigureAwait(false);
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
    }

    public void SetGameLoading(bool isGameLoading)
    {
        IsGameLoading = isGameLoading;
    }

    public string GetRawLobbyIdentifier()
    {
        return _sessionId;
    }

    private static bool ShouldSuppressOutboundMessage<T>(T message, out string reason)
    {
        var messageType = typeof(T);
        if (string.Equals(messageType.FullName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Game.Sync.PeerInputMessage", StringComparison.Ordinal))
        {
            reason = "peer input is not required for lobby-only broker sync";
            return true;
        }

        if (string.Equals(messageType.FullName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerChangedCharacterMessage", StringComparison.Ordinal)
            && IsNullCharacterChange(message))
        {
            reason = "null character change is an initialization artifact";
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

    public Task DispatchEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        _log?.Invoke($"Broker inbound: sessionId={envelope.SessionId} source={envelope.SourceClientId} target={envelope.TargetClientId ?? "broadcast"} messageType={envelope.MessageType} sequence={envelope.Sequence}.");
        if (!_handlersByMessageType.TryGetValue(envelope.MessageType, out var handlers))
        {
            return Task.CompletedTask;
        }

        foreach (var handler in handlers.ToArray())
        {
            var parameters = handler.Method.GetParameters();
            var parameterType = parameters[0].ParameterType;
            var message = BrokerEnvelopeMessageSerializer.Deserialize(envelope, parameterType);
            if (parameters.Length == 1)
            {
                InvokeHandler(handler, message);
            }
            else
            {
                InvokeHandler(handler, message, ClientIdToPlayerId(envelope.SourceClientId));
            }
        }

        return Task.CompletedTask;
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
