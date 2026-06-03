# LocalCoop Log Tags Design

Date: 2026-06-03

## Purpose

Make LocalCoop runtime evidence easier to navigate without replacing the current human-readable logs. The immediate change is to add stable grep-able tags to log lines and make startup-script cleanup cover the LocalCoop-owned evidence for a fresh two-client run.

## Context

LocalCoop is a same-machine transport bridge around STS2 native multiplayer. The logs are evidence for STS2-owned lobby, routing, run transition, and gameplay behavior, so the logging layer should make existing native message flow easier to inspect without inventing new lobby or gameplay truth.

Current evidence lives primarily in `mods/LocalCoop`:

- `localcoop-host-0-events.txt`
- `localcoop-client-1-events.txt`
- `localcoop-transport-probe-client-*.txt`
- `localcoop-events.txt`

The two-client startup script already removes the owned mod event and probe logs before launching clients. This design keeps that cleanup narrow, moves it early enough that a fresh run starts with fresh evidence, and includes the script-owned broker launcher log only when the script is starting a fresh broker.

## Design

Each LocalCoop event log line should keep the current timestamp-first text format, then include one or more stable tags before the existing message text:

```text
2026-06-03T01:09:17.0464501-05:00 [lc.route] Broker peer ready for broadcasting: sessionId=local-peerfix-1 peerId=5495323171043147777.
```

Tags should be short, lowercase, and stable enough to use in saved grep commands:

- `[lc.startup]` for broker-mode enablement, disabled state, probe output, and launch cleanup messages.
- `[lc.patch]` for Harmony patch install or failure messages.
- `[lc.join]` for join-screen, lobby-handshake, and client join flow messages.
- `[lc.route]` for broker connection, peer tracking, outbound/inbound envelope movement, and target/source routing.
- `[lc.dispatch]` for handler registration, queued/flushed/dispatched messages, and handler failures.
- `[lc.run]` for begin-run, run identity, and run transition diagnostics.
- `[lc.combat]` for combat sync diagnostics.
- `[lc.input]` for controller/input ownership diagnostics and high-volume peer input messages.
- `[lc.error]` may be added alongside the domain tag for failures and exceptions.

Segment boundary tags should be sparse. Use them only where they help navigate long runs, such as startup, join, run transition, and combat diagnostics:

```text
2026-06-03T01:09:16.8917500-05:00 [lc.segment.begin] [lc.join] Broker client join flow begin: clientId=client-1.
2026-06-03T01:09:17.0663907-05:00 [lc.segment.end] [lc.join] Broker client join flow completed: clientId=client-1.
```

Navigation tools can stay simple at first: grep for tags, or scan line-by-line for segment begin/end markers and print that segment plus optionally the following segment. They should not need to parse the whole current prose format.

## JSONL Note

Do not convert the main logs to JSONL in this pass. A separate JSONL sidecar may be valuable later for high-value structured streams where machine analysis matters more than quick human inspection, especially envelope routing, dispatch, begin-run transitions, and error events. If added later, JSONL should be a separate file such as `localcoop-host-0-events.jsonl`, not a replacement for `localcoop-host-0-events.txt`.

## Error Handling

Log tagging should be best-effort and must not throw during game startup or dispatch. If a message cannot be categorized, it should still be written with a fallback tag such as `[lc.general]`.

Startup cleanup should continue to delete only LocalCoop-owned event, probe, broker launcher, and future sidecar log files. It should preserve marker files, DLLs, manifests, and user notes. When `-ReuseExistingBroker` is supplied and a broker is already running, the script should not delete that live broker's current log.

## Testing

Focused tests should cover:

- `BrokerEventLog` writes timestamped lines containing supplied tags and message text.
- Uncategorized messages receive `[lc.general]`.
- Existing concurrent writes remain safe.
- Startup cleanup clears LocalCoop-owned `.txt` logs, the script-owned broker launcher log for fresh brokers, and any future `.jsonl` sidecars while preserving non-log files.
- Any segment helper, if added in this pass, can extract a tagged segment without parsing STS2 message text.
