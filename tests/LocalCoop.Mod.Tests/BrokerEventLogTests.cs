using LocalCoop.Mod.Runtime;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace LocalCoop.Mod.Tests;

[TestClass]
public sealed class BrokerEventLogTests
{
    [TestMethod]
    public void WriteAppendsTimestampedMessage()
    {
        var directory = CreateTempDirectory();
        var logPath = Path.Combine(directory, "localcoop-client-1-events.txt");
        var log = new BrokerEventLog(logPath);

        log.Write("broker mode enabled");

        var line = File.ReadAllText(logPath);
        StringAssert.Contains(line, "broker mode enabled");
        var timestamp = line.Split(' ', 2)[0];
        Assert.IsTrue(DateTimeOffset.TryParse(timestamp, out _), $"Timestamp was not parseable: {timestamp}");
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "LocalCoopModTests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }
}
