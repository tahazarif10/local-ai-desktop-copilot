<p align="center">
  <img src="docs/assets/project-mark.svg" width="104" alt="Local AI Desktop Copilot project mark" />
</p>

<h1 align="center">Local AI Desktop Copilot</h1>

<p align="center">
  <strong>A privacy-first Windows desktop copilot foundation built around bounded local context sensing, explicit privacy capabilities, deterministic verification, and a local-first architecture.</strong>
</p>

<p align="center">
  <img alt="Platform: Windows" src="https://img.shields.io/badge/Platform-Windows-0078D4?style=for-the-badge&amp;logo=windows11&amp;logoColor=white" />
  <img alt="Runtime: .NET 10" src="https://img.shields.io/badge/Runtime-.NET_10-512BD4?style=for-the-badge&amp;logo=dotnet&amp;logoColor=white" />
  <img alt="UI: WinUI 3" src="https://img.shields.io/badge/UI-WinUI_3-2563EB?style=for-the-badge" />
  <img alt="Current gate: M3.4" src="https://img.shields.io/badge/Current_Gate-M3.4-F59E0B?style=for-the-badge" />
  <a href="https://github.com/tahazarif10/local-ai-desktop-copilot/actions/workflows/ci.yml"><img alt="CI status" src="https://github.com/tahazarif10/local-ai-desktop-copilot/actions/workflows/ci.yml/badge.svg?branch=main" /></a>
</p>

<p align="center">
  <a href="docs/PROJECT_STATE.md">Project State</a> ·
  <a href="docs/ARCHITECTURE.md">Architecture</a> ·
  <a href="docs/PRIVACY_MODEL.md">Privacy Model</a> ·
  <a href="docs/ROADMAP.md">Roadmap</a> ·
  <a href="docs/ENGINEERING_WORKFLOW.md">Engineering Workflow</a> ·
  <a href="docs/decisions/README.md">ADRs</a>
</p>

<p align="center">
  <img src="docs/assets/readme-hero.svg" width="100%" alt="Windows sensing client connected through a protected local boundary to a separate local AI server" />
</p>

This repository is an engineering foundation, not a finished AI assistant. It focuses first on trustworthy sensing, bounded resource ownership, privacy gates, stale-result rejection, deterministic behavior, and verifiable failure handling.

> [!IMPORTANT]
> The accepted baseline includes foreground sensing, RAM-only capture, capability-based privacy and epochs, bounded change detection, application-owned lifecycle, UI Automation root probing, bounded non-text structure, separately authorized bounded semantic snapshots, and the M3.4.1 content-free orchestration admission policy. The M3.4.1 policy is intentionally **not wired to automatic runtime UIA**. Provider-isolation evidence is the next gate.

## Current status

| Area | Current truth |
| --- | --- |
| Active milestone | **M3.4 — Orchestrated UI enrichment and isolation decision** |
| Accepted M3.4 slice | **M3.4.1 portable orchestration admission** via [PR #19](https://github.com/tahazarif10/local-ai-desktop-copilot/pull/19) |
| Automated verification | **140/140 deterministic tests** on Ubuntu and Windows plus both PowerShell runner parses and strict Windows app build in [CI #49](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/33153232435) |
| Next gate | Provider-isolation evidence and explicit in-process vs restartable-helper decision |
| Runtime composition | Existing packaged WinUI process; automatic UIA enrichment remains blocked |
| Cloud path | Forbidden by the product architecture |
| Autonomous input/actions | Out of scope |

For exact accepted commits, evidence, and the live milestone state, use [docs/PROJECT_STATE.md](docs/PROJECT_STATE.md). That document is authoritative when README wording becomes stale.

## What exists today

The accepted product foundation provides:

- event-driven foreground observation and identity-first privacy evaluation
- explicit Arm/Disarm lifecycle ownership
- RAM-only Windows Graphics Capture with bounded latest-wins frame ownership
- low-resolution change detection and metadata-only diagnostic correlation
- capability-based privacy gates with epoch invalidation and stale-result rejection
- UI Automation root probing on a dedicated COM MTA worker
- bounded non-text Control View structure snapshots
- separately authorized, bounded, short-lived semantic UI snapshots
- a content-free orchestration admission policy with debounce, deduplication, priority, backpressure, and one-active/one-pending bounds
- deterministic tests across portable Core paths plus strict Windows build/runtime acceptance gates

Not implemented yet: **OCR, memory, model inference, voice, autonomous actions, production privacy settings UI, and automatic UIA orchestration**.

## Architecture at a glance

```text
Foreground / user context
        |
        v
 identity-first privacy gate
        |
        +--> bounded capture/change sensing
        |
        +--> separately authorized UI Automation
        |
        v
 epoch + capability publication gate
        |
        v
 short-lived local context
        |
        +--> planned authenticated local-LAN AI boundary
```

The target deployment uses a Windows sensing client and a separate local AI server on the same trusted LAN. The server path is architectural target work; the protocol and model runtime are not implemented yet.

## Non-negotiable engineering contracts

- **Correctness > Reliability > Privacy > Performance > Maintainability.**
- Sensing is explicit and defaults to OFF.
- Process identity is evaluated before title, pixels, UIA text, OCR, memory, or network access.
- Blocked contexts stop before protected content acquisition.
- Every asynchronous result is epoch-bound and rejected when stale.
- Normal capture frames stay in RAM and are disposed promptly.
- Diagnostics contain metadata, not raw titles, UIA/OCR text, keys, coordinates, audio, screenshots, prompts, or model responses.
- Volatile streams are bounded and latest-wins; no unbounded frame/event queues.
- UI Automation is read-only; Invoke/SetValue/input automation and autonomous actions are forbidden.
- Performance claims require measurements on the fixed target hardware.

See [Privacy Model](docs/PRIVACY_MODEL.md) for the complete capability and data-lifetime contract.

## Build and verification

From PowerShell 5.1 or newer on Windows:

```powershell
dotnet test .\tests\LocalCopilot.Core.Tests\LocalCopilot.Core.Tests.csproj -c Release --settings .\tests\LocalCopilot.Core.Tests\.runsettings
dotnet build .\src\LocalCopilot.App\LocalCopilot.App.csproj -c Debug -r win-x64
```

For an explicit diagnostic session:

```powershell
.\run-debug.ps1
```

Run diagnostics from a normal, non-Administrator PowerShell. The runner fails closed when elevated so the app does not inherit administrator integrity and invalidate the accepted security matrix.

For the full acceptance workflow, physical gates, and Git process, see [Engineering Workflow](docs/ENGINEERING_WORKFLOW.md).

## Documentation map

- [AGENTS.md](AGENTS.md) — repository rules and source-of-truth order
- [Project State](docs/PROJECT_STATE.md) — implemented, verified, missing, and next
- [Architecture](docs/ARCHITECTURE.md) — boundaries, data flow, threading, queues, failure handling
- [Privacy Model](docs/PRIVACY_MODEL.md) — mandatory capabilities and data-lifetime rules
- [Roadmap](docs/ROADMAP.md) — milestone sequence and exit criteria
- [Engineering Workflow](docs/ENGINEERING_WORKFLOW.md) — build, diagnosis, acceptance, and Git process
- [Architecture Decisions](docs/decisions/README.md) — accepted ADRs

## Repository layout

```text
.
├── .github/
├── AGENTS.md
├── README.md
├── run-debug.ps1
├── docs/
├── src/
│   ├── LocalCopilot.Core/
│   └── LocalCopilot.App/
└── tests/
    └── LocalCopilot.Core.Tests/
```

`LocalCopilot.App` and `LocalCopilot.Core` are separate assemblies but currently run in one desktop process. Larger process boundaries remain targets to introduce only when milestone evidence justifies them.

## Contributing and security

See [CONTRIBUTING.md](CONTRIBUTING.md) before proposing changes. Security and privacy-sensitive reports should follow [SECURITY.md](SECURITY.md) and should not disclose exploit details in a public issue.

## License

[MIT](LICENSE)
