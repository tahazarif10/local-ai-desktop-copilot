---
state_schema: 1
reference_code_commit: e48b067f1c13ee5ba211bcd36de663b30ca27246
last_verified_date: 2026-08-23
completed_through: M3.1
next_milestone: M3.2
next_milestone_name: Bounded Structural Snapshot
---

# Project state

This document separates verified implementation from target architecture. Update it in every milestone PR that changes status, evidence, constraints, or the next gate.

## Executive state

The implementation at `e48b067f1c13ee5ba211bcd36de663b30ca27246` is the accepted M3.1 functional baseline. [PR #15](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/15) adds the smallest capability-gated UI Automation boundary: a manual, root-only `ElementFromHandle` probe on one application-owned COM MTA worker. The canonical Windows `win-x64` build, 91 deterministic tests on Ubuntu and Windows, and two physical diagnostic sessions passed. Documentation-only or merge descendants may follow this SHA without changing the accepted runtime behavior; always resolve live HEAD from GitHub/Git.

There is no known blocking defect in the accepted M2 sensing path, completed M2.4 foundation-hardening gate, or M3.1 root-probe boundary. No rework is required unless a reproducible regression appears.

The repository is not yet a complete copilot. It is a hardened diagnostic WinUI shell around the sensing foundation plus a metadata-only UIA root-availability probe. M3.1 intentionally reads no UIA property, child, view, text, pattern, or action. M3.2 bounded structural snapshots are the only approved next implementation gate; semantic text collection remains M3.3.

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
| M2.4.3 Capability-based privacy policy | Complete | [PR #13](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/13), validated code head `a9c0adb` | 58/58 deterministic tests locally and on Ubuntu/Windows CI; canonical Windows build; true Off, capability gates, deny/recovery, identity revalidation, Disarm/Re-arm, stale rejection, and armed shutdown passed |
| M2.4.4 Diagnostics and input hardening | Complete | [PR #14](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/14), validated code head `cfcc480` | 68/68 deterministic tests on Ubuntu/Windows CI; strict Windows build; default/custom diagnostic roots, normal-launch no-write proof, all four correlation outcomes, 1,965 measured callbacks with zero errors/mismatches, and clean unhook/shutdown passed |
| M3.1 UIA capability and worker probe | Complete | [PR #15](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/15), validated code head `e48b067` | 91/91 deterministic tests on Ubuntu/Windows CI; strict Windows build; Win32/packaged/browser roots, privacy and integrity denial, timeout recovery, latest-wins/stale publication, and active-worker teardown passed |

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

Verdict: **accepted**. PR #12 is the merge record. At that gate M2.4.3 was the only approved next step; M2.4.3 and M2.4.4 are now accepted below.

### M2.4.3 acceptance evidence

The behavior-bearing code was validated at PR #13 head `a9c0adb877d010305a551cf705b87f952532c511` on 2026-08-21. The runtime bundle recorded the exact branch/SHA and an empty working tree.

- Local Windows test run: 58 passed, 0 failed, 0 skipped.
- Local canonical app build: `Debug/win-x64 --warnaserror`, 0 warnings, 0 errors.
- [CI run #12](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32492536649): 58/58 core tests passed on Ubuntu and Windows; the Windows `Debug/win-x64 --warnaserror` app build passed.
- Capability tests covered independent grants, deny precedence, exact application overrides, safe product defaults, diagnostic-only derived retention, invalid configuration, policy revision/change notifications, and epoch cancellation when capabilities or policy revision change.
- Physical-client Off proof: after startup and again for the full Disarm interval, the independent OS probe observed Chrome/Explorer transitions while the application produced no foreground hook callback, identity lookup, title read, capture sample, or input-tracking event.
- Every persistent session began only after a successful `SERVICE.IDENTITY_REVALIDATE` with matching expected/actual PID. This included Chrome, Explorer, post-deny recovery, and Re-arm.
- Diagnostic Notepad was denied with capability mask zero before any title read or WGC session. The previous allowed epoch/session was cancelled, input tracking stopped, and returning to Explorer advanced to a fresh allowed epoch automatically.
- Four sessions ended without error: context transition, privacy denial, user Disarm, and application shutdown. Three reported `staleDropped=0`; one in-flight sample during Disarm reported `staleDropped=1` and was correctly rejected rather than published.
- The foreground hook stopped successfully on Disarm and shutdown. Re-arm installed a fresh hook; final shutdown stopped capture/input, reset/disposed the epoch, detached subscriptions, disposed the coordinator, and rejected the queued session-end UI notification as stale.
- Diagnostic privacy scan found only approved metadata and activity kinds. No raw title, key/text value, coordinate, clipboard content, pixel payload, audio, prompt, or response was recorded; the independent OS probe retained title length only.

Verdict: **accepted**. PR #13 is the review/merge record. At that gate M2.4.4 was the only approved next step; its acceptance is recorded below.

### M2.4.4 acceptance evidence

The behavior-bearing code was validated at PR #14 head `cfcc4806b266bd8654fa93745783e8c8ae6b5b60` on 2026-08-21. Both diagnostic bundles recorded the exact branch/SHA and an empty working tree; the later acceptance-documentation commit does not replace that runtime evidence.

- Automated validation: 68/68 deterministic core tests passed on Ubuntu and Windows; Windows PowerShell 5.1 parsed `run-debug.ps1`; the strict `Debug/win-x64 --warnaserror` WinUI build passed in [CI run #32500012379](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32500012379).
- Default-root and custom-root sessions each completed with build, packaged application, activation handshake, independent foreground probe, runner, and three-file bundle all passing. The custom root contained a space and required no code change.
- A subsequent packaged launch without a diagnostic token returned exit code zero and left all 14 files across the default root, custom root, and legacy log target byte-for-byte and timestamp-for-timestamp unchanged.
- Correlation regression directly observed `MouseClick`, `MouseWheel`, `KeyboardActivity`, and expired `None`. The custom session alone recorded 2, 16, 8, and 49 correlated changes respectively.
- Four hook lifetimes processed 1,965 callbacks. Every lifetime reported zero callback errors, subscriber errors, and installing-thread mismatches; keyboard and mouse unhook both succeeded. Weighted mean callback duration was 92.8 microseconds and the maximum was 929.8 microseconds, with no callback above 1 millisecond.
- Physical lifecycle regression covered startup Off, explicit Arm, allowed sensing, exact Notepad deny before content, automatic Chrome recovery, context cancellation/stale rejection, user Disarm, Re-arm, and close while Armed. Sessions ended with `hadError=False`; teardown stopped hooks/input/capture and disposed subscriptions/coordinator cleanly.
- Diagnostic privacy review found only approved metadata. No title text, key/scan code, typed text, coordinate, clipboard content, pixel payload, audio, prompt, response, provider exception message, or stack text appeared.
- Decision: retain the current synchronous diagnostic-only low-level hooks. A dedicated hook thread or Raw Input is not justified by current target-hardware evidence and requires a new measured regression before replacement.

Verdict: **accepted**. PR #14 is the review/merge record. At that gate M2.4 was complete and M3.1 was the only approved next implementation step; M3.1 is now accepted below.

### M3.1 acceptance evidence

The behavior-bearing code was validated at PR #15 head `e48b067f1c13ee5ba211bcd36de663b30ca27246` on 2026-08-23. Both physical bundles recorded the exact feature branch/SHA and an empty working tree; the acceptance-documentation commit does not replace that runtime evidence.

- [CI run #22](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32633213008) passed 91/91 deterministic core tests on Ubuntu and Windows, parsed `run-debug.ps1` with Windows PowerShell, and built the packaged app as strict `Debug/win-x64 --warnaserror` with zero warnings and zero errors.
- Physical session `cefae151-90c8-4d52-a94c-a445ef39e55b` proved `Available/RootResolved` against classic PowerShell, Chrome, and packaged `ApplicationFrameHost` targets. Every successful native call used worker managed thread 10 after `UIA.WORKER_START apartment=MTA`; the WinUI log thread remained thread 2.
- Diagnostic Notepad returned `Unavailable/CapabilityDenied` without a `UIA.QUEUE` or native root request for its denied epoch. Product defaults still deny `ReadUiStructure`; the validated diagnostic launch granted structure without granting UIA text.
- Three forced-expiry probes returned `Timeout/DeadlineExpired`. A normal packaged-target probe immediately after the final expiry returned `Available/RootResolved` on the same worker thread, proving typed deadline handling and worker recovery without claiming that every hostile provider is interruptible.
- The first session exercised 78 rapid foreground epochs across allowed, denied, transient, and own windows. Prior-context work never rendered as current, teardown stopped the MTA worker once, and `UIA.WORKER_DISPOSE` reported `started=True joined=True`.
- Targeted session `6db7d86a-526f-421d-a729-befd76fff088` ran from a confirmed non-Administrator shell (`Runner elevated: False`). An elevated target reported current RID 8192 and target RID 12288, `inspected=True`, `mayRead=False`, HRESULT `0x80070005`, and `Unavailable/HigherIntegrity` before UIA.
- Two deterministic three-request bursts each kept one request active, replaced the older pending request as `Cancelled/Superseded`, converted both non-latest final results to `Stale/PublicationRejected`, and published only the newest request as `Available/RootResolved`.
- A third burst was interrupted by application shutdown while the two-second hold was active. Pending and active work completed `Cancelled/WorkerStopped`, every final result was rejected as stale, the worker emitted one stop, and disposal joined it successfully. Existing sensing teardown also reported successful hook removal, zero callback/subscriber/thread errors, and successful keyboard/mouse unhook.
- Both diagnostic privacy scans found no title text, UIA Name/Value/Text, tree/property/control data, key or scan code, typed text, coordinates, clipboard content, pixel payload, audio, prompt, response, provider exception message, or stack text.

The accepted boundary is deliberately narrow:

- `ReadUiStructure` remains denied by product defaults and is granted without `ReadUiText` only by a validated expiring diagnostic launch.
- `DesktopCopilotCoordinator` gates before queueing and applies a current epoch/capability publication gate after completion.
- `UiAutomationProbeWorker` starts lazily, owns one dedicated COM MTA thread, and permits one active plus one coalesced newest pending request.
- The worker revalidates HWND/PID, rejects unreadable or higher-integrity targets before UIA, creates `CUIAutomation8` as `IUIAutomation2`, configures 1.5-second native connection/transaction timeouts, and applies a 2.5-second normal end-to-end deadline. A separate diagnostic command forces an expired request so typed timeout and immediate same-worker recovery can be observed deterministically.
- Runtime evidence records current/target integrity RIDs, and `run-debug.ps1` rejects an elevated host before creating a session so an administrator runner cannot invalidate the higher-integrity negative case.
- A diagnostic-only two-second worker hold plus three-request burst makes active/latest-pending replacement and stale publication observable without depending on human clicks beating a millisecond-scale provider call. It does not alter normal probe timing.
- `ElementFromHandle` is the only UIA operation. The root pointer is released on the worker and no UIA object, property, text, tree node, pattern, or action crosses the apartment.
- Outcomes are typed as `Available`, `Unavailable`, `Timeout`, `Cancelled`, `Stale`, or `Faulted`; diagnostics contain only reason/timing/HRESULT/thread/identity metadata.
- The portable suite grows from the accepted 68-test baseline to 91 tests, adding capability separation, HRESULT classification, stale/latest-request publication, and bounded latest-pending replacement coverage.

Verdict: **accepted**. PR #15 is the review and merge record. M3.2 is the only approved next implementation gate.

## Current implementation map

### Current stack

| Area | Current repository value | Verification boundary |
| --- | --- | --- |
| Language/runtime | C# on .NET 10 | `net10.0-windows10.0.26100.0` |
| Desktop UI | Packaged WinUI 3 | `Microsoft.WindowsAppSDK` 2.4.0 |
| Portable logic | `LocalCopilot.Core` class library | `net10.0`; no WinUI/Windows API dependency |
| Characterization tests | MSTest 4.3.3 | Accepted M3.1 baseline: 91/91 deterministic tests |
| Continuous integration | GitHub Actions | [PR #15 code-head run](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32633213008) passed 91 core tests on Ubuntu/Windows, Windows PowerShell parsing, and the strict `Debug/win-x64` app build |
| Capture/image interop | Windows Graphics Capture + Win2D | `Microsoft.Graphics.Win2D` 1.4.0 |
| UIA interop | Direct minimal `CUIAutomation8`/`IUIAutomation2` ABI inside `LocalCopilot.App` | Accepted root-only manual probe; no property/text/traversal/action and no extra runtime package/process |
| Packaging/trust | MSIX tooling, full-trust desktop app | Development package identity used by `dotnet run` |
| Canonical validated target | `Debug`, `win-x64` | Physical Windows client acceptance |
| Declared OS minimum | Windows build 17763 | Declaration only; it is not broad runtime-support evidence |
| Reserved AI manifest capability | `systemAIModels` | Declared but unused by current code |

The solution's `Any CPU` mapping currently resolves to x86, while the accepted workflow builds the project explicitly with `-r win-x64`. Use the canonical command in `ENGINEERING_WORKFLOW.md`; do not infer platform support from solution configurations.

Qwen3-VL-2B appeared as an early candidate in the original product prompt. It is not an accepted model selection. OCR, VLM, text LLM, STT, and TTS choices remain benchmark-gated on the fixed two-computer hardware.

| File/component | Implemented responsibility | Important current boundary |
| --- | --- | --- |
| `ForegroundWindowObserver` | Out-of-context foreground WinEvent hook and latest pending HWND dispatch | External foreground changes only; own process skipped |
| `ForegroundWindowService` | HWND/PID/process identity, privacy evaluation, allowed title read, and immediate pre-capture identity revalidation | Process identity is the privacy bootstrap data |
| `PrivacyPolicy` | Eleven independent capability grants, strict emergency/app/global precedence, normalized exact-app rules, immutable snapshots, revisioning, and change notification | Configuration boundary exists; settings UI and persistence do not |
| `ContextEpochManager` | Immutable context identity, cancellation, reuse, stale-work boundary | Epoch changes on HWND/PID/capability/rule/reason/policy-revision changes |
| `GraphicsCaptureItemFactory` | HWND to WGC capture target COM interop | Unavailable target is a typed failure |
| `SingleFrameCaptureService` | Explicit one-frame WGC diagnostic capture | RAM-only result metadata; no image persistence |
| `ChangeDetector` | Luminance frame comparison and region/classification | Pure CPU logic; Baseline/Insignificant/Meaningful/Large only, not semantic importance |
| `ChangeDetectionProbeService` | Manual single-sample detector profiles | Diagnostic path retained separately from persistent path |
| `PersistentChangeDetectionService` | Persistent WGC, capacity-one latest frame, resize/recreate, downscale/readback/luma/diff | 640 px / 500 ms is the accepted current profile |
| `SensingOrchestrator` | Explicit arm, context settle, previous-session cleanup, start/stop/status | Defaults OFF; no semantic stages yet |
| `InputActivityTracker` | Global low-level activity-kind hooks plus teardown health aggregation | Records kind/count/timing only; no key, scan code, text, coordinate, clipboard, or target data; synchronous path retained from physical measurements |
| `DiagnosticTimeline` | Bounded epoch-scoped activity retention | Capacity 256, five-second retention |
| `ChangeCorrelationService` | Two-second possible-trigger lookup for meaningful/large visual change | Diagnostic correlation, not causality or semantic event detection |
| `DiagnosticLog` / `run-debug.ps1` | Expiring launch-scoped metadata sink, isolated session directory, activation handshake, and exact three-file bundle | No persistent enable flag; normal launch writes nothing; default root is ignored and custom roots are supported |
| `ApplicationCompositionRoot` | Constructs the current service graph once for the desktop process | Concrete composition remains in the Windows app assembly |
| `DesktopCopilotCoordinator` | Owns sensing integration, subscriptions, immutable view state, commands, start/stop, and teardown | One UI-thread-owned coordinator per application/window lifetime |
| `UiAutomationProbeWorker` | Owns the lazy COM MTA, capacity-one latest-pending slot, integrity/identity checks, native timeouts, root resolution, and same-thread COM release | `ElementFromHandle` only; returns metadata outcomes and exposes no UIA object, property, tree, text, pattern, or action |
| `ApplicationLifecycleGate` | Thread-safe one-shot Created/Running/Stopped/Disposed transitions | Portable lifecycle state only; Windows resource teardown remains coordinator-owned |
| `MainPage` | Diagnostic rendering and command forwarding through `IDesktopCopilotView` | Attaches/detaches as a view; it does not construct or own sensing resources |

## Verified current invariants

- Screen observation is OFF until the user explicitly arms it; Off has no foreground hook, target identity lookup, title read, capture, or input tracking.
- Disarm stops the observer, capture and input sources, cancels/resets the epoch, and Re-arm begins from no prior context.
- Privacy is checked from HWND/PID/process identity before an allowed title read.
- Every current WGC entry point requires `CapturePixels` and revalidates HWND/PID immediately before capture-item/session creation.
- Title, pixels, UIA structure/text, OCR, derived retention, microphone, and local-server text/pixel/audio transmission are independent grants; one never implies another.
- A blocked context does not create a capture target or start persistent sensing.
- Repeated notifications for the same allowed HWND/PID reuse the epoch.
- Context changes cancel prior work; stale UI/sample results are dropped.
- Persistent frame ownership is bounded: a newer frame disposes the prior buffered frame.
- Cursor capture is disabled when the OS API supports it.
- Heavy resize/readback/luma/diff processing is outside `FrameArrived`.
- Normal capture does not write screenshot files.
- Input diagnostics do not record input content; accepted hook health reports only bounded counts, durations, thread consistency, and unhook results.
- Diagnostics are disabled unless one validated, expiring process launch token binds the app to one isolated session; a normal launch does not create or modify diagnostic files.
- `App` owns one coordinator; page load/unload only attaches/detaches the view.
- Service subscription, observer, capture/input session, epoch, and coordinator teardown paths were exercised without a resource leak.
- Root-only UIA work starts only after `ReadUiStructure`, runs on one dedicated COM MTA worker, revalidates HWND/PID and integrity, releases COM pointers on that worker, and passes the epoch/capability/latest-request publication gate.

## Not implemented

The following capabilities do not exist in `main` and must not be described as complete:

- Product privacy settings UI, pause control, or persisted policy configuration
- Bounded UIA structural or semantic snapshot; the accepted root-only probe collects no properties, children, views, patterns, or text
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

M2.4.1 through M2.4.4 resolved the characterization/CI, ownership/lifecycle, capability-privacy, diagnostic-session, exception-redaction, and input-measurement findings. M3.1 accepted the minimal UIA execution boundary. The next unresolved slice is narrower than continuous semantic sensing:

1. **M3.2 needs a separately bounded structural contract.** Before any traversal, define and test explicit node, depth, elapsed-time, property-count/string-byte, and result-size budgets; choose Control/Content view behavior; batch only required non-text properties through caching; and reevaluate generated/CsWin32 interop because the accepted manual ABI was approved only for the tiny root probe.
2. **Process isolation remains evidence-gated.** M3.1 platform timeouts and physical recovery are sufficient for a manual root probe, not proof that every hostile provider is interruptible. M3.4 must decide whether continuous UIA needs a restartable helper process from measured M3.2/M3.3 behavior.

### Important hardening debt

1. `DiagnosticLog` remains a static compatibility facade, although activation, sink binding, line sanitation, exception metadata, and bundle membership are now session-scoped and validated.
2. The accepted synchronous low-level hooks must be remeasured if callback errors, thread mismatches, latency regressions, or missing activity appear; replacement with a dedicated thread or Raw Input is not currently justified.
3. Only `win-x64` has runtime acceptance. x86 and ARM64 are declared project platforms but are not verified targets.
4. `main` is not branch-protected and has no required checks.
5. Error strings may be displayed in the diagnostic UI. Future content-bearing errors need redaction before display, logs, bundles, or server transmission.
6. The capability policy has a validated configuration boundary and exact per-app behavior, but a user-facing settings UI, pause state, and persisted rules remain future product work.

## Current hardware and deployment assumptions

These are fixed design inputs, not upgrade suggestions:

- Client: Intel i7-6700K, 32 GB RAM, AMD Radeon R9 M395X, Windows desktop.
- AI server: Lenovo LOQ 15IAX9, Intel i5-12450HX, 16 GB RAM, RTX 3050 Laptop GPU 6 GB, Windows 11 Pro.
- All inference and data processing remain local. The LAN server is still a privacy/egress boundary.
- The original single-machine RTX 3060 assumption is obsolete and must not drive new architecture.

## Immediate next gate

The next implementation branch is:

```text
dev/m3-2-bounded-structural-snapshot
```

M3.1 is accepted through PR #15. M3.2 may extend the same `ReadUiStructure` boundary only into a bounded, foreground-HWND structural snapshot with non-text metadata and explicit view/cache/traversal/result budgets. Its first task is contract and interop design backed by portable budget tests; it must preserve the accepted worker, privacy, epoch, latest-wins, and teardown behavior.

Do not add UIA Name/Value/Text retention, OCR, action patterns, elevation/`uiAccess`, continuous semantic orchestration, or a speculative helper process in M3.2.

## How to update this file

For every milestone merge:

1. Set `reference_code_commit` to the exact accepted functional commit whose code was inspected; do not pretend a PR knows its future squash SHA.
2. Record the Windows acceptance date and evidence.
3. Move only verified work to “implemented.”
4. Add discovered limitations without hiding them behind roadmap language.
5. Set one exact next milestone and branch name.
6. Update the matching roadmap and ADR when architecture changed.
7. Resolve and report live `main` HEAD separately; the current commit cannot embed its own future hash.
