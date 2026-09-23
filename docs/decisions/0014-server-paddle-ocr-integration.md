# ADR 0014: Server-local PaddleOCR integration target and explicit OCR egress gate

- Status: Accepted
- Date: 2026-09-23
- Accepted: 2026-09-23
- Supersedes: none
- Extends: ADR 0004, ADR 0012, ADR 0013

## Context

M4.2.2 completed controlled target-hardware benchmarking for the initial OCR
candidate set. The fixed deployment is a two-computer system: a Windows sensing
client and a separate local AI server with an RTX 3050 Laptop GPU.

The strongest measured multilingual configuration on the fixed AI server was:

- PaddleOCR 3.7.0;
- PaddlePaddle GPU 3.2.0;
- `PP-OCRv5_server_det`;
- `arabic_PP-OCRv5_mobile_rec`;
- `gpu:0`.

On the seven-category controlled corpus it measured strict CER 0.23004695,
strict WER 0.22388060, warm p50/p95 91.392/117.272 ms, and 562 MiB measured
GPU VRAM delta, with zero physical failures/timeouts. The lighter Paddle
configuration was faster but less accurate. Tesseract `tessdata_fast` and
`tessdata_best` were both materially less accurate and slower. Legacy Windows
Media OCR remained ineligible for the Persian/mixed-language hard gate because
the fixed machines expose only `en-US`.

This evidence selects an OCR engine/configuration target, but it does not grant
permission to transmit pixels across the LAN. The selected runtime was measured
on the AI server, while the product's capture and ROI planning live on the
client. ADR 0004 and the privacy model treat the client-to-server boundary as
explicit egress requiring authentication, encryption, capability checks,
payload limits, deadlines, cancellation, and no-retention behavior.

## Decision

Carry `PP-OCRv5_server_det + arabic_PP-OCRv5_mobile_rec` forward as the
M4.2.3 product OCR integration target on the fixed local AI server.

Keep model/library names out of portable Core contracts. Core may describe only
OCR operation semantics, topology, privacy requirements, epochs, bounded ROI
ownership, cancellation, and publication rules.

Split M4.2.3 implementation into two dependency-ordered slices.

### M4.2.3a — portable OCR integration gate

Add a pure Core gate that:

- requires the product to be explicitly Armed;
- requires the same current, uncancelled epoch;
- requires `CapturePixels + RunOcr` for any OCR dispatch;
- additionally requires `SendPixelsToLocalServer` when the execution topology
  is the local AI server;
- rejects an empty ROI plan rather than widening to a full frame;
- rechecks the same requirements before publication;
- rejects non-latest results before publication;
- carries the accepted M4.1 `RegionOfInterestPlan` rather than inventing a
  second ROI policy.

This slice does not capture pixels, invoke PaddleOCR, create a socket, serialize
an ROI, grant any new capability, or retain OCR text.

### M4.2.3b — authenticated bounded OCR transport/runtime integration

Before any product pixel crosses the LAN:

- define an authenticated encrypted local transport contract;
- bind every request to request ID, epoch ID, deadline, cancellation, and
  bounded payload metadata;
- enforce `CapturePixels + RunOcr + SendPixelsToLocalServer` on the client
  immediately before serialization;
- enforce protocol byte/count/deadline limits on the server;
- send only accepted M4.1 ROI pixels, never an implicit full frame;
- pre-provision the selected Paddle models so product operation performs no
  silent model/network download;
- disable content-bearing third-party runtime logging;
- return bounded OCR text as short-lived sensitive content;
- revalidate current epoch/capabilities/latest request before publication;
- deterministically dispose client pixel buffers and server request/result
  content on completion, cancellation, stale rejection, or shutdown;
- log only whitelisted aggregate metadata.

The minimal transport may be implemented before the broader M6 feature set
because it is now a direct dependency of the selected M4.2.3 OCR topology. It
must remain a narrow OCR transport rather than silently pulling text/VLM/audio
features forward.

## Capability clarification

`SendPixelsToLocalServer` authorizes a bounded, policy-approved ROI pixel
payload for a local-server visual operation. It is not VLM-specific. It does
not authorize:

- full-desktop transmission;
- screenshot persistence;
- unrelated future requests;
- OCR by itself;
- text/audio transfer;
- cloud egress.

Server OCR therefore requires the conjunction:

`CapturePixels + RunOcr + SendPixelsToLocalServer`.

A client-process OCR backend, if one is measured and selected in the future,
would require only `CapturePixels + RunOcr`.

## Consequences

- The benchmark-selected Paddle configuration is an application/runtime choice,
  not a Core-domain type.
- No raw or unauthenticated LAN path is permitted as a temporary shortcut.
- M4.3 remains blocked until M4.2.3 product integration and physical acceptance
  pass.
- The existing product default remains unchanged: `RunOcr` and
  `SendPixelsToLocalServer` are denied unless an explicit policy/diagnostic
  path grants them.
- The recorded Paddle cuDNN compile/runtime mismatch remains a compatibility
  risk to monitor; it is not fixed by manually overriding the pinned benchmark
  environment.
- If the server is unavailable, OCR degrades to unavailable; there is no cloud
  fallback and no automatic switch to an unmeasured backend.

## Verification

M4.2.3a requires deterministic portable tests covering:

- Armed/current/uncancelled epoch;
- client versus local-server capability conjunctions;
- empty-plan rejection;
- capability revocation before publication;
- stale/non-latest publication rejection.

M4.2.3b additionally requires:

- protocol and payload-limit tests;
- authentication/encryption configuration review;
- cancellation/deadline and stale-result tests;
- deterministic buffer/result disposal;
- content-free diagnostic scans;
- strict Windows build;
- one-command physical acceptance on the fixed client/server pair.
