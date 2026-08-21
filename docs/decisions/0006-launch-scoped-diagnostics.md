# ADR 0006: Launch-scoped diagnostic sessions

- Status: Accepted
- Date: 2026-08-21
- Supersedes: fixed-path diagnostic enable flag

## Context

The accepted M2 runner enabled diagnostics through a file at a developer-specific `H:` path and wrote milestone-named files into one shared directory. A fixed flag can survive an abnormal runner termination, a shared log can mix sessions, and exception messages can carry content discovered by future providers. Packaged WinUI launch also means ordinary child-process environment inheritance is not a sufficient activation contract. In WinUI desktop apps, `Microsoft.UI.Xaml.LaunchActivatedEventArgs.Arguments` is always empty; the supported process-command-line API must be used instead.

Diagnostics remain necessary for physical Windows acceptance, but they must be explicit, fail-closed, reproducible, and safe to paste as one bundle.

## Decision

`run-debug.ps1` creates a unique session directory beneath a repository-relative ignored root by default, with an optional caller-supplied root. It passes a bounded base64url JSON descriptor through the packaged-app `WinAppLaunchArgs` property. The app reads the real argument vector with `Environment.GetCommandLineArgs()` and accepts the descriptor only when schema, GUID, absolute path, directory/session binding, creation time, expiry, and maximum lifetime validate. This follows Microsoft's WinUI desktop guidance for command-line arguments: <https://learn.microsoft.com/windows/windows-app-sdk/api/winrt/microsoft.ui.xaml.launchactivatedeventargs.arguments>.

There is no persistent enable flag. The application initializes its diagnostic sink once at launch and writes the fixed `app.log` filename only inside the validated session. After the packaged process exits, the runner requires `app.log` to contain the expected session marker; a missing or mismatched marker makes the run fail. The runner stops its independent probe before creating a final bundle from the explicit `session-meta.txt`, `app.log`, and `os-foreground.log` whitelist.

Exception diagnostics contain type and HRESULT rather than provider message/stack text. Low-level input callbacks perform no file logging; one teardown summary records content-free health counters, latency buckets, thread consistency, and unhook results. Target-hardware acceptance measured the synchronous path as clean and sub-millisecond, so it remains until a reproducible regression justifies a dedicated hook thread or Raw Input.

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

- 68 portable tests cover token validation, expiry, path/session binding, one-shot initialization, line sanitization, exception redaction, and hook-health aggregation.
- [CI run #32500012379](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/32500012379) passed Ubuntu/Windows tests, Windows PowerShell 5.1 runner parsing, and the strict `Debug/win-x64 --warnaserror` build.
- Default and custom roots each produced one isolated session with a valid `app.log` handshake and exact three-source bundle; the custom root contained a space.
- A subsequent normal launch left 14 existing diagnostic files across both roots and the legacy target unchanged.
- Physical correlation observed MouseClick, MouseWheel, KeyboardActivity, and expired None.
- Four hook lifetimes processed 1,965 callbacks with zero callback/subscriber errors, zero installing-thread mismatches, successful keyboard/mouse unhook, a 92.8-microsecond weighted mean, and a 929.8-microsecond maximum.
- Privacy/lifecycle acceptance covered Off, deny/recovery, stale rejection, Disarm/Re-arm, and close while Armed without prohibited content or teardown failure.
