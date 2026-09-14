# ADR 0011: Measured UI Automation provider isolation

- Status: Accepted
- Date: 2026-08-28
- Accepted: 2026-09-15
- Supersedes: none
- Extends: ADR 0007, ADR 0009, and ADR 0010

## Context

The accepted UI Automation worker performs read-only cross-process COM calls on
one application-owned MTA thread. Its cancellation token and request deadline
are checked between calls, while `IUIAutomation2.TransactionTimeout` is set to
1,500 milliseconds. Before M3.4 Slice 2, those mechanisms had not been shown to
recover from a provider that actually enters a content property getter and
blocks.

M3.4 may submit semantic snapshots automatically. A permanently blocked
in-process worker would prevent later epochs from being inspected and could fail
its five-second shutdown join. Deterministic expired-request tests and diagnostic
sleeps do not exercise that failure mode, so a controlled physical provider-hang
measurement was required before selecting the process boundary.

## Measurement

Slice 2 adds diagnostic instrumentation and a one-command Windows PowerShell
5.1 runner; it does not connect the accepted orchestration policy or enable
automatic UIA.

- A same-integrity HWND-backed Windows Forms fixture exposes an explicit
  server-side `IRawElementProviderSimple` through `WM_GETOBJECT` and
  `AutomationInteropProvider.ReturnRawElementProvider`. Its UI Automation
  `Name` property signals that the real cross-process provider call was entered,
  then waits on a separate release event until cleanup.
- The raw provider is independently smoke-tested in Windows CI with a second
  process. CI proves that UI Automation enters the `Name` call, remains blocked
  while release is closed, and returns the randomized sentinel only after
  release.
- The diagnostic UI queues that semantic request through the accepted M3.3
  worker after a two-second diagnostic hold, giving the runner a deterministic
  point at which to arm the provider. The controlled provider receives focus
  before capture so it is the first eligible semantic candidate.
- While the provider remains blocked, the runner changes to a separate healthy
  same-integrity target. That cancels the old epoch and queues the accepted M3.1
  root probe on the same worker.
- The runner observes recovery for 12 seconds. This exceeds both the
  1,500-millisecond native transaction timeout and the semantic request's
  10-second deadline.
- The app is then closed before the provider is released. The runner records
  whether the MTA worker joined, verifies bounded app shutdown, releases every
  fixture in `finally`, and scans the explicit-whitelist evidence for a
  randomized content sentinel.

Windows CI parses the diagnostic runners and physical wrapper, validates the
wrapper's audited raw-provider substitutions, compiles the controlled provider,
passes the independent cross-process provider smoke gate, runs the portable and
Windows regression suites, and completes the strict WinUI build before the
physical command is used.

## Decision rule

- Select the existing in-process boundary only if the real provider call returns
  early enough for the healthy target to complete before the 10-second request
  deadline, and shutdown reports `joined=True`.
- Select a restartable helper process if recovery occurs only after that
  deadline, or if the healthy request remains blocked through the 12-second
  observation and shutdown reports `joined=False`.
- Treat any missing provider-entry, same-integrity, healthy-target, shutdown, or
  sentinel evidence as inconclusive rather than choosing a boundary.

## Decision

Keep the UI Automation worker **in-process** for M3.4 runtime integration.

The decisive physical run was executed on the target non-elevated Windows
client at behavior-bearing head
`44d4752864372116a911de2ae3acf611ef033c1e`, session
`2a7d17af-4d9b-4ddc-80ac-c727ddd60dc7`, with a clean working tree.

Measured evidence:

- provider integrity RID: `8192`;
- client integrity RID: `8192`;
- same integrity: `True`;
- real provider call entered: `True`;
- provider-entry elapsed time: `2008 ms`;
- healthy provider recovered before release: `True`;
- recovery observation elapsed time: `3701 ms`;
- request deadline: `10000 ms`;
- shutdown elapsed time: `118 ms`;
- UIA worker joined: `True`;
- recovery state: `RecoveredWithinDeadline`;
- architecture classification: `InProcessCandidate`;
- randomized prohibited-content sentinel scan: `PASS`.

The blocked semantic request was cancelled as stale after the foreground/epoch
change, and the queued healthy M3.1 root probe completed on the same worker as
`Available / RootResolved` before the request deadline. The application then
shut down cleanly while the controlled provider was still blocked, and the
worker joined successfully.

CI #87 also passed on the exact physical behavior head: portable and Windows
Core tests, Windows PowerShell runner parsing, wrapper validation, controlled
raw-provider compilation, cross-process enter/block/release/sentinel smoke test,
WinUI restore, and strict `Debug/win-x64 --warnaserror` build.

Earlier physical attempts that failed before proving the intended provider call
are explicitly inconclusive harness failures and are not architecture evidence.

## Consequences

The accepted M3.4 runtime path may continue using the existing application-owned
MTA worker; a helper-process split is not required by the measured failure mode.
The measured recovery bound is now a runtime invariant: automatic orchestration
must preserve epoch cancellation, capability/identity revalidation, the current
10-second request deadline, and clean worker join on shutdown.

This decision does not broaden sensing. Product defaults remain unchanged:
sensing is explicitly armed, semantic content remains separately capability-
gated and RAM-only, no OCR/action/elevation/persistence/implicit egress is
introduced, and Slice 2 itself does not enable automatic UIA.

M3.4 Slice 3 may now bind the accepted portable admission policy to the existing
M3.3 snapshot path. Its final one-command acceptance matrix must include the
provider-isolation regression and must reject any change that violates the
measured in-process recovery invariant.

## Verification

Accepted from the exact physical evidence above and CI #87. The recorded run
contains the clean branch/head, non-elevated session, same-integrity provider
entry, healthy-target recovery timing, bounded shutdown, `joined=True`, and the
prohibited-content sentinel PASS required by the decision rule.
