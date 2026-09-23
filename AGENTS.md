# Repository instructions for AI coding agents

This file is the durable handoff for any AI or engineer entering this repository without prior chat history.

## Mandatory read order

Before planning or editing, read:

1. `docs/PROJECT_STATE.md`
2. `docs/ARCHITECTURE.md`
3. `docs/PRIVACY_MODEL.md`
4. `docs/ROADMAP.md`
5. `docs/ENGINEERING_WORKFLOW.md`
6. Relevant records under `docs/decisions/`
7. The actual implementation and merged PRs for the area being changed

Then report the current branch, exact HEAD, working-tree status, current milestone, requested slice, and any code/document mismatch. Never rely on a conversation summary when the repository can answer the question.

## Source-of-truth order

When sources disagree, use this order:

1. Current code plus reproducible runtime evidence describes implemented behavior.
2. `docs/PROJECT_STATE.md` describes verified project status and known gaps.
3. Accepted architecture decision records describe durable decisions.
4. `docs/ARCHITECTURE.md` describes the approved target architecture.
5. `docs/ROADMAP.md` describes planned order, not implemented behavior.
6. Issues, PR descriptions, and chat history provide supporting context only.

If code and an accepted invariant disagree, stop and surface the conflict. Do not silently reinterpret either one.

## Product boundaries

- This is a product-grade, fully local Windows copilot, not a demo.
- The fixed deployment has a Windows sensing client and a separate local AI server. Do not recommend hardware upgrades unless the user explicitly asks.
- The system may watch, understand, remember briefly, listen, and answer.
- It may not control mouse or keyboard, invoke UI Automation actions, modify other applications, or perform autonomous actions.
- Cloud APIs, cloud telemetry, account systems, and remote synchronization are out of scope.
- Model and OCR backend names are not architectural commitments until measured on the fixed hardware.

## Safety and privacy invariants

These are release blockers, not preferences:

1. Explicit activation; sensing defaults to OFF.
2. Evaluate HWND/PID/process identity before any content-bearing API.
3. A denied context means no title, pixels, UIA, OCR, memory, audio association, or local-server transmission.
4. Every asynchronous operation carries an immutable epoch ID and cancellation token. Drop stale results before publishing, displaying, persisting, or transmitting them.
5. Raw frames are RAM-only during normal operation and are disposed as soon as they are replaced or consumed.
6. Never log window titles, UIA/OCR text, key values, coordinates, clipboard data, audio, screenshots, prompts, or model responses.
7. UI Automation is read-only. Do not request `uiAccess`, elevation, or secure-desktop access; report inaccessible/elevated targets as unavailable.
8. Treat the local AI server as an egress boundary even though it is on the LAN.
9. Use bounded queues. For volatile state, prefer capacity one and latest-wins. For discrete events, define capacity, deduplication, overflow behavior, and metrics explicitly.
10. Diagnostics are opt-in, fail-safe, content-minimized, and whitelisted.

Read `docs/PRIVACY_MODEL.md` before adding any new source of content.

## Architecture discipline

- Preserve the verified M2 sensing path unless a failing test or measured problem requires change.
- M2.4 foundation hardening and M3.1 through M4.1 have passed their acceptance matrices. M4.1 bounded ROI planning is accepted under ADR 0012 and PR #29. M4.2 OCR benchmark/integration is the next gate; do not skip directly to VLM, actions, or unbounded/full-frame visual sensing.
- Keep Win32/WinRT/COM adapters behind narrow contracts; pure policy and event logic must be unit-testable without Windows interop.
- UI code must not become the lifetime owner and orchestration implementation for new product services. M2.4.2 moved composition/lifecycle out of `MainPage`; preserve the application-owned coordinator boundary.
- UI Automation calls belong on a dedicated COM MTA worker, never the WinUI thread. Accepted M3.2 uses breadth-first Control View traversal, cached `IsContentElement` as the Content subset marker, and explicit structural budgets. Accepted M3.3 reads only separately authorized selected Name, advertised ValuePattern state/value, and visible TextPattern ranges under independent content budgets and clear-on-dispose ownership. Do not add Raw View or a second unbounded traversal.
- Do not assume `Task` cancellation can interrupt a blocked cross-process COM provider. ADR 0011's measured recovery decision keeps the current worker in-process; preserve its request deadline, epoch cancellation, identity/capability revalidation, bounded shutdown, joined-worker regression, and sentinel scan. Revisit the process boundary only with new physical evidence.
- Do not make OCR or VLM always-on. M4.1 ROI planning must remain geometry-only, bounded by count/area, explicit about screen-to-capture projection, and must never synthesize a full-frame fallback. Use the semantic escalation ladder: UIA, then changed-region/ROI OCR, then VLM only if required.
- The newer Windows AI OCR API currently requires an NPU and therefore is not a default fit for the fixed machines. Re-evaluate official hardware support at M4 and benchmark candidates.
- Introduce physical projects/processes only at their roadmap gate. The architecture documents logical target boundaries; they do not authorize a speculative rewrite.
- Record material architectural changes as an ADR and update `PROJECT_STATE.md` and `ROADMAP.md` in the same PR.

## Engineering workflow

- Work from a clean feature branch, never directly on `main`.
- Diagnose with the smallest hypothesis-specific Windows runtime test.
- Do not commit while diagnosis is unresolved.
- Preserve unrelated user changes and stage only explicit paths.
- Required pre-commit checks: focused tests, applicable regression tests, Windows `win-x64` build, `git diff --check`, and diff review.
- Windows interop milestones require runtime evidence on the physical client; CI and a successful build are not substitutes.
- After runtime PASS: cleanup, final diff review, build/test, commit, push, PR, review, then merge.
- Include exact commands, observed evidence, privacy impact, performance measurements, and known limitations in the PR.
- Use PowerShell 5.1-compatible, copy/paste-ready commands for the user. Avoid brittle regex rewrites and broad destructive commands.
- Every future physical acceptance matrix must provide a normal-user, one-command PowerShell harness that automates deterministic fixtures, app commands, assertions, cleanup, and whitelisted evidence. Only unavoidable OS security prompts may remain manual; any other manual case needs an explicit non-automatable justification. CI must parse every acceptance runner before physical use.
- Commit, push, PR creation, and merge require the user's authorization for those Git actions.

Full details are in `docs/ENGINEERING_WORKFLOW.md`.

## Definition of done for a milestone

A milestone is complete only when all of the following are true:

- Scope and non-goals are explicit.
- Privacy, epoch, cancellation, queue, ownership, and teardown paths are reviewed.
- Pure logic has automated tests where applicable.
- The Windows build succeeds with zero warnings and zero errors.
- The exact runtime acceptance scenario passes on target hardware.
- Previous accepted milestones still pass their relevant regression checks.
- Diagnostics contain no prohibited content.
- Documentation and project state are updated.
- Changes are reviewed and merged; an unmerged branch is not project state.

## Current handoff
M4.2.1 is accepted and merged through PR #31. Accepted preflight/behavior head `d8f12b00524934138115f22cc8bd7149e20f4452`; merge commit `920bbecbb4c7ef4c22ca3ff055df1bcaa801e911`; ADR 0013 accepted. Client/server preflights remain the environment evidence: both machines expose only `en-US` through legacy Windows OCR, neither has Tesseract/PaddleOCR installed, and both existing Python installs are 3.14.

M4.2.2 is active on `dev/m4-2-2-controlled-benchmark`. The first gate is a server-local PaddleOCR GPU benchmark environment. `run-m4-2-ocr-benchmark-setup.ps1` must run non-elevated from a clean exact branch and places Python 3.12 below ignored `.localcopilot/ocr-benchmark`. It prefers Python Install Manager `py install --target`; when the machine exposes only legacy `py.exe`, use the project-local CPython NuGet package fallback (`python` 3.12.10). Do not install the benchmark runtime under C:. For the fixed server, pass `-BenchmarkRoot D:\LocalAI-Prerequisites`. It pins `paddlepaddle-gpu==3.2.0` from the CUDA 12.6 wheel index and `paddleocr==3.7.0`. The fixed server driver 596.49 exceeds the documented 550.54.14 minimum. Existing Python 3.14 must remain untouched.

Setup is not OCR acceptance: it may download/install benchmark-only dependencies but must report `ocr_executed=False`, `benchmark_content_created=False`, and `backend_selected=False`. After environment proof, build/run the local-only Persian/English/mixed/UI corpus and add a Tesseract Persian+English baseline before selecting any product backend. Keep screenshots, ground truth, and OCR text out of Git/diagnostics; only aggregate metrics may leave the benchmark directory. Do not wire OCR into product runtime, add LAN pixel transport, or begin M4.3 VLM work before controlled benchmark evidence is reviewed.
