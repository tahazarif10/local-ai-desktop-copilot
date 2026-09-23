# Architecture decision records

ADRs preserve why a material decision exists so a new session does not undo it accidentally.

## Status meanings

- **Proposed** — under review; not yet binding.
- **Accepted** — current architecture; changing it requires a superseding ADR.
- **Superseded** — replaced by a linked later ADR.
- **Rejected** — considered and deliberately not selected.

## Index

| ADR | Status | Decision |
| --- | --- | --- |
| [0001](0001-privacy-before-content.md) | Accepted | Evaluate policy before every content-bearing source |
| [0002](0002-context-epochs-and-stale-results.md) | Accepted | Bind asynchronous work to cancellable context epochs |
| [0003](0003-bounded-latest-wins-sensing.md) | Accepted | Use bounded/latest-wins flow for volatile sensing data |
| [0004](0004-two-computer-semantic-escalation.md) | Accepted | Fixed two-computer topology and UIA→OCR→VLM escalation |
| [0005](0005-foundation-hardening-before-uia.md) | Accepted | Complete M2.4 hardening before M3 UIA |
| [0006](0006-launch-scoped-diagnostics.md) | Accepted | Use expiring launch-scoped diagnostic sessions and explicit bundle sources |
| [0007](0007-root-only-uia-mta-probe.md) | Accepted | Validate a root-only probe on an application-owned COM MTA worker before broader UIA |
| [0008](0008-generated-bounded-uia-snapshot.md) | Accepted | Use generated interop and explicit budgets for a non-text Control View snapshot |
| [0009](0009-capability-gated-semantic-uia-snapshot.md) | Accepted | Read selected visible UI semantics behind a separate capability and lifetime budget |
| [0010](0010-bounded-priority-ui-enrichment-policy.md) | Accepted | Admit UI enrichment through a bounded, priority-aware, per-epoch policy before runtime wiring |
| [0011](0011-measured-uia-provider-isolation.md) | Accepted | Keep the automatic UIA worker in-process after measured blocking-provider recovery and joined shutdown |
| [0012](0012-bounded-region-of-interest-planning.md) | Accepted | Plan bounded capture ROIs from change regions and explicit UIA screen projections before OCR |
| [0013](0013-evidence-gated-ocr-benchmark.md) | Proposed | Gate OCR backend selection behind target-machine preflight and controlled Persian/English benchmark evidence |

## When to add an ADR

Add or supersede an ADR for changes to:

- machine/process/trust boundaries;
- privacy capabilities/defaults;
- persistence or network egress;
- context epoch identity;
- queue/drop/backpressure semantics;
- model/runtime ownership;
- the no-action/autonomy boundary;
- accepted milestone ordering.

## Template

```markdown
# ADR NNNN: Short decision title

- Status: Proposed
- Date: YYYY-MM-DD
- Supersedes: none

## Context

What problem and constraints require a durable decision?

## Decision

What exactly is decided?

## Consequences

Positive and negative tradeoffs.

## Verification

What tests/evidence demonstrate the decision is upheld?
```

Keep implementation detail in code/design documents. An ADR records the stable choice and its consequences.
