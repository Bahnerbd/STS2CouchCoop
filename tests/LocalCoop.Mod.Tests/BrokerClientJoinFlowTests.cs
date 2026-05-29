using LocalCoop.Mod.Runtime;
using MegaCrit.Sts2.Core.Entities.Multiplayer;
using MegaCrit.Sts2.Core.Multiplayer.Game;
using MegaCrit.Sts2.Core.Multiplayer.Messages.Lobby;
using MegaCrit.Sts2.Core.Runs;
using MegaCrit.Sts2.Core.Unlocks;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerClientJoinFlowTests
{
    [TestMethod]
    public void ShouldUseBrokerJoinForAnyEnabledBrokerMode()
    {
        var clientSettings = Settings(BrokerClientRole.Client);
        var hostSettings = Settings(BrokerClientRole.Host);

        Assert.IsTrue(BrokerClientJoinFlow.ShouldUseBrokerJoin(clientSettings));
        Assert.IsTrue(BrokerClientJoinFlow.ShouldUseBrokerJoin(hostSettings));
        Assert.IsFalse(BrokerClientJoinFlow.ShouldUseBrokerJoin(new BrokerModeSettings(false, null, "client-0", "events.txt", null)));
    }

    [TestMethod]
    public void CreateStandardLobbyJoinResultContainsOnlyLocalIdentityWithoutCharacters()
    {
        var result = BrokerClientJoinFlow.CreateStandardLobbyJoinResult();

        Assert.AreEqual(GameMode.Standard, result.gameMode);
        Assert.AreEqual(RunSessionState.InLobby, result.sessionState);
        Assert.IsTrue(result.joinResponse.HasValue);

        var response = result.joinResponse.Value;
        Assert.IsNotNull(response.playersInLobby);
        Assert.AreEqual(1, response.playersInLobby.Count);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(1), response.playersInLobby[0].id);
        Assert.AreEqual(1, response.playersInLobby[0].slotId);
        Assert.IsFalse(response.playersInLobby[0].isReady);
        Assert.IsNull(response.playersInLobby[0].character);
        Assert.IsNotNull(response.modifiers);
        Assert.AreEqual(0, response.modifiers.Count);
    }

    [TestMethod]
    public void CreateStandardLobbyJoinResultUsesConfiguredClientIndexForIdentityOnly()
    {
        var factory = typeof(BrokerClientJoinFlow).GetMethod(nameof(BrokerClientJoinFlow.CreateStandardLobbyJoinResult), [typeof(int)]);

        Assert.IsNotNull(factory);

        var result = (JoinResult)factory!.Invoke(null, [2])!;
        var response = result.joinResponse!.Value;
        var players = response.playersInLobby;

        Assert.IsNotNull(players);
        Assert.AreEqual(1, players!.Count);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(2), players[0].id);
        Assert.AreEqual(2, players[0].slotId);
        Assert.IsNull(players[0].character);
    }

    [TestMethod]
    public void PlaceholderInitializerCompletesWithoutSteamConnection()
    {
        var initializer = new BrokerClientJoinFlow.PlaceholderClientConnectionInitializer();

        var result = initializer.Connect(null!, CancellationToken.None).Result;

        Assert.IsFalse(result.HasValue);
    }

    [TestMethod]
    public void CreateJoinRequestUsesProgressAndUnlockState()
    {
        var unlockState = new SerializableUnlockState();

        var request = BrokerClientJoinFlow.CreateJoinRequest(7, unlockState);

        Assert.AreEqual(7, request.maxAscensionUnlocked);
        Assert.AreEqual(unlockState, request.unlockState);
    }

    [TestMethod]
    public void TryHideJoinScreenInvokesGodotVisibleSetter()
    {
        var screen = new FakeJoinScreen();
        var messages = new List<string>();

        BrokerClientJoinFlow.TryHideJoinScreen(screen, messages.Add);

        Assert.IsFalse(screen.VisibleState);
        StringAssert.Contains(messages.Single(), "hiding join screen");
    }

    [TestMethod]
    public void TryHideJoinScreenPopsScreenFromSubmenuStackWhenItIsOnTop()
    {
        var screen = new FakeJoinScreenWithStack();
        var messages = new List<string>();

        BrokerClientJoinFlow.TryHideJoinScreen(screen, messages.Add);

        Assert.AreEqual(1, screen.Stack.PopCount);
        Assert.IsFalse(screen.VisibleState);
        Assert.IsTrue(messages.Any(message => message.Contains("popped join screen", StringComparison.Ordinal)));
    }

    private static BrokerModeSettings Settings(BrokerClientRole role)
    {
        return new BrokerModeSettings(
            true,
            new BrokerClientConfig(role, role == BrokerClientRole.Host ? 0 : 1, "127.0.0.1", 38993, "local-test"),
            role == BrokerClientRole.Host ? "client-0" : "client-1",
            "events.txt",
            null);
    }

    private sealed class FakeJoinScreen
    {
        public bool VisibleState { get; private set; } = true;

        public void set_Visible(bool visible)
        {
            VisibleState = visible;
        }
    }

    private sealed class FakeJoinScreenWithStack
    {
        private readonly FakeSubmenuStack _stack;

        public FakeJoinScreenWithStack()
        {
            _stack = new FakeSubmenuStack(this);
        }

        public FakeSubmenuStack Stack => _stack;

        public bool VisibleState { get; private set; } = true;

        public void set_Visible(bool visible)
        {
            VisibleState = visible;
        }
    }

    private sealed class FakeSubmenuStack(object top)
    {
        public int PopCount { get; private set; }

        public object Peek()
        {
            return top;
        }

        public void Pop()
        {
            PopCount++;
        }
    }
}
