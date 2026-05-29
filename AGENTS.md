# LocalCoop Agent Guide

This repo is a clean-start local transport broker for same-machine STS2 multiplayer experiments. Treat it as a transport bridge, not as an alternate game or lobby implementation.

## Core Philosophy

- STS2 owns lobby and gameplay truth. Its native handlers should decide players, character choice, ready state, run start, and game state.
- LocalCoop should bridge communication between separate STS2 processes. Preserve sender ids, target ids, message type, payload bytes, and dispatch order.
- The broker should remain gameplay-agnostic. It should route opaque envelopes and track connection/session facts only.
- Avoid synthetic player control, panel UI, manual character replay, lobby-state reconciliation, timing delays, or echo-suppression heuristics unless a failing test and runtime logs prove a transport-level need.
- Do not assume `client-0` is semantically the host. Current harness defaults may use `client-0`, but the intended architecture is runtime host identity discovered from the process that enters STS2's host lifecycle.

## Development Rules

- Use tests before changing broker behavior. Prefer focused tests that encode message routing, dispatch threading, and config semantics.
- Keep inbound broker dispatch on the game update path. The receive loop may enqueue envelopes, but should not invoke STS2 handlers directly from a background TCP task.
- Keep changes narrow. Do not restore the held paneling UI or synthetic in-process player code.
- Same-PC loopback is the target. LAN, Steam networking, four-client polish, and combat/run sync are follow-up milestones after two-client lobby behavior is stable.
- Treat manual smoke logs as evidence. Important files are in `mods\LocalCoop`: per-client event logs and transport probe reports.

## Standard Verification

```powershell
dotnet test LocalCoopTransport.sln --no-restore -p:OutputPath=bin\Debug\net9.0-test\
dotnet build src\LocalCoop.Mod\LocalCoop.Mod.csproj
```

For broker smoke setup:

```powershell
dotnet run --project tools\LocalCoop.MultiClientHarness -- prepare-two-client .localcoop-clients local-test 38989
dotnet run --project src\LocalCoop.Broker.Cli -- local-test 38989
```

