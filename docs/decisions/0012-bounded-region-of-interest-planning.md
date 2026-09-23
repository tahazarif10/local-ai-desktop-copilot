# ADR 0012: Bounded region-of-interest planning before OCR

- Status: Proposed
- Date: 2026-09-23
- Supersedes: none
- Extends: ADR 0003, ADR 0008, ADR 0009, ADR 0010, and ADR 0011

## Context

M4.1 is the first visual-content planning step after accepted M3.4 orchestration.
The existing client already has two useful geometry sources:

- `ChangeDetector.ChangedRegion`, expressed in the downscaled persistent-change
  map (`OutputWidth` / `OutputHeight`);
- UI Automation `BoundingRectangle` values from the accepted bounded M3.2/M3.3
  snapshot, expressed in screen coordinates.

OCR and later VLM work must not default to a whole window or whole desktop. The
privacy model already requires `CapturePixels + RunOcr` before OCR and says OCR
should receive only an allowed bounded ROI when possible. The planner therefore
needs a deterministic coordinate contract before any OCR backend is selected.

The two geometry sources are not in the same coordinate space. The change map
is frame-local and downscaled; UIA rectangles are screen-relative. M4.1 must not
silently assume DPI, window-border, or capture-origin equivalence. It also must
not create a new content-retention or logging boundary.

## Decision

Introduce a portable `LocalCopilot.Core` ROI planner before any OCR integration.

The planner:

- receives source-frame pixel dimensions explicitly;
- maps a low-resolution `ChangeRegion` back to source pixels with outward
  rounding so changed pixels are not lost, then clamps to the capture frame;
- maps UIA rectangles only when the caller supplies an explicit screen-space
  projection for that capture frame; no implicit `HWND`/DPI/window-origin
  conversion exists in Core;
- rejects non-finite, zero-area, and out-of-capture UIA rectangles;
- applies bounded source-pixel padding after coordinate conversion;
- for background-change planning, accepts only UIA candidates that intersect
  the changed-region association envelope; if none survive, the mapped changed
  region is the fallback;
- for user-question planning, may use caller-ordered UIA candidates even when no
  change region exists, with the changed region only as fallback;
- keeps source provenance (`ChangedRegion` or `UiAutomation`) on every planned
  ROI;
- deduplicates nested UIA candidates conservatively;
- enforces both per-region and total-area budgets and a hard region-count cap;
- rejects an oversized candidate instead of silently cropping it to an
  arbitrary smaller rectangle;
- never synthesizes a full-frame fallback.

Initial M4.1 defaults are privacy/resource bounds, not performance claims:

- 16 source pixels of ROI padding;
- 24 source pixels of background association margin;
- at most 4 planned regions;
- at most 25% of the source frame per region;
- at most 40% total planned area.

The options type also enforces hard ceilings independent of configured defaults:

- at most 8 regions;
- at most 50% of the frame per region;
- at most 75% total planned area;
- bounded padding and association margins.

M4.2 may tune the normal operating values from target-hardware OCR benchmarks,
but widening a hard ceiling requires explicit architecture/privacy review.

M4.1 is geometry planning only. It does not:

- capture/crop pixels;
- invoke OCR or VLM;
- add an OCR backend;
- inspect question text;
- retain UIA text or pixels;
- log ROI coordinates or UIA bounds;
- change privacy capabilities or product defaults;
- permit full-frame OCR/VLM.

A future explicit user-requested full-frame escalation, if needed, is a separate
policy path and is not produced by this planner.

## Consequences

- Change and UIA geometry can be tested deterministically without WinUI, WGC,
  UIA COM, DPI APIs, OCR engines, or a physical desktop.
- Core cannot accidentally reinterpret screen-space UIA bounds as frame-local
  pixels; a Windows adapter must supply an explicit capture projection later.
- Background planning remains anchored to an observed changed region and cannot
  widen to unrelated UIA controls.
- An empty plan is a valid safe outcome when geometry is missing or budgets
  reject every candidate; later stages must degrade or wait rather than widen
  silently.
- Because the planner stores coordinates in RAM, diagnostics must expose only
  aggregate counts/budget outcomes. Existing rules forbidding bound/coordinate
  logging remain unchanged.
- The planner deliberately does not decide which OCR engine to run or whether
  OCR text is sufficient. Those are M4.2 and later concerns.

## Verification

Before acceptance, require:

- deterministic Core tests for outward scaling, clipping, projection,
  association, fallback, invalid/outside input, padding, deduplication, count
  limits, per-region limits, total-area limits, and no-full-frame behavior;
- all existing portable and Windows regression tests;
- strict Windows app build;
- documentation alignment across `PROJECT_STATE`, `ARCHITECTURE`, `ROADMAP`,
  and the ADR index.

Because this slice adds only portable geometry/policy logic and no new Windows
interop, capture path, content acquisition, or runtime wiring, no separate
physical Windows evidence is required unless implementation scope expands.
