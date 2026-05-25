using LocalCoop.Protocol;

namespace LocalCoop.Mod.Runtime;

public sealed record BrokerModeSettings(
    bool Enabled,
    BrokerClientConfig? Config,
    string ClientId,
    string EventLogPath,
    string? FailureReason)
{
    public const string MarkerFileName = "enable-local-broker.txt";

    public static BrokerModeSettings LoadFromDirectory(string modDirectory)
    {
        if (string.IsNullOrWhiteSpace(modDirectory))
        {
            throw new ArgumentException("Mod directory must not be blank.", nameof(modDirectory));
        }

        var markerPath = Path.Combine(modDirectory, MarkerFileName);
        if (!File.Exists(markerPath))
        {
            return Disabled(modDirectory, failureReason: null);
        }

        try
        {
            var config = BrokerClientConfig.Parse(File.ReadAllText(markerPath));
            var clientId = $"client-{config.ClientIndex}";
            return new BrokerModeSettings(
                Enabled: true,
                config,
                clientId,
                EventLogPathFor(modDirectory, config),
                FailureReason: null);
        }
        catch (Exception exception) when (exception is FormatException or IOException or UnauthorizedAccessException)
        {
            return Disabled(modDirectory, exception.Message);
        }
    }

    private static BrokerModeSettings Disabled(string modDirectory, string? failureReason)
    {
        return new BrokerModeSettings(
            Enabled: false,
            Config: null,
            ClientId: "client-0",
            Path.Combine(modDirectory, "localcoop-events.txt"),
            failureReason);
    }

    private static string EventLogPathFor(string modDirectory, BrokerClientConfig config)
    {
        var role = config.Role.ToString().ToLowerInvariant();
        return Path.Combine(modDirectory, $"localcoop-{role}-{config.ClientIndex}-events.txt");
    }
}

