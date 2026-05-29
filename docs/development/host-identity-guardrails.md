# Host Identity Guardrails Plan

## Summary

Next guardrail slice: remove the semantic assumption that `client-0` is always the host. The harness may still default to `client-0` for convenience, but runtime routing should follow the STS2 process that actually enters the host lifecycle.

## Key Changes

- Allow any client index to register as the session host once it enters `InitializeMultiplayerAsHost`.
- Route client untargeted sends to the registered host client id, not a hardcoded `client-0`.
- Keep host untargeted sends as broker broadcast and direct sends as explicit target routing.
- Keep broker routing opaque and gameplay-agnostic. Host identity is a transport/session fact, not lobby state.
- Update harness/docs so `client-0` is described as a default launch plan, not an architectural rule.

## Test Plan

- Config/protocol tests allow host role with any valid client index and prevent more than one active host per session.
- Broker session tests register host at nonzero index and route direct/default traffic to that host.
- Mod adapter tests prove client default sends use the registered host id.
- Harness tests prove generated two-client configs still default to `client-0` host while documenting that the default is not required by runtime design.

## Assumptions

- This is a follow-up implementation slice, not part of the documentation setup.
- Runtime host identity should be derived from STS2 host lifecycle entry, not from a fixed index.
- Two-client lobby correctness remains the acceptance milestone before four-client scaling or combat sync.

