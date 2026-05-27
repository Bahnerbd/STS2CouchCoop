namespace LocalCoop.MultiClientHarness;

public static class TwoClientHarnessPreparation
{
    public static TwoClientHarnessPreparationResult Prepare(
        string rootDirectory,
        string gameExecutablePath,
        string sessionId,
        string brokerHost,
        int brokerPort)
    {
        var plan = ClientLaunchPlan.CreateTwoClient(sessionId, brokerHost, brokerPort);
        var setup = ClientConfigFileSetup.Write(rootDirectory, plan);
        var launchCommands = ClientLaunchInstructionFormatter.FormatPowerShell(setup, gameExecutablePath);

        return new TwoClientHarnessPreparationResult(
            setup,
            $"dotnet run --project src\\LocalCoop.Broker.Cli -- {sessionId} {brokerPort}",
            launchCommands);
    }
}

public sealed record TwoClientHarnessPreparationResult(
    ClientConfigFileSetupResult ConfigSetup,
    string BrokerCommand,
    IReadOnlyList<string> LaunchCommands);
