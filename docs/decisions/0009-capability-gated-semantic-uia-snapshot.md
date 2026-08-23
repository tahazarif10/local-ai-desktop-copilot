# ADR 0009: Capability-gated selected semantic UI snapshots

- Status: Proposed
- Date: 2026-08-23
- Extends: ADR 0008

## Context

M3.2 proves that the foreground HWND can be traversed through a bounded,
non-text Control View snapshot on one application-owned COM MTA worker. That
structural grant does not authorize UIA Name, Value, or Text content. Semantic
content is more sensitive than structure, UIA providers can expose content
outside the visible viewport, and UIA Name/Value calls do not accept a caller
supplied maximum return length.

M3.3 therefore needs a second authorization boundary, deterministic node
selection, independent content budgets, explicit lifetime metadata, and
content-free diagnostics before any live semantic read is acceptable.

## Decision

For M3.3:

- Require both `ReadUiStructure` and `ReadUiText` before queueing a semantic
  request and again at publication. Product defaults and ordinary diagnostic
  launches continue to deny `ReadUiText`. The Windows runner exposes a separate
  explicit `-EnableUiText` launch opt-in in its expiring descriptor.
- Preserve ADR 0008's foreground-HWND, Control View, breadth-first traversal,
  structural cache schema, integrity check, one-active/one-latest queue, MTA
  ownership, native timeouts, and request deadline. Do not add a second tree
  walk, Raw View, desktop roots, or descendant-wide queries.
- Select semantic candidates only from the already bounded structural result.
  A candidate must be a Content element, on-screen, and not a password element.
  Prefer the keyboard-focused candidate, then window/dialog candidates, then
  the existing breadth-first order. Keep at most 32 candidates.
- Keep the accepted 27-property structural cache text-free. For each selected
  candidate only, read:
  - the read-only `Name` property;
  - `CurrentValue` and `CurrentIsReadOnly` from ValuePattern when advertised;
  - visible TextPattern ranges only, using `GetVisibleRanges` and bounded
    `GetText(maxLength)` calls.
- Never use TextPattern `DocumentRange`, because providers may include
  off-screen or virtualized document content. Never call `SetValue`, `Invoke`,
  `Select`, `ScrollIntoView`, or any other action method.
- Apply immutable default semantic budgets before provider content calls:
  32 selected nodes, 64 retained strings, 1,024 UTF-16 code units per retained
  string, 32 visible text ranges, 16 KiB retained UTF-8 content, 800 ms semantic
  read time, a 24 KiB estimated semantic result, and a five-second TTL. Reaching
  a boundary produces typed truncation metadata and never widens a limit.
- Normalize only the selected node's existing structural facts plus window/
  dialog and ValuePattern read-only facts. Preserve structural index/parent,
  control type, focus, enabled/off-screen state, bounds, source kind, epoch,
  capture timestamp, sensitivity, provenance, and expiry.
- Keep semantic text in owned, clearable in-memory buffers. Dispose and clear
  every buffer on expiry, epoch/capability/identity/latest-request rejection,
  cancellation, shutdown, or after the current diagnostic consumer has emitted
  aggregate evidence. Do not persist, render, log, or transmit the content.
- Diagnostics may record only outcome/reason, counts by source, retained UTF-8
  bytes, selected/skipped counts, truncation flags, estimated size, timings,
  expiry, and disposal state. Provider messages, strings, bounds, control IDs,
  per-node states, and pattern details remain prohibited.

`GetText(maxLength)` bounds each TextPattern return. The COM APIs for Name and
Value return BSTRs without a caller length parameter, so a provider can create
a larger transient return before the client truncates it into the retained
budget. M3.3 hard-bounds the retained snapshot and minimizes such calls; M3.4
must use physical provider measurements when deciding whether continuous UIA
requires restartable process isolation. This limitation must not be described
as a hard bound on a hostile provider's transient COM allocation.

## Consequences

- Structure and text remain independently revocable, and a diagnostic session
  cannot silently become a content-reading session.
- Shallow visible content is preferred while password and off-screen nodes are
  excluded before any content property or pattern request.
- Visible TextPattern ranges avoid deliberate full-document extraction, but
  provider calls remain cross-process and cancellation is advisory during one
  call.
- Semantic snapshots can feed later M3.4 orchestration without changing their
  privacy, provenance, lifetime, or backpressure contract.
- A same-integrity hostile provider remains capable of oversized transient
  Name/Value BSTRs. This is recorded evidence for, not a premature decision
  about, helper-process isolation.

## Verification required before acceptance

- Portable tests for capability separation, candidate selection, every budget,
  UTF-8-safe truncation, TTL, clear-on-dispose, and stale/revoked publication.
- Strict Windows CI build and tests using the generated UIA interfaces.
- Physical classic, packaged, and browser provider evidence for Name, Value,
  and visible Text sources with all immutable budgets reported.
- Separate evidence that ordinary diagnostics deny before queueing and the
  explicit text opt-in still respects the exact Notepad deny fixture.
- Password/off-screen exclusion, latest-wins stale disposal, forced deadline
  recovery, higher-integrity fail-closed behavior, and active-worker teardown.
- A prohibited-content scan proving that no captured semantic string entered
  app logs, OS probe logs, session metadata, or the diagnostic bundle.

## References

- [IUIAutomationElement::get_CurrentName](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-get_currentname)
- [IUIAutomationValuePattern::get_CurrentValue](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationvaluepattern-get_currentvalue)
- [IUIAutomationTextPattern::GetVisibleRanges](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-getvisibleranges)
- [IUIAutomationTextRange::GetText](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-gettext)
- [About the Text and TextRange control patterns](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-about-text-and-textrange-patterns)
