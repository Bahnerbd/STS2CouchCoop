namespace LocalCoop.MultiClientHarness;

public sealed record ClientLaunchPlan(IReadOnlyList<ClientLaunchPlanEntry> Clients)
{
    public static ClientLaunchPlan CreateDefault(string sessionId, string brokerHost, int brokerPort)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id must not be blank.", nameof(sessionId));
        }

        if (string.IsNullOrWhiteSpace(brokerHost))
        {
            throw new ArgumentException("Broker host must not be blank.", nameof(brokerHost));
        }

        if (brokerPort is <= 0 or > 65535)
        {
            throw new ArgumentOutOfRangeException(nameof(brokerPort), "Broker port must be 1 through 65535.");
        }

        return new ClientLaunchPlan(
            Enumerable.Range(0, 4)
                .Select(index => new ClientLaunchPlanEntry(
                    ClientId: $"client-{index}",
                    ConfigContent: FormatConfig(
                        role: index == 0 ? "host" : "client",
                        clientIndex: index,
                        brokerHost,
                        brokerPort,
                        sessionId)))
                .ToArray());
    }

    private static string FormatConfig(
        string role,
        int clientIndex,
        string brokerHost,
        int brokerPort,
        string sessionId)
    {
        return string.Join(
            Environment.NewLine,
            $"role={role}",
            $"clientIndex={clientIndex}",
            $"endpoint={brokerHost}:{brokerPort}",
            $"sessionId={sessionId}",
            string.Empty);
    }
}

public sealed record ClientLaunchPlanEntry(string ClientId, string ConfigContent);

