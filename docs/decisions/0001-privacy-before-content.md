# ADR 0001: Privacy before content

- Status: Accepted
- Date: 2026-08-20
- Supersedes: none

## Context

Window titles, captured pixels, accessibility properties, OCR text, audio, memory, and inference payloads can contain sensitive information. Filtering after capture is too late: the sensitive API has already been called and data may already have escaped into logs, buffers, or asynchronous work.

Policy still needs a minimum identity to decide which application is active.

## Decision

Read only HWND, PID, and normalized process identity as bootstrap metadata. Evaluate policy before any content-bearing API. Revalidate HWND/PID immediately before content access.

A denied context permits no title read, WGC target/session, UIA traversal, OCR/VLM, memory event, audio association, or local-server request. Failures and diagnostics report only a generic state plus metadata rule ID/reason.

The target policy is capability-based rather than one Boolean. The complete contract is defined in `docs/PRIVACY_MODEL.md`.

## Consequences

- Privacy is enforceable at every source boundary.
- Policy decisions become part of context identity and cancellation.
- Each new content source must declare its required capability.
- Some previously convenient “collect now, filter later” designs are forbidden.
- Current M2 binary/default-allow policy and page-load title observation are acknowledged gaps scheduled for M2.4, not silently treated as final behavior.

## Verification

- Denied target produces policy metadata before any title/capture/UIA event.
- No content event follows denial.
- Policy change cancels/advances the epoch.
- Logs and bundles contain no prohibited content.
- Capability tests prove one grant does not imply another.
