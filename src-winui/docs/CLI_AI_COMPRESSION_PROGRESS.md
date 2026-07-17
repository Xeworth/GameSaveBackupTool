# CLI AI compression progress

This note documents the `gsbt compress --ai` progress contract and a local
benchmark captured on 2026-07-17. It is guidance for agents and maintainers,
not a promise that every machine will pause at the same percentage.

## Event contract

- Progress events are newline-delimited JSON written to stderr.
- The final command result is one JSON document written to stdout.
- A changed percentage is emitted once. Repeated native callbacks for the same
  percentage are suppressed.
- Progress changes of at least 5 percentage points are emitted immediately.
  Smaller changes are emitted after the minimum interval.
- If the compressor stops issuing callbacks, an independent observer checks
  liveness once per second. An unchanged percentage emits one heartbeat every
  15 seconds.
- Heartbeats have `heartbeat: true` and include `elapsedSeconds` and
  `plateauSeconds`. They do not claim that the percentage advanced.
- A heartbeat at 99% uses the message `Still finalizing the archive`.
- Progress never moves backwards when a late native callback arrives.

The main tuning values are `DefaultMinimumInterval` and
`DefaultHeartbeatInterval` in
`src/GSBT.Cli/Output/CliProgressEventThrottle.cs`. The one-second independent
observer is in `CompressCommand.ObserveCompressionHeartbeatAsync`.

## Bundled agent notebook

GSBT embeds a structured agent notebook inside `gsbt.exe`. It is not emitted by
normal human help or shown in the GUI. `gsbt help --ai` exposes it as the
top-level `agentNotebook` object so an agent can ground answers in product facts
before starting work.

Compression heartbeat events also provide:

- `agentStatus: "working"` as an explicit liveness interpretation.
- `knowledgeRef` as a stable ID that points into a notebook behavior.
- `agentHint` on the first heartbeat of a plateau. Later heartbeats retain the
  reference without repeating the full hint.

Current compression knowledge IDs are:

- `compression.chunky-large-batch-plateau`
- `compression.archive-finalization`
- `compression.progress-plateau`

The notebook records the 75% plateau as a measured example, not a universal
threshold. Agents must continue to use live heartbeat fields because hardware,
storage, file mix, and compression settings can move the pause elsewhere.

The notebook is machine-facing rather than secret. Embedding keeps it out of
ordinary UX and avoids a loose internal text file, but a person intentionally
running `gsbt help --ai` or inspecting the executable can read it. Third-party
agents require that access for the feature to work.

## Agent behavior

1. Read progress events from stderr while the process is running.
2. Treat a new percentage as normal forward progress.
3. Treat `heartbeat: true` as confirmation that the process is alive during a
   compression plateau. Use `knowledgeRef` and the first `agentHint` to explain
   the known behavior without improvising.
4. At 99%, describe a heartbeat as archive finalization, not as a hang.
5. Do not report success until the final stdout JSON has `success: true` and an
   archive path.
6. Report a failure only from a failed final result, process exit, cancellation,
   or explicit error event. A plateau by itself is not an error.

## Local benchmark

Input was the current 12-game backup set on the development machine, using
automatic thread selection. Archive size and timing depend on save contents,
storage, CPU, memory, and 7-Zip behavior.

| Mode | Level | Time | Archive | Events | Heartbeats | Longest observed plateau |
| --- | ---: | ---: | ---: | ---: | ---: | --- |
| chunky | 1 | 15.1 s | 799.3 MiB | 53 | 0 | 100% for 4.7 s |
| chunky | 3 | 15.3 s | 776.8 MiB | 39 | 0 | 100% for 4.6 s |
| chunky | 5 | 55.2 s | 409.4 MiB | 37 | 1 | 99% for 16.6 s |
| chunky | 7 | 91.4 s | 325.2 MiB | 31 | 4 | 75% for 43.8 s |
| chunky | 9 | 93.2 s | 323.7 MiB | 31 | 4 | 75% for 43.9 s |
| smooth | 1 | 19.1 s | 799.5 MiB | 67 | 0 | 100% for 0.7 s |
| smooth | 3 | 125.4 s | 781.4 MiB | 103 | 0 | 100% for 4.8 s |
| smooth | 5 | 399.4 s | 729.8 MiB | 103 | 0 | 25% for 5.0 s |
| smooth | 7 | 586.2 s | 715.2 MiB | 103 | 0 | 25% for 7.6 s |
| smooth | 9 | 581.0 s | 715.2 MiB | 103 | 0 | 25% for 7.6 s |

The matrix captured one redundant initial compression 0% event per run. The
final patch seeds the compression throttle from the start event and removes
that duplicate. A post-patch chunky mx1 smoke completed in 14.3 seconds with
51 events and `NormalDuplicateEvents: 0`.

On this dataset, chunky mx7 and mx9 paused around 75% and later around 99%.
Chunky mx5 paused mainly at 99%. Smooth mode advanced nearly percentage by
percentage and never required a 15-second heartbeat. Other machines and backup
sets may plateau at different values, so agents must use event fields rather
than hardcoded percentages.

The summarized measurements above are the public benchmark record. Raw stdout,
stderr NDJSON, and harness summaries remain local because they contain
machine-specific paths and timestamps.

The ten benchmark archives total 6.226 GiB and remain in the configured backup
folder. The final smoke created one additional archive.
