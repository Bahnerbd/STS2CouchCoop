using LocalCoop.Broker;
using System.Net;

var sessionId = args.Length > 0 ? args[0] : "local-default";
var port = args.Length > 1 && int.TryParse(args[1], out var parsedPort) ? parsedPort : 38989;

await using var server = new BrokerTcpServer(sessionId, IPAddress.Loopback, port);
await server.StartAsync(CancellationToken.None);

Console.WriteLine($"LocalCoop broker session '{sessionId}' listening on 127.0.0.1:{server.Port}.");
Console.WriteLine("Press Ctrl+C to stop.");

var stop = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
Console.CancelKeyPress += (_, eventArgs) =>
{
    eventArgs.Cancel = true;
    stop.TrySetResult();
};

await stop.Task;
