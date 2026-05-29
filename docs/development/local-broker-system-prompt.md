# Local Broker System Prompt

Use this prompt/checklist when starting a future LocalCoop broker session.

## Project Stance

You are working on `LocalCoopMod`, a same-machine transport bridge for Slay the Spire 2 native multiplayer. The goal is to let separate STS2 client processes communicate through a local broker while leaning on the game's own multiplayer logic.

The broker and mod should not become a second lobby implementation. Preserve and route STS2 messages; do not invent lobby truth.

## Rules Of The Road

- STS2 owns players, character selection, ready state, lobby begin-run state, and gameplay state.
- The broker transports opaque envelopes and preserves sender id, target id, message type, payload, and order.
- Do not add synthetic-player control, panel UI, manual character replay, lobby-state coalescing, wall-clock timing suppressions, or state reconciliation as a first response.
- If behavior is broken, gather logs first: `localcoop-*-events.txt`, `localcoop-transport-probe-client-*.txt`, and `godot.log`.
- Fix root causes at transport seams. Prefer constructor/factory/lifecycle seams over lower-level Steam send/receive interception.
- Keep dispatch game-thread safe: receive loop enqueues; update path dispatches.
- Do not treat `client-0` as the architecture's host. Runtime host identity should come from the process that enters STS2's host lifecycle.

## Current Workflow

```powershell
dotnet test LocalCoopTransport.sln --no-restore -p:OutputPath=bin\Debug\net9.0-test\
dotnet build src\LocalCoop.Mod\LocalCoop.Mod.csproj
dotnet run --project tools\LocalCoop.MultiClientHarness -- prepare-two-client .localcoop-clients local-test 38989
dotnet run --project src\LocalCoop.Broker.Cli -- local-test 38989
```

Manual smoke should start fresh broker/session state, launch the host path first, then launch the join path. Acceptance evidence is native lobby message flow in logs and consistent UI behavior, not broker-side fabricated state.

## Debugging Checklist

- Confirm which process is host/client from `enable-local-broker.txt` and runtime logs.
- Confirm the STS2 lifecycle method reached: `InitializeMultiplayerAsHost` or `InitializeMultiplayerAsClient`.
- Confirm broker registration, route targets, and sender ids before changing message handling.
- If a crash happens, read the exact exception in `godot.log` before patching.
- If a fix requires manual lobby state, stop and look for a thinner transport seam first.

