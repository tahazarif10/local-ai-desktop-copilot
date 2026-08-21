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

## ▶ M2.4 — Foundation hardening before semantic content

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

### ▶ M2.4.2 Composition and lifecycle separation

Scope:

- Move construction, subscriptions, start/stop, and teardown out of `MainPage` into an application-owned coordinator/composition boundary.
- Keep the page as a view/command surface.
- Introduce narrow interfaces around OS adapters only where tests or lifetime ownership need them.

Exit criteria:

- Page unload/navigation cannot leak hooks, sessions, timers, registrations, or event subscriptions.
- Arm/Disarm and foreground transitions behave exactly as the M2.3 baseline.
- Existing runtime acceptance and new tests pass.

### ◻ M2.4.3 Capability-based privacy policy

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

### ◻ M2.4.4 Diagnostics and input hardening

Scope:

- Replace fixed `H:` paths and legacy milestone filenames with a configurable, session-scoped location while retaining one-command bundle collection.
- Keep an explicit diagnostic whitelist and reliable flag cleanup.
- Measure low-level hook health; decide, with evidence, whether a dedicated hook thread or Raw Input is required.

Exit criteria:

- Diagnostic runs work without a particular drive letter.
- Normal launches never leave diagnostics enabled.
- Bundle contents are documented, minimized, and automatically copied as before.
- Existing MouseClick/MouseWheel/KeyboardActivity/None correlation behavior regresses cleanly.

## ◻ M3 — Read-only UI understanding

UI Automation is a semantic source, not an automation/action feature.

### M3.1 UIA capability and worker probe

- Resolve the foreground root element from its HWND only after the capability privacy gate.
- Run all UIA calls on a dedicated COM MTA worker, never the WinUI thread.
- Compare native COM interop options; select the smallest stable packaged `.NET` path based on build/runtime evidence.
- Return typed `Available`, `Unavailable`, `Timeout`, `Cancelled`, `Stale`, and `Faulted` outcomes.
- Treat elevated/secure/inaccessible targets as unavailable; do not request `uiAccess` or elevation.

Acceptance includes accessible Win32/WinUI/browser targets, an inaccessible target, rapid window switches, timeout/recovery, and clean worker teardown.

### M3.2 Bounded structural snapshot

- Traverse only the foreground HWND subtree.
- Prefer Control View; use Content View for user-relevant content; never default to the unbounded Raw View.
- Batch properties through UIA caching to reduce cross-process calls.
- Enforce explicit budgets for nodes, depth, elapsed time, string count/bytes, and result size.
- Initially collect structural metadata and pattern availability; raw text remains out of logs.

### M3.3 Semantic UI snapshot

- Add policy-authorized Name/Value/Text extraction in RAM.
- Normalize focus, dialog/window, control type, enabled/off-screen state, bounding rectangle, and read-only pattern facts.
- Never call action patterns such as Invoke, SetValue, ExpandCollapse, Selection, or Scroll.
- Attach provenance, epoch, timestamps, sensitivity, and expiry to every snapshot.

### M3.4 Orchestrated UI enrichment

- Trigger bounded snapshots after meaningful changes and on high-priority user questions.
- Add deduplication, debounce, backpressure, and stale-result disposal.
- Decide whether continuous UIA requires a restartable helper process based on measured provider-hang recovery.

## ◻ M4 — Visual text and visual fallback

### M4.1 Region-of-interest planner

- Convert changed regions and UIA bounding rectangles into bounded capture ROIs.
- Avoid full-screen OCR/VLM unless a user query explicitly requires it.

### M4.2 OCR benchmark and integration

- Benchmark local candidates on Persian, English, mixed Persian-English, terminals, dialogs, browser UI, and application UI.
- Measure latency, CPU/RAM/GPU use, accuracy, language coverage, packaging, and cancellation.
- The current Windows AI OCR API requires an NPU and is therefore not assumed suitable for the fixed client/server hardware. Recheck official support at this milestone.
- Select a backend only after target-hardware evidence; keep it behind a contract.

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
