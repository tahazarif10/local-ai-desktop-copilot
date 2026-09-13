# Contributing

Thanks for helping improve Local AI Desktop Copilot. This repository treats privacy, failure handling, bounded resource ownership, and reproducible verification as product requirements rather than optional polish.

## Start with the source of truth

Before changing code, read these in order:

1. `AGENTS.md`
2. `docs/PROJECT_STATE.md`
3. `docs/ARCHITECTURE.md`
4. `docs/PRIVACY_MODEL.md`
5. `docs/ROADMAP.md`
6. `docs/ENGINEERING_WORKFLOW.md`
7. relevant ADRs under `docs/decisions/`

Do not infer current implementation state from roadmap or target-architecture language.

## Change discipline

Keep each pull request narrow enough to review and verify independently. Preserve the existing separation between portable Core logic and Windows-specific runtime behavior. Do not weaken privacy gates, queue bounds, cancellation, epoch checks, stale-result rejection, or diagnostic redaction to simplify an implementation.

New sensing or semantic access must have an explicit capability, bounded lifetime, bounded size/work, cancellation behavior, stale-result policy, and tests for denied/error paths before runtime integration.

## Verification

Portable Core changes should pass:

```powershell
dotnet test .\tests\LocalCopilot.Core.Tests\LocalCopilot.Core.Tests.csproj -c Release --settings .\tests\LocalCopilot.Core.Tests\.runsettings
```

Windows app changes should also pass the canonical build:

```powershell
dotnet build .\src\LocalCopilot.App\LocalCopilot.App.csproj -c Debug -r win-x64 --warnaserror
```

When a milestone requires physical Windows/provider evidence, follow `docs/ENGINEERING_WORKFLOW.md`; portable tests are not a substitute for COM/provider/runtime acceptance.

## Pull requests

A good PR should state:

- the exact problem and milestone/gate it addresses
- what behavior changes and what intentionally does not
- privacy/security impact
- resource and failure-mode implications
- tests and runtime evidence performed
- known limits and follow-up work

Update `docs/PROJECT_STATE.md` when accepted implementation status, evidence, constraints, or the next gate changes.

## Privacy and diagnostics

Never commit screenshots, raw window titles, UIA/OCR text, key values, coordinates, clipboard data, audio, prompts, model responses, machine-specific private diagnostics, credentials, or secrets.

If a proposed change needs any new retained or transferred data class, stop and update the privacy model/ADR first.

## Security reports

Do not open a public issue containing exploit details or sensitive user data. Follow `SECURITY.md` instead.
