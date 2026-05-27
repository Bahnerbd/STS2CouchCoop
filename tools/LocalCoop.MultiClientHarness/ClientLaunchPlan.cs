namespace LocalCoop.MultiClientHarness;

public sealed record ClientLaunchPlan(IReadOnlyList<ClientLaunchPlanEntry> Clients)
{
    public static ClientLaunchPlan CreateDefault(string sessionId, string brokerHost, int brokerPort)
    {
        return Create(sessionId, brokerHost, brokerPort, clientCount: 4);
    }

    public static ClientLaunchPlan CreateTwoClient(string sessionId, string brokerHost, int brokerPort)
    {
        return Create(sessionId, brokerHost, brokerPort, clientCount: 2);
    }

    private static ClientLaunchPlan Create(string sessionId, string brokerHost, int brokerPort, int clientCount)
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

        if (clientCount is < 2 or > 4)
        {
            throw new ArgumentOutOfRangeException(nameof(clientCount), "Client count must be 2 through 4.");
        }

        return new ClientLaunchPlan(
            Enumerable.Range(0, clientCount)
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
