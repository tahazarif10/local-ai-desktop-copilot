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

The accepted M2.3 baseline `c29099a` had no automated tests. M2.4.1 added a portable characterization suite for the extracted core logic. Run it with:

```powershell
dotnet test .\tests\LocalCopilot.Core.Tests\LocalCopilot.Core.Tests.csproj -c Release --settings .\tests\LocalCopilot.Core.Tests\.runsettings
```

The accepted M3.1 baseline contains 91 deterministic tests for capability-based `PrivacyPolicy`, `ContextEpochManager`, `ChangeDetector`, `DiagnosticTimeline`, `ChangeCorrelationService`, the one-shot `ApplicationLifecycleGate`, launch-scoped `DiagnosticSession` parsing/logging, `InputHookHealthMonitor`, UIA native-result classification, stale/latest-request publication gating, and the capacity-one latest-pending slot. The runsettings file makes zero discovered tests a hard failure. The suite must remain free of WGC, live global hooks, live UI Automation providers, XAML, and a live desktop. A passing core suite does not replace the canonical Windows app build or milestone-specific physical provider evidence.

The M3.2 candidate adds portable tests for its immutable zero-string contract, exact budget boundaries, result-size accounting, topology validation, rectangle sanitation, conservative depth-boundary reporting, and stale-snapshot removal. Do not record a new total until the exact clean CI run reports it.

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
- rejects an elevated PowerShell host so the packaged app cannot accidentally inherit administrator integrity during privacy/security acceptance;
- creates a unique session directory under the repository-local ignored diagnostic root, or a caller-supplied root;
- records session ID, branch, SHA, .NET, PowerShell, OS, `Runner elevated: False`, and Git status;
- builds `Debug/win-x64 --warnaserror`;
- runs an independent metadata-only foreground probe;
- enables application diagnostics only through an expiring launch argument passed by the packaged-app run target and read from the desktop process command line;
- uses no persistent enable flag, so a later normal launch is Off even after abnormal termination;
- stops the probe before reading bundle sources;
- creates the final bundle only from the exact session's explicit three-file whitelist;
- copies the complete bundle to the clipboard.

Default output shape:

```text
<repository>\.localcopilot\diagnostics\<utc>-<session-id>\
  session-meta.txt
  app.log
  os-foreground.log
  diagnostic-bundle.txt
```

To select another root without changing product code:

```powershell
.\run-debug.ps1 -DiagnosticRoot "D:\LocalCopilotDiagnostics"
```

The app reads the argument vector with `Environment.GetCommandLineArgs()` because `Microsoft.UI.Xaml.LaunchActivatedEventArgs.Arguments` is always empty for WinUI desktop apps. It accepts the diagnostic token only when its schema, GUID, absolute session path, folder/session binding, creation time, expiry, and maximum lifetime validate. The runner treats a missing or mismatched `app.log` session marker as a failed activation handshake. Base64url is transport encoding, not encryption or authorization; explicit possession of the launch argument is the opt-in mechanism.

Each input-hook lifetime ends with one `INPUT.HOOK_HEALTH` record. It reports callback/activity counts, callback/subscriber errors, installing-thread mismatches, average/maximum callback duration, fixed latency buckets, and both unhook results. It contains no key, text, scan-code, coordinate, clipboard, or target-control data.

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

### M3.1 physical matrix

This matrix was accepted on 2026-08-23 at functional head `e48b067f1c13ee5ba211bcd36de663b30ca27246`. Keep it as the root-worker regression matrix when M3.2 expands UIA.

Run `run-debug.ps1`, Arm once, and keep the generated bundle open until all cases are complete. For each external target, focus the target, return to LocalCopilot, and use the root-probe command; own-process foreground transitions are excluded, so the coordinator retains the last external epoch.

Required cases:

1. **Classic Win32:** a non-elevated classic process such as a standalone PowerShell console or another known Win32 window returns `Available/RootResolved`.
2. **WinUI/packaged UI:** Calculator or another non-elevated packaged Windows UI returns `Available/RootResolved`.
3. **Browser:** the normal non-elevated Chrome window returns `Available/RootResolved`.
4. **Privacy deny:** diagnostic Notepad returns `Unavailable/CapabilityDenied` and produces no `UIA.QUEUE`/native probe for that epoch.
5. **Integrity deny:** the runner metadata must say `Runner elevated: False`. An explicitly elevated target returns `Unavailable/HigherIntegrity` or `Unavailable/AccessInspectionFailed`; `UIA.INTEGRITY_CHECK` must show the target RID greater than the current RID (or a failed inspection), and the manifest/process remains non-elevated and without `uiAccess`.
6. **Deterministic deadline/recovery:** on an allowed epoch, click `Force timeout, then retry normal probe`; first observe `Timeout/DeadlineExpired`, then immediately run the normal probe and observe `Available/RootResolved` on the same worker thread. This validates the request deadline/recovery path; it is not evidence that every hostile provider is interruptible.
7. **Latest-wins and rapid transitions:** on an allowed epoch, click `Exercise latest-wins/stale burst`. The diagnostic-only two-second hold makes one request active while two more are published: raw results must include `Cancelled/Superseded` for the replaced pending request; final results must convert both non-latest requests to `Stale/PublicationRejected`; the newest request must complete normally. Then switch rapidly among allowed, denied, and own windows and confirm no prior target result renders as current.
8. **Teardown:** click `Exercise latest-wins/stale burst` and close the app before its two-second hold completes. Require cancellation/worker-stop metadata, one `UIA.WORKER_STOP`, and `UIA.WORKER_DISPOSE` with `joined=True`, plus the existing clean sensing/input/foreground/coordinator teardown.

All successful native probes must use one nonzero worker managed-thread ID distinct from the UI log thread, and `UIA.WORKER_START` must report `apartment=MTA`. The bundle must contain no title text, UIA Name/Value/Text, control value, tree/property data, key/text value, coordinate, clipboard content, pixel payload, prompt, or response.

### M3.2 physical matrix

This matrix is pending. Run it only on the exact candidate SHA after Ubuntu/Windows core tests and the clean-restore strict Windows build pass.

Use one non-elevated `run-debug.ps1` session, Arm once, focus each external target before returning to LocalCopilot, and use `Capture bounded structural snapshot`. Record the complete aggregate status and matching `UIA.QUEUE`, `UIA.INTEGRITY_CHECK`, `UIA.REQUEST_COMPLETE`, and `UIA.PROBE_RESULT` records.

Required cases:

1. **Classic Win32, packaged UI, browser:** PowerShell (or another classic window), Calculator (or another packaged UI), and Chrome each return `Available/SnapshotCaptured` with `view=Control`, `nodes` in `0..256`, `propertyValues=nodes*27`, `stringCount=0`, `stringBytes=0`, `estimatedBytes<=32768`, and a nonnegative traversal time.
2. **Budget evidence:** use `Exercise depth-0 structural budget` on an allowed target and require one node, 27 property values, zero strings/bytes, the one-node estimate, and `DepthLimit`. This diagnostic command passes a smaller immutable budget through the same production worker API; it does not change product defaults or rely on a timing race.
3. **Recovery:** immediately after a truncated/timeout/unavailable case, the same allowed target returns another typed snapshot on the same worker thread.
4. **Privacy deny:** diagnostic Notepad returns `Unavailable/CapabilityDenied` and produces no queue/native snapshot work for its epoch.
5. **Integrity deny:** from the required non-elevated runner, an explicitly elevated target fails before UIA as `HigherIntegrity` or `AccessInspectionFailed`; do not add elevation or `uiAccess`.
6. **Stale disposal:** use `Exercise structural latest-wins/stale burst`. Its held first request must produce a raw structural snapshot, the middle pending request must be superseded, both non-latest publications must become `Stale/PublicationRejected` with `snapshot=none`, and only the newest structural result may publish aggregate counts.
7. **Backpressure and timeout regression:** rerun the accepted M3.1 forced-timeout/recovery and root latest-wins burst controls. One active/one-newest behavior, typed outcomes, and the non-UI MTA thread must remain unchanged after generated interop replaces the manual ABI.
8. **Teardown:** close during active worker work and require one worker stop plus `UIA.WORKER_DISPOSE started=True joined=True`, along with clean observer/capture/input/coordinator shutdown.
9. **Privacy scan:** the bundle may contain aggregate view/node/content/depth/property/string/estimated-byte/truncation/timing fields. It must contain no UIA Name/Value/Text, title text, bounding coordinate, control-type ID, per-node state, pattern detail, key/input value, clipboard, pixel payload, prompt, response, exception message, or stack.

CI success alone does not accept the generated COM projection. Record the exact functional SHA, provider/process matrix, worker thread ID, counters/timings, denial/stale/teardown evidence, and prohibited-content scan before changing ADR 0008 or `completed_through` to Accepted/M3.2.

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
