using LocalCoop.MultiClientHarness;
using LocalCoop.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.MultiClientHarness.Tests;

[TestClass]
public sealed class ClientLaunchPlanTests
{
    [TestMethod]
    public void CreatesFourSameMachineBrokerConfigs()
    {
        var plan = ClientLaunchPlan.CreateDefault("local-test", "127.0.0.1", 38989);

        Assert.AreEqual(4, plan.Clients.Count);
        CollectionAssert.AreEqual(new[] { "client-0", "client-1", "client-2", "client-3" }, plan.Clients.Select(client => client.ClientId).ToArray());

        var parsed = plan.Clients
            .Select(client => BrokerClientConfig.Parse(client.ConfigContent))
            .ToArray();

        Assert.AreEqual(BrokerClientRole.Host, parsed[0].Role);
        Assert.IsTrue(parsed.Skip(1).All(config => config.Role == BrokerClientRole.Client));
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, parsed.Select(config => config.ClientIndex).ToArray());
        Assert.IsTrue(parsed.All(config => config.SessionId == "local-test"));
        Assert.IsTrue(parsed.All(config => config.Port == 38989));
    }

    [TestMethod]
    public void CreatesTwoClientBrokerConfigsForLobbySmoke()
    {
        var plan = ClientLaunchPlan.CreateTwoClient("local-test", "127.0.0.1", 38989);

        Assert.AreEqual(2, plan.Clients.Count);
        CollectionAssert.AreEqual(new[] { "client-0", "client-1" }, plan.Clients.Select(client => client.ClientId).ToArray());

        var parsed = plan.Clients
            .Select(client => BrokerClientConfig.Parse(client.ConfigContent))
            .ToArray();

        Assert.AreEqual(BrokerClientRole.Host, parsed[0].Role);
        Assert.AreEqual(BrokerClientRole.Client, parsed[1].Role);
        CollectionAssert.AreEqual(new[] { 0, 1 }, parsed.Select(config => config.ClientIndex).ToArray());
    }
}
