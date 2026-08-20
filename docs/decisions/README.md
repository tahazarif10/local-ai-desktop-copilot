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
