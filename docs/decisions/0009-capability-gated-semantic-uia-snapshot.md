# ADR 0009: Capability-gated selected semantic UI snapshots

- Status: Accepted
- Date: 2026-08-23
- Accepted: 2026-08-25
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

## Verification

- Functional head `3dccdbc24fc60093f46f903dec4f7ca04c08dc14`
  passed 124 deterministic tests on Ubuntu and Windows, Windows PowerShell
  runner parsing, and the strict packaged `Debug/win-x64 --warnaserror` build.
  Acceptance-runner fix [CI #43](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32894314154)
  repeated those gates and parsed both PowerShell runners.
- Physical sessions proved ordinary diagnostic denial before queueing, explicit
  text opt-in with Notepad deny precedence, and classic, packaged, and browser
  Name/Value/visible-Text providers. Every result reported redacted aggregates
  within the immutable semantic budgets.
- Provider evidence exercised off-screen exclusion. Chrome did not expose its
  visible password control as `IsPassword=true`; that provider limitation is
  explicit, while deterministic selector/snapshot tests prove exact password
  exclusion.
- Browser session `dfb73977-bfe6-4165-95ce-e158bbe3efb7` proved default
  budgets plus tiny-budget truncation and immediate recovery on the same MTA
  worker.
- One-command session `b0af762a-6949-463f-98bf-aa1a0956ea87` at clean
  acceptance head `1f2383a2224904e94062c23e44375d42fbe7e3bd` passed
  semantic latest-wins, stale `PublicationRejected` clearing, consumer
  clearing, higher-integrity fail-closed behavior, M3.1 timeout/root
  regressions, M3.2 structural/depth/latest-wins regressions, and held-work
  teardown with exactly one worker stop and `joined=True`.
- Both the provider matrix and randomized one-command fixture passed the exact
  prohibited-content scan across session metadata, app log, OS foreground log,
  and the final whitelisted bundle.
- [PR #18](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/18)
  is the review and merge record.

## References

- [IUIAutomationElement::get_CurrentName](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationelement-get_currentname)
- [IUIAutomationValuePattern::get_CurrentValue](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationvaluepattern-get_currentvalue)
- [IUIAutomationTextPattern::GetVisibleRanges](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextpattern-getvisibleranges)
- [IUIAutomationTextRange::GetText](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtextrange-gettext)
- [About the Text and TextRange control patterns](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-about-text-and-textrange-patterns)
