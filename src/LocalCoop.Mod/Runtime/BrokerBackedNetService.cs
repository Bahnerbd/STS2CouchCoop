using LocalCoop.Protocol;

namespace LocalCoop.Mod.Runtime;

public sealed class BrokerBackedNetService
{
    private readonly string _sessionId;
    private readonly string _clientId;
    private readonly IBrokerEnvelopeTransport _transport;
    private readonly Dictionary<string, List<Delegate>> _handlersByMessageType = new(StringComparer.Ordinal);
    private long _sequence;

    public BrokerBackedNetService(
        string sessionId,
        string clientId,
        int clientIndex,
        IBrokerEnvelopeTransport transport)
    {
        _sessionId = string.IsNullOrWhiteSpace(sessionId)
            ? throw new ArgumentException("Session id must not be blank.", nameof(sessionId))
            : sessionId;
        _clientId = string.IsNullOrWhiteSpace(clientId)
            ? throw new ArgumentException("Client id must not be blank.", nameof(clientId))
            : clientId;
        NetId = BrokerPlayerId.ForClientIndex(clientIndex);
        _transport = transport;
    }

    public ulong NetId { get; }

    public void RegisterMessageHandler<T>(Action<T> handler)
    {
        var key = MessageTypeKey<T>();
        if (!_handlersByMessageType.TryGetValue(key, out var handlers))
        {
            handlers = [];
            _handlersByMessageType[key] = handlers;
        }

        handlers.Add(handler);
    }

    public void UnregisterMessageHandler<T>(Action<T> handler)
    {
        var key = MessageTypeKey<T>();
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
        var targetClientId = targetPlayerId is null ? null : PlayerIdToClientId(targetPlayerId.Value);
        var envelope = BrokerEnvelopeMessageSerializer.ToEnvelope(
            _sessionId,
            _clientId,
            targetClientId,
            message,
            Interlocked.Increment(ref _sequence));
        await _transport.SendEnvelopeAsync(envelope, cancellationToken);
    }

    public Task DispatchEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
    {
        cancellationToken.ThrowIfCancellationRequested();
        if (!_handlersByMessageType.TryGetValue(envelope.MessageType, out var handlers))
        {
            return Task.CompletedTask;
        }

        foreach (var handler in handlers.ToArray())
        {
            var parameterType = handler.Method.GetParameters().Single().ParameterType;
            var message = BrokerEnvelopeMessageSerializer.Deserialize(envelope, parameterType);
            handler.DynamicInvoke(message);
        }

        return Task.CompletedTask;
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
}

