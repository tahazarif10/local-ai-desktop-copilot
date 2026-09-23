#!/usr/bin/env python3
"""Content-free physical probe for the M4.2.3 OCR LAN transport."""

from __future__ import annotations

import argparse
import hashlib
import hmac
import http.client
import json
from pathlib import Path
import secrets
import ssl
import struct
import threading
import time

REQUEST_MAGIC = b"LCOPROC1"
RESPONSE_MAGIC = b"LCOPRS01"
REQUEST_PATH = "/v1/ocr"
STATUS_OK = 0
STATUS_BUSY = 3


def load_bundle(path: Path) -> tuple[str, int, str, bytearray]:
    config_path = path / "ocr-client-config.json"
    data = json.loads(config_path.read_text(encoding="utf-8-sig"))
    if data.get("schema") != 1:
        raise ValueError("Unexpected client configuration schema.")

    server_name = str(data["server_name"]).strip()
    port = int(data["port"])
    certificate = str(data["server_certificate_sha256"]).strip().lower()
    key_name = str(data["authentication_key_file"]).strip()

    if not server_name:
        raise ValueError("Client configuration server name is empty.")
    if port != 49321:
        raise ValueError("Client configuration port is not pinned to 49321.")
    if len(certificate) != 64:
        raise ValueError("Client certificate pin length is invalid.")

    encoded = bytearray((path / key_name).read_bytes())
    try:
        start = 0
        end = len(encoded)
        whitespace = {9, 10, 13, 32}
        while start < end and encoded[start] in whitespace:
            start += 1
        while end > start and encoded[end - 1] in whitespace:
            end -= 1

        hex_length = end - start
        if hex_length < 64 or hex_length > 128 or hex_length % 2 != 0:
            raise ValueError("Client authentication key length is invalid.")

        key = bytearray(hex_length // 2)
        try:
            for index in range(len(key)):
                pair = encoded[
                    start + index * 2 : start + index * 2 + 2
                ]
                key[index] = int(bytes(pair), 16)
        except BaseException:
            for index in range(len(key)):
                key[index] = 0
            raise
    finally:
        for index in range(len(encoded)):
            encoded[index] = 0

    return server_name, port, certificate, key


def build_request(
    request_id: int,
    epoch_id: int,
    deadline_ms: int,
) -> bytes:
    width = 64
    height = 32
    stride = width * 4
    pixels = bytes([255, 255, 255, 255]) * (width * height)

    fixed = struct.pack(
        ">8sHHqqqi",
        REQUEST_MAGIC,
        1,
        0,
        request_id,
        epoch_id,
        deadline_ms,
        1,
    )
    region = struct.pack(
        ">iiiiiHHi",
        0,
        0,
        width,
        height,
        stride,
        1,
        0,
        len(pixels),
    )
    return fixed + region + pixels


def signature(
    key: bytes,
    timestamp: int,
    nonce: str,
    body_hash: str,
) -> str:
    import base64

    canonical = (
        "POST\n"
        + REQUEST_PATH
        + "\n"
        + str(timestamp)
        + "\n"
        + nonce.lower()
        + "\n"
        + body_hash.lower()
    ).encode("ascii")

    return base64.b64encode(
        hmac.new(key, canonical, hashlib.sha256).digest()
    ).decode("ascii")


def authenticated_headers(
    key: bytes,
    body: bytes,
    *,
    timestamp: int | None = None,
    nonce: str | None = None,
) -> dict[str, str]:
    if timestamp is None:
        timestamp = int(time.time())
    if nonce is None:
        nonce = secrets.token_hex(16)

    body_hash = hashlib.sha256(body).hexdigest()
    return {
        "Content-Type": "application/octet-stream",
        "Content-Length": str(len(body)),
        "X-LC-KeyId": "v1",
        "X-LC-Timestamp": str(timestamp),
        "X-LC-Nonce": nonce,
        "X-LC-Body-SHA256": body_hash,
        "X-LC-Signature": signature(
            key,
            timestamp,
            nonce,
            body_hash,
        ),
    }


def send(
    host: str,
    port: int,
    expected_certificate_sha256: str,
    body: bytes,
    headers: dict[str, str],
) -> tuple[int, bytes]:
    context = ssl.SSLContext(ssl.PROTOCOL_TLS_CLIENT)
    context.minimum_version = ssl.TLSVersion.TLSv1_2
    context.check_hostname = False
    context.verify_mode = ssl.CERT_NONE

    connection = http.client.HTTPSConnection(
        host,
        port,
        timeout=20,
        context=context,
    )

    try:
        connection.connect()
        assert connection.sock is not None
        peer = connection.sock.getpeercert(binary_form=True)
        actual = hashlib.sha256(peer).hexdigest()
        if not hmac.compare_digest(
            actual,
            expected_certificate_sha256.lower(),
        ):
            raise ssl.SSLError("Pinned certificate mismatch.")

        connection.request(
            "POST",
            REQUEST_PATH,
            body=body,
            headers=headers,
        )
        response = connection.getresponse()
        payload = response.read(64 * 1024 + 1)
        if len(payload) > 64 * 1024:
            raise ValueError("Transport probe response exceeded limit.")
        return response.status, payload
    finally:
        connection.close()


def response_status(payload: bytes) -> int:
    if len(payload) < 32:
        raise ValueError("Binary OCR response is truncated.")
    magic, version, status = struct.unpack_from(">8sHH", payload, 0)
    if magic != RESPONSE_MAGIC or version != 1:
        raise ValueError("Binary OCR response header is invalid.")
    return status


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--client-bundle")
    parser.add_argument("--server-host")
    parser.add_argument("--expect-busy", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    if args.validate_only:
        if len(REQUEST_MAGIC) != 8 or len(RESPONSE_MAGIC) != 8:
            raise RuntimeError("Protocol magic length mismatch.")
        print("M4.2.3 OCR TRANSPORT PROBE VALIDATION: PASS")
        return 0

    if not args.client_bundle:
        raise ValueError("--client-bundle is required.")

    bundle = Path(args.client_bundle).resolve()
    configured_host, port, certificate, key = load_bundle(bundle)
    host = args.server_host or configured_host

    try:
        now_ms = int(time.time() * 1000)
        body = build_request(101, 201, now_ms + 10_000)
        headers = authenticated_headers(key, body)

        http_status, payload = send(
            host,
            port,
            certificate,
            body,
            headers,
        )
        if http_status != 200 or response_status(payload) != STATUS_OK:
            raise RuntimeError("Valid authenticated OCR transport probe failed.")
        print("valid_authenticated_tls=PASS")

        replay_status, _ = send(
            host,
            port,
            certificate,
            body,
            headers,
        )
        if replay_status != 401:
            raise RuntimeError("Replay request was not rejected.")
        print("replay_rejection=PASS")

        wrong_key = bytearray(key)
        wrong_key[0] ^= 0xFF
        try:
            wrong_headers = authenticated_headers(bytes(wrong_key), body)
            wrong_status, _ = send(
                host,
                port,
                certificate,
                body,
                wrong_headers,
            )
        finally:
            for index in range(len(wrong_key)):
                wrong_key[index] = 0

        if wrong_status != 401:
            raise RuntimeError("Wrong authentication key was not rejected.")
        print("wrong_key_rejection=PASS")

        wrong_pin = (
            ("0" if certificate[0] != "0" else "1")
            + certificate[1:]
        )
        wrong_pin_pass = False
        try:
            send(
                host,
                port,
                wrong_pin,
                body,
                authenticated_headers(key, body),
            )
        except ssl.SSLError:
            wrong_pin_pass = True

        if not wrong_pin_pass:
            raise RuntimeError("Wrong server certificate pin was not rejected.")
        print("wrong_certificate_pin_rejection=PASS")

        expired = build_request(
            102,
            202,
            int(time.time() * 1000) - 1,
        )
        expired_status, _ = send(
            host,
            port,
            certificate,
            expired,
            authenticated_headers(key, expired),
        )
        if expired_status != 400:
            raise RuntimeError("Expired protocol deadline was not rejected.")
        print("expired_deadline_rejection=PASS")

        if args.expect_busy:
            first_body = build_request(
                103,
                203,
                int(time.time() * 1000) + 10_000,
            )
            first_headers = authenticated_headers(key, first_body)
            first_result: list[tuple[int, bytes] | BaseException] = []

            def first_request() -> None:
                try:
                    first_result.append(
                        send(
                            host,
                            port,
                            certificate,
                            first_body,
                            first_headers,
                        )
                    )
                except BaseException as exc:
                    first_result.append(exc)

            thread = threading.Thread(
                target=first_request,
                daemon=True,
            )
            thread.start()
            time.sleep(0.25)

            second_body = build_request(
                104,
                204,
                int(time.time() * 1000) + 10_000,
            )
            second_http, second_payload = send(
                host,
                port,
                certificate,
                second_body,
                authenticated_headers(key, second_body),
            )

            thread.join(timeout=20)
            if thread.is_alive() or not first_result:
                raise RuntimeError("Primary delayed OCR request did not complete.")
            if isinstance(first_result[0], BaseException):
                raise first_result[0]

            first_http, first_payload = first_result[0]
            if (
                first_http != 200
                or response_status(first_payload) != STATUS_OK
                or second_http != 200
                or response_status(second_payload) != STATUS_BUSY
            ):
                raise RuntimeError("Bounded Busy behavior was not observed.")
            print("single_active_busy_rejection=PASS")

        print("raw_pixels_logged=False")
        print("raw_ocr_logged=False")
        print("authentication_key_printed=False")
        print("M4.2.3 OCR TRANSPORT PROBE: PASS")
        return 0
    finally:
        for index in range(len(key)):
            key[index] = 0


if __name__ == "__main__":
    raise SystemExit(main())
