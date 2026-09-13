# Security Policy

## Supported code

Security and privacy fixes are applied to the current `main` branch. This project is still under active development and does not yet publish a stable production release line.

## Reporting a vulnerability

Please do **not** publish exploit details, sensitive screenshots, captured UI text, credentials, personal data, diagnostic bundles, or proof-of-concept material in a public issue.

Use GitHub's private vulnerability reporting / security-advisory flow for this repository when it is available. If that private channel is unavailable, open a minimal public issue that says only that you need a private security contact; do not include technical exploit details in the issue.

A useful private report includes:

- affected commit or branch
- affected capability or boundary
- concise reproduction steps
- expected vs actual behavior
- whether protected content can be read, retained, logged, or transferred unexpectedly
- whether the issue bypasses identity checks, capability gates, epoch invalidation, cancellation, integrity checks, queue bounds, or diagnostic redaction
- minimal proof needed to reproduce without unnecessary user data

## Privacy-sensitive classes

Treat the following as sensitive even when they are useful for debugging: raw window titles, screenshots/pixels, UI Automation or OCR text, keys, mouse coordinates, clipboard contents, audio, prompts, responses, credentials, and machine-specific diagnostic data.

Do not attach those classes to public reports.

## Scope

Particularly important findings include privacy-gate bypasses, stale-context publication, unauthorized UI Automation reads, diagnostic leakage, cross-process or local-network trust-boundary bypasses, resource-exhaustion paths that defeat boundedness, and unsafe behavior after cancellation or teardown.

This policy does not make a production-security claim for unimplemented roadmap components such as model inference, OCR, voice, memory, or the planned local-LAN AI protocol.
