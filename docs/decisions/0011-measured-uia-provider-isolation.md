# ADR 0011: Measured UI Automation provider isolation

- Status: Proposed
- Date: 2026-08-28
- Supersedes: none
- Extends: ADR 0007, ADR 0009, and ADR 0010

## Context

The accepted UI Automation worker performs read-only cross-process COM calls on
one application-owned MTA thread. Its cancellation token and request deadline
are checked between calls, while `IUIAutomation2.TransactionTimeout` is set to
1,500 milliseconds. None of those mechanisms has yet been shown to recover
from a provider that actually enters a content property getter and never
returns.

M3.4 may eventually submit semantic snapshots automatically. A permanently
blocked in-process worker would then prevent later epochs from being inspected
and could fail its five-second shutdown join. Deterministic expired-request
tests and diagnostic sleeps do not exercise that failure mode, so they cannot
select the process boundary.

## Proposed measurement

Slice 2 adds diagnostic instrumentation and a one-command Windows PowerShell
5.1 runner; it does not connect the accepted orchestration policy or enable
automatic UIA.

- A same-integrity HWND-backed Windows Forms fixture exposes an explicit
  server-side `IRawElementProviderSimple` through `WM_GETOBJECT` and
  `AutomationInteropProvider.ReturnRawElementProvider`. Its UI Automation
  `Name` property signals that the real cross-process provider call was
  entered, then waits on a separate release event until cleanup.
- The raw provider is independently smoke-tested in Windows CI with a second
  process. CI must prove that UI Automation enters the `Name` call, remains
  blocked while release is closed, and returns the randomized sentinel only
  after release. A fixture that cannot prove that sequence is not eligible for
  physical architecture evidence.
- The diagnostic UI queues that semantic request through the accepted M3.3
  worker after a two-second diagnostic hold, giving the runner a deterministic
  point at which to arm the provider. The controlled provider receives focus
  before capture so it is the first eligible semantic candidate.
- While the provider remains blocked, the runner changes to a separate healthy
  same-integrity target. That cancels the old epoch and queues the accepted
  M3.1 root probe on the same worker.
- The runner observes recovery for 12 seconds. This exceeds both the
  1,500-millisecond native transaction timeout and the semantic request's
  10-second deadline.
- The app is then closed before the provider is released. The runner records
  whether the MTA worker joined, verifies the bounded app shutdown, releases
  every fixture in `finally`, and scans the explicit-whitelist evidence for a
  randomized content sentinel.

Windows CI must parse the diagnostic runners and physical wrapper, validate the
wrapper's audited raw-provider substitutions, compile the controlled provider,
pass the independent cross-process provider smoke gate, run the portable and
Windows regression suites, and complete the strict WinUI build before the
physical command is used.

## Decision rule

This ADR remains Proposed until the physical evidence is attached.

- Select the existing in-process boundary only if the real provider call
  returns early enough for the healthy target to complete before the
  10-second request deadline, and shutdown reports `joined=True`.
- Select a restartable helper process if recovery occurs only after that
  deadline, or if the healthy request remains blocked through the 12-second
  observation and shutdown reports `joined=False`.
- Treat any missing provider-entry, same-integrity, healthy-target, shutdown,
  or sentinel evidence as inconclusive rather than choosing a boundary.

## Consequences

The measurement can justify a process split without first implementing one.
It also preserves the current product defaults: sensing remains explicitly
armed, semantic content remains separately capability-gated and RAM-only, and
no continuous UIA request is introduced.

If a helper is required, its restart protocol, request/result schema, epoch and
capability revalidation, hard termination bound, content ownership, and
diagnostic counters belong to the next implementation slice. If in-process
recovery is accepted, the measured bound becomes a runtime invariant and must
be covered by the final one-command M3.4 acceptance matrix.

## Verification

Pending the exact clean branch/head, CI run, physical session ID, timing
measurements, recovery state, worker-join result, and prohibited-content scan.
