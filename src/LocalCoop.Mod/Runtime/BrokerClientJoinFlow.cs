using System.Reflection;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;

namespace LocalCoop.Mod.Runtime;

public static class BrokerClientJoinFlow
{
    public static bool ShouldUseBrokerJoin(BrokerModeSettings settings)
    {
        return settings.Enabled
            && settings.Config is { Role: BrokerClientRole.Client };
    }

    public static JoinResult CreateStandardLobbyJoinResult()
    {
        return CreateStandardLobbyJoinResult(1);
    }

    public static JoinResult CreateStandardLobbyJoinResult(int localClientIndex)
    {
        return new JoinResult
        {
            gameMode = GameMode.Standard,
            sessionState = RunSessionState.InLobby,
            joinResponse = new ClientLobbyJoinResponseMessage
            {
                playersInLobby = CreateInitialLobbyPlayers(localClientIndex),
                modifiers = []
            }
        };
    }

    private static List<LobbyPlayer> CreateInitialLobbyPlayers(int localClientIndex)
    {
        var players = new List<LobbyPlayer>
        {
            CreateLobbyPlayer(0)
        };

        if (localClientIndex != 0)
        {
            players.Add(CreateLobbyPlayer(localClientIndex));
        }

        return players;
    }

    private static LobbyPlayer CreateLobbyPlayer(int clientIndex)
    {
        return new LobbyPlayer
        {
            id = BrokerPlayerId.ForClientIndex(clientIndex),
            slotId = clientIndex,
            maxMultiplayerAscensionUnlocked = 0,
            isReady = false
        };
    }

    public static ClientLobbyJoinRequestMessage CreateJoinRequest(
        int maxAscensionUnlocked,
        SerializableUnlockState unlockState)
    {
        return new ClientLobbyJoinRequestMessage
        {
            maxAscensionUnlocked = maxAscensionUnlocked,
            unlockState = unlockState
        };
    }

    public static ClientLobbyJoinRequestMessage CreateJoinRequestFromCurrentSave(Action<string>? log)
    {
        var maxAscensionUnlocked = 0;
        var unlockState = new SerializableUnlockState();

        try
        {
            var saveManagerType = ResolveType("MegaCrit.Sts2.Core.Saves.SaveManager");
            var saveManager = saveManagerType?.GetProperty("Instance", BindingFlags.Public | BindingFlags.Static)?.GetValue(null);
            if (saveManager is null)
            {
                log?.Invoke("Broker client lobby handshake: SaveManager unavailable; using default join request progress.");
                return CreateJoinRequest(maxAscensionUnlocked, unlockState);
            }

            var progress = saveManager.GetType().GetProperty("Progress", BindingFlags.Public | BindingFlags.Instance)?.GetValue(saveManager);
            var maxAscension = progress?.GetType().GetProperty("MaxMultiplayerAscension", BindingFlags.Public | BindingFlags.Instance)?.GetValue(progress);
            if (maxAscension is int parsedMaxAscension)
            {
                maxAscensionUnlocked = parsedMaxAscension;
            }

            var generatedUnlockState = saveManager.GetType()
                .GetMethod("GenerateUnlockStateFromProgress", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(saveManager, null);
            var serializableUnlockState = generatedUnlockState?.GetType()
                .GetMethod("ToSerializable", BindingFlags.Public | BindingFlags.Instance)
                ?.Invoke(generatedUnlockState, null);
            if (serializableUnlockState is SerializableUnlockState parsedUnlockState)
            {
                unlockState = parsedUnlockState;
            }
        }
        catch (Exception exception)
        {
            log?.Invoke($"Broker client lobby handshake: failed to read save progress; using default join request progress: {exception.GetType().Name}: {exception.Message}");
        }

        return CreateJoinRequest(maxAscensionUnlocked, unlockState);
    }

    public static void TryHideJoinScreen(object screen, Action<string>? log)
    {
        try
        {
            TryPopJoinScreenFromStack(screen, log);
            screen.GetType()
                .GetMethod("set_Visible", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance, [typeof(bool)])
                ?.Invoke(screen, [false]);
            log?.Invoke("Broker join friend screen: hiding join screen after broker join trigger.");
        }
        catch (Exception exception)
        {
            log?.Invoke($"Broker join friend screen: failed to hide join screen: {exception.GetType().Name}: {exception.Message}");
        }
    }

    private static void TryPopJoinScreenFromStack(object screen, Action<string>? log)
    {
        var stack = FindInstanceField(screen.GetType(), "_stack")?.GetValue(screen);
        if (stack is null)
        {
            return;
        }

        var peek = stack.GetType()
            .GetMethod("Peek", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(stack, null);
        if (!ReferenceEquals(peek, screen))
        {
            return;
        }

        stack.GetType()
            .GetMethod("Pop", BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.Instance)
            ?.Invoke(stack, null);
        log?.Invoke("Broker join friend screen: popped join screen from submenu stack.");
    }

    private static FieldInfo? FindInstanceField(Type type, string name)
    {
        for (var current = type; current is not null; current = current.BaseType)
        {
            var field = current.GetField(name, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);
            if (field is not null)
            {
                return field;
            }
        }

        return null;
    }

    private static Type? ResolveType(string fullName)
    {
        return Type.GetType($"{fullName}, sts2", throwOnError: false)
            ?? AppDomain.CurrentDomain.GetAssemblies()
                .FirstOrDefault(assembly => string.Equals(assembly.GetName().Name, "sts2", StringComparison.OrdinalIgnoreCase))
                ?.GetType(fullName, throwOnError: false);
    }

    public sealed class PlaceholderClientConnectionInitializer : IClientConnectionInitializer
    {
        public Task<NetErrorInfo?> Connect(NetClientGameService service, CancellationToken cancelToken)
        {
            return Task.FromResult<NetErrorInfo?>(null);
        }
    }
}
