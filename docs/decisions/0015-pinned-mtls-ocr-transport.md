# ADR 0015: Pinned mTLS HTTPS and bounded binary OCR transport

- Status: Accepted
- Date: 2026-09-23
- Accepted: 2026-09-23
- Supersedes: none
- Extends: ADR 0004, ADR 0014

## Context

ADR 0014 selects server-local PaddleOCR as the M4.2.3 integration target.
The client therefore needs to send only already-authorized M4.1 ROI pixels to
the fixed local AI server.

The LAN is an egress/trust boundary. A temporary plaintext socket, benchmark
file share, unauthenticated HTTP endpoint, or JSON/base64 screenshot protocol
would weaken the accepted privacy model and create avoidable content copies.

The transport must remain narrow enough that bringing OCR forward does not
silently implement the broader M6 text/VLM/audio gateway.

## Decision

Use HTTPS between the fixed Windows client and local AI server with mutual TLS
and explicit peer-certificate pinning.

For the first product OCR transport:

- each machine owns its private certificate key locally in the Windows
  CurrentUser certificate store;
- only public certificate fingerprints are exchanged/configured;
- the client presents a client certificate and pins the expected server
  certificate fingerprint;
- the server requires a client certificate and pins the expected client
  fingerprint;
- no trust-all callback, plaintext fallback, internet fallback, or automatic
  certificate enrollment is permitted;
- configuration missing a peer pin fails closed.

The application protocol is versioned binary data over a single HTTPS POST
endpoint. It carries no window title, process name, file path, UIA text, ROI
screen coordinates, or diagnostic strings.

Request metadata contains only:

- protocol version;
- request ID;
- epoch ID;
- absolute deadline;
- region count;
- per-region pixel width/height;
- per-region lossless PNG byte length and bytes.

Response metadata contains only:

- protocol version;
- typed outcome/reason;
- matching request ID and epoch ID;
- one bounded UTF-8 OCR text payload per submitted region when successful.

## Initial immutable transport ceilings

The first protocol version fixes conservative limits:

- at most 4 ROI regions per request;
- at most 4,194,304 pixels per region;
- at most 8,192 pixels in either region dimension;
- at most 16 MiB encoded PNG bytes per region;
- at most 32 MiB encoded PNG bytes across a request;
- request deadline at most 15 seconds into the future;
- at most 16 KiB UTF-8 OCR text per region;
- at most 64 KiB UTF-8 OCR text across a response;
- one active server inference request; overload is explicit rather than
  unbounded queue growth.

These transport limits do not widen M4.1. The client must still pass the
stricter current M4.1 ROI planner/integration gate before encoding.

## Sensitive-memory ownership

Encoded ROI bytes and returned OCR UTF-8 bytes are content-bearing.

- request/response payload owners implement deterministic disposal;
- owned byte arrays are zeroed on disposal;
- stale, cancelled, rejected, timed-out, failed, or shutdown paths dispose
  content before returning;
- transport objects render only redacted metadata from `ToString()`;
- application/HTTP/runtime logging must never include body bytes or OCR text.

The server-side Paddle process may necessarily materialize OCR text internally
during inference, but the host protocol converts it to bounded UTF-8 content,
does not persist it, and releases references immediately after the response.

## Server runtime boundary

The HTTPS host and the Paddle worker are separate processes.

- the HTTPS host owns TLS, protocol validation, request limits, cancellation,
  and the one-active-request gate;
- a long-lived Python child owns the pre-provisioned Paddle models;
- host-to-worker communication is local-only and framed/bounded;
- if inference exceeds its deadline or the worker becomes unhealthy, the host
  kills the worker process and restarts it for a later request rather than
  allowing an uninterruptible request to outlive its boundary;
- model source checks/downloads are disabled during product operation.

The concrete server/worker implementation is a later slice after the portable
protocol codec is accepted.

## Consequences

- Private keys never need to be copied between the two machines.
- Public certificate pinning gives deterministic peer identity without
  depending on public PKI or DNS.
- Certificate provisioning/pairing becomes an explicit deployment step.
- Binary framing avoids base64 expansion and avoids turning screenshot bytes
  into long-lived managed strings.
- The transport is OCR-specific. Reuse for VLM/text/audio requires its own
  later contract review rather than accidental endpoint expansion.
- Server unavailability is a typed local degradation; there is no cloud or
  unauthenticated fallback.

## Verification

Before network wiring:

- deterministic Core tests must round-trip request/response framing;
- malformed magic/version/count/dimension/length/size inputs must fail closed;
- request/response byte limits must be enforced before allocation where
  possible;
- sensitive request/response buffers must zero on disposal;
- cancellation during decode must propagate without returning partial content.

Before physical acceptance:

- TLS fails without the expected peer certificate;
- wrong client/server pins fail;
- correct pinned mTLS succeeds;
- oversize payloads and expired deadlines reject before Paddle inference;
- one-active overload is explicit;
- cancellation/timeout kills or invalidates the worker and a later request
  recovers;
- diagnostic/log scans contain no ROI bytes, OCR text, paths, titles, prompts,
  or certificate private material.
