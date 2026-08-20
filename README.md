# Local AI Desktop Copilot

A privacy-first, fully local Windows desktop copilot that observes, understands, remembers, listens, and answers. It is being built as a product-grade system, not as a screenshot-to-LLM demo.

> [!IMPORTANT]
> The repository currently contains a validated sensing foundation and diagnostic UI. It does **not** yet contain UI Automation understanding, OCR, memory, model inference, voice, autonomous actions, or a production privacy-settings UI.

## Project status in 60 seconds

| Item | Current truth |
| --- | --- |
| Last verified functional code baseline | `c29099a1c96680229f82f7b6b400cf962e51b5cc` (later documentation-only commits may be descendants) |
| Baseline date | 2026-08-21 (Windows runtime acceptance) |
| Completed | Foreground context, RAM-only capture, privacy/epoch gate, low-resolution change detection, persistent latest-wins capture, sensing orchestration, diagnostic input correlation |
| Current implementation shape | One packaged WinUI 3 application with a diagnostic page; persistent capture/input correlation require explicit Arm and default OFF; foreground identity/title observation currently starts on page load |
| Next implementation milestone | `M2.4 Foundation Hardening`, starting with characterization tests and lifecycle separation |
| Next product capability after hardening | `M3 UI Understanding` through read-only Windows UI Automation |
| Automated tests / CI | Not present yet; this is an explicit M2.4 gap |
| Cloud use | Forbidden by the product architecture |
| Autonomous input/actions | Out of scope |

The detailed, evidence-backed state is in [Project State](docs/PROJECT_STATE.md). Do not infer implementation status from the target architecture or roadmap.

## Start here in a new AI or engineering session

Read these files in order before proposing code:

1. [AGENTS.md](AGENTS.md) — repository rules and source-of-truth order.
2. [Project State](docs/PROJECT_STATE.md) — what is implemented, verified, missing, and next.
3. [Architecture](docs/ARCHITECTURE.md) — current and target boundaries, data flow, threading, queues, and failure handling.
4. [Privacy Model](docs/PRIVACY_MODEL.md) — mandatory gates and data-lifetime rules.
5. [Roadmap](docs/ROADMAP.md) — milestone sequence and exit criteria.
6. [Engineering Workflow](docs/ENGINEERING_WORKFLOW.md) — build, diagnosis, runtime acceptance, and Git process.
7. [Architecture decisions](docs/decisions/README.md) — durable reasons behind non-obvious choices.

A new session should fetch the live repository and first report its branch, exact HEAD, working-tree status, last functional code commit, current milestone, next gate, and any mismatch between documentation and code. A commit cannot truthfully embed its own future squash SHA, so live Git is authoritative for current HEAD. Chat history is context, not the repository source of truth.

## Product goal

The final product should let a user ask a natural question such as “Why did this error appear?” without manually capturing or explaining the screen. The answer must be produced locally from the smallest useful context.

The system is intentionally hierarchical:

```mermaid
flowchart LR
    A[Foreground context] --> B[Privacy and context epoch]
    B --> C[Cheap visual change sensing]
    C --> D[Read-only UI Automation]
    D --> E[ROI OCR when needed]
    E --> F[Small VLM only when needed]
    F --> G[Structured events and short-term memory]
    G --> H[Local reasoning]
    H --> I[Text / local voice response]
```

UI Automation, OCR, and vision are escalation levels, not parallel always-on collectors. A direct user question may request a fresh bounded context snapshot, but it still passes through the same privacy and epoch gates.

## Fixed two-computer deployment target

The architecture is designed for the hardware already available. Hardware upgrades are not an architectural assumption.

| Node | Fixed hardware | Intended responsibility |
| --- | --- | --- |
| Windows client | Intel i7-6700K, 32 GB RAM, AMD Radeon R9 M395X | Foreground detection, privacy gates, Windows capture, cheap change detection, UI Automation, lightweight preprocessing, user interface |
| Local AI server | Lenovo LOQ 15IAX9, Intel i5-12450HX, 16 GB RAM, RTX 3050 Laptop GPU 6 GB, Windows 11 Pro | Local model runtimes, resource scheduling, text/VLM inference, and later STT/TTS |

The server is a local-LAN trust boundary, not “the cloud.” Nothing may cross from the client to the server without an explicit privacy capability, size/deadline limits, cancellation, and an authenticated local protocol. That protocol is not implemented yet.

## Non-negotiable invariants

- Priority order: **Correctness > Reliability > Privacy > Performance > Maintainability**.
- Target product sensing is explicit and defaults to OFF. Current M2 Arm already gates persistent capture/input, while complete Off semantics are an M2.4 requirement.
- Process identity is evaluated before title, pixels, UIA text, OCR, memory, or network access.
- A blocked context produces no title read, capture, UIA, OCR, memory, or network request.
- Every asynchronous result is bound to a context epoch and discarded if stale.
- Normal capture frames remain in RAM and are disposed promptly; screenshots are not written to disk.
- Diagnostic logs contain metadata, not raw titles, UIA/OCR text, keys, coordinates, audio, screenshots, or model prompts.
- Volatile state streams are bounded and latest-wins; no unbounded frame or event queues.
- UI Automation is read-only. Invoke, SetValue, selection changes, mouse control, keyboard automation, and autonomous actions are forbidden.
- OCR and VLM work only on demand or after a meaningful trigger; VLM is never the default sensing loop.
- Performance claims require measurements on the fixed target hardware.

See [Privacy Model](docs/PRIVACY_MODEL.md) for the exact current-versus-target distinction.

## What works today

The merged M2 sensing path is:

```text
WinEvent foreground hook
  -> HWND/PID/process identity
  -> privacy evaluation
  -> explicit Arm
  -> immutable ContextEpoch + cancellation
  -> 200 ms settle
  -> persistent Windows Graphics Capture
  -> capacity-one latest-frame ownership
  -> 640 px luminance sample every 500 ms
  -> Baseline / Insignificant / Meaningful / Large
  -> optional diagnostic correlation with mouse/keyboard activity kind
```

The activity tracker records only `MouseClick`, `MouseWheel`, or `KeyboardActivity` plus an epoch and monotonic timestamp. It does not record keys, text, mouse coordinates, clipboard data, or target controls.

Current stack: C#/.NET 10, packaged WinUI 3, Windows App SDK 2.4.0, Win2D 1.4.0, and Windows Graphics Capture. Only the explicit `Debug/win-x64` path has runtime acceptance. Model/OCR/STT/TTS backends are not selected yet; early candidate names are not commitments.

## Build and diagnostic entry points

From PowerShell 5.1 or newer on the Windows client:

```powershell
dotnet build .\src\LocalCopilot.App\LocalCopilot.App.csproj -c Debug -r win-x64
```

For an explicit diagnostic session:

```powershell
.\run-debug.ps1
```

`run-debug.ps1` builds, launches the app, collects a whitelisted diagnostic bundle, disables diagnostic mode during cleanup, and copies the final bundle to the clipboard. Its current `H:\DevCache\LocalCopilot` path and legacy `m1-3` filenames are known development-only debt scheduled for M2.4.

For the full acceptance and Git workflow, use [Engineering Workflow](docs/ENGINEERING_WORKFLOW.md).

## Repository layout

```text
.
├── AGENTS.md
├── README.md
├── run-debug.ps1
├── docs/
│   ├── ARCHITECTURE.md
│   ├── ENGINEERING_WORKFLOW.md
│   ├── PRIVACY_MODEL.md
│   ├── PROJECT_STATE.md
│   ├── ROADMAP.md
│   └── decisions/
└── src/
    └── LocalCopilot.App/
        ├── Diagnostics/
        ├── Services/
        ├── MainPage.xaml
        └── MainPage.xaml.cs
```

The single application project is the **current** structure. The multi-project/process boundaries in the architecture are targets to introduce only when their milestone requires them.

## License

[MIT](LICENSE)
