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
- M2.4 foundation hardening and the M3.1 through M3.4 UIA/orchestration slices have passed their acceptance matrices. M3.3's separately authorized semantic snapshot is accepted under ADR 0009 and PR #18; M3.4 bounded orchestration is accepted through ADR 0010, ADR 0011, and PR #24. M4.1 region-of-interest planning is the next gate; do not skip directly to OCR/VLM backend selection, actions, or unbounded semantic sensing.
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
M3.4 is accepted and merged. The accepted main baseline is `55b98d9368d67bb15a1190a1292c8c37e531a9a1`; the documentation closeout merge is `77bd2407c6f5bf5cf0d9476ffcd290f05d23e16d`. Product runtime evidence remains anchored to M3.4 behavior head `979ed5a2318d32ff151d2950f7ace77ec274d601` and physical candidate `26c3290bf196701473b558da657d5c39c8c97e8a`.

M4.1 is now active on `dev/m4-1-roi-planner`. The current slice is a portable, geometry-only `RegionOfInterestPlanner` plus proposed ADR 0012. It maps low-resolution `ChangeRegion` coordinates outward into source-frame pixels and maps UIA screen rectangles only through an explicit caller-supplied capture screen projection. Background planning uses only UIA rectangles associated with the observed change envelope and otherwise falls back to the mapped change region; user-question planning may use caller-ordered UIA bounds without a concurrent change.

The planner has bounded padding, region count, per-region area, and total-area budgets; oversized candidates are rejected, nested UIA candidates are deduplicated, and no full-frame fallback is synthesized. This slice does not crop WGC pixels, run OCR/VLM, inspect prompt text, persist content, log ROI coordinates, or change privacy capabilities/defaults. Acceptance requires deterministic Core tests plus all existing portable/Windows regressions and the strict WinUI build. Separate physical Windows evidence is not required unless the slice expands into Windows interop or content acquisition. Do not start M4.2 OCR backend selection until M4.1 and ADR 0012 are reviewed and accepted.
