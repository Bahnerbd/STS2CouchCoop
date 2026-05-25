# LocalCoop Transport Broker

This orphan branch is a clean-start prototype for same-machine multi-client local co-op.

The previous in-process synthetic-player and paneling UI work is preserved on:

```text
hold/paneling-ui-wip-20260525
```

## Current Slice

- `LocalCoop.Protocol` defines broker client config parsing, transport envelopes, and length-prefixed frame encoding.
- `LocalCoop.Protocol` also includes a reusable TCP client connection helper that registers with the broker and reads/writes envelopes.
- `LocalCoop.Broker` tracks one host plus up to three clients, acknowledges registration, and routes direct or broadcast envelopes without understanding gameplay.
- `LocalCoop.Broker.Cli` starts a loopback TCP broker.
- `LocalCoop.MultiClientHarness` creates deterministic four-client broker config text, writes per-client config folders, and can run a non-game four-client broker smoke.
- `LocalCoop.Mod` can read `enable-local-broker.txt` from the mod directory, derive the per-process client id and log path, initialize through the STS2 mod loader, run a focused transport seam probe, and passively log lobby/net-service diagnostics.
- `LocalCoop.Mod` includes a broker-backed net-service adapter spike for typed lobby message registration, send, and dispatch. It is not yet substituted into STS2's native `INetGameService`.

## Run

```powershell
dotnet test LocalCoopTransport.sln
dotnet run --project src\LocalCoop.Broker.Cli -- local-test 38989
```

## Next Slice

The next implementation should use the new diagnostics to choose the safest STS2 seam for substituting or patching the native multiplayer transport. Start with two clients and lobby-only sync before attempting four clients or combat.

## Manual Two-Client Smoke

1. Remove stale `enable-local-injection.txt` from `mods\LocalCoop` if present; this clean branch does not use it.
2. Start the broker:

```powershell
dotnet run --project src\LocalCoop.Broker.Cli -- local-test 38989
```

3. Write an `enable-local-broker.txt` for the first game process:

```text
role=host
clientIndex=0
endpoint=127.0.0.1:38989
sessionId=local-test
```

4. Launch STS2 and navigate to multiplayer character select as host.
5. Inspect `mods\LocalCoop\localcoop-host-0-events.txt` and `mods\LocalCoop\localcoop-transport-probe-client-0.txt`.
6. Repeat with `role=client` and `clientIndex=1` once process isolation/config copying is added for multiple simultaneous game instances.

Expected evidence for this slice is diagnostic only: startup, probe report, and passive lobby/net-service method logs. Native lobby synchronization is the next spike.
