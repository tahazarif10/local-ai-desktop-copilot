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

The accepted M3.2 baseline contains 108 deterministic tests for capability-based `PrivacyPolicy`, `ContextEpochManager`, `ChangeDetector`, `DiagnosticTimeline`, `ChangeCorrelationService`, the one-shot `ApplicationLifecycleGate`, launch-scoped `DiagnosticSession` parsing/logging, `InputHookHealthMonitor`, UIA native-result classification, stale/latest-request publication gating, the capacity-one latest-pending slot, and the bounded structural snapshot contract. The runsettings file makes zero discovered tests a hard failure. The suite must remain free of WGC, live global hooks, live UI Automation providers, XAML, and a live desktop. A passing core suite does not replace the canonical Windows app build or milestone-specific physical provider evidence.

M3.2 added 17 portable tests for its immutable zero-string contract, exact budget boundaries, result-size accounting, topology validation, rectangle sanitation, conservative depth-boundary reporting, and stale-snapshot removal. [CI run #28](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32648315641) reported the accepted 108-test total on both operating systems.

Accepted M3.3 raises the suite to 124 tests covering capability separation, selected-node/exclusion policy, independent character/string/range/UTF-8/result/time/TTL limits, provenance/sensitivity, clear-on-dispose, and expired/revoked/stale publication. [CI run #43](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32894314154) passed all 124 tests on Ubuntu/Windows, parsed both PowerShell runners, and completed the strict app build. Its separate physical evidence is recorded below.

Accepted M3.4 slice 1 raises the suite to 140 tests with content-free orchestration admission, debounce, per-kind deduplication, question priority/backpressure, bounded invalidation, and shutdown coverage. [CI run #49](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/33153232435) passed all 140 tests on Ubuntu/Windows, parsed both PowerShell runners, and completed the strict app build. The slice is not runtime-composed, so it adds no physical UIA claim; provider-isolation measurement remains the next gate.

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

The ordinary command above intentionally grants structure without text. Only the M3.3 physical content matrix uses the extra, launch-scoped opt-in:

```powershell
.\run-debug.ps1 -EnableUiText
```

`-EnableUiText` is not persistent and does not authorize logging, retention, OCR, or transmission. It only adds `ReadUiText` to this validated expiring diagnostic launch; the exact Notepad deny fixture and every identity/integrity/epoch/publication gate still apply.

For the accepted one-command M3.3 regression and completion gate, run this from a normal, non-Administrator PowerShell:

```powershell
.\run-m3-3-acceptance.ps1
```

The wrapper delegates the strict build, launch token, and whitelisted bundle to `run-debug.ps1 -EnableUiText`. It creates controlled same-integrity and elevated WinForms fixtures, invokes only LocalCopilot's own diagnostic buttons from an external test harness, and automatically validates semantic latest-wins/clearing, the integrity gate, M3.1/M3.2 regressions, and held-work teardown. Windows still requires one operator UAC confirmation for the elevated fixture; the wrapper never approves or bypasses that prompt. It prints PASS only after one non-UI MTA remains reusable, the worker stops and joins exactly once, the runner completes cleanly, and randomized semantic sentinels are absent from the three whitelisted sources and final bundle. Accepted session `b0af762a-6949-463f-98bf-aa1a0956ea87` passed this route at clean head `1f2383a2224904e94062c23e44375d42fbe7e3bd`; it complements rather than replaces the provider and capability evidence in matrix items 1-6.

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

### One-command physical acceptance standard

Every future physical acceptance matrix must ship a PowerShell 5.1-compatible,
normal-user wrapper before asking the operator to run the matrix. The wrapper
must:

- preflight the exact branch/HEAD and require a clean working tree;
- delegate the canonical strict build and validated diagnostic activation path;
- create controlled fixtures where deterministic provider behavior is required;
- drive only the product's explicit diagnostic controls and never mutate target
  application state;
- assert every expected outcome, privacy-negative path, recovery, stale/latest
  behavior, regression, and teardown condition;
- fail closed on a missing or contradictory record and print per-control plus
  overall `PASS/FAIL`;
- stop all helper/app processes and produce only the approved whitelisted
  evidence bundle;
- use randomized exact sentinels and scan every whitelisted source plus the
  final bundle for prohibited content; and
- be parsed by Windows PowerShell in CI before physical use.

Only an unavoidable OS security boundary such as UAC may require operator
confirmation. A provider case that cannot be automated must be explicitly
identified, justified, and retained as separate evidence; it must never be
silently skipped or represented as automated.

### M3.1 physical matrix

This matrix was accepted on 2026-08-23 at functional head `e48b067f1c13ee5ba211bcd36de663b30ca27246`. Keep it as the root-worker regression matrix when later UIA milestones expand semantics.

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

This matrix was accepted on 2026-08-23 at functional head `e1a50741580379f0f65c80e212f04c449e5a8c9b`. Keep it as the structural-provider, budget, privacy, stale, and teardown regression matrix for M3.3 and M3.4.

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

Acceptance record: [CI run #28](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32648315641) passed 108 tests on Ubuntu/Windows, PowerShell runner parsing, and the strict Windows build. Full session `22e567be-059d-4d19-bb1f-55b60a7a8646` passed the complete matrix at the functional SHA; short session `fd287eb1-c062-423f-881a-4f4c3ca1b0a7` confirmed the corrected `Milestone: M3.2` metadata at diagnostic-label-only descendant `26770254618189d693ad9553d92bcba896a8b81b`. Detailed measured provider counts/timings and privacy evidence are retained in `PROJECT_STATE.md`. CI success alone remains insufficient for future generated COM or content-bearing changes.

### M3.3 physical matrix

This matrix was accepted on 2026-08-25 for PR #18. Provider/capability cases used non-elevated sessions, and the remaining controls used the one-command wrapper at exact clean head `1f2383a2224904e94062c23e44375d42fbe7e3bd`. Keep it as the semantic privacy/budget/stale/regression/teardown matrix for M3.4.

1. **Ordinary diagnostic denial:** run `run-debug.ps1` without `-EnableUiText`, Arm, retain an allowed external epoch, and click the semantic command. Require `Unavailable/CapabilityDenied` with no semantic `UIA.QUEUE` or native content work.
2. **Explicit opt-in and deny precedence:** start a fresh `run-debug.ps1 -EnableUiText` session. Confirm `UI text opt-in: True`. Diagnostic Notepad must still return `Unavailable/CapabilityDenied` before queueing.
3. **Classic, packaged, and browser sources:** on PowerShell/another classic window, Calculator/another packaged UI, and Chrome, run the normal semantic command. Require `Available/SnapshotCaptured`, `content=redacted`, aggregate Name/Value/visible-Text counts, and no raw string. Across the matrix, exercise at least one nonzero Name, Value, and visible-Text count.
4. **Eligibility evidence:** exercise a target with a visible password control and a target/tree containing off-screen controls. Require nonzero aggregate `excludedPassword` and `excludedOffscreen` where the provider exposes those facts; no excluded node may contribute a selected string. Deterministic selector/snapshot tests remain the source of exact functional proof when a provider does not expose the relevant property.
5. **Default budgets:** every normal result must report selected nodes `<=32`, strings `<=64`, per-string cap 1,024, visible ranges `<=32`, retained UTF-8 `<=16384`, semantic elapsed/truncation against 800 ms, estimated semantic result `<=24576`, and TTL 5,000 ms.
6. **Tiny-budget path:** click `Exercise tiny semantic budgets`. Require selected nodes `<=1`, strings `<=1`, per-string cap 4, visible ranges `<=1`, UTF-8 `<=8`, estimated result within the reported tiny budget, typed truncation when the provider exceeds a boundary, and immediate normal-request recovery on the same MTA worker.
7. **Latest-wins and clearing:** click `Exercise semantic latest-wins/clear-on-stale burst`. Require one active/one newest pending behavior, middle `Cancelled/Superseded`, non-latest `Stale/PublicationRejected`, only the newest semantic aggregate published, and `UIA.SEMANTIC_DISPOSE ... disposed=True` for stale and consumer-complete paths.
8. **Integrity, timeout, and M3.1/M3.2 regressions:** an elevated target must fail before UIA; rerun root forced-timeout/recovery, root latest-wins, structural normal/depth-budget/latest-wins, and verify the same non-UI MTA thread remains usable.
9. **Teardown:** close during the held semantic burst. Require cancelled worker results, one `UIA.WORKER_STOP`, `UIA.WORKER_DISPOSE ... joined=True`, and clean existing sensing/input/observer/coordinator teardown.
10. **Prohibited-content scan:** place unique known sentinel strings in the exercised Name/Value/Text/password sources, then scan all three whitelisted sources and the final bundle for those exact sentinels plus titles, keys, coordinates, clipboard, pixels, prompts, responses, exception messages, and stacks. Only aggregate counts/budgets/truncation/timing and `content=redacted` are allowed.

Acceptance record: functional head `3dccdbc24fc60093f46f903dec4f7ca04c08dc14` and [CI #43](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32894314154) passed 124/124 tests on Ubuntu/Windows, both PowerShell runner parses, and the strict win-x64 build. Earlier provider sessions passed ordinary denial, opt-in/Notepad precedence, classic/packaged/browser sources, default/tiny budgets, recovery, and redaction; Chrome's non-exposure of `IsPassword` is recorded as provider-inapplicable with deterministic tests supplying exact exclusion proof. One-command session `b0af762a-6949-463f-98bf-aa1a0956ea87` passed semantic latest-wins clearing, higher-integrity denial, M3.1/M3.2 regressions, held-work joined teardown, and randomized prohibited-content scanning. ADR 0009 is accepted.

### M4.2.1 OCR benchmark preflight

Before installing an OCR dependency or running content-bearing benchmark
fixtures, execute the target-machine preflight from a clean, non-elevated
Windows PowerShell on the feature branch:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-preflight.ps1 -MachineRole Client
```

For the fixed AI server, use the same repository head and:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-preflight.ps1 -MachineRole Server
```

The preflight must remain content-free. It may report hardware, OS/runtime
versions, installed Windows OCR language tags, Tesseract version plus
`eng`/`fas` availability, Python/Paddle versions/device metadata, and
NVIDIA model/driver/VRAM metadata. It must not capture pixels, run OCR, install
language packs/packages/models, include usernames or arbitrary local paths, or
make a backend selection.

CI validates the runner with:

```powershell
.\run-m4-2-ocr-preflight.ps1 -ValidateOnly
```

Copy only the generated preflight summary into review/chat. The output is stored
under ignored `.localcopilot/benchmarks`.

The controlled M4.2.2 benchmark is a separate content-bearing step. Its images,
ground truth, and OCR output remain local. PR evidence may contain only aggregate
CER/WER/exact-match, latency, resource, footprint, failure, and cancellation
measurements with sample/category identifiers.

### M4.2.2 isolated OCR benchmark environment

After M4.2.1 is merged, prepare the first server GPU candidate from a clean,
non-elevated PowerShell on `dev/m4-2-2-controlled-benchmark`:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-benchmark-setup.ps1 -MachineRole Server -PreparePaddleGpu -BenchmarkRoot D:\LocalAI-Prerequisites
```

The setup is allowed to download benchmark-only dependencies. It must not
replace or modify the existing Python 3.14 installation. Python 3.12 may be placed below ignored `.localcopilot/ocr-benchmark`, or outside the repository through `-BenchmarkRoot`. On the fixed server use `D:\LocalAI-Prerequisites`. Prefer Python Install Manager `py install --target`; when only the legacy launcher is present, use the script's project-local CPython NuGet package fallback (`python` 3.12.10). The NuGet client and extracted runtime stay inside the repository-local benchmark directory and do not install a runtime under `%LocalAppData%` or `Program Files`. The pinned first candidate is PaddlePaddle GPU 3.2.0
from the CUDA 12.6 wheel index plus PaddleOCR 3.7.0.

CI validates only the static/setup contract:

```powershell
.\run-m4-2-ocr-benchmark-setup.ps1 -ValidateOnly
```

Physical setup evidence must report isolated Python 3.12, pinned package
versions, `paddle_compiled_with_cuda=True`, a GPU device, and the negative
content flags `ocr_executed=False`, `benchmark_content_created=False`, and
`backend_selected=False`.

The later content-bearing OCR benchmark remains local. Screenshots, ground truth,
and raw OCR output must not be copied into PRs, diagnostics, or committed files.

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
