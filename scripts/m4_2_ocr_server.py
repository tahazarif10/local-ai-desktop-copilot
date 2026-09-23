#!/usr/bin/env python3
"""Authenticated TLS OCR server for the fixed local AI server.

Product requests are RAM-only. Raw pixels and OCR text are never logged or
written to disk. Paddle models must already exist below the configured model
root; product operation never downloads models.
"""

from __future__ import annotations

import argparse
import base64
import contextlib
import hashlib
import hmac
import io
import os
from pathlib import Path
import re
import ssl
import struct
import threading
import time
from collections import OrderedDict
from dataclasses import dataclass
from http.server import BaseHTTPRequestHandler, ThreadingHTTPServer
from typing import Iterable

PROTOCOL_VERSION = 1
REQUEST_MAGIC = b"LCOPROC1"
RESPONSE_MAGIC = b"LCOPRS01"
REQUEST_PATH = "/v1/ocr"
REQUEST_FIXED_HEADER = 40
REQUEST_REGION_HEADER = 28
RESPONSE_FIXED_HEADER = 32

MAX_REGIONS = 4
MAX_REQUEST_BYTES = 16 * 1024 * 1024
MAX_RESPONSE_BYTES = 64 * 1024
MAX_TEXT_BYTES_PER_REGION = 16 * 1024
MAX_DEADLINE_MS = 15_000
MAX_CLOCK_SKEW_SECONDS = 30
MAX_REPLAY_NONCES = 256
REPLAY_RETENTION_SECONDS = 60

PIXEL_FORMAT_BGRA8 = 1

STATUS_OK = 0
STATUS_BAD_REQUEST = 1
STATUS_UNAUTHORIZED = 2
STATUS_BUSY = 3
STATUS_DEADLINE_EXCEEDED = 4
STATUS_INFERENCE_UNAVAILABLE = 5
STATUS_INTERNAL_ERROR = 6

NONCE_RE = re.compile(r"^[0-9a-fA-F]{32}$")
HASH_RE = re.compile(r"^[0-9a-fA-F]{64}$")


class ProtocolError(ValueError):
    pass


class AuthenticationError(ValueError):
    pass


@dataclass(frozen=True)
class RegionView:
    x: int
    y: int
    width: int
    height: int
    stride: int
    pixel_format: int
    data: memoryview


@dataclass(frozen=True)
class ParsedRequest:
    request_id: int
    epoch_id: int
    deadline_unix_ms: int
    regions: tuple[RegionView, ...]


class ReplayCache:
    def __init__(self) -> None:
        self._lock = threading.Lock()
        self._nonces: OrderedDict[str, int] = OrderedDict()

    def accept(self, nonce: str, timestamp: int, now_seconds: int) -> bool:
        with self._lock:
            cutoff = now_seconds - REPLAY_RETENTION_SECONDS
            expired = [
                key
                for key, value in self._nonces.items()
                if value < cutoff
            ]
            for key in expired:
                self._nonces.pop(key, None)

            if nonce in self._nonces:
                return False

            self._nonces[nonce] = timestamp
            while len(self._nonces) > MAX_REPLAY_NONCES:
                self._nonces.popitem(last=False)
            return True


def _canonical_signature_input(
    timestamp: int,
    nonce: str,
    body_hash_hex: str,
) -> bytes:
    return (
        "POST\n"
        + REQUEST_PATH
        + "\n"
        + str(timestamp)
        + "\n"
        + nonce.lower()
        + "\n"
        + body_hash_hex.lower()
    ).encode("ascii")


def compute_signature(
    authentication_key: bytes,
    timestamp: int,
    nonce: str,
    body_hash_hex: str,
) -> str:
    if len(authentication_key) < 32:
        raise ValueError("Authentication key must contain at least 32 bytes.")
    canonical = _canonical_signature_input(
        timestamp,
        nonce,
        body_hash_hex,
    )
    return base64.b64encode(
        hmac.new(authentication_key, canonical, hashlib.sha256).digest()
    ).decode("ascii")


def validate_authentication(
    headers,
    body: bytes,
    authentication_key: bytes,
    replay_cache: ReplayCache,
    now_seconds: int,
) -> None:
    key_id = headers.get("X-LC-KeyId", "")
    timestamp_text = headers.get("X-LC-Timestamp", "")
    nonce = headers.get("X-LC-Nonce", "")
    body_hash = headers.get("X-LC-Body-SHA256", "")
    signature = headers.get("X-LC-Signature", "")

    if key_id != "v1":
        raise AuthenticationError("Unknown key ID.")
    if not NONCE_RE.fullmatch(nonce):
        raise AuthenticationError("Invalid nonce.")
    if not HASH_RE.fullmatch(body_hash):
        raise AuthenticationError("Invalid body hash.")

    try:
        timestamp = int(timestamp_text)
    except ValueError as exc:
        raise AuthenticationError("Invalid timestamp.") from exc

    if abs(now_seconds - timestamp) > MAX_CLOCK_SKEW_SECONDS:
        raise AuthenticationError("Timestamp outside allowed skew.")

    actual_hash = hashlib.sha256(body).hexdigest()
    if not hmac.compare_digest(actual_hash, body_hash.lower()):
        raise AuthenticationError("Body hash mismatch.")

    expected = compute_signature(
        authentication_key,
        timestamp,
        nonce,
        body_hash,
    )
    if not hmac.compare_digest(expected, signature):
        raise AuthenticationError("Signature mismatch.")

    if not replay_cache.accept(nonce.lower(), timestamp, now_seconds):
        raise AuthenticationError("Replay detected.")


def parse_request(body: bytes, now_unix_ms: int) -> ParsedRequest:
    if len(body) < REQUEST_FIXED_HEADER:
        raise ProtocolError("Request header is truncated.")
    if len(body) > MAX_REQUEST_BYTES:
        raise ProtocolError("Request exceeds byte limit.")

    (
        magic,
        version,
        reserved,
        request_id,
        epoch_id,
        deadline_unix_ms,
        region_count,
    ) = struct.unpack_from(">8sHHqqqi", body, 0)

    if magic != REQUEST_MAGIC:
        raise ProtocolError("Request magic is invalid.")
    if version != PROTOCOL_VERSION or reserved != 0:
        raise ProtocolError("Request version is invalid.")
    if request_id <= 0 or epoch_id <= 0:
        raise ProtocolError("Request identity is invalid.")
    if region_count <= 0 or region_count > MAX_REGIONS:
        raise ProtocolError("Region count is invalid.")
    if deadline_unix_ms <= now_unix_ms:
        raise ProtocolError("Request deadline has expired.")
    if deadline_unix_ms - now_unix_ms > MAX_DEADLINE_MS:
        raise ProtocolError("Request deadline exceeds limit.")

    header_bytes = REQUEST_FIXED_HEADER + region_count * REQUEST_REGION_HEADER
    if header_bytes > len(body):
        raise ProtocolError("Region descriptors are truncated.")

    descriptor_offset = REQUEST_FIXED_HEADER
    payload_offset = header_bytes
    regions: list[RegionView] = []

    for _ in range(region_count):
        (
            x,
            y,
            width,
            height,
            stride,
            pixel_format,
            descriptor_reserved,
            byte_length,
        ) = struct.unpack_from(">iiiiiHHi", body, descriptor_offset)

        if x < 0 or y < 0 or width <= 0 or height <= 0:
            raise ProtocolError("Region geometry is invalid.")
        if width > 8192 or height > 8192:
            raise ProtocolError("Region dimensions exceed transport limits.")
        if pixel_format != PIXEL_FORMAT_BGRA8 or descriptor_reserved != 0:
            raise ProtocolError("Pixel format is invalid.")
        if stride < width * 4:
            raise ProtocolError("Region stride is invalid.")
        if byte_length != stride * height or byte_length <= 0:
            raise ProtocolError("Region byte length is invalid.")
        if payload_offset + byte_length > len(body):
            raise ProtocolError("Region pixel payload is truncated.")

        regions.append(
            RegionView(
                x=x,
                y=y,
                width=width,
                height=height,
                stride=stride,
                pixel_format=pixel_format,
                data=memoryview(body)[payload_offset : payload_offset + byte_length],
            )
        )

        descriptor_offset += REQUEST_REGION_HEADER
        payload_offset += byte_length

    if payload_offset != len(body):
        raise ProtocolError("Request contains trailing bytes.")

    return ParsedRequest(
        request_id=request_id,
        epoch_id=epoch_id,
        deadline_unix_ms=deadline_unix_ms,
        regions=tuple(regions),
    )


def _truncate_utf8(value: str, max_bytes: int) -> bytes:
    encoded = value.encode("utf-8")
    if len(encoded) <= max_bytes:
        return encoded

    candidate = encoded[:max_bytes]
    while candidate:
        try:
            candidate.decode("utf-8", errors="strict")
            return candidate
        except UnicodeDecodeError:
            candidate = candidate[:-1]
    return b""


def build_response(
    status: int,
    request_id: int,
    epoch_id: int,
    texts: Iterable[tuple[int, str]] = (),
) -> bytes:
    if request_id <= 0 or epoch_id <= 0:
        raise ValueError("Response identity must be positive.")
    if status not in {
        STATUS_OK,
        STATUS_BAD_REQUEST,
        STATUS_UNAUTHORIZED,
        STATUS_BUSY,
        STATUS_DEADLINE_EXCEEDED,
        STATUS_INFERENCE_UNAVAILABLE,
        STATUS_INTERNAL_ERROR,
    }:
        raise ValueError("Unknown response status.")

    encoded_texts: list[tuple[int, bytes]] = []
    for region_index, text in texts:
        if region_index < 0 or region_index >= MAX_REGIONS:
            raise ValueError("Response region index is invalid.")
        encoded_texts.append(
            (
                region_index,
                _truncate_utf8(text, MAX_TEXT_BYTES_PER_REGION),
            )
        )

    if len(encoded_texts) > MAX_REGIONS:
        raise ValueError("Response contains too many regions.")

    body = bytearray(
        struct.pack(
            ">8sHHqqi",
            RESPONSE_MAGIC,
            PROTOCOL_VERSION,
            status,
            request_id,
            epoch_id,
            len(encoded_texts),
        )
    )

    for region_index, encoded in encoded_texts:
        body += struct.pack(">ii", region_index, len(encoded))
        body += encoded

    if len(body) > MAX_RESPONSE_BYTES:
        raise ValueError("Response exceeds byte limit.")
    return bytes(body)


def _extract_text(result_items) -> str:
    texts: list[str] = []
    for result in result_items:
        for value in result["rec_texts"]:
            text = str(value).strip()
            if text:
                texts.append(text)
    return "\n".join(texts)


class PaddleRuntime:
    def __init__(self, model_root: Path, device: str) -> None:
        det_dir = (
            model_root
            / "official_models"
            / "PP-OCRv5_server_det"
        )
        rec_dir = (
            model_root
            / "official_models"
            / "arabic_PP-OCRv5_mobile_rec"
        )

        for required in (det_dir, rec_dir):
            if not required.is_dir() or not any(required.iterdir()):
                raise RuntimeError(
                    "Required pre-provisioned OCR model directory is missing."
                )

        os.environ["PADDLE_PDX_CACHE_HOME"] = str(model_root)
        os.environ["PADDLE_PDX_DISABLE_MODEL_SOURCE_CHECK"] = "True"

        from paddleocr import PaddleOCR

        with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(
            io.StringIO()
        ):
            self._ocr = PaddleOCR(
                device=device,
                engine="paddle_static",
                text_detection_model_name="PP-OCRv5_server_det",
                text_detection_model_dir=str(det_dir),
                text_recognition_model_name="arabic_PP-OCRv5_mobile_rec",
                text_recognition_model_dir=str(rec_dir),
                use_doc_orientation_classify=False,
                use_doc_unwarping=False,
                use_textline_orientation=False,
            )

    def predict(self, request: ParsedRequest) -> list[tuple[int, str]]:
        import numpy as np

        results: list[tuple[int, str]] = []
        for index, region in enumerate(request.regions):
            if int(time.time() * 1000) >= request.deadline_unix_ms:
                raise TimeoutError("OCR request deadline expired.")

            source = np.frombuffer(region.data, dtype=np.uint8)
            rows = source.reshape((region.height, region.stride))
            bgra = rows[:, : region.width * 4].reshape(
                (region.height, region.width, 4)
            )
            bgr = bgra[:, :, :3].copy()

            try:
                with contextlib.redirect_stdout(io.StringIO()), contextlib.redirect_stderr(
                    io.StringIO()
                ):
                    output = self._ocr.predict(bgr)
                results.append((index, _extract_text(output)))
            finally:
                bgr.fill(0)
                del bgr
                del bgra
                del rows
                del source

        return results


@dataclass
class ServerState:
    authentication_key: bytes
    runtime: PaddleRuntime
    replay_cache: ReplayCache
    inference_lock: threading.Lock
    diagnostic_delay_ms: int


class OcrRequestHandler(BaseHTTPRequestHandler):
    protocol_version = "HTTP/1.1"
    server_version = "LocalCopilotOcr/1"
    sys_version = ""

    def log_message(self, format, *args) -> None:
        return

    def _empty_http_error(self, status: int) -> None:
        self.send_response(status)
        self.send_header("Content-Length", "0")
        self.send_header("Connection", "close")
        self.end_headers()

    def _binary_response(self, body: bytes) -> None:
        self.send_response(200)
        self.send_header("Content-Type", "application/octet-stream")
        self.send_header("Content-Length", str(len(body)))
        self.send_header("Cache-Control", "no-store")
        self.send_header("Connection", "close")
        self.end_headers()
        self.wfile.write(body)

    def do_POST(self) -> None:
        if self.path != REQUEST_PATH:
            self._empty_http_error(404)
            return

        try:
            length = int(self.headers.get("Content-Length", ""))
        except ValueError:
            self._empty_http_error(400)
            return

        if length <= 0 or length > MAX_REQUEST_BYTES:
            self._empty_http_error(413)
            return

        body = self.rfile.read(length)
        if len(body) != length:
            self._empty_http_error(400)
            return

        state: ServerState = self.server.state  # type: ignore[attr-defined]

        try:
            validate_authentication(
                self.headers,
                body,
                state.authentication_key,
                state.replay_cache,
                int(time.time()),
            )
        except AuthenticationError:
            self._empty_http_error(401)
            return

        try:
            request = parse_request(body, int(time.time() * 1000))
        except ProtocolError:
            self._empty_http_error(400)
            return

        if not state.inference_lock.acquire(blocking=False):
            self._binary_response(
                build_response(
                    STATUS_BUSY,
                    request.request_id,
                    request.epoch_id,
                )
            )
            return

        try:
            if int(time.time() * 1000) >= request.deadline_unix_ms:
                response = build_response(
                    STATUS_DEADLINE_EXCEEDED,
                    request.request_id,
                    request.epoch_id,
                )
            else:
                try:
                    if state.diagnostic_delay_ms > 0:
                        time.sleep(state.diagnostic_delay_ms / 1000.0)
                        if int(time.time() * 1000) >= request.deadline_unix_ms:
                            raise TimeoutError("OCR request deadline expired.")

                    texts = state.runtime.predict(request)
                    response = build_response(
                        STATUS_OK,
                        request.request_id,
                        request.epoch_id,
                        texts,
                    )
                except TimeoutError:
                    response = build_response(
                        STATUS_DEADLINE_EXCEEDED,
                        request.request_id,
                        request.epoch_id,
                    )
                except Exception:
                    response = build_response(
                        STATUS_INFERENCE_UNAVAILABLE,
                        request.request_id,
                        request.epoch_id,
                    )

            self._binary_response(response)
        finally:
            state.inference_lock.release()
            del body


class BoundedHttpServer(ThreadingHTTPServer):
    request_queue_size = 2
    allow_reuse_address = True
    daemon_threads = True
    block_on_close = True
    max_handler_threads = 4

    def __init__(self, server_address, handler_class):
        super().__init__(server_address, handler_class)
        self._handler_slots = threading.BoundedSemaphore(
            self.max_handler_threads
        )

    def process_request(self, request, client_address):
        if not self._handler_slots.acquire(blocking=False):
            self.shutdown_request(request)
            return

        try:
            super().process_request(request, client_address)
        except BaseException:
            self._handler_slots.release()
            raise

    def process_request_thread(self, request, client_address):
        try:
            super().process_request_thread(request, client_address)
        finally:
            self._handler_slots.release()


def _read_authentication_key(path: Path) -> bytes:
    text = path.read_text(encoding="ascii").strip()
    if not re.fullmatch(r"[0-9a-fA-F]{64,128}", text) or len(text) % 2 != 0:
        raise ValueError(
            "Authentication key file must contain 32-64 bytes as hexadecimal."
        )
    return bytes.fromhex(text)


def validate_contract() -> None:
    if REQUEST_FIXED_HEADER != struct.calcsize(">8sHHqqqi"):
        raise RuntimeError("Request fixed-header size mismatch.")
    if REQUEST_REGION_HEADER != struct.calcsize(">iiiiiHHi"):
        raise RuntimeError("Request region-header size mismatch.")
    if RESPONSE_FIXED_HEADER != struct.calcsize(">8sHHqqi"):
        raise RuntimeError("Response fixed-header size mismatch.")
    if MAX_REGIONS > 4 or MAX_REQUEST_BYTES > 16 * 1024 * 1024:
        raise RuntimeError("Transport limits widened unexpectedly.")


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--bind", default="0.0.0.0")
    parser.add_argument("--port", type=int, default=49321)
    parser.add_argument("--cert-file")
    parser.add_argument("--key-file")
    parser.add_argument("--auth-key-file")
    parser.add_argument("--model-root")
    parser.add_argument("--device", default="gpu:0")
    parser.add_argument("--diagnostic-delay-ms", type=int, default=0)
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    validate_contract()
    if args.validate_only:
        print("M4.2.3 OCR SERVER CONTRACT: PASS")
        return 0

    required = {
        "cert-file": args.cert_file,
        "key-file": args.key_file,
        "auth-key-file": args.auth_key_file,
        "model-root": args.model_root,
    }
    missing = [name for name, value in required.items() if not value]
    if missing:
        raise ValueError("Missing required server arguments: " + ", ".join(missing))
    if args.port <= 0 or args.port > 65535:
        raise ValueError("Port is invalid.")
    if args.diagnostic_delay_ms < 0 or args.diagnostic_delay_ms > 5000:
        raise ValueError("Diagnostic delay must be between 0 and 5000 ms.")

    cert_file = Path(args.cert_file).resolve()
    key_file = Path(args.key_file).resolve()
    auth_key_file = Path(args.auth_key_file).resolve()
    model_root = Path(args.model_root).resolve()

    authentication_key = _read_authentication_key(auth_key_file)
    runtime = PaddleRuntime(model_root, args.device)

    server = BoundedHttpServer((args.bind, args.port), OcrRequestHandler)
    server.state = ServerState(  # type: ignore[attr-defined]
        authentication_key=authentication_key,
        runtime=runtime,
        replay_cache=ReplayCache(),
        inference_lock=threading.Lock(),
        diagnostic_delay_ms=args.diagnostic_delay_ms,
    )

    context = ssl.SSLContext(ssl.PROTOCOL_TLS_SERVER)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.load_cert_chain(certfile=str(cert_file), keyfile=str(key_file))
    server.socket = context.wrap_socket(server.socket, server_side=True)

    print(
        "M4.2.3 OCR SERVER: READY "
        f"protocol={PROTOCOL_VERSION} max_regions={MAX_REGIONS} "
        f"max_request_bytes={MAX_REQUEST_BYTES} "
        f"diagnostic_delay_ms={args.diagnostic_delay_ms}"
    )

    try:
        server.serve_forever(poll_interval=0.2)
    finally:
        server.server_close()

    return 0


if __name__ == "__main__":
    raise SystemExit(main())
