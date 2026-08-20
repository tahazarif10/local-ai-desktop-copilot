# ADR 0004: Fixed two-computer topology and semantic escalation

- Status: Accepted
- Date: 2026-08-20
- Supersedes: the original single-machine RTX 3060 assumption

## Context

The available client is suited to Windows sensing and light processing but not to keeping several generative models active. A Lenovo LOQ with an RTX 3050 Laptop GPU and 6 GB VRAM is available as a separate local AI server. Continuous full-frame VLM inference would waste resources and interfere with the user's primary work.

Structured accessibility and OCR often answer UI questions more cheaply and accurately than a VLM.

## Decision

Keep Windows-specific sensing, privacy, UI Automation, cheap change detection, and lightweight preprocessing on the client. Put model runtimes and resource scheduling on the fixed local AI server.

Use the semantic ladder:

```text
foreground + cheap change
  -> read-only UI Automation
  -> OCR on relevant ROI
  -> small VLM only when structured/text context is insufficient
  -> structured event/context
  -> local text reasoning
```

Treat the LAN boundary as explicit privacy egress with authentication, encryption, capability checks, byte/time limits, cancellation, and no internet fallback. Defer concrete transport and model choices until their benchmark milestones.

## Consequences

- GPU work stays mostly idle until a question or unresolved semantic event.
- The client can continue sensing if the AI server is unavailable, but cannot silently use cloud inference.
- Network contracts need versioning, health/capability negotiation, and no-retention defaults.
- Models are replaceable runtime choices, not core-domain types.
- Hardware upgrade recommendations are outside the architecture unless explicitly requested.

## Verification

- Background loop performs no generative inference.
- UIA/OCR sufficiency prevents VLM escalation.
- Local-server requests require explicit capability and bounded payload/deadline.
- Resource tests on the RTX 3050 measure VRAM and foreground-work interference before model acceptance.
