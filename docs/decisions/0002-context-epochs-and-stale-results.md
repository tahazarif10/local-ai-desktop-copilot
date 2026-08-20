# ADR 0002: Context epochs and stale-result publication gates

- Status: Accepted
- Date: 2026-08-20
- Supersedes: none

## Context

Desktop context changes faster than capture, UIA, OCR, or model work may complete. Cancellation alone is advisory: an API can finish after cancellation or may not support interruption. Publishing an old result under a new foreground window is both incorrect and a privacy risk.

## Decision

Represent the active context as an immutable monotonically identified epoch containing foreground identity, privacy decision, start time, and cancellation token.

Reuse an epoch only when the relevant HWND/PID/process/privacy identity is unchanged. Cancel the prior epoch when context or policy changes. Every asynchronous result carries the originating epoch and must pass a current-epoch plus required-capability check before display, event publication, retention, or transmission.

## Consequences

- Old work can complete harmlessly if it is disposed and not published.
- All async contracts need epoch/request identity.
- Same-target duplicate foreground callbacks do not cause needless session restarts.
- Policy representation changes must participate in epoch equality.
- Tests must cover cancellation and late completion, not only successful operations.

## Verification

- Same allowed HWND/PID reuses the epoch.
- Target/policy change cancels the previous token and advances ID.
- Late capture/UIA/OCR/inference results are dropped and disposed.
- Session/UI status cannot regress to an older epoch.
