## Scope

- Milestone/slice:
- What changed:
- Explicit non-goals:
- Base SHA:

## Architecture and privacy

- [ ] Preserves accepted architecture or includes an ADR for the change
- [ ] Lists every privacy capability/data class touched
- [ ] Evaluates identity/policy before content
- [ ] Carries epoch/cancellation and drops stale results before publication
- [ ] Adds no raw title/UIA/OCR/input/audio/pixel/prompt/response content to logs
- [ ] Adds no cloud path or autonomous action

## Concurrency and ownership

- [ ] Threads/processes and UI-dispatch boundaries are explicit
- [ ] Queues/collections have capacity and overflow behavior
- [ ] Frames, buffers, registrations, hooks, events, COM/WinRT objects, timers, and transports have owners and teardown paths
- [ ] Timeout/unavailable/retry behavior is bounded

## Validation

- Focused tests:
- Regression tests:
- Windows build command/result:
- Target-hardware runtime scenario:
- Privacy-negative case:
- Cancellation/stale case:
- Teardown result:
- Measurements (hardware, samples, warmup, average/percentile/max):

## Repository truth

- [ ] `git diff --check` passes
- [ ] Only intended files are staged
- [ ] `docs/PROJECT_STATE.md` is accurate
- [ ] `docs/ROADMAP.md` is accurate
- [ ] New limitations are recorded
- [ ] No generated diagnostics, screenshots, model weights, secrets, or machine-local files are included
