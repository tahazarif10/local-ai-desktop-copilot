# Privacy model

## 1. Status

Privacy is a control-plane boundary, not a filter applied after capture.

The accepted M2.4.3 implementation evaluates process identity before content, keeps all target observation Off until explicit Arm, and uses independent typed capabilities rather than a single sensing Boolean. The foreground hook, identity lookup, title read, WGC, and input correlation all stop on Disarm. Every current capture entry point requires `CapturePixels` and revalidates HWND/PID immediately before WGC creation. M2.4.4 adds validated launch-scoped diagnostics, isolated whitelisted bundles, exception type/HRESULT redaction, and measured content-free input-hook health; a normal launch has no diagnostic sink. Accepted M3.1 applies the same pattern to root-only UIA, accepted M3.2 preserves it for a bounded non-text Control View snapshot, and accepted M3.3 adds an independent `ReadUiText` gate before queueing and publication. HWND/PID and integrity are revalidated on the MTA worker immediately before every UIA path.

The policy configuration boundary supports emergency deny, normalized exact-application rules, global grants, strict precedence, immutable revisioned snapshots, and change notification. Product defaults grant only Armed ephemeral identity/title/pixel work. An ordinary launch-scoped diagnostic configuration separately adds derived-event retention and `ReadUiStructure` but still denies `ReadUiText`. Accepted M3.3 can add `ReadUiText` only when the same validated, expiring diagnostic launch also carries the explicit `-EnableUiText` opt-in; the exact Notepad deny fixture still wins. OCR, microphone, and local-server transmission capabilities remain denied.

The accepted product is still a diagnostic foundation: there is no user-facing policy editor, persisted rule store, pause control, automatic semantic orchestration, or server transport. M3.2 structure remains text-free. M3.3 accepts only bounded, short-lived Name/advertised Value/visible Text reads behind the separate launch-scoped capability; content is neither rendered nor persisted, logged, or transmitted.

## 2. Privacy goals

The target system must provide:

- explicit global activation, pause, and microphone mute;
- identity-before-content evaluation;
- per-application policy;
- separate authorization for different data operations;
- epoch-scoped cancellation and stale-result rejection;
- RAM-only raw pixels during normal operation;
- bounded derived data and short retention;
- no raw content in logs or diagnostic bundles;
- an explicit egress decision for the separate local AI server;
- fail-closed behavior when policy, identity, or capability is uncertain;
- visible degraded/blocked state rather than silent permission widening.

## 3. Activation states

Target product states:

| State | Allowed work |
| --- | --- |
| `Off` | Application UI and self-health only. No target-window hook/identity/title/pixels/UIA/OCR/memory/server transmission. |
| `Armed` | Foreground identity may be read to evaluate policy. Each later operation requires its own capability. |
| `Paused` | Cancel active epoch, stop sources, dispose transient content, retain only user-authorized memory. No new target-window reads. |
| `ShuttingDown` | Reject new work, cancel and dispose in deterministic order. |

Microphone activation/mute is independent. Screen `Armed` must not imply microphone permission, and microphone listening must not imply screen content permission.

A future explicit user question can request a high-priority snapshot while Armed. It is not a bypass for Off, Paused, per-app deny, or server-egress policy.

## 4. Bootstrap identity

Policy needs a minimum identity before it can decide whether content is accessible. The bootstrap set is intentionally small:

- HWND;
- PID;
- normalized process name;
- optional executable identity only if a later policy milestone proves it necessary and safe.

Do not read title, UIA properties, pixels, clipboard, command line, document path, or other content during bootstrap.

Immediately before the first content-bearing operation, revalidate that the HWND still belongs to the expected PID. This is already implemented before the title read and remains required for UIA and capture.

## 5. Capability decision

The target policy decision must express operations independently. The exact code representation is selected in M2.4, but it must cover at least:

| Capability | Authorizes | Does not implicitly authorize |
| --- | --- | --- |
| `ObserveIdentity` | HWND/PID/process bootstrap while Armed | Title, pixels, UIA, retention, network |
| `ReadWindowTitle` | Current allowed window title in RAM/UI | Logging, retention, server transfer |
| `CapturePixels` | Ephemeral client RAM capture and cheap processing | Screenshot files, OCR, vision, transmission |
| `ReadUiStructure` | Read-only control types, hierarchy, states, bounds, pattern availability | UIA text/value or any action pattern |
| `ReadUiText` | Policy-scoped UIA Name/Value/Text in RAM | Logging, retention, OCR, transmission |
| `RunOcr` | OCR on an allowed bounded pixel region | Full-screen OCR, retention, server transfer |
| `RetainDerivedEvent` | Store bounded derived facts until TTL | Raw frames/audio or indefinite memory |
| `SendTextToLocalServer` | Bounded selected text/events over authenticated LAN | Pixels, audio, unrelated memory |
| `SendPixelsToLocalServer` | Bounded ROI for a required VLM request | Full desktop, file persistence, future requests |
| `CaptureMicrophone` | Local microphone frames for the voice pipeline | Screen data or network transfer |
| `SendAudioToLocalServer` | Bounded activated utterance to local STT | Ambient retention, screen data, cloud transfer |

One capability must never be inferred from another. A call site states the capability it needs and receives a typed decision with rule ID/reason.

## 6. Rule precedence and defaults

Decision precedence is strict:

```text
Off / Paused / ShuttingDown
  > emergency global deny
  > exact application deny
  > exact application capability overrides
  > global feature grants
  > safe default
```

Target default behavior:

- global screen state starts Off;
- after explicit Arm, bootstrap identity is allowed solely to evaluate per-app policy;
- exact denied applications receive no content operations;
- ephemeral client-side capabilities may follow an explicit global feature grant;
- retention, microphone, text/pixel/audio LAN transfer, and diagnostics require separate visible grants;
- a missing, corrupt, unreadable, or unknown policy decision fails closed for content.

Changing any rule that affects the current context advances/cancels its epoch before the new decision is used.

## 7. Denied-context behavior

For a denied context the system must:

1. avoid title access;
2. avoid creating a WGC capture item/session;
3. avoid UIA root resolution or traversal;
4. avoid OCR/VLM work;
5. stop input correlation for that epoch;
6. avoid creating or retaining derived events;
7. avoid local-server requests;
8. dispose/cancel prior-context transient artifacts;
9. show only a generic blocked status;
10. log metadata-only rule ID/reason.

Denial must not reveal the blocked window's title or content in UI errors, exception strings, metrics, or logs.

## 8. Data classification and lifetime

| Class | Examples | Default lifetime |
| --- | --- | --- |
| Bootstrap metadata | HWND, PID, process name, rule ID | Active epoch plus bounded diagnostics |
| Raw visual content | WGC frame, BGRA/luma buffer, ROI | RAM only; dispose after use/replacement/cancellation |
| UI semantic content | UIA Name/Value/Text, OCR text | RAM, active operation/epoch unless derived retention is allowed |
| Derived event | “dialog appeared,” normalized error fact, confidence/provenance | Bounded short-term memory, target five-minute TTL |
| Model context | Selected text/events/ROI/request | Request lifetime; server discards after response unless an explicit future policy says otherwise |
| Audio | microphone frames/activated utterance | Ring buffer/utterance lifetime only; no ambient archive |
| Diagnostic metadata | timings, counts, classifications, rule IDs, error types | Opt-in session file with whitelist and user-controlled sharing |

Clear sensitive arrays when practical and beneficial, but deterministic ownership/disposal and removal of references are mandatory. Do not retain a large raw buffer merely to clear it later on a different path.

## 9. Epoch and publication rule

Every content-producing operation follows this sequence:

```text
capture immutable epoch + required capability
  -> revalidate identity
  -> perform bounded work with deadline/cancellation
  -> verify epoch still current
  -> verify capability still valid
  -> publish / retain / transmit only if both checks pass
  -> otherwise dispose and record metadata-only stale outcome
```

Cancellation is advisory for APIs that cannot be interrupted. The publication gate is mandatory even after cancellation has been requested.

## 10. UI Automation privacy rules

UIA can expose structured text beyond what a naive screenshot pipeline might expect, including off-screen controls. Therefore:

- the M3.1 probe resolves and immediately releases only the foreground root interface pointer; it requests no property, child, cache, pattern, Name, Value, or Text data;
- accepted M3.2 requests only 27 enumerated non-text Boolean/numeric properties through an Element-scope cache, walks only foreground-HWND Control View breadth-first, and represents Content membership with cached `IsContentElement`; it never uses Raw View or a desktop/descendant-wide query;
- its immutable defaults are 256 nodes, depth 8, 1,200 ms traversal, 6,912 total property values, zero strings and zero string bytes, and 32 KiB estimated result; a boundary produces truncation flags rather than widening collection;
- product-default policy denies the probe; ordinary diagnostics grant structure only, while semantic text additionally requires the validated launch's explicit `-EnableUiText` bit;
- at most one request executes and one newest request waits; a context/policy change cancels the epoch and the publication gate converts late completion to `Stale`;
- target integrity above the client or an unreadable target token fails closed before UIA as `Unavailable`; `uiAccess`, elevation, and secure-desktop access remain forbidden;
- root traversal at the current foreground HWND only;
- Control View traversal with cached Content membership; no Raw View;
- exclude own UI and desktop-wide traversal;
- bounded depth/node/string bytes/time;
- accepted M3.3 selects only cached `IsContentElement=true`, `IsOffscreen=false`, `IsPassword=false` nodes before any content read;
- structure and text are separate capabilities;
- property caching remains exactly the 27 M3.2 non-text properties; the separately authorized semantic phase may request Name, advertised ValuePattern value/read-only state, and advertised TextPattern visible ranges only for selected nodes, while AutomationId, ClassName, HelpText, DocumentRange, and all other strings remain forbidden;
- accepted M3.3 defaults are 32 selected nodes, 64 strings, 1,024 UTF-16 code units per string, 32 visible ranges, 16 KiB retained UTF-8, 800 ms, 24 KiB estimated result, and a five-second TTL;
- unmanaged Value/Text BSTRs are copied then freed immediately; retained content uses clearable character buffers and is disposed on consumer completion, expiry, stale/latest rejection, or capability loss;
- raw UIA text is never logged;
- bounding rectangles, control-type IDs, per-node states, and pattern-availability details are never logged; only aggregate counts, budgets, truncation, sizes, and timings may leave the snapshot result for diagnostics;
- no action patterns or `uiAccess`/elevation;
- inaccessible/elevated/secure targets return a generic unavailable outcome.

## 11. OCR and vision privacy rules

- OCR requires `CapturePixels` and `RunOcr` for the same current epoch.
- OCR receives only an allowed ROI when possible.
- M4.1 ROI planning is geometry-only and grants no content capability by itself. A planned ROI is not permission to crop pixels, run OCR, retain text, or transmit pixels.
- UIA bounds may be converted to frame-local ROI coordinates only through an explicit capture screen projection. Missing/invalid projection must fail to no UIA-derived ROI rather than guessing DPI, window origin, or border offsets.
- The M4.1 planner must remain bounded by region count and area budgets and must not synthesize a full-frame fallback. Oversized candidates are rejected, not silently widened or arbitrarily cropped.
- ROI coordinates and UIA bounds remain short-lived RAM metadata and are prohibited from diagnostics; only aggregate candidate/rejection/budget counts may be logged.
- Sending OCR text to the server requires `SendTextToLocalServer`.
- VLM input requires `SendPixelsToLocalServer`; it should be an ROI, not a whole desktop by default.
- A future full-frame visual request, if supported, requires a separate explicit user-request policy path and is not implied by an empty ROI plan.
- A local model server must not retain requests, screenshots, prompts, or responses by default.
- Model/runtime debug logging must be configured independently so third-party runtimes cannot silently write prompts or images.

## 12. Local-server boundary

The client must enforce policy before serialization. The server must also enforce protocol limits; defense is not delegated to one side.

Required protocol properties before first content transfer:

- authenticated encryption;
- allowlisted endpoint and no internet fallback;
- versioned schema and capability negotiation;
- request/epoch IDs;
- deadlines and cancellation;
- byte/dimension/token/audio-duration caps;
- server-side no-retention default;
- metadata-only audit of request type, sizes, timings, and outcome;
- visible failure/degraded status.

“Local LAN” is not a reason to use unauthenticated plaintext.

## 13. Diagnostics and sharing

Never write these to application logs or diagnostic bundles:

- window title text;
- UIA/OCR text or control values;
- keys or keyboard scan codes;
- mouse coordinates or clicked target;
- clipboard data;
- screenshot/frame/ROI pixels;
- microphone audio or transcription;
- model prompt/context/response;
- secrets, document paths, or command lines discovered incidentally.

Allowed diagnostic fields include event IDs, timestamps, thread ID, epoch, HWND/PID/process name where policy permits, rule ID, numeric process-integrity RIDs, classification, dimensions, counts, durations, queue metrics, exception type/HRESULT, and a sanitized bounded reason.

Exception `.Message` values from content-bearing providers must be treated as potentially sensitive and sanitized before logging or bundling.

The bundle builder must use an explicit filename whitelist and diagnostic session boundary. The user reviews the bundle before public sharing.

Diagnostic activation is launch-scoped: `run-debug.ps1` passes one validated, expiring token to the packaged app, the desktop process reads its actual argument vector, and the app binds its fixed `app.log` filename to that token's unique session directory. There is no persistent enable flag. The final bundle includes only `session-meta.txt`, `app.log`, and `os-foreground.log` from the same session. Base64url only makes the launch descriptor command-line safe; it is not encryption and contains no captured content.

Application exception diagnostics use exception type and HRESULT, never `.Message` or stack text from a content-bearing provider. Input-hook health records contain counts, durations, thread consistency, and teardown results only.

## 14. Persistence and deletion

- Raw frames and audio are never persisted during normal operation.
- MVP short-term memory begins in RAM.
- Product policy storage may persist rules, never captured content.
- A “Clear memory” command must synchronously make retained events unavailable and asynchronously perform any safe cleanup required by the selected store.
- Long-term memory requires a separate ADR, encryption/threat model, retention UI, export/delete behavior, and explicit opt-in.

## 15. Privacy acceptance checklist

A content-bearing milestone cannot pass unless tests/runtime evidence show:

- Off and Paused prevent the operation;
- exact denied context stops before the first content API;
- capability A does not grant capability B;
- HWND/PID is revalidated;
- policy/context change cancels work and stale results are not published;
- raw artifacts are disposed and queues remain bounded;
- logs/bundles contain no prohibited content;
- server transmission is absent or explicitly authorized and bounded;
- teardown removes hooks/sessions/subscriptions;
- error/unavailable paths do not reveal content.

## 16. Current remediation sequence

Foundation hardening and read-only UIA advance in this order:

1. ✅ characterize current privacy/epoch behavior with tests (M2.4.1);
2. ✅ separate lifetime/composition from the page (M2.4.2);
3. ✅ implement capability decisions, a product policy configuration boundary, true Off semantics, and cancel-on-policy-change (M2.4.3);
4. ✅ remove fixed diagnostic paths/content-risky exception logging and measure input-hook hardening (M2.4.4);
5. ✅ validate the capability-gated, root-only MTA worker probe in M3.1; UIA text remains a later, separately authorized slice;
6. ✅ validate bounded non-text structure behind the same gates in M3.2; ADR 0008 is accepted after CI and physical provider/privacy/budget/stale/teardown evidence passed;
7. ✅ validate M3.3's separately authorized `ReadUiText` path on classic/packaged/browser providers, privacy denial, password/off-screen eligibility, stale disposal, higher-integrity denial, prior UIA regressions, held-work teardown, and prohibited-content scanning; PR #18 and accepted ADR 0009 record the boundary.
8. ▶ define M3.4's content-free, capability/epoch-gated trigger policy without
   runtime UIA wiring; then measure controlled provider hangs and decide the
   isolation boundary before automatic semantic reads are eligible.

See [ADR 0001](decisions/0001-privacy-before-content.md), [ADR 0005](decisions/0005-foundation-hardening-before-uia.md), accepted [ADR 0007](decisions/0007-root-only-uia-mta-probe.md), accepted [ADR 0008](decisions/0008-generated-bounded-uia-snapshot.md), accepted [ADR 0009](decisions/0009-capability-gated-semantic-uia-snapshot.md), and accepted [ADR 0010](decisions/0010-bounded-priority-ui-enrichment-policy.md).
