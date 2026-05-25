using LocalCoop.Mod.Runtime;
using LocalCoop.Protocol;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerModeSettingsTests
{
    [TestMethod]
    public void LoadFromDirectoryReturnsDisabledWhenMarkerIsAbsent()
    {
        var modDirectory = CreateTempDirectory();

        var settings = BrokerModeSettings.LoadFromDirectory(modDirectory);

        Assert.IsFalse(settings.Enabled);
        Assert.IsNull(settings.Config);
        Assert.AreEqual(Path.Combine(modDirectory, "localcoop-events.txt"), settings.EventLogPath);
    }

    [TestMethod]
    public void LoadFromDirectoryParsesMarkerAndDerivesClientIdentity()
    {
        var modDirectory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(modDirectory, BrokerModeSettings.MarkerFileName), """
            role=client
            clientIndex=2
            endpoint=127.0.0.1:38989
            sessionId=local-test
            """);

        var settings = BrokerModeSettings.LoadFromDirectory(modDirectory);

        Assert.IsTrue(settings.Enabled);
        Assert.AreEqual(BrokerClientRole.Client, settings.Config?.Role);
        Assert.AreEqual("client-2", settings.ClientId);
        Assert.AreEqual(Path.Combine(modDirectory, "localcoop-client-2-events.txt"), settings.EventLogPath);
    }

    [TestMethod]
    public void LoadFromDirectoryKeepsFailureReasonForMalformedMarker()
    {
        var modDirectory = CreateTempDirectory();
        File.WriteAllText(Path.Combine(modDirectory, BrokerModeSettings.MarkerFileName), "role=client");

        var settings = BrokerModeSettings.LoadFromDirectory(modDirectory);

        Assert.IsFalse(settings.Enabled);
        StringAssert.Contains(settings.FailureReason, "Missing broker config key");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "LocalCoopModTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}

