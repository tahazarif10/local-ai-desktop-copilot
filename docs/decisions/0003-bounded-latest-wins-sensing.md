# ADR 0003: Bounded latest-wins sensing

- Status: Accepted
- Date: 2026-08-20
- Supersedes: none

## Context

Windows Graphics Capture can produce frames faster than CPU readback and change processing consume them. An unbounded queue increases memory, latency, and privacy exposure while processing obsolete screen state.

Different data types have different loss semantics: visual snapshots are replaceable, while user questions and discrete semantic events are not.

## Decision

Use capacity-one latest-wins ownership for volatile screen frames and foreground candidates. Dispose the replaced item immediately and expose replacement/drop counters.

Every later queue must declare capacity and overflow semantics. Discrete semantic events use a bounded, deduplicated, priority-aware policy with observable overflow. User questions receive explicit backpressure/rejection and are never silently dropped.

Heavy capture processing remains outside `FrameArrived`.

## Consequences

- Memory and latency remain bounded under load.
- The system intentionally skips obsolete intermediate visual states.
- Ownership/disposal and drop metrics are mandatory.
- One queue abstraction/policy cannot be copied blindly to every stream.

## Verification

- Runtime shows frames arrived can exceed processed samples and replacements occur without growth.
- Replaced frames are disposed.
- Queue capacity and overflow are tested for each new stream.
- Shutdown/cancellation disposes the remaining item and leaves no worker/session active.
