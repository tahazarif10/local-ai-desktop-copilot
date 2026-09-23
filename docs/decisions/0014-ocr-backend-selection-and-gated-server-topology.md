# ADR 0014: OCR backend selection and gated server topology

- Status: Accepted
- Date: 2026-09-23
- Accepted: 2026-09-23
- Supersedes: none
- Extends: ADR 0012, ADR 0013

## Context

M4.2.2 completed controlled OCR benchmarking on the fixed hardware and closed the candidate search.

The strongest measured multilingual configuration was:

`PP-OCRv5_server_det + arabic_PP-OCRv5_mobile_rec`

on the fixed Lenovo LOQ local AI server with RTX 3050 Laptop GPU 6 GB.

Full seven-category controlled evidence:

- strict CER: 0.23004695;
- strict WER: 0.22388060;
- exact normalized match: 0.0;
- warm p50/p95: 91.392 / 117.272 ms;
- GPU VRAM delta: 562 MiB;
- zero failures;
- zero timeouts;
- deterministic timeout-guard self-test PASS;
- raw OCR text was not logged or persisted.

The speed-oriented and accuracy-oriented Tesseract 5.5.3 `fas+eng` baselines were materially less accurate and slower. Legacy Windows Media OCR was fast on the matched English subset but the fixed machines expose only `en-US`, so it fails the Persian + mixed-language hard gate.

The selected Paddle configuration is measured on the AI server, not on the Windows sensing client. Therefore product use would cross the LAN trust boundary. Existing architecture already treats that boundary as separate from “fully local” execution.

## Decision

Select `PP-OCRv5_server_det + arabic_PP-OCRv5_mobile_rec` as the M4.2.3 OCR backend configuration for the fixed deployment.

The selected execution topology is **Local AI server**, but selection does not authorize transport.

M4.2.3 is split into ordered slices:

### M4.2.3.1 — portable OCR request and publication contract

Define a backend-neutral request contract and fail-closed runtime gates.

A client-local OCR request requires:

- `CapturePixels`;
- `RunOcr`.

A local-AI-server OCR request additionally requires:

- `SendPixelsToLocalServer`.

Admission and publication must also require:

- explicit Armed state;
- the same current epoch;
- a non-cancelled epoch;
- a non-empty accepted M4.1 ROI;
- latest-request ownership at publication.

This slice contains no pixel crop, no OCR execution, no LAN I/O, and no OCR text retention.

### M4.2.3.2 — authenticated bounded OCR transport

Before any pixel crosses the LAN boundary, introduce a narrow transport dedicated to bounded OCR ROI requests.

The transport must:

- require `CapturePixels + RunOcr + SendPixelsToLocalServer` in the same current epoch immediately before send;
- accept only already-planned M4.1 ROIs;
- reject full-frame fallback and oversized payloads;
- use authenticated encryption on the LAN;
- use request IDs, deadlines, bounded payload sizes, and cancellation;
- reject responses for stale epochs or superseded requests;
- avoid raw pixel/OCR text diagnostics;
- avoid silent internet fallback or runtime model download.

This transport may later be generalized by M6.1, but M4.2.3 must not depend on an unauthenticated placeholder protocol.

### M4.2.3.3 — product runtime integration and physical acceptance

After the transport passes its own boundary tests:

- crop only accepted ROI pixels in RAM;
- dispatch through the bounded OCR request owner;
- execute the selected pinned Paddle configuration on the local AI server;
- publish only after current-epoch/capability/latest-request revalidation;
- dispose pixels and OCR text on rejection, cancellation, supersession, policy change, or consumer completion;
- keep diagnostics aggregate-only;
- prove deterministic shutdown and no post-Disarm publication.

## Consequences

- PaddleOCR is now an explicit product backend selection, not merely a benchmark candidate.
- The model pair remains replaceable behind the OCR contract.
- The server topology cannot borrow permission from `CapturePixels` or `RunOcr`; LAN pixel egress has its own capability.
- M4.2.3.1 can proceed entirely in portable Core without introducing content-bearing runtime behavior.
- Product OCR remains unavailable until M4.2.3.2 and M4.2.3.3 pass.
- M4.3 VLM remains blocked.
- The repeated Paddle Windows cuDNN compile/runtime mismatch remains a recorded compatibility risk for server acceptance; it is not silently “fixed” by overriding the pinned package set.

## Rejected alternatives

### Tesseract as the primary multilingual backend

Rejected from current evidence because both official `tessdata_fast` and `tessdata_best` measured materially higher strict error and latency than the selected Paddle configuration.

### Windows Media OCR as the primary backend

Rejected because the fixed machines expose only `en-US`; it does not satisfy the Persian and mixed Persian-English hard gate.

### Silent LAN integration inside the OCR adapter

Rejected because it would collapse a separate privacy/egress boundary and bypass `SendPixelsToLocalServer`.

### Full-frame fallback when ROI planning yields nothing

Rejected. M4.1 deliberately fails closed when no bounded ROI survives.

## Acceptance for M4.2.3.1

M4.2.3.1 is accepted only when:

- portable tests prove exact capability requirements for client-local and local-server topologies;
- server topology fails closed without `SendPixelsToLocalServer`;
- stale/cancelled/mismatched/disarmed requests cannot dispatch;
- publication requires the latest request and repeats the same epoch/capability checks;
- empty ROI requests fail closed;
- no app/runtime composition, capture, network, OCR execution, or content logging is added;
- Ubuntu and Windows Core tests pass;
- the strict Windows `win-x64` application build remains green.
