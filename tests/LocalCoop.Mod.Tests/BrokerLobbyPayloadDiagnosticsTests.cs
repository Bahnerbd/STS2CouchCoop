using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerLobbyPayloadDiagnosticsTests
{
    [TestMethod]
    public void SummarizeReadyMessageIncludesReadyValue()
    {
        var summary = BrokerLobbyPayloadDiagnostics.Summarize(new LobbyPlayerSetReadyMessage { ready = true });

        StringAssert.Contains(summary, "ready=True");
    }

    [TestMethod]
    public void SummarizeBeginRunMessageIncludesLobbyPlayers()
    {
        var summary = BrokerLobbyPayloadDiagnostics.Summarize(new LobbyBeginRunMessage
        {
            playersInLobby =
            [
                new LobbyPlayer
                {
                    id = BrokerPlayerId.ForClientIndex(1),
                    slotId = 1,
                    isReady = true
                }
            ]
        });

        StringAssert.Contains(summary, "players=[");
        StringAssert.Contains(summary, "slot=1");
        StringAssert.Contains(summary, "ready=True");
    }
}
