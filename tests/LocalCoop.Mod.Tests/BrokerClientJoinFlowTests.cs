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
    public void CreateStandardLobbyJoinResultContainsTwoIdentityPlayersWithoutCharacters()
    {
        var result = BrokerClientJoinFlow.CreateStandardLobbyJoinResult();

        Assert.AreEqual(GameMode.Standard, result.gameMode);
        Assert.AreEqual(RunSessionState.InLobby, result.sessionState);
        Assert.IsTrue(result.joinResponse.HasValue);

        var response = result.joinResponse.Value;
        Assert.IsNotNull(response.playersInLobby);
        Assert.AreEqual(2, response.playersInLobby.Count);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), response.playersInLobby[0].id);
        Assert.AreEqual(0, response.playersInLobby[0].slotId);
        Assert.IsFalse(response.playersInLobby[0].isReady);
        Assert.IsNull(response.playersInLobby[0].character);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(1), response.playersInLobby[1].id);
        Assert.AreEqual(1, response.playersInLobby[1].slotId);
        Assert.IsFalse(response.playersInLobby[1].isReady);
        Assert.IsNull(response.playersInLobby[1].character);
        Assert.IsNotNull(response.modifiers);
        Assert.AreEqual(0, response.modifiers.Count);
    }

    [TestMethod]
    public void CreateStandardLobbyJoinResultUsesOtherClientAsTwoClientHostIdentity()
    {
        var factory = typeof(BrokerClientJoinFlow).GetMethod(nameof(BrokerClientJoinFlow.CreateStandardLobbyJoinResult), [typeof(int)]);

        Assert.IsNotNull(factory);

        var result = (JoinResult)factory!.Invoke(null, [0])!;
        var response = result.joinResponse!.Value;
        var players = response.playersInLobby;

        Assert.IsNotNull(players);
        Assert.AreEqual(2, players!.Count);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(1), players[0].id);
        Assert.AreEqual(0, players[0].slotId);
        Assert.IsNull(players[0].character);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), players[1].id);
        Assert.AreEqual(1, players[1].slotId);
        Assert.IsNull(players[1].character);
    }

    [TestMethod]
    public void CreateStandardLobbyJoinResultCanSeedFourIdentityPlayersWithRuntimeHostFirst()
    {
        var result = BrokerClientJoinFlow.CreateStandardLobbyJoinResult(
            localClientIndex: 3,
            hostClientIndex: 2,
            clientCount: 4);
        var players = result.joinResponse!.Value.playersInLobby;

        Assert.IsNotNull(players);
        CollectionAssert.AreEqual(
            new[]
            {
                BrokerPlayerId.ForClientIndex(2),
                BrokerPlayerId.ForClientIndex(0),
                BrokerPlayerId.ForClientIndex(1),
                BrokerPlayerId.ForClientIndex(3)
            },
            players!.Select(player => player.id).ToArray());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, players.Select(player => player.slotId).ToArray());
        Assert.IsTrue(players.All(player => player.character is null));
        Assert.IsTrue(players.All(player => !player.isReady));
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

    [TestMethod]
    public void TryTriggerBrokerJoinFromOpenedJoinScreenInvokesJoinAndHidesScreenWhenBrokerEnabled()
    {
        var screen = new FakeOpenedJoinScreen();
        var messages = new List<string>();

        var triggered = BrokerClientJoinFlow.TryTriggerBrokerJoinFromOpenedJoinScreen(screen, Settings(BrokerClientRole.Client), messages.Add);

        Assert.IsTrue(triggered);
        Assert.AreEqual(1, screen.JoinGameCount);
        Assert.IsInstanceOfType<BrokerClientJoinFlow.PlaceholderClientConnectionInitializer>(screen.LastInitializer);
        Assert.IsFalse(screen.VisibleState);
        Assert.IsTrue(messages.Any(message => message.Contains("triggering broker client join", StringComparison.Ordinal)));
        Assert.IsTrue(messages.Any(message => message.Contains("hiding join screen", StringComparison.Ordinal)));
    }

    [TestMethod]
    public void TryTriggerBrokerJoinFromOpenedJoinScreenDoesNothingWhenBrokerDisabled()
    {
        var screen = new FakeOpenedJoinScreen();
        var settings = new BrokerModeSettings(false, null, "client-1", "events.txt", null);

        var triggered = BrokerClientJoinFlow.TryTriggerBrokerJoinFromOpenedJoinScreen(screen, settings, _ => { });

        Assert.IsFalse(triggered);
        Assert.AreEqual(0, screen.JoinGameCount);
        Assert.IsTrue(screen.VisibleState);
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

    private sealed class FakeOpenedJoinScreen
    {
        public int JoinGameCount { get; private set; }

        public object? LastInitializer { get; private set; }

        public bool VisibleState { get; private set; } = true;

        public void JoinGame(object initializer)
        {
            JoinGameCount++;
            LastInitializer = initializer;
        }

        public void set_Visible(bool visible)
        {
            VisibleState = visible;
        }
    }
}
