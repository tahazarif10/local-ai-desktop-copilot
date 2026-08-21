---
state_schema: 1
reference_code_commit: dd6f56300fd32d9329f01e6d8515af925e0014bd
last_verified_date: 2026-08-21
completed_through: M2.4.2
next_milestone: M2.4.3
next_milestone_name: Capability-based Privacy Policy
---

# Project state

This document separates verified implementation from target architecture. Update it in every milestone PR that changes status, evidence, constraints, or the next gate.

## Executive state

The implementation at `dd6f56300fd32d9329f01e6d8515af925e0014bd` is the accepted M2.4.2 functional baseline. [PR #12](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/12) moved sensing composition, subscriptions, and lifecycle into an application-owned coordinator while retaining `MainPage` as a view/command adapter. The canonical Windows `win-x64` build, 49 deterministic tests, CI, and the full physical-client sensing/privacy/teardown regression passed. Documentation-only descendants may follow this SHA without changing the accepted runtime behavior; always resolve live HEAD from GitHub/Git.

There is no known blocking defect in the accepted M2 path or M2.4.2 lifecycle foundation. No rework is required unless a reproducible regression appears.

The repository is not yet a complete copilot. It is a diagnostic WinUI shell around the sensing foundation. The next implementation work is M2.4.3 capability-based privacy policy before diagnostics hardening and any UI Automation content collection.

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
| M2.4.1 Characterization tests and CI foundation | Complete | [PR #10](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/10), squash `833a0af` | 43/43 deterministic tests locally and on Ubuntu/Windows CI; canonical Windows build; unchanged M2 path passed the physical-client regression and privacy scan |
| M2.4.2 Composition and lifecycle separation | Complete | [PR #12](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/12), validated code head `dd6f563` | 49/49 deterministic tests locally and on Ubuntu/Windows CI; canonical Windows build; application-owned coordinator passed lifecycle, privacy, recovery, unavailable-target, and armed-shutdown regression |

PR #7 was squash-merged as `c29099a`. Its feature-branch head (`abcbf08`) is not the `main` baseline.

PR #10 was squash-merged as `833a0af`. Its behavior-bearing validation head (`ab504ee`) and final documentation-only PR head (`af9a916`) are supporting evidence, not the current `main` baseline.

### M2.4.1 acceptance evidence

The behavior-bearing code was validated at PR #10 head `ab504eea5c4baabf2b770c92d0866ff89d1caac9` on 2026-08-21, then squash-merged without a tree-content change as `833a0af915a5ed58dd61642c1e188c623d3b90d4`. Later documentation-only descendants do not replace that runtime evidence.

- Local Windows test run: 43 passed, 0 failed, 0 skipped.
- Local canonical app build: `Debug/win-x64`, 0 warnings, 0 errors.
- [CI run #2](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32472133991): 43/43 core tests passed on Ubuntu and Windows; the Windows `Debug/win-x64` app build passed.
- [Final PR CI run #3](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32474169112) passed on the documentation-complete PR head before merge.
- Physical-client regression: explicit Arm, allowed sensing, metadata-only input correlation, diagnostic privacy blocking before content access, automatic allowed recovery, same-context reuse, Disarm/Re-arm, allowed context transition, and page unload all behaved as the accepted M2 baseline.
- Every exercised persistent session ended with `hadError=False` and `staleDropped=0`. Privacy transitions ended the prior session before publication to the blocked epoch; frame replacement remained active under the latest-wins capacity-one policy.
- Teardown evidence: input tracking stopped, persistent sensing was already stopped, the foreground hook returned `success=True` with `lastError=0`, the observer reported stopped, and the final epoch was cancelled/reset.
- Diagnostic privacy scan found only the approved metadata classes; no raw title, key, text, coordinate, clipboard, pixel payload, audio, prompt, or response content was recorded. Session identifiers, process/window metadata, and machine-specific performance measurements are intentionally retained outside the public repository.

Verdict: **accepted and merged**. This M2.4.1 evidence remains part of the baseline; the current next gate is recorded below.

### M2.4.2 acceptance evidence

The behavior-bearing code was validated at PR #12 head `dd6f56300fd32d9329f01e6d8515af925e0014bd` on 2026-08-21. The runtime bundle recorded the exact branch/SHA and an empty working tree. Later documentation-only descendants do not replace that runtime evidence.

- Local Windows test run: 49 passed, 0 failed, 0 skipped.
- Local canonical app build: `Debug/win-x64 --warnaserror`, 0 warnings, 0 errors.
- [CI run #8](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32487234647): 49/49 core tests passed on Ubuntu and Windows; the Windows `Debug/win-x64 --warnaserror` app build passed.
- Physical-client regression: initial application-owned start, one service-subscription set, one view attachment, target probe, RAM-only frame capture, 640 px change sample, explicit Arm, allowed sensing, metadata-only input correlation, diagnostic Notepad blocking, automatic allowed recovery, same-context reuse, Disarm/Re-arm, unavailable-target handling/recovery, and close while Armed all behaved as designed.
- Four persistent sessions ended cleanly: epoch 3 on privacy transition (105 samples), epoch 5 on user Disarm (62 samples), epoch 5 on allowed context transition (20 samples), and epoch 7 on application shutdown (12 samples). Every session reported `hadError=False` and `staleDropped=0`; latest-wins replacement remained active.
- Denied Notepad reached `PRIVACY.DENY` and `SERVICE.PRIVACY_DENY` before any title or capture event. The prior epoch/session was cancelled, input tracking stopped, and Explorer recovery advanced to a new allowed epoch automatically.
- Shutdown cardinality was one coordinator start/stop/dispose and one subscription attach/detach. The foreground hook stopped on its installing UI thread with `success=True` and `lastError=0`; the active persistent session and input tracker stopped; the final epoch reset/disposed. Two already-queued UI notifications arrived after disposal and were rejected by the existing epoch stale-publication gates without rendering or touching a disposed sensing resource.
- Diagnostic privacy scan found only approved metadata. No raw title, key/text value, coordinate, clipboard content, pixel payload, audio, prompt, or response was recorded; the independent OS probe agreed with the application transition sequence while retaining only title length.

Verdict: **accepted**. PR #12 is the merge record. M2.4.3 is the only approved next implementation gate; this evidence does not authorize M3 work.

## Current implementation map

### Current stack

| Area | Current repository value | Verification boundary |
| --- | --- | --- |
| Language/runtime | C# on .NET 10 | `net10.0-windows10.0.26100.0` |
| Desktop UI | Packaged WinUI 3 | `Microsoft.WindowsAppSDK` 2.4.0 |
| Portable logic | `LocalCopilot.Core` class library | `net10.0`; no WinUI/Windows API dependency |
| Characterization tests | MSTest 4.3.3 | 49 deterministic tests; 49/49 passed locally and on both CI runners at the accepted M2.4.2 baseline |
| Continuous integration | GitHub Actions | [PR #12 code-head run](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32487234647) passed core tests on Ubuntu/Windows and the `Debug/win-x64` app build on Windows |
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
| `ApplicationCompositionRoot` | Constructs the current service graph once for the desktop process | Concrete composition remains in the Windows app assembly |
| `DesktopCopilotCoordinator` | Owns sensing integration, subscriptions, immutable view state, commands, start/stop, and teardown | One UI-thread-owned coordinator per application/window lifetime |
| `ApplicationLifecycleGate` | Thread-safe one-shot Created/Running/Stopped/Disposed transitions | Portable lifecycle state only; Windows resource teardown remains coordinator-owned |
| `MainPage` | Diagnostic rendering and command forwarding through `IDesktopCopilotView` | Attaches/detaches as a view; it does not construct or own sensing resources |

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
- `App` owns one coordinator; page load/unload only attaches/detaches the view.
- Service subscription, observer, capture/input session, epoch, and coordinator teardown paths were exercised without a resource leak.

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

M2.4.1 resolved the missing automated characterization/CI foundation, and M2.4.2 resolved page-owned composition/lifetime. The remaining findings are:

1. **Privacy is binary and not product-configurable.** `AllowsSensing` cannot express different permissions for title, pixels, UIA text, memory, or LAN transfer. It also has no user-managed deny rules. M2.4.3 introduces the capability contract and configuration boundary before UIA exposes richer text.
2. **Off is not yet a complete screen-privacy state.** The application-owned foreground observer and allowed title read begin at application startup; Arm gates persistent WGC/input tracking only. M2.4.3 must make Off/Paused stop target-window content reads.
3. **No UIA execution boundary exists.** Official Windows guidance requires UIA client calls on a separate COM MTA worker, not the UI thread. Bounded traversal, caching, time budgets, and recovery/isolation are M3 acceptance requirements.

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

The next implementation branch is:

```text
dev/m2-4-3-capability-privacy
```

M2.4.2 is accepted through PR #12. M2.4.3 must replace binary sensing permission with explicit capability decisions and true Off/Paused semantics while preserving the accepted sensing, lifecycle, test, CI, privacy-negative, and teardown evidence. It must not introduce UIA implementation, diagnostic-path refactoring, local-server transport, or other later-slice behavior.

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
