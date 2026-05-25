using LocalCoop.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Protocol.Tests;

[TestClass]
public sealed class BrokerClientConfigTests
{
    [TestMethod]
    public void ParsesHostConfig()
    {
        var config = BrokerClientConfig.Parse("""
            role=host
            clientIndex=0
            endpoint=127.0.0.1:38989
            sessionId=local-test
            """);

        Assert.AreEqual(BrokerClientRole.Host, config.Role);
        Assert.AreEqual(0, config.ClientIndex);
        Assert.AreEqual("127.0.0.1", config.Host);
        Assert.AreEqual(38989, config.Port);
        Assert.AreEqual("local-test", config.SessionId);
    }

    [TestMethod]
    public void RejectsClientIndexOutsideFourLocalClients()
    {
        var exception = Assert.ThrowsException<FormatException>(() => BrokerClientConfig.Parse("""
            role=client
            clientIndex=4
            endpoint=127.0.0.1:38989
            sessionId=local-test
            """));

        StringAssert.Contains(exception.Message, "clientIndex");
    }

    [TestMethod]
    public void RequiresHostRoleAtClientIndexZero()
    {
        var exception = Assert.ThrowsException<FormatException>(() => BrokerClientConfig.Parse("""
            role=client
            clientIndex=0
            endpoint=127.0.0.1:38989
            sessionId=local-test
            """));

        StringAssert.Contains(exception.Message, "host");
    }
}
