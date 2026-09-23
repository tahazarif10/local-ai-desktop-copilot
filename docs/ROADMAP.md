# Roadmap

This roadmap is dependency-ordered. It is not a promise that every later implementation detail is already selected. A milestone moves to complete only after merge plus Windows runtime acceptance where applicable.

## Status legend

- ✅ Complete and merged with acceptance evidence
- ▶ Next approved implementation gate
- ◻ Planned; scope may be refined by earlier measurements
- ⛔ Explicitly out of scope

## Completed foundation

### M1 — Windows context and capture

- ✅ **M1.3 Event-driven foreground observer** — foreground WinEvent hook, own-process exclusion, transient Explorer filtering, clean teardown.
- ✅ **M1.4 RAM-only foreground-window capture** — HWND to Windows Graphics Capture, one real frame, bounded downscale and CPU bitmap metadata, no screenshot file.
- ✅ **M1.5 Privacy hardening** — identity-before-title order, deterministic diagnostic deny fixture, epoch reuse correction. This corrective milestone merged after M2.1 but logically belongs to the M1 safety foundation.

### M2 — Cheap persistent sensing

- ✅ **M2.0 Privacy gate and context epochs** — cancellation/stale-result safety envelope.
- ✅ **M2.1 Change detector** — low-resolution luminance diff, tile ratios, changed bounding region, four non-semantic classifications.
- ✅ **M2.2 Persistent capture** — persistent WGC session, capacity-one latest-wins ownership, 640 px / 500 ms, resize recreation, clean context cancellation.
- ✅ **M2.3 Sensing orchestrator** — explicit Arm/Disarm, settling, context reuse, blocked/unavailable/error states, session handover.
- ✅ **M2.3.1 Diagnostic correlation** — bounded input-activity kinds and possible-trigger timing for Meaningful/Large changes.

## ✅ M2.4 — Foundation hardening before semantic content

M2.4 is a deliberate architecture gate added after the M2.3 audit. It prevents UIA/OCR/content logic from being coupled to the diagnostic page or a binary privacy flag.

### ✅ M2.4.1 Characterization tests and CI foundation

Scope:

- Add test projects without changing runtime behavior.
- Characterize `PrivacyPolicy`, `ContextEpochManager`, `ChangeDetector`, `DiagnosticTimeline`, and `ChangeCorrelationService`.
- Introduce seams for time/identity only where deterministic tests require them.
- Add a Windows CI build/test workflow after verifying the exact commands on the target client and the GitHub runner.

Exit criteria:

- Existing accepted behavior is covered by deterministic tests, including epoch reuse/cancellation and timeline staleness.
- Tests do not require screen capture, global hooks, or a live desktop.
- Windows `win-x64` build remains zero-warning/zero-error.
- CI is green, or its absence/blocker is explicitly recorded rather than silently bypassed.
- No product behavior or diagnostic event meaning changes.

### ✅ M2.4.2 Composition and lifecycle separation

Scope:

- Move construction, subscriptions, start/stop, and teardown out of `MainPage` into an application-owned coordinator/composition boundary.
- Keep the page as a view/command surface.
- Introduce narrow interfaces around OS adapters only where tests or lifetime ownership need them.

Exit criteria:

- Page unload/navigation cannot leak hooks, sessions, timers, registrations, or event subscriptions.
- Arm/Disarm and foreground transitions behave exactly as the M2.3 baseline.
- Existing runtime acceptance and new tests pass.

Accepted evidence: [PR #12](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/12), 49 deterministic tests, Ubuntu/Windows CI, canonical `Debug/win-x64` build, and a physical Windows regression covering privacy denial/recovery, same-context reuse, unavailable-target recovery, Disarm/Re-arm, and close while Armed with clean hook/session/input/subscription disposal.

### ✅ M2.4.3 Capability-based privacy policy

Scope:

- Replace the single `AllowsSensing` meaning with explicit permissions for metadata, title, pixels, UIA structure, UIA text, OCR, derived-event retention, microphone, and local-server transmission.
- Add a product policy configuration boundary and per-application deny behavior.
- Keep explicit global activation default OFF.
- Preserve the process-identity bootstrap before every content-bearing API.

Exit criteria:

- Denied contexts fail closed before title/UIA/capture.
- Permissions can differ by data operation without implicit widening.
- Policy changes cancel/advance the active epoch.
- Tests cover deny precedence, capability separation, rule changes, and stale results.
- No raw content appears in diagnostics.

Accepted evidence: [PR #13](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/13), 58 deterministic tests, Ubuntu/Windows CI, canonical `Debug/win-x64 --warnaserror` build, and a physical Windows regression proving true Off, exact Notepad deny before content, automatic recovery, pre-WGC HWND/PID revalidation, Disarm/Re-arm, correct stale rejection, and clean armed shutdown.

### ✅ M2.4.4 Diagnostics and input hardening

Scope:

- Replace fixed `H:` paths and legacy milestone filenames with a configurable, session-scoped location while retaining one-command bundle collection.
- Replace the persistent enable flag with a validated, expiring launch token, exact session handshake, and explicit diagnostic whitelist.
- Measure low-level hook health; decide, with evidence, whether a dedicated hook thread or Raw Input is required.

Exit criteria:

- Diagnostic runs work without a particular drive letter.
- Normal launches never leave diagnostics enabled.
- Bundle contents are documented, minimized, and automatically copied as before.
- Existing MouseClick/MouseWheel/KeyboardActivity/None correlation behavior regresses cleanly.

Accepted evidence: [PR #14](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/14), 68 deterministic tests, Ubuntu/Windows CI, Windows PowerShell runner parsing, strict `Debug/win-x64 --warnaserror` build, successful default/custom-root sessions, an unchanged 14-file snapshot after normal launch, all four correlation outcomes, 1,965 callbacks with zero errors/mismatches and sub-millisecond maximum latency, and clean Disarm/Re-arm/armed-shutdown teardown.

Decision: keep the measured synchronous diagnostic-only hooks. Reconsider a dedicated hook thread or Raw Input only after a reproducible target-hardware regression.

## ✅ M3 — Read-only UI understanding

UI Automation is a semantic source, not an automation/action feature.

### ✅ M3.1 UIA capability and worker probe

- Resolve the foreground root element from its HWND only after the capability privacy gate.
- Run all UIA calls on a dedicated COM MTA worker, never the WinUI thread.
- Compare native COM interop options; select the smallest stable packaged `.NET` path based on build/runtime evidence.
- Return typed `Available`, `Unavailable`, `Timeout`, `Cancelled`, `Stale`, and `Faulted` outcomes.
- Treat elevated/secure/inaccessible targets as unavailable; do not request `uiAccess` or elevation.

Acceptance includes accessible Win32/WinUI/browser targets, an inaccessible target, rapid window switches, timeout/recovery, and clean worker teardown.

Accepted implementation on `dev/m3-1-uia-worker-probe`:

- Product defaults keep `ReadUiStructure` denied; an expiring diagnostic launch grants structure without granting UIA text.
- A lazy application-owned thread initializes COM as MTA and owns `CUIAutomation8`, every returned root pointer, and final release.
- `IUIAutomation2` connection/transaction timeouts are 1.5 seconds; the normal end-to-end request deadline is 2.5 seconds. A diagnostic command forces an already-expired request, then requires a normal retry, to prove typed deadline/recovery deterministically without pretending to simulate every hostile provider.
- The queue permits one executing request plus one coalesced newest pending request. Replaced work completes as `Cancelled/Superseded`.
- HWND/PID is revalidated immediately before UIA; targets above the client integrity level fail closed as `Unavailable/HigherIntegrity`.
- Only typed outcome, reason, timing, HRESULT, worker-thread ID, and identity-check metadata leave the worker. No property, text, tree, pattern, or action call is in scope.
- Portable tests cover the result classifier, publication gate, capability separation, and bounded pending slot.

Accepted evidence: [PR #15](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/15), 91 deterministic tests on Ubuntu/Windows, Windows PowerShell parsing, strict `Debug/win-x64 --warnaserror` build, and two physical Windows sessions proving classic/packaged/browser roots, privacy denial before queueing, higher-integrity fail-closed behavior from a non-elevated runner, same-worker deadline recovery, deterministic latest-wins/stale publication, and joined teardown during active work. Both bundles passed the prohibited-content scan.

### ✅ M3.2 Bounded structural snapshot

- Runtime accepted on `dev/m3-2-bounded-structural-snapshot` at functional head `e1a50741580379f0f65c80e212f04c449e5a8c9b`; [PR #16](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/16) squash-merged the accepted tree as `be0a437ddbe09fc2a9830a9b10da56f83a8051d9`.
- Traverse only the foreground HWND subtree breadth-first through Control View. Cache `IsContentElement` for the user-relevant Content subset; never use Raw View or an unbounded descendant query.
- Batch exactly 27 non-text Boolean/numeric properties through an Element-scope UIA cache request. Pattern availability is metadata only; never obtain or invoke a pattern object.
- Enforce immutable defaults of 256 nodes, depth 8, 1,200 ms traversal, 27 values per node / 6,912 total, zero strings/bytes, and 32 KiB estimated result. Report every reached boundary with truncation flags.
- Use pinned private build-time CsWin32 source generation from Microsoft Win32 metadata instead of expanding the M3.1 manual ABI; accepted ADR 0008 records the decision.
- Preserve one-active/one-latest backpressure, current epoch/capability/latest publication, snapshot removal on stale publication, same-worker COM release, typed unavailable/timeout/cancelled/faulted outcomes, and deterministic teardown.
- Publish/log only aggregate counts, truncation, estimated bytes, and timing. Name, Value, Text content, bounds, control IDs, per-node states, and pattern details remain absent from diagnostics.

Accepted evidence: [CI run #28](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32648315641) passed 108 tests on Ubuntu/Windows, PowerShell runner parsing, and the strict Windows build. Full physical session `22e567be-059d-4d19-bb1f-55b60a7a8646` passed classic/packaged/browser providers, immutable budgets, privacy/integrity denial, stale disposal, M3.1 regression, recovery, joined teardown, and the prohibited-content scan. Short session `fd287eb1-c062-423f-881a-4f4c3ca1b0a7` confirmed corrected M3.2 runner metadata. Detailed measurements are in `PROJECT_STATE.md`.

### ✅ M3.3 Semantic UI snapshot

- Accepted at functional head `3dccdbc24fc60093f46f903dec4f7ca04c08dc14`; [PR #18](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/18) is the review/merge record and accepted [ADR 0009](decisions/0009-capability-gated-semantic-uia-snapshot.md) fixes the contract.
- Require separate `ReadUiStructure` and `ReadUiText` authorization before queue and publication; structure acceptance alone never authorizes text.
- Read only Name, advertised ValuePattern value/read-only state, and advertised TextPattern visible ranges for selected content/on-screen/non-password nodes from the already bounded structure.
- Enforce 32-node, 64-string, 1,024-character, 32-visible-range, 16-KiB UTF-8, 800-ms, 24-KiB result, and five-second TTL budgets with clear-on-dispose RAM ownership.
- Preserve focus/window priority plus control type, dialog, enabled/off-screen, bounds, password/content eligibility, provenance, sensitivity, expiry, and removal on stale/latest/capability/expiry rejection.
- Keep Invoke, SetValue, ExpandCollapse, Selection, Scroll, Raw View, DocumentRange, OCR, persistence, elevation, and implicit egress out of scope.

Accepted evidence: 124/124 tests on Ubuntu/Windows, both runner parses, and strict win-x64 build passed through [CI #43](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32894314154). Physical provider sessions proved ordinary denial, explicit opt-in/Notepad precedence, classic/packaged/browser sources, budgets, tiny truncation/recovery, and redaction. Clean one-command session `b0af762a-6949-463f-98bf-aa1a0956ea87` at acceptance head `1f2383a2224904e94062c23e44375d42fbe7e3bd` passed semantic stale clearing, higher-integrity denial, M3.1/M3.2 regressions, held-work joined teardown, and the randomized prohibited-content scan.

### ✅ M3.4 Orchestrated UI enrichment

- **Slice 1 — portable admission policy (accepted):** [PR #19](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/19) and [ADR 0010](decisions/0010-bounded-priority-ui-enrichment-policy.md) define content-free Meaningful/Large-change and user-question triggers, explicit debounce input, per-epoch/per-kind deduplication, one-active/one-pending bounds, question priority with observable retryable backpressure, and invalidation handles.
- **Slice 2 — measured provider isolation (accepted):** [ADR 0011](decisions/0011-measured-uia-provider-isolation.md) selected the existing in-process COM MTA worker from a real same-integrity blocking-provider measurement. Behavior head `44d4752864372116a911de2ae3acf611ef033c1e`, session `2a7d17af-4d9b-4ddc-80ac-c727ddd60dc7`, recovered the healthy target before the 10-second deadline, shut down in 118 ms with `joined=True`, classified `InProcessCandidate`, and passed the randomized sentinel scan.
- **Slice 3 — runtime integration and acceptance (accepted):** product runtime behavior was introduced at `979ed5a2318d32ff151d2950f7ace77ec274d601`. It composes an application-owned enrichment runtime with persistent Meaningful/Large samples and the existing bounded M3.3 semantic snapshot, uses a five-second background debounce, gives explicit user-question work priority/backpressure, revalidates Armed/current epoch/cancellation/`ReadUiStructure | ReadUiText` before dispatch and publication, clears results immediately after aggregate observation, and stops before coordinator/UIA teardown.
- The clean physical candidate `26c3290bf196701473b558da657d5c39c8c97e8a` passed the full one-command Windows acceptance on 2026-09-23: denial-before-dispatch, automatic dispatch/debounce, user-question routing, Disarm no-post-dispatch, redaction, teardown ordering, joined worker, and the provider-isolation regression. [CI #115](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/35844099601) passed the portable/Windows suites, PowerShell parsing, M3.4 wrapper validation, raw-provider smoke gate, and strict WinUI build.

Accepted invariants:

- Product defaults still deny `ReadUiText`; automatic semantic UIA cannot run merely because M3.4 exists.
- Sensing must be explicitly Armed, the current epoch must remain uncancelled, and both UIA capabilities must pass at admission and publication.
- User questions are never silently dropped or replaced; overload remains explicit and retryable.
- Queue capacity, replacement ownership, cancellation, stale disposal, teardown, and diagnostics remain bounded and observable.
- M3.3 content selection, immutable budgets, RAM-only lifetime, no-action boundary, and prohibited-content rules are unchanged.
- ADR 0011's measured in-process recovery invariant remains a regression gate; a helper-process split is not required unless new physical evidence invalidates that decision.

## ▶ M4 — Visual text and visual fallback

### ✅ M4.1 Region-of-interest planner

Accepted [ADR 0012](decisions/0012-bounded-region-of-interest-planning.md) defines the portable contract. [PR #29](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/29) merged behavior head `03f719a1bb4a98a3834348d1f0d53fa50828c92e` to `main` as `5ad17ee5276dccefbe5ac4d3e6f7ab845061fdaa`.

- Convert downscaled changed regions to source-frame pixels with outward rounding and capture-bound clipping.
- Convert UIA screen rectangles only through an explicit caller-supplied capture screen projection; do not assume DPI, border, or origin equivalence in Core.
- For background planning, admit only UIA rectangles associated with the observed change envelope; fall back to the changed region when no bounded UIA candidate survives.
- For user-question planning, allow caller-ordered UIA candidates without requiring a concurrent change region; the planner still never manufactures a full-frame fallback.
- Preserve ROI provenance and enforce bounded padding, deduplication, count, per-region area, and total-area budgets. Oversized candidates are rejected rather than arbitrarily cropped.
- Keep the slice geometry-only: no WGC crop, OCR/VLM backend, question-text inspection, persistence, coordinate logging, capability change, or server transfer.

Accepted initial bounds are 16 px padding, 24 px association margin, 4 regions, 25% maximum area per region, and 40% total planned area. Hard ceilings prevent a configured full-frame plan. These are privacy/resource bounds, not OCR performance claims.

Acceptance candidate `03f719a1bb4a98a3834348d1f0d53fa50828c92e` passed [CI #126](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/35847291181): 160/160 Core tests on Ubuntu and Windows, prior M3.4 runner/provider regressions, and the strict Windows build.

Exit criteria:

- deterministic Core tests cover scaling, clipping, explicit screen projection, background association, change fallback, user-question UIA planning, invalid/outside rectangles, padding, deduplication, count/per-region/total budgets, and no-full-frame behavior;
- all prior portable/Windows regression tests and the strict WinUI build remain green;
- diagnostics/privacy rules remain unchanged: no bounds or coordinates are logged;
- no physical Windows acceptance is required while the slice remains pure Core geometry with no new interop or content acquisition;
- M4.1 is complete on `main`; M4.2 OCR backend benchmarking/selection is now the next gate.

### ▶ M4.2 OCR benchmark and integration

Accepted M4.1 ROI/privacy bounds remain the input boundary for all OCR work. Proposed [ADR 0013](decisions/0013-evidence-gated-ocr-benchmark.md) splits this milestone into evidence-first slices.

#### ✅ M4.2.1 Benchmark contract and target-machine preflight

- Add portable strict OCR scoring: Unicode NFC normalization, code-point CER, WER, and exact normalized match. Do not silently canonicalize Persian/Arabic variants in the primary score.
- Add a one-command non-elevated Windows preflight that records only hardware, runtime/version, installed OCR-language, and accelerator metadata. It must not capture pixels, execute OCR, install packages/models, or select a backend.
- Initial candidate groups: legacy `Windows.Media.Ocr`, Tesseract 5 with `fas+eng`, and PaddleOCR PP-OCRv5 Persian/English.
- Microsoft's current Windows AI Text Recognition API is NPU-only and is excluded on the fixed machines rather than scored as a failing OCR engine.
- CI must parse and execute the preflight `-ValidateOnly` path before any physical use.
- Accepted evidence: behavior/preflight head `d8f12b00524934138115f22cc8bd7149e20f4452`; CI #137 PASS; physical client and server preflights both completed content-free. Neither fixed machine currently exposes Persian through legacy Windows OCR; Tesseract/PaddleOCR are absent; both machines' existing Python 3.14 installations are outside the current Paddle Windows Python support range.
- M4.2.1 is merged through PR #31 at `920bbecbb4c7ef4c22ca3ff055df1bcaa801e911`. M4.2.2 controlled benchmarking is active on `dev/m4-2-2-controlled-benchmark`.

#### ✅ M4.2.2 Controlled OCR benchmark

Environment preparation is accepted and merged through PR #32 at `1486fb673a623b248e9238c747ac1cd49fa73979`. The controlled local-only corpus/runner is active on `dev/m4-2-2-benchmark-runner`. The setup uses project-local Python 3.12 via Python Install Manager `--target`, pins PaddlePaddle GPU 3.2.0 on the CUDA 12.6 wheel index and PaddleOCR 3.7.0, and leaves the existing Python 3.14 installation untouched. See [M4.2 controlled OCR benchmark](M4_2_OCR_BENCHMARK.md).

- Benchmark viable local candidates on Persian, English, mixed Persian-English, terminal/console, dialog, browser UI, and desktop application UI.
- Keep real screenshots, ground truth, and OCR output local; PR/diagnostic evidence contains only sample IDs/categories and aggregate metrics.
- Measure strict CER, strict WER, exact-match rate, cold initialization, warm per-ROI p50/p95 latency, failure count, memory, GPU VRAM where measurable, package/model footprint, and cancellation/timeout behavior.
- Compare both fixed-machine roles where applicable. No LAN transport is implied by benchmarking a server-local model.
- Select a backend only after target-hardware evidence; keep it behind a narrow replaceable contract.
- Accepted benchmark evidence: Paddle server-detector multilingual configuration CER/WER 0.23004695/0.22388060 with warm p50 91.392 ms and 562 MiB VRAM delta; Tesseract fast/best remained materially less accurate and slower; Windows Media OCR remained English-only. PR #33 is the review/merge record for the controlled benchmark slice.

#### ▶ M4.2.3 Backend integration and acceptance

ADR 0014 selects the measured server-local PaddleOCR configuration as the integration target while keeping Core model-agnostic.

##### ✅ M4.2.3a Portable OCR integration gate

- Revalidate Armed state, same current uncancelled epoch, and `CapturePixels + RunOcr`.
- For the selected local-AI-server topology, additionally require `SendPixelsToLocalServer`.
- Feed only accepted M4.1 bounded ROI plans; an empty plan is a safe rejection and never widens to a full frame.
- Recheck capabilities/current epoch/latest request before publication.
- Keep this slice transport-free and content-free so the gate is deterministic in Core.
- Accepted through PR #35 at functional head `62a64f60a09924c8007ccc2f4dd1560211ab55ea`; CI #216 passed portable/Windows Core tests and the strict Windows build. No separate physical run was required because this slice adds no interop, content acquisition, or transport.

##### ▶ M4.2.3b Authenticated bounded OCR transport/runtime

- Define the narrow authenticated/encrypted client-to-server OCR transport before any product ROI pixel crosses the LAN.
- Bind request ID, epoch, deadline, cancellation and byte/count limits on both sides.
- Pre-provision the selected Paddle models; no silent model/network download during product operation.
- Use bounded request ownership/queues, deterministic teardown, short-lived sensitive OCR text, and content-free diagnostics.
- Treat server unavailable as a typed degraded outcome; no cloud or unmeasured-backend fallback.
- Do not begin M4.3 VLM fallback until OCR integration passes the fixed client/server physical acceptance matrix.

### M4.3 VLM fallback

- Use a small quantized VLM only when UIA/OCR cannot answer the visual question.
- Benchmark VRAM, time-to-first-token, total latency, screenshot/UI understanding, and interference with foreground workloads.
- Do not keep a VLM resident if the resource budget cannot support it safely.

## ◻ M5 — Structured events, context, and short-term memory

### M5.1 Event normalization

- Convert foreground, UIA, OCR, visual, and input-correlation facts into a versioned structured event schema.
- Preserve provenance and uncertainty; do not convert correlation into asserted causality.

### M5.2 Five-minute short-term memory

- In-memory, bounded, TTL-based event store.
- No raw screenshots in memory.
- Derived text only when privacy allows; delete on policy change or explicit user clear.

### M5.3 Context selection

- Select only relevant recent events and current facts for a question.
- Enforce prompt/token/byte budgets and sensitivity policy before inference.

Long-term semantic memory remains out of MVP.

## ◻ M6 — Local AI server and resource management

### M6.1 Authenticated local protocol

- Versioned contracts, request IDs, deadlines, cancellation, payload limits, health/capability negotiation, and authenticated encryption on the LAN.
- Bind/allow only the intended private network path.
- No internet fallback.

### M6.2 Model runtime adapters

- Text/VLM runtime selection is benchmark-driven and replaceable.
- Do not encode a model name in core domain contracts.

### M6.3 Resource manager

- States include Normal, LowResource, UserQuery, and HeavyGpuApp.
- User questions outrank background inference.
- Enforce VRAM/RAM/concurrency budgets and unload/degrade safely.

## ◻ M7 — Screen question and local answer

- Explicit user question obtains a fresh policy-authorized context snapshot.
- Reason over selected structured context, with VLM only when needed.
- Stream a local text answer with latency instrumentation.
- End-to-end success: a visible VS Code/terminal/application error can be explained without a manual screenshot and without internet access.

## ◻ M8 — Local voice interaction

- Lightweight VAD and explicit conversational activation strategy.
- Benchmark local STT for Persian, English, and mixed technical speech.
- Local TTS prioritizing first-audio latency.
- Microphone mute, visible listening state, and independent audio privacy capability.
- Ambient speech must not trigger arbitrary responses.

## ◻ M9 — Productization

- Tray/background experience, pause/mute controls, privacy-rule UI, memory clear, health and resource status.
- Packaging, signing, update strategy, crash recovery, accessibility, and supportable diagnostics.
- Branch protection, required CI, release checklist, threat model, and privacy review.

## ⛔ Outside the MVP

- Mouse or keyboard control
- UI Automation actions
- Autonomous agents
- Cloud backend or paid API fallback
- User accounts or remote synchronization
- Mobile app
- Complex long-term semantic memory
- Broad per-application plugin ecosystem

These require a separate architecture/security decision after the watch-understand-remember-listen-answer experience is proven.
