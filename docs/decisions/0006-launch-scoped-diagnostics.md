# ADR 0006: Launch-scoped diagnostic sessions

- Status: Accepted
- Date: 2026-08-21
- Supersedes: fixed-path diagnostic enable flag

## Context

The accepted M2 runner enabled diagnostics through a file at a developer-specific `H:` path and wrote milestone-named files into one shared directory. A fixed flag can survive an abnormal runner termination, a shared log can mix sessions, and exception messages can carry content discovered by future providers. Packaged WinUI launch also means ordinary child-process environment inheritance is not a sufficient activation contract.

Diagnostics remain necessary for physical Windows acceptance, but they must be explicit, fail-closed, reproducible, and safe to paste as one bundle.

## Decision

`run-debug.ps1` creates a unique session directory beneath a repository-relative ignored root by default, with an optional caller-supplied root. It passes a bounded base64url JSON descriptor through the packaged-app `WinAppLaunchArgs` property. The app accepts it only when schema, GUID, absolute path, directory/session binding, creation time, expiry, and maximum lifetime validate.

There is no persistent enable flag. The application initializes its diagnostic sink once at launch and writes the fixed `app.log` filename only inside the validated session. The runner stops its independent probe before creating a final bundle from the explicit `session-meta.txt`, `app.log`, and `os-foreground.log` whitelist.

Exception diagnostics contain type and HRESULT rather than provider message/stack text. Low-level input callbacks perform no file logging; one teardown summary records content-free health counters, latency buckets, thread consistency, and unhook results. The current synchronous hook path remains until target-hardware evidence justifies a dedicated hook thread or Raw Input.

The descriptor encoding is transport-safe, not secret or encrypted. Supplying the launch option is deliberate developer opt-in, not an end-user authorization system.

## Consequences

- Normal launches cannot inherit a stale file-based diagnostic state.
- Sessions cannot overwrite or silently mix one another.
- A custom diagnostic drive/folder requires no code change.
- Bundle membership is deterministic and reviewable.
- Future event fields still require source-level privacy review; a whitelist cannot make unsafe content safe.
- Session directories persist until the developer removes them; automatic retention/deletion is intentionally not introduced here.
- The low-level hook implementation is not replaced speculatively.

## Verification

- Portable tests cover token validation, expiry, path/session binding, one-shot initialization, line sanitization, and hook-health aggregation.
- Windows PowerShell parses the runner in CI.
- Windows CI runs the portable tests and strict `Debug/win-x64` build.
- Physical acceptance confirms `app.log` appears in the selected session, a normal launch produces no diagnostic file, the three-file bundle is copied, hook metrics contain no content, all expected activity kinds correlate, and hook teardown succeeds.
