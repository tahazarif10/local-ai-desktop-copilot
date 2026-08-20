# ADR 0005: Foundation hardening before UI Automation

- Status: Accepted
- Date: 2026-08-21
- Supersedes: direct progression from M2.3.1 to M3.1

## Context

The M2.3 audit confirmed the sensing path is stable, but also found four risks that would become harder to fix after adding UIA text:

1. no automated tests/CI for pure accepted behavior;
2. `MainPage` constructs, subscribes, coordinates, and tears down every service;
3. privacy is binary/default-allow and Arm does not gate the foreground title read;
4. diagnostics use fixed `H:` paths and legacy milestone names.

UIA introduces rich cross-process content and threading/provider-hang concerns. Adding it directly to the page and the Boolean gate would entrench unsafe boundaries.

## Decision

Insert M2.4 Foundation Hardening before M3:

1. characterization tests and CI foundation;
2. application-owned composition/lifecycle separation;
3. capability-based privacy with true Off/Paused semantics and per-app policy boundary;
4. configurable/session-scoped diagnostics and measured input-hook hardening.

Do not add UIA implementation until M2.4 exit criteria pass. Preserve M2 runtime behavior while hardening; do not use this decision to justify a broad rewrite.

## Consequences

- M3 starts later but on testable, privacy-appropriate boundaries.
- The immediate next branch becomes `dev/m2-4-1-characterization-tests`, not `dev/m3-1-uia-structure-probe`.
- Accepted M2 functionality remains complete; M2.4 is prerequisite productization, not a declaration that M2.3 failed.
- Documentation, tests, and lifecycle contracts become part of the feature foundation.

## Verification

- M2.4 exit criteria in `docs/ROADMAP.md` pass.
- Full M2.3/M2.3.1 Windows regression remains clean.
- Off/Paused produce no target content reads.
- New UIA work begins only from the accepted M2.4 baseline.
