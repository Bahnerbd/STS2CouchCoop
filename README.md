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
- `LocalCoop.MultiClientHarness` creates deterministic four-client broker config text for future process launching.
- `LocalCoop.Mod` can read `enable-local-broker.txt` from the mod directory and derive the per-process client id and log path.

## Run

```powershell
dotnet test LocalCoopTransport.sln
dotnet run --project src\LocalCoop.Broker.Cli -- local-test 38989
```

## Next Slice

The next implementation should spike replacing or patching STS2 multiplayer transport so native multiplayer messages are forwarded through the broker.
