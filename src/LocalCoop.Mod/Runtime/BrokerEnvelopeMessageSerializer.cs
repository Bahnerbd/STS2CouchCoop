using LocalCoop.Protocol;
using System.Text.Json;

namespace LocalCoop.Mod.Runtime;

public static class BrokerEnvelopeMessageSerializer
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    public static BrokerEnvelope ToEnvelope<T>(
        string sessionId,
        string sourceClientId,
        string? targetClientId,
        T message,
        long sequence)
    {
        return new BrokerEnvelope(
            sessionId,
            sourceClientId,
            targetClientId,
            typeof(T).AssemblyQualifiedName ?? typeof(T).FullName ?? typeof(T).Name,
            JsonSerializer.SerializeToUtf8Bytes(message, JsonOptions),
            sequence);
    }

    public static object Deserialize(BrokerEnvelope envelope, Type targetType)
    {
        return JsonSerializer.Deserialize(envelope.Payload, targetType, JsonOptions)
            ?? throw new InvalidDataException($"Could not deserialize broker envelope as {targetType.FullName}.");
    }
}

