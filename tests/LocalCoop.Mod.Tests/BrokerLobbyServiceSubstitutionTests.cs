using LocalCoop.Mod.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerLobbyServiceSubstitutionTests
{
    [TestMethod]
    public void TrySubstituteFirstArgumentReplacesServiceWhenBrokerModeIsEnabled()
    {
        var originalService = new object();
        object?[] args = [originalService, 4];
        var settings = EnabledSettings();

        var substituted = BrokerLobbyServiceSubstitution.TrySubstituteFirstArgument(
            settings,
            args,
            () => new CapturingTransport(),
            _ => { });

        Assert.IsTrue(substituted);
        Assert.AreNotSame(originalService, args[0]);
        Assert.AreEqual(BrokerPlayerId.ForClientIndex(0), args[0]!.GetType().GetProperty("NetId")!.GetValue(args[0]));
    }

    [TestMethod]
    public void TrySubstituteFirstArgumentLeavesArgumentsWhenBrokerModeIsDisabled()
    {
        var originalService = new object();
        object?[] args = [originalService, 4];
        var settings = new BrokerModeSettings(false, null, "client-0", "events.txt", null);

        var substituted = BrokerLobbyServiceSubstitution.TrySubstituteFirstArgument(
            settings,
            args,
            () => throw new InvalidOperationException("transport should not be created"),
            _ => { });

        Assert.IsFalse(substituted);
        Assert.AreSame(originalService, args[0]);
    }

    private static BrokerModeSettings EnabledSettings()
    {
        return new BrokerModeSettings(
            true,
            new BrokerClientConfig(BrokerClientRole.Host, 0, "127.0.0.1", 38989, "local-test"),
            "client-0",
            "events.txt",
            null);
    }

    private sealed class CapturingTransport : IBrokerEnvelopeTransport
    {
        public Task SendEnvelopeAsync(BrokerEnvelope envelope, CancellationToken cancellationToken)
        {
            return Task.CompletedTask;
        }

        public Task<BrokerEnvelope?> ReceiveEnvelopeAsync(CancellationToken cancellationToken)
        {
            return Task.FromResult<BrokerEnvelope?>(null);
        }
    }
}
