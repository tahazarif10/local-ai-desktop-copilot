# ADR 0008: Generated interop for bounded non-text UIA snapshots

- Status: Proposed
- Date: 2026-08-23
- Supersedes: the M3.1-only manual UIA ABI portion of ADR 0007 when accepted

## Context

M3.1 deliberately used a tiny local ABI for two timeout setters and
`ElementFromHandle`. ADR 0007 requires a fresh interop decision before adding
cache requests, properties, tree walkers, or traversal. M3.2 also needs a
structural result that is useful enough for later semantic enrichment without
reading UI text, walking the desktop, blocking the WinUI thread, or creating an
unmeasured helper-process boundary.

UIA property access is cross-process unless the client asks the provider to
populate a cache. A descendant-wide `FindAllBuildCache` call would hand control
of the amount of work to the provider, so it cannot enforce the project's
per-node deadline and cancellation checks. Raw View can also be much larger
than the user-facing tree. The client therefore needs explicit ownership of
both traversal order and every budget decision.

The interop alternatives were:

1. extend the M3.1 hand-authored vtable ABI across several inherited COM
   interfaces, which would make slot/layout review too fragile;
2. import `System.Windows.Automation`, which would add the WindowsDesktop/WPF
   automation surface and hide the existing COM-apartment ownership;
3. generate an interop assembly from a machine-installed type library, which
   makes the build depend on local SDK tooling and an additional artifact;
4. use CsWin32 source generation from Microsoft Win32 metadata with an explicit
   input file and no runtime package dependency;
5. move UIA into a restartable helper process now, before M3.2 supplies evidence
   that the added packaging/protocol/trust boundary is necessary.

## Proposed decision

For the M3.2 candidate:

- Pin `Microsoft.Windows.CsWin32` `0.3.321` as a private build-time dependency.
  Generate the managed `IUIAutomation2` dependency graph from the checked-in
  `NativeMethods.txt`/`NativeMethods.json` inputs. Do not extend the manual COM
  vtable declarations.
- Keep `CUIAutomation8`, its cache request, its Control View walker, every
  element reference, and final COM release on the existing lazy application-
  owned MTA worker. Keep the 1.5-second native connection/transaction timeouts
  inside the 2.5-second request deadline.
- Admit work only after `ReadUiStructure`, then repeat HWND/PID and integrity
  checks immediately before UIA. Preserve one active plus one coalesced newest
  pending request and the current-epoch/capability/latest-request publication
  gate. A rejected result must discard its snapshot.
- Start at the current foreground HWND with
  `ElementFromHandleBuildCache`. Traverse breadth-first with
  `GetFirstChildElementBuildCache` and
  `GetNextSiblingElementBuildCache`, checking cancellation and budgets between
  calls. Never use desktop-wide roots, Raw View, or descendant-wide
  `FindAllBuildCache`.
- Traverse Control View only. Record cached `IsContentElement` so downstream
  code can select the user-relevant Content View subset without a second tree
  walk. Reaching the configured depth is reported conservatively as a depth
  boundary rather than issuing an extra provider call merely to discover
  whether a deeper child exists.
- Use one Element-scope cache request with the Control View condition and full
  element references needed for incremental traversal. Request exactly 27
  non-text properties per visited node:
  `BoundingRectangle`, `ControlType`, `HasKeyboardFocus`,
  `IsKeyboardFocusable`, `IsEnabled`, `IsControlElement`,
  `IsContentElement`, `IsPassword`, `IsOffscreen`, and the 18 availability
  Boolean properties for Dock, ExpandCollapse, GridItem, Grid, Invoke,
  MultipleView, RangeValue, Scroll, ScrollItem, SelectionItem, Selection,
  Table, TableItem, Text, Toggle, Transform, Value, and Window patterns.
  Pattern availability is metadata; no pattern object or action is requested.
- Do not request `Name`, `Value`, TextPattern content, AutomationId, ClassName,
  HelpText, item text, or any other string property. The M3.2 contract fixes
  both string count and UTF-8 string-byte budgets at zero.
- Apply the following immutable default budgets: 256 nodes; root depth 0
  through maximum depth 8; 1,200 ms traversal time; 27 cached property values
  per node and 6,912 total; zero strings and zero string bytes; and a 32 KiB
  estimated result limit. The in-memory size model reserves 128 bytes per
  snapshot plus 96 bytes per node. Any reached boundary is represented by typed
  truncation flags instead of silently widening the limit.
- Keep nodes immutable and topology-validated. Bounding rectangles may exist in
  the short-lived RAM result, but UI and diagnostics may publish only aggregate
  counts, depth, budget/truncation flags, byte estimates, and timings. They must
  not log bounds, control-type IDs, per-node states, pattern flags, or text.
- Do not add UIA events, action patterns, elevation, `uiAccess`, secure-desktop
  access, continuous orchestration, text extraction, or a helper process in
  this slice.

## Consequences

- The broader COM surface is generated from the same Microsoft metadata used by
  Windows bindings instead of being copied into local vtable structs. The
  source generator increases restore/build work but adds no runtime package.
- Breadth-first traversal preserves the shallowest useful structure when a
  budget is reached, and holding at most the node budget also bounds live COM
  element references.
- Per-node BuildCache calls cost more round trips than one descendant-wide
  query, but permit budget/cancellation checks and deterministic ownership
  between calls. Native timeout remains the recovery mechanism while a single
  provider call is blocked; cancellation is still advisory during that call.
- Content relevance is represented without a second traversal. M3.3 may read
  separately authorized text from selected nodes, but cannot reinterpret this
  ADR as permission to collect it in M3.2.
- The snapshot is process-local and short-lived. It is not persisted or sent to
  the AI server, and a stale/capability-revoked result loses its snapshot before
  publication.
- Helper-process isolation remains evidence-gated for M3.4. Physical provider
  timeout/recovery and teardown evidence are still required before this ADR can
  be accepted.

## Acceptance evidence required

- Portable boundary/topology/publication tests pass on Ubuntu and Windows,
  including proof that stale publication removes the snapshot and the node
  contract exposes no string property.
- The packaged `Debug/win-x64 --warnaserror` build proves the generated COM
  projection compiles from a clean restore.
- Physical classic Win32, packaged/WinUI, and browser targets produce bounded
  structural summaries on one non-UI MTA worker.
- Capability denial occurs before queueing; higher-integrity targets fail
  closed; rapid context changes publish no stale snapshot.
- A reached node/depth/time/result boundary returns a typed truncation summary,
  a subsequent normal request recovers on the same worker, and active-worker
  shutdown joins cleanly.
- The diagnostic bundle contains no UIA text, bounds, control-type IDs,
  per-node states, or pattern details.

## References

- [Caching UI Automation properties and control patterns](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-cachingforclients)
- [UI Automation tree overview](https://learn.microsoft.com/en-us/windows/win32/winauto/uiauto-treeoverview)
- [`IUIAutomationTreeWalker::GetFirstChildElementBuildCache`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtreewalker-getfirstchildelementbuildcache)
- [`IUIAutomationTreeWalker::GetNextSiblingElementBuildCache`](https://learn.microsoft.com/en-us/windows/win32/api/uiautomationclient/nf-uiautomationclient-iuiautomationtreewalker-getnextsiblingelementbuildcache)
- [Microsoft CsWin32](https://github.com/microsoft/CsWin32)
- [Microsoft.Windows.CsWin32 0.3.321](https://www.nuget.org/packages/Microsoft.Windows.CsWin32/0.3.321)
- [Windows UI Automation metadata](https://github.com/microsoft/win32metadata/blob/main/generation/WinSDK/RecompiledIdlHeaders/um/UIAutomationClient.idl)
