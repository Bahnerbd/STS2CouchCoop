using System.Collections;

namespace LocalCoop.Mod.Runtime;

public static class BrokerLobbyPayloadDiagnostics
{
    private static readonly string[] KnownLobbyMessageTypeNames =
    [
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinRequestMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginRunMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerChangedCharacterMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerSetReadyMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerJoinedMessage",
        "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerLeftMessage"
    ];

    public static string Summarize(object? message)
    {
        if (message is null)
        {
            return string.Empty;
        }

        var typeName = message.GetType().FullName ?? message.GetType().Name;
        if (Matches(typeName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerSetReadyMessage"))
        {
            return $"ready={ReadMember(message, "ready")}";
        }

        if (Matches(typeName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyPlayerChangedCharacterMessage"))
        {
            return $"character={SummarizeCharacter(ReadMember(message, "character"))}";
        }

        if (Matches(typeName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.LobbyBeginRunMessage")
            || Matches(typeName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinResponseMessage"))
        {
            return SummarizePlayers(ReadMember(message, "playersInLobby"));
        }

        if (Matches(typeName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerJoinedMessage"))
        {
            return SummarizePlayerEnvelope(message, "player");
        }

        if (Matches(typeName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.PlayerLeftMessage"))
        {
            return $"playerId={ReadFirstMember(message, "playerId", "id", "playerNetId")}";
        }

        if (Matches(typeName, "MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby.ClientLobbyJoinRequestMessage"))
        {
            return $"netId={ReadFirstMember(message, "netId", "NetId", "playerId", "id")}";
        }

        return string.Empty;
    }

    public static string Summarize(BrokerEnvelope envelope)
    {
        if (!KnownLobbyMessageTypeNames.Any(typeName => Matches(envelope.MessageType, typeName)))
        {
            return string.Empty;
        }

        var messageType = Type.GetType(envelope.MessageType, throwOnError: false);
        if (messageType is null)
        {
            return "payloadType=unavailable";
        }

        try
        {
            return Summarize(BrokerEnvelopeMessageSerializer.Deserialize(envelope, messageType));
        }
        catch (Exception exception)
        {
            return $"payloadSummaryError={exception.GetType().Name}";
        }
    }

    private static string SummarizePlayers(object? players)
    {
        if (players is not IEnumerable enumerable)
        {
            return "players=[]";
        }

        var summaries = new List<string>();
        foreach (var player in enumerable)
        {
            summaries.Add(SummarizePlayer(player));
        }

        return $"players=[{string.Join("; ", summaries)}]";
    }

    private static string SummarizePlayerEnvelope(object message, string playerMemberName)
    {
        var player = ReadMember(message, playerMemberName);
        return player is null ? "player=null" : $"player={SummarizePlayer(player)}";
    }

    private static string SummarizePlayer(object? player)
    {
        if (player is null)
        {
            return "null";
        }

        return $"id={ReadMember(player, "id")},slot={ReadMember(player, "slotId")},ready={ReadMember(player, "isReady")},character={SummarizeCharacter(ReadMember(player, "character"))}";
    }

    private static string SummarizeCharacter(object? character)
    {
        if (character is null)
        {
            return "null";
        }

        var id = ReadFirstMember(character, "id", "Id", "key", "Key", "name", "Name");
        var typeName = character.GetType().Name;
        return id is null ? typeName : $"{typeName}:{id}";
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
        var type = instance.GetType();
        var field = type.GetField(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        if (field is not null)
        {
            return field.GetValue(instance);
        }

        var property = type.GetProperty(
            name,
            System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
        return property?.GetValue(instance);
    }

    private static bool Matches(string messageType, string fullName)
    {
        return string.Equals(messageType, fullName, StringComparison.Ordinal)
            || messageType.StartsWith(fullName + ",", StringComparison.Ordinal);
    }
}
