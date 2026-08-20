# Privacy model

## 1. Status

Privacy is a control-plane boundary, not a filter applied after capture.

The current M2 implementation correctly evaluates process identity before title/capture and blocks capture for denied contexts. It is still a diagnostic foundation, not the final product privacy system. In particular:

- the foreground observer starts when `MainPage` loads, independently of the Arm button;
- an allowed window title is read and displayed even while auto capture sensing is OFF;
- the Arm button gates persistent WGC and diagnostic input tracking, not all foreground awareness;
- policy is a binary `AllowsSensing` decision;
- normal product configuration defaults to allow because no user blocklist/settings UI exists;
- Notepad is denied only as a deterministic fixture while diagnostic mode is enabled.

Therefore the accurate current claim is **“automatic capture sensing defaults OFF”**, not “all screen observation defaults OFF.” M2.4 must close this gap before semantic UI text is introduced.

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

- root traversal at the current foreground HWND;
- Control View by default, selective Content View;
- exclude own UI and desktop-wide traversal;
- bounded depth/node/string bytes/time;
- `IsOffscreen` is collected as a fact; off-screen text is not automatically included;
- structure and text are separate capabilities;
- property caching must request only required properties;
- raw UIA text is never logged;
- no action patterns or `uiAccess`/elevation;
- inaccessible/elevated/secure targets return a generic unavailable outcome.

## 11. OCR and vision privacy rules

- OCR requires `CapturePixels` and `RunOcr` for the same current epoch.
- OCR receives only an allowed ROI when possible.
- Sending OCR text to the server requires `SendTextToLocalServer`.
- VLM input requires `SendPixelsToLocalServer`; it should be an ROI, not a whole desktop by default.
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

Allowed diagnostic fields include event IDs, timestamps, thread ID, epoch, HWND/PID/process name where policy permits, rule ID, classification, dimensions, counts, durations, queue metrics, exception type/HRESULT, and a sanitized bounded reason.

Exception `.Message` values from content-bearing providers must be treated as potentially sensitive and sanitized before logging or bundling.

The bundle builder must use an explicit filename whitelist and diagnostic session boundary. The user reviews the bundle before public sharing.

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

M2.4 closes privacy gaps in this order:

1. characterize current privacy/epoch behavior with tests;
2. separate lifetime/composition from the page;
3. implement capability decisions and true Off/Paused semantics;
4. add user-configurable policy boundary and cancel-on-policy-change;
5. remove fixed diagnostic path/content-risky exception logging;
6. only then introduce UIA structure/text in M3.

See [ADR 0001](decisions/0001-privacy-before-content.md) and [ADR 0005](decisions/0005-foundation-hardening-before-uia.md).
