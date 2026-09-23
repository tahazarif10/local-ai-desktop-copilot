# ADR 0015: Pinned-TLS and HMAC authenticated OCR transport

- Status: Proposed
- Date: 2026-09-23
- Supersedes: none
- Extends: ADR 0004, ADR 0012, ADR 0014

## Context

M4.2.3a established that server-side OCR requires the same current uncancelled
epoch plus `CapturePixels + RunOcr + SendPixelsToLocalServer` and a non-empty
bounded M4.1 ROI plan. The selected Paddle runtime lives on the fixed local AI
server, so M4.2.3b must cross the LAN without weakening the local-only privacy
model.

The narrow OCR transport must provide:

- encryption in transit;
- server authentication;
- client authorization;
- replay resistance;
- request/epoch/deadline binding;
- strict byte/count/text limits;
- no raw pixel or OCR text logging;
- no screenshot/OCR persistence;
- no silent model download during product operation;
- bounded concurrency/backpressure and deterministic content disposal.

This is an OCR dependency of M4.2.3, not permission to pull the broader M6
general-purpose local AI protocol forward.

## Decision

Use one narrow HTTPS endpoint, `POST /v1/ocr`, with TLS 1.2 or later.

### Server authentication and encryption

The client pins the SHA-256 fingerprint of the exact server leaf certificate.
The certificate/key are provisioned outside Git. A public CA is not required
for the fixed private LAN because trust is established by the explicit pin.

The client rejects any TLS peer whose leaf-certificate hash does not match the
configured pin. There is no plaintext HTTP fallback and no certificate-warning
bypass path.

### Client authentication and replay protection

Each installation pair shares a randomly generated 32-64 byte authentication
key stored outside Git. Requests use key ID `v1` and carry:

- Unix timestamp seconds;
- a fresh 16-byte random nonce encoded as 32 hex characters;
- SHA-256 of the exact request body;
- HMAC-SHA256 over the canonical string:
  `POST\n/v1/ocr\n<timestamp>\n<nonce>\n<body-hash>`.

The server compares hashes/signatures in constant time, allows at most 30
seconds clock skew, and keeps a bounded 256-entry / 60-second nonce replay
cache. A nonce may be accepted once.

The HMAC key authenticates the client request; TLS plus the pinned certificate
authenticates the server and encrypts/integrity-protects the connection.

### Bounded binary request

Protocol version 1 uses a deterministic binary body:

- 8-byte magic/versioned header;
- request ID;
- epoch ID;
- absolute deadline;
- region count;
- one descriptor per ROI containing only width, height, stride, pixel format, and byte length;
- concatenated raw BGRA8 ROI bytes.

Source-frame X/Y coordinates are intentionally omitted from the wire format because the OCR server does not need them. M4.1 geometry remains client-local.

Hard transport defaults:

- maximum 4 regions;
- maximum 16 MiB total request body;
- maximum 15-second request deadline;
- maximum 64 KiB response;
- maximum 16 KiB UTF-8 OCR text per region.

The transport never synthesizes a full-frame request. The product gate remains
responsible for accepted M4.1 ROI geometry; the transport independently checks
its own byte/count/format/deadline limits.

### Response ownership

Responses bind request ID and epoch ID and return a typed status plus at most
one bounded UTF-8 text item per input region. Client OCR text is represented as
clear-on-dispose sensitive memory. Pixel request buffers and serialized request
and response buffers are zeroed when ownership ends.

### Server runtime

The fixed server runs a single active Paddle inference at a time. The
application does not maintain an unbounded request queue; overlapping work is a
typed Busy outcome and the client remains responsible for latest-request
ownership.

The runtime uses only pre-provisioned:

- `PP-OCRv5_server_det`;
- `arabic_PP-OCRv5_mobile_rec`.

Model directories must already exist. Product startup must fail unavailable
rather than download a missing model. Paddle prediction stdout/stderr is
suppressed so third-party content does not enter product diagnostics.

The server keeps pixels and OCR text in RAM only and does not write request,
image, or OCR-result files.

## Non-goals

This ADR does not authorize:

- a general-purpose LAN API;
- VLM/text/audio endpoints;
- cloud fallback;
- full-screen transport;
- long-term OCR text retention;
- remote administration;
- model installation over the product protocol;
- automatic certificate/key enrollment.

Credential provisioning and rotation remain a fixed-installation operation and
must be documented/tested before release.

## Consequences

- The fixed pair has a simple explicit trust anchor and no dependency on public
  PKI.
- Clock synchronization is required within the 30-second replay window.
- The shared HMAC key is sensitive installation data and must never be logged,
  committed, embedded in binaries, or copied into diagnostics.
- A certificate rotation requires updating the client pin.
- Server Busy/unavailable is a typed degraded state; there is no cloud or
  unmeasured-backend fallback.
- The protocol remains replaceable because Core product semantics do not name
  Paddle or Python.

## Verification

Before this ADR may become Accepted, M4.2.3b requires:

- portable C# tests for binary limits, identity binding, signatures and
  clear-on-dispose content;
- Python tests for request parsing, HMAC validation, replay/skew rejection and
  response limits;
- Windows build of the pinned-certificate client transport;
- TLS-negative test with the wrong certificate pin;
- authentication-negative test with the wrong key;
- replay-negative test;
- payload/deadline/Busy tests;
- pre-provisioned-model/no-download proof;
- cancellation/stale/latest publication proof in the product path;
- deterministic pixel/text disposal and prohibited-content scan;
- one-command physical acceptance across the fixed client/server pair.
