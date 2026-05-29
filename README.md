# LocalCoop Transport Broker

This orphan branch is a clean-start prototype for same-machine multi-client local co-op.

The previous in-process synthetic-player and paneling UI work is preserved on:

```text
hold/paneling-ui-wip-20260525
```

## Current Slice

- `LocalCoop.Protocol` defines broker client config parsing, transport envelopes, and length-prefixed frame encoding.
- `LocalCoop.Protocol` also includes a reusable TCP client connection helper that registers with the broker and reads/writes envelopes.
- `LocalCoop.Broker` tracks registered clients, acknowledges registration, and routes direct or broadcast envelopes without understanding gameplay.
- `LocalCoop.Broker.Cli` starts a loopback TCP broker.
- `LocalCoop.MultiClientHarness` creates deterministic four-client broker config text, writes per-client config folders, and can run a non-game four-client broker smoke.
- `LocalCoop.Mod` reads `enable-local-broker.txt` from `LOCALCOOP_CONFIG_DIR` or the mod directory, derives the per-process client id and log path, initializes through the STS2 mod loader, runs a focused transport seam probe, and logs lobby/net-service diagnostics.
- `LocalCoop.Mod` includes a broker-backed net-service adapter for typed lobby message registration, send, and main-thread dispatch. It is substituted into the STS2 character-select host/client lifecycle in broker mode.

## Architecture Principles

See [AGENTS.md](AGENTS.md) for the repo's agent-facing development rules.

- STS2 owns lobby and gameplay truth.
- LocalCoop should bridge communication, not manually reconcile lobby state.
- The broker transports opaque envelopes and preserves sender/target identity.
- `client-0` may be a harness default, but it should not be treated as the semantic host in future runtime design.

## Run

```powershell
dotnet test LocalCoopTransport.sln --no-restore -p:OutputPath=bin\Debug\net9.0-test\
dotnet build src\LocalCoop.Mod\LocalCoop.Mod.csproj
dotnet run --project tools\LocalCoop.MultiClientHarness -- prepare-two-client .localcoop-clients local-test 38989
dotnet run --project src\LocalCoop.Broker.Cli -- local-test 38989
```

## Next Slice

The next implementation should decouple host identity from `client-0` and use runtime host registration from the STS2 host lifecycle. Start with two-client lobby correctness before attempting four clients or combat.

## Manual Two-Client Smoke

1. Remove stale `enable-local-injection.txt` from `mods\LocalCoop` if present; this clean branch does not use it.
2. Generate per-client configs:

```powershell
dotnet run --project tools\LocalCoop.MultiClientHarness -- prepare-two-client .localcoop-clients local-test 38989
```

3. Start the broker:

```powershell
dotnet run --project src\LocalCoop.Broker.Cli -- local-test 38989
```

4. Launch STS2 from Steam for the host path using the generated `client-0` config directory.
5. Launch STS2 from Steam for the client path using the generated `client-1` config directory.
6. Inspect these files under `mods\LocalCoop`:
   - `localcoop-host-0-events.txt`
   - `localcoop-client-1-events.txt`
   - `localcoop-transport-probe-client-0.txt`
   - `localcoop-transport-probe-client-1.txt`

Expected evidence is native lobby message flow over the broker: broker mode enabled, lifecycle entry logs, thin transport lobby message logs, and no `Broker replay outbound`, pending character flush, or lobby-state coalescing.
