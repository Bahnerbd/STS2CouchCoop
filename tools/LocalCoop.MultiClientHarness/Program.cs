namespace LocalCoop.MultiClientHarness;

public static class Program
{
    public static int Main(string[] args)
    {
        if (args.Length == 0 || !string.Equals(args[0], "prepare-two-client", StringComparison.OrdinalIgnoreCase))
        {
            Console.WriteLine("Usage: dotnet run --project tools\\LocalCoop.MultiClientHarness -- prepare-two-client <configRoot> [sessionId] [port] [gameExe]");
            return 1;
        }

        if (args.Length < 2)
        {
            Console.WriteLine("Missing configRoot.");
            return 1;
        }

        var rootDirectory = args[1];
        var sessionId = args.Length > 2 ? args[2] : "local-test";
        var port = args.Length > 3 && int.TryParse(args[3], out var parsedPort) ? parsedPort : 38989;
        var gameExe = args.Length > 4
            ? args[4]
            : Path.GetFullPath(Path.Combine(AppContext.BaseDirectory, "..", "..", "..", "..", "..", "SlayTheSpire2.exe"));

        var result = TwoClientHarnessPreparation.Prepare(rootDirectory, gameExe, sessionId, "127.0.0.1", port);

        Console.WriteLine("Broker:");
        Console.WriteLine(result.BrokerCommand);
        Console.WriteLine();
        Console.WriteLine("Clients:");
        foreach (var command in result.LaunchCommands)
        {
            Console.WriteLine(command);
        }

        return 0;
    }
}
