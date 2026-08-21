---
state_schema: 1
reference_code_commit: c29099a1c96680229f82f7b6b400cf962e51b5cc
last_verified_date: 2026-08-21
completed_through: M2.3.1
next_milestone: M2.4.1
next_milestone_name: Characterization Tests and CI Foundation
---

# Project state

This document separates verified implementation from target architecture. Update it in every milestone PR that changes status, evidence, constraints, or the next gate.

## Executive state

The implementation at `c29099a1c96680229f82f7b6b400cf962e51b5cc` is the accepted M2.3/M2.3.1 functional baseline. The Windows `win-x64` build passed with zero warnings and zero errors, and the orchestrated sensing/correlation path passed targeted runtime acceptance on the physical client. Documentation-only `main` commits may follow this SHA without changing the accepted runtime behavior; always resolve live HEAD from GitHub/Git.

There is no known blocking defect in the accepted M2 path. No further M2.3 rework is required unless a reproducible regression appears.

The repository is not yet a complete copilot. It is a diagnostic WinUI shell around the sensing foundation. The next implementation work is M2.4 foundation hardening before any UI Automation content collection.

The active M2.4.1 candidate in [PR #10](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/10) extracts deterministic logic into `LocalCopilot.Core`, defines 43 characterization tests, and adds Linux/Windows CI. Candidate validation is complete: the full suite passed locally and on both CI runners, the canonical Windows build passed, and the unchanged M2 runtime path passed a physical-client regression scenario. This remains pre-merge evidence rather than a completed milestone: `c29099a` is still the accepted functional baseline until PR #10 is merged.

## Accepted milestone evidence

| Milestone | Status | Merged evidence | Runtime evidence |
| --- | --- | --- | --- |
| M1.3 Event-driven foreground observer | Complete | [PR #1](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/1) | WinEvent foreground changes, transient Explorer filtering, clean hook teardown |
| M1.4 RAM-only window capture | Complete | [PR #2](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/2) | Real WGC frame; 5120×2784 to 2560×1392; about 13.6 MB CPU bitmap and 56 ms in the recorded acceptance run; no screenshot file |
| M2.0 Privacy gate and context epochs | Complete | [PR #3](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/3) | Blocked contexts stop before capture; previous epoch cancellation; stale capture drop; no raw title in application logs |
| M2.1 Low-resolution change detector | Complete | [PR #4](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/4) | 640 px selected over 960 px after local runtime comparison |
| M1.5 Privacy/epoch hardening | Complete | [PR #5](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/5) | Identity is checked before title; same HWND/PID reuses epoch; diagnostic Notepad deny fixture; allowed recovery |
| M2.2 Persistent latest-wins sensing | Complete | [PR #6](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/6) | 640 px / 500 ms; frames replaced under pressure; resize recreation; context cancellation; clean recovery and teardown |
| M2.3 Sensing orchestrator | Complete | [PR #7](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/7) | Explicit Arm/Disarm, settle/reuse/block/unavailable paths, context-scoped session lifecycle |
| M2.3.1 Diagnostic correlation | Complete | [PR #7](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/7) | MouseWheel, MouseClick, KeyboardActivity, and expired `None`; final run processed 33 samples with `staleDropped=0`; clean `hadError=False` shutdown and hook removal |

PR #7 was squash-merged as `c29099a`. Its feature-branch head (`abcbf08`) is not the `main` baseline.

### M2.4.1 candidate validation evidence

The behavior-bearing candidate was validated at PR #10 code head `ab504eea5c4baabf2b770c92d0866ff89d1caac9` on 2026-08-21. Later documentation-only descendants do not replace that runtime evidence.

- Local Windows test run: 43 passed, 0 failed, 0 skipped.
- Local canonical app build: `Debug/win-x64`, 0 warnings, 0 errors.
- [CI run #2](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32472133991): 43/43 core tests passed on Ubuntu and Windows; the Windows `Debug/win-x64` app build passed.
- Physical-client regression: explicit Arm, allowed sensing, metadata-only input correlation, diagnostic privacy blocking before content access, automatic allowed recovery, same-context reuse, Disarm/Re-arm, allowed context transition, and page unload all behaved as the accepted M2 baseline.
- Every exercised persistent session ended with `hadError=False` and `staleDropped=0`. Privacy transitions ended the prior session before publication to the blocked epoch; frame replacement remained active under the latest-wins capacity-one policy.
- Teardown evidence: input tracking stopped, persistent sensing was already stopped, the foreground hook returned `success=True` with `lastError=0`, the observer reported stopped, and the final epoch was cancelled/reset.
- Diagnostic privacy scan found only the approved metadata classes; no raw title, key, text, coordinate, clipboard, pixel payload, audio, prompt, or response content was recorded. Session identifiers, process/window metadata, and machine-specific performance measurements are intentionally retained outside the public repository.

Verdict: **candidate PASS; merge pending**. This evidence does not mark M2.4.1 complete before merge and does not authorize M2.4.2 or M3 work by itself.

## Current implementation map

### Current stack

| Area | Current repository value | Verification boundary |
| --- | --- | --- |
| Language/runtime | C# on .NET 10 | `net10.0-windows10.0.26100.0` |
| Desktop UI | Packaged WinUI 3 | `Microsoft.WindowsAppSDK` 2.4.0 |
| Portable logic | `LocalCopilot.Core` class library | `net10.0`; no WinUI/Windows API dependency |
| Characterization tests | MSTest 4.3.3 | 43 deterministic tests; 43/43 passed locally and on both CI runners at the PR candidate |
| Continuous integration | GitHub Actions | [PR #10 run #2](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32472133991) passed core tests on Ubuntu/Windows and the `Debug/win-x64` app build on Windows |
| Capture/image interop | Windows Graphics Capture + Win2D | `Microsoft.Graphics.Win2D` 1.4.0 |
| Packaging/trust | MSIX tooling, full-trust desktop app | Development package identity used by `dotnet run` |
| Canonical validated target | `Debug`, `win-x64` | Physical Windows client acceptance |
| Declared OS minimum | Windows build 17763 | Declaration only; it is not broad runtime-support evidence |
| Reserved AI manifest capability | `systemAIModels` | Declared but unused by current code |

The solution's `Any CPU` mapping currently resolves to x86, while the accepted workflow builds the project explicitly with `-r win-x64`. Use the canonical command in `ENGINEERING_WORKFLOW.md`; do not infer platform support from solution configurations.

Qwen3-VL-2B appeared as an early candidate in the original product prompt. It is not an accepted model selection. OCR, VLM, text LLM, STT, and TTS choices remain benchmark-gated on the fixed two-computer hardware.

| File/component | Implemented responsibility | Important current boundary |
| --- | --- | --- |
| `ForegroundWindowObserver` | Out-of-context foreground WinEvent hook and latest pending HWND dispatch | External foreground changes only; own process skipped |
| `ForegroundWindowService` | HWND/PID/process identity, privacy evaluation, then allowed title read | Process identity is the privacy bootstrap data |
| `PrivacyPolicy` | Binary allow/block evaluation | Product policy configuration is not implemented; normal default is allow |
| `ContextEpochManager` | Immutable context identity, cancellation, reuse, stale-work boundary | Epoch changes on HWND/PID/privacy decision changes |
| `GraphicsCaptureItemFactory` | HWND to WGC capture target COM interop | Unavailable target is a typed failure |
| `SingleFrameCaptureService` | Explicit one-frame WGC diagnostic capture | RAM-only result metadata; no image persistence |
| `ChangeDetector` | Luminance frame comparison and region/classification | Pure CPU logic; Baseline/Insignificant/Meaningful/Large only, not semantic importance |
| `ChangeDetectionProbeService` | Manual single-sample detector profiles | Diagnostic path retained separately from persistent path |
| `PersistentChangeDetectionService` | Persistent WGC, capacity-one latest frame, resize/recreate, downscale/readback/luma/diff | 640 px / 500 ms is the accepted current profile |
| `SensingOrchestrator` | Explicit arm, context settle, previous-session cleanup, start/stop/status | Defaults OFF; no semantic stages yet |
| `InputActivityTracker` | Global low-level activity-kind hooks | Records kind only; no key, text, coordinate, clipboard, or target data |
| `DiagnosticTimeline` | Bounded epoch-scoped activity retention | Capacity 256, five-second retention |
| `ChangeCorrelationService` | Two-second possible-trigger lookup for meaningful/large visual change | Diagnostic correlation, not causality or semantic event detection |
| `DiagnosticLog` / `run-debug.ps1` | Opt-in metadata logging and whitelisted bundle | Developer-specific `H:\DevCache\LocalCopilot` and legacy `m1-3` names |
| `MainPage` | Diagnostic UI, object construction, service lifetime, handlers, orchestration integration | Too many product responsibilities; scheduled for M2.4 separation |

## Verified current invariants

- Auto sensing is OFF until the user explicitly arms it.
- The foreground observer and allowed title read currently begin on page load even while auto sensing is OFF; this is a documented diagnostic-shell gap, not the target privacy behavior.
- Privacy is checked from HWND/PID/process identity before an allowed title read.
- A blocked context does not create a capture target or start persistent sensing.
- Repeated notifications for the same allowed HWND/PID reuse the epoch.
- Context changes cancel prior work; stale UI/sample results are dropped.
- Persistent frame ownership is bounded: a newer frame disposes the prior buffered frame.
- Cursor capture is disabled when the OS API supports it.
- Heavy resize/readback/luma/diff processing is outside `FrameArrived`.
- Normal capture does not write screenshot files.
- Input diagnostics do not record input content.
- Event subscribers are exception-contained and teardown paths were exercised.

## Not implemented

The following capabilities do not exist in `main` and must not be described as complete:

- Product privacy settings, per-application deny rules, pause controls, or persisted policy
- Capability-specific privacy decisions for title/pixels/UIA/OCR/memory/local-server data
- UI Automation client or semantic UI snapshot
- Dialog/error/notification semantic detection
- OCR or changed-region text extraction
- Vision model or visual-description service
- Structured product event schema
- Five-minute short-term memory or context selector
- Local AI server, authenticated LAN transport, model runtime, or resource manager
- Question/answer pipeline
- Microphone, VAD, STT, TTS, or conversational activation
- Tray/background product experience
- Required CI checks or a protected `main` branch
- Production installer, update path, or release pipeline

The `systemAIModels` manifest capability is present, but no Windows AI model API is currently used. It is not evidence of model integration.

## Audit findings and required response

### Must be resolved before M3 continuous semantic sensing

1. **Characterization coverage is validated but not merged yet.** The active M2.4.1 candidate passed portable privacy, epoch, change-classification, timeline, and correlation tests, two-runner CI, the canonical Windows build, and a physical-client runtime regression. It remains incomplete until PR #10 is merged and live `main` is verified.
2. **UI owns product composition and lifetime.** `MainPage.xaml.cs` constructs and coordinates every service. M2.4 separates a composition/lifecycle boundary without changing accepted behavior.
3. **Privacy is binary and not product-configurable.** `AllowsSensing` cannot express different permissions for title, pixels, UIA text, memory, or LAN transfer. It also has no user-managed deny rules. M2.4 introduces the capability contract and configuration boundary before UIA exposes richer text.
4. **Off is not yet a complete screen-privacy state.** The foreground observer and allowed title read run on page load; Arm gates persistent WGC/input tracking only. M2.4 must make Off/Paused stop target-window content reads.
5. **No UIA execution boundary exists.** Official Windows guidance requires UIA client calls on a separate COM MTA worker, not the UI thread. Bounded traversal, caching, time budgets, and recovery/isolation are M3 acceptance requirements.

### Important hardening debt

1. Diagnostic storage is hard-coded to `H:` and filenames still say `m1-3`.
2. Diagnostic logging is static and string-based rather than typed/session-scoped.
3. Low-level input hooks are intentionally content-free and currently fast, but Microsoft recommends dedicated-thread handoff or Raw Input for robust monitoring. Keep the current validated implementation until a measured replacement slice is approved.
4. Only `win-x64` has runtime acceptance. x86 and ARM64 are declared project platforms but are not verified targets.
5. `main` is not branch-protected and has no required checks.
6. Error strings may be displayed in the diagnostic UI. Future content-bearing errors need redaction before logs, bundles, or server transmission.

## Current hardware and deployment assumptions

These are fixed design inputs, not upgrade suggestions:

- Client: Intel i7-6700K, 32 GB RAM, AMD Radeon R9 M395X, Windows desktop.
- AI server: Lenovo LOQ 15IAX9, Intel i5-12450HX, 16 GB RAM, RTX 3050 Laptop GPU 6 GB, Windows 11 Pro.
- All inference and data processing remain local. The LAN server is still a privacy/egress boundary.
- The original single-machine RTX 3060 assumption is obsolete and must not drive new architecture.

## Immediate next gate

The current candidate branch is:

```text
dev/m2-4-1-characterization-tests
```

M2.4.1 candidate validation is complete; the next action is review and merge of PR #10. After merge and live-`main` verification, the next implementation branch is `dev/m2-4-2-composition-lifecycle`. M2.4.2 must preserve this evidence while moving construction and lifetime ownership out of `MainPage`.

Do not start M3.1 UIA code directly from the M2.3 baseline.

## How to update this file

For every milestone merge:

1. Set `reference_code_commit` to the exact accepted functional commit whose code was inspected; do not pretend a PR knows its future squash SHA.
2. Record the Windows acceptance date and evidence.
3. Move only verified work to “implemented.”
4. Add discovered limitations without hiding them behind roadmap language.
5. Set one exact next milestone and branch name.
6. Update the matching roadmap and ADR when architecture changed.
7. Resolve and report live `main` HEAD separately; the current commit cannot embed its own future hash.
