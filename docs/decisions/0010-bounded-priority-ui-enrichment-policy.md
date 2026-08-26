# ADR 0010: Bounded priority-aware UI enrichment admission

- Status: Proposed
- Date: 2026-08-26
- Supersedes: none
- Extends: ADR 0002, ADR 0003, and ADR 0009

## Context

M3.3 accepts a manual, capability-gated, bounded semantic UI snapshot. M3.4
needs to decide when that expensive and content-bearing work may be requested
without turning volatile screen changes into an unbounded queue or silently
discarding a user question.

The existing UIA worker keeps one active request and one latest pending
request. That replacement rule is correct for volatile background snapshots
but is insufficient as an orchestration contract: ADR 0003 requires explicit,
observable overflow for user questions. Provider calls are also cross-process
and cancellation during one call remains advisory. M3.3's deterministic
deadline recovery therefore does not prove that automatic, continuous UIA is
safe against a hostile or hung same-integrity provider.

## Decision

The first M3.4 slice adds a portable, content-free admission policy only. It is
not connected to `DesktopCopilotCoordinator`, `UiAutomationProbeWorker`, or any
native UIA call.

- A trigger contains only epoch ID, a positive monotonic source ID, trigger
  kind, and, for background work, the existing non-semantic change
  classification. It carries no title, text, pixel, node, prompt, or answer
  content.
- Eligible background triggers are limited to `Meaningful` and `Large`
  changes. An explicit user question is the second trigger kind and has no
  change classification.
- Admission requires global sensing to be Armed, a matching current and
  uncancelled epoch, and both `ReadUiStructure` and `ReadUiText`. The same
  checks are repeated before pending work may be dispatched.
- The runtime owner must supply a positive background debounce duration. This
  slice chooses no product debounce value. Tests inject a monotonic clock and
  an explicit test-only duration.
- Deduplication is bounded to the current epoch and trigger kind. Only an
  accepted source ID advances that kind's watermark, so a rejected question
  may be retried after explicit backpressure clears.
- The policy owns at most one active and one pending request. A newer
  background request may replace pending background work. A user question may
  replace pending background work and otherwise takes the pending slot behind
  active work. Background work cannot displace a pending question.
- A second pending user question is rejected with explicit, retryable
  backpressure. User questions are never silently replaced or reported as
  accepted when the bounded policy cannot retain them.
- Epoch changes, capability loss, cancellation, Disarm, explicit invalidation,
  and shutdown return the bounded active/pending request handles to the future
  runtime owner. Replaced pending work is also returned explicitly. The policy
  itself owns no content and performs no disposal.
- The accepted M3.3 publication gate remains the final authority for
  epoch/capability/latest revalidation and clearing content-bearing stale
  results. Runtime integration must apply that gate before completing a policy
  request or dispatching its successor.
- Runtime integration must submit only `DispatchNow` and `DispatchNext`
  decisions to the worker. A policy-queued request remains in the policy until
  promotion; it must not also occupy the worker's pending slot and accidentally
  widen the declared bound.

This decision does not select automatic trigger wiring, a production debounce
duration, a helper-process boundary, cancellation mechanics, or diagnostic
event names. Those require the next physical measurement and integration
slices.

## Consequences

- Queue capacity and overflow semantics are testable without a Windows
  desktop, UIA provider, or semantic content.
- Volatile background work remains latest-wins while user intent has stronger,
  observable delivery semantics.
- The runtime remains unchanged in this slice; no background UIA request can
  start because of this policy alone.
- The future owner must cancel superseded request handles, dispose every
  rejected content result, expose content-free counters/reasons, and preserve
  the existing M3.3 limits.
- Automatic UIA remains blocked until controlled provider-hang evidence
  supports an explicit in-process or restartable-helper decision.

## Verification

Portable deterministic tests cover trigger validation, capability and epoch
admission, Meaningful/Large filtering, debounce boundaries, per-kind
deduplication, background replacement, question priority and retryable
backpressure, pending promotion, epoch invalidation, Disarm, cancellation, and
shutdown. CI and strict build evidence must be recorded after this branch is
pushed and actually completes; this proposed ADR does not claim an unobserved
pass.
