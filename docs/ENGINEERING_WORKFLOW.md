# Engineering workflow

This workflow exists to keep a Windows-interop, privacy-sensitive project reproducible. Fast iteration is useful only when the evidence remains trustworthy.

## 1. Operating rules

- One bounded milestone slice at a time.
- One concrete hypothesis per diagnostic run.
- Do not commit unresolved diagnostic experiments.
- Do not start the next feature until the current slice passes its exit criteria.
- Preserve prior accepted behavior unless a test/evidence-backed change explicitly replaces it.
- Use the repository as the handoff; update state and architecture in the same PR as a material change.
- A successful build is not a Windows interop runtime test.
- A runtime result without branch, SHA, status, and scenario metadata is not acceptance evidence.

## 2. Preflight on the Windows client

The normal repository path on the current developer machine is `H:\AIProjects\local-ai-desktop-copilot`, but scripts and product code must not assume that path.

Use a PowerShell 5.1-compatible preflight:

```powershell
Set-Location H:\AIProjects\local-ai-desktop-copilot

git branch --show-current
git rev-parse HEAD
git status --short
git log -1 --oneline
```

Before creating a new branch:

- confirm the intended base branch and exact SHA;
- confirm `git status --short` is empty;
- fetch/pull with fast-forward only when updating `main`;
- stop if unrelated changes exist;
- use one branch per milestone slice.

Branch convention:

```text
dev/m<major>-<minor>-<slice>-<short-name>
fix/m<major>-<minor>-<short-name>
docs/<short-name>
```

Examples:

```text
dev/m2-4-1-characterization-tests
dev/m3-1-uia-worker-probe
fix/m2-4-diagnostic-cleanup
```

## 3. Plan the slice before editing

Write down:

1. exact goal;
2. why it is the next dependency;
3. files/contracts expected to change;
4. privacy capabilities touched;
5. epoch/cancellation/publication rule;
6. threads/processes and resource ownership;
7. queue capacity/drop/backpressure behavior;
8. explicit non-goals;
9. focused automated tests;
10. one target-hardware runtime scenario;
11. expected PASS evidence and failure signatures;
12. regression checks from prior milestones.

If the slice changes a trust boundary, persistence, network path, queue semantics, epoch identity, model ownership, autonomy boundary, or milestone order, add/update an ADR before implementation.

## 4. Diagnosis loop

Use this loop for a failure:

```text
observe exact symptom
  -> inspect current code and full diagnostic session
  -> state one falsifiable hypothesis
  -> add the smallest metadata-only instrumentation or change
  -> build
  -> run one targeted Windows scenario
  -> compare expected and actual evidence
  -> keep, revise, or revert the hypothesis
```

Do not:

- ask for broad manual clicking without a hypothesis;
- change multiple subsystems to “see if it helps”;
- publish code while the diagnosis is uncertain;
- infer causality from activity correlation;
- accept a partial log excerpt when teardown/context history matters;
- hide an unexpected exception behind a generic PASS statement.

## 5. Build and automated tests

The accepted Windows build command is:

```powershell
dotnet build .\src\LocalCopilot.App\LocalCopilot.App.csproj -c Debug -r win-x64
```

Expected result:

```text
Build succeeded.
0 Warning(s)
0 Error(s)
```

Only `win-x64` has runtime acceptance. Do not claim x86/ARM64 support merely because platforms appear in the project.

The accepted M2.3 baseline `c29099a` had no automated tests. M2.4.1 adds a portable characterization suite for the extracted core logic. Run it with:

```powershell
dotnet test .\tests\LocalCopilot.Core.Tests\LocalCopilot.Core.Tests.csproj -c Release --settings .\tests\LocalCopilot.Core.Tests\.runsettings
```

The suite currently contains 43 deterministic tests for `PrivacyPolicy`, `ContextEpochManager`, `ChangeDetector`, `DiagnosticTimeline`, and `ChangeCorrelationService`. The runsettings file makes zero discovered tests a hard failure. The suite must remain free of WGC, global hooks, UI Automation, XAML, and a live desktop. A passing core suite does not replace the canonical Windows app build above.

The CI workflow runs the core suite on both Ubuntu and Windows, then builds the packaged app as `Debug/win-x64` on Windows. Test-result artifacts are retained for failed as well as successful runs. Do not write “all tests passed” unless the relevant local/CI run is identified and actually passed; report build, test, CI, and physical runtime evidence as separate facts.

Run a focused filter during diagnosis when useful, then the full suite before commit:

```powershell
dotnet test .\tests\LocalCopilot.Core.Tests\LocalCopilot.Core.Tests.csproj -c Release --settings .\tests\LocalCopilot.Core.Tests\.runsettings --filter "FullyQualifiedName~ChangeDetectorTests"
```

## 6. Diagnostic runner

Run only when diagnostics are intentionally required:

```powershell
Set-Location H:\AIProjects\local-ai-desktop-copilot
.\run-debug.ps1
```

Current behavior:

- rejects launch if `LocalCopilot.App` is already running;
- removes a stale diagnostic-enable flag before the new session;
- records session ID, branch, SHA, .NET, PowerShell, OS, and Git status;
- builds `Debug/win-x64`;
- runs an independent metadata-only foreground probe;
- enables application diagnostics only around `dotnet run`;
- refreshes a bundle from an explicit diagnostic filename whitelist;
- disables diagnostics first during cleanup;
- stops jobs, performs a final refresh, and copies the bundle to clipboard.

Known development-only limitation:

```text
H:\DevCache\LocalCopilot
m1-3-*.log / m1-3-debug-bundle.txt
```

M2.4.4 replaces the fixed drive and legacy names without losing the one-command/copy-entire-bundle workflow.

Before sharing a bundle, check it for unexpected content. Normal logs must never include titles, UIA/OCR text, input values, coordinates, clipboard data, pixels, audio, prompts, or responses.

## 7. Runtime acceptance structure

Every Windows runtime acceptance record must include:

```text
Milestone/slice:
Branch:
HEAD:
Working tree status:
Target machine:
Build command/result:
Exact scenario:
Expected event sequence:
Observed event sequence:
Counters/timings:
Privacy-negative case:
Cancellation/stale case:
Teardown result:
Regression checks:
Verdict: PASS / FAIL / INCONCLUSIVE
Known limitations:
```

Required categories for a content-bearing asynchronous feature:

- happy path;
- blocked/privacy-negative path;
- context switch and stale-result path;
- timeout/unavailable path;
- repeated/reuse path;
- stop/unload/teardown path;
- relevant prior-milestone regression.

A single happy-path screenshot is not acceptance.

## 8. Performance evidence

Performance measurements must name:

- exact hardware and OS;
- build configuration/runtime identifier;
- input dimensions/profile;
- warmup count;
- sample count;
- average plus useful percentile/max;
- CPU/RAM/GPU/VRAM measurement method when reported;
- foreground workload and resource mode;
- whether the number is an estimate or measured value.

Instrument before optimizing. Preserve the current 640 px / 500 ms profile until a benchmark shows a better tradeoff on the fixed client.

## 9. Diff review and repository hygiene

Before staging:

```powershell
git status --short
git diff --check
git diff --stat
git diff
```

Review for:

- unrelated files;
- accidental raw content, logs, bundles, screenshots, model files, or secrets;
- fixed machine paths;
- unbounded queues/collections;
- missing cancellation and stale publication checks;
- event subscription/COM/WinRT/frame disposal;
- exception messages that may contain content;
- widened permissions or manifest capabilities;
- documentation status drift.

Stage only explicit paths:

```powershell
git add -- path\to\file1 path\to\file2
git diff --cached --check
git diff --cached --stat
git diff --cached
```

Never use `git add .`, `git add -A`, or `git add --all` in this workflow.

## 10. Commit, push, PR, review, merge

These are separate publishing actions. Obtain the user's authorization for commit, push, PR creation, and merge.

After runtime PASS and final review:

1. stage only confirmed files;
2. run staged diff checks;
3. commit one coherent slice;
4. push its exact branch;
5. create one draft PR unless the user explicitly requests ready-for-review;
6. include scope, non-goals, privacy/data impact, architecture impact, tests, Windows runtime evidence, metrics, and known limits;
7. inspect the GitHub diff and all checks;
8. address review findings and rerun affected evidence;
9. merge only after authorization and approval;
10. fast-forward local `main`, verify exact merged SHA and clean status;
11. update `PROJECT_STATE.md` baseline when it was not already updated in the milestone PR.

An unmerged feature branch is not completed project state.

## 11. PR evidence minimum

Every functional PR body should answer:

- What changed and what intentionally did not?
- Which accepted invariant does it preserve or replace?
- Which privacy capabilities/data classes are touched?
- What owns each resource and how is it cancelled/disposed?
- What is bounded and what happens under pressure?
- Which tests passed?
- What exact Windows runtime scenario passed?
- What measurements are real?
- What remains unsupported?
- Does `PROJECT_STATE.md`, `ROADMAP.md`, or an ADR need updating?

Use `.github/PULL_REQUEST_TEMPLATE.md` as the checklist.

## 12. Documentation truth rules

- Use “implemented” only for merged code with evidence.
- Use “validated” only with the exact build/runtime/measurement source.
- Use “planned” for architecture without code.
- Separate target behavior from current gaps in the same section.
- Link PRs/commits instead of copying unverifiable chat claims.
- Keep exactly one next implementation gate in `PROJECT_STATE.md`.
- Replace the baseline SHA/date after every accepted merge.
- If documentation and code disagree, record the discrepancy before new feature work.

## 13. Safe failure behavior

Stop and investigate when:

- the working tree contains unknown changes;
- branch/HEAD differs from the expected base;
- a blocked context reaches a content API;
- stale work is published;
- logs contain content;
- queue capacity/overflow is undefined;
- teardown cannot remove a hook/session/subscription;
- a Windows provider hangs and recovery is not bounded;
- CI/runtime evidence is missing or contradictory;
- a dependency/model choice is being made without target-hardware validation.

Correctness and a trustworthy handoff are more important than advancing the milestone label.
