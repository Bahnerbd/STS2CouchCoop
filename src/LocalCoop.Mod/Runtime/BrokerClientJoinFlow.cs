using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Connection;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Runs;

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
        return new JoinResult
        {
            gameMode = GameMode.Standard,
            sessionState = RunSessionState.InLobby,
            joinResponse = new ClientLobbyJoinResponseMessage
            {
                playersInLobby =
                [
                    new LobbyPlayer
                    {
                        id = BrokerPlayerId.ForClientIndex(0),
                        slotId = 0,
                        maxMultiplayerAscensionUnlocked = 0,
                        isReady = false
                    }
                ],
                modifiers = []
            }
        };
    }

    public sealed class PlaceholderClientConnectionInitializer : IClientConnectionInitializer
    {
        public Task<NetErrorInfo?> Connect(NetClientGameService service, CancellationToken cancelToken)
        {
            return Task.FromResult<NetErrorInfo?>(null);
        }
    }
}
