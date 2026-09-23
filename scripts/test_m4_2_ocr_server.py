#!/usr/bin/env python3

from __future__ import annotations

import base64
import hashlib
import hmac
import importlib.util
from pathlib import Path
import struct
import sys
import time
import unittest

SCRIPT_PATH = Path(__file__).with_name("m4_2_ocr_server.py")
SPEC = importlib.util.spec_from_file_location("m4_2_ocr_server", SCRIPT_PATH)
SERVER = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = SERVER
assert SPEC.loader is not None
SPEC.loader.exec_module(SERVER)


class Headers(dict):
    def get(self, key, default=None):
        return super().get(key, default)


def request_body(
    request_id=5,
    epoch_id=7,
    deadline_unix_ms=None,
    pixels=None,
):
    if deadline_unix_ms is None:
        deadline_unix_ms = int(time.time() * 1000) + 5000
    if pixels is None:
        pixels = bytes(range(32))

    fixed = struct.pack(
        ">8sHHqqqi",
        SERVER.REQUEST_MAGIC,
        SERVER.PROTOCOL_VERSION,
        0,
        request_id,
        epoch_id,
        deadline_unix_ms,
        1,
    )
    region = struct.pack(
        ">iiiHHi",
        4,
        2,
        16,
        SERVER.PIXEL_FORMAT_BGRA8,
        0,
        len(pixels),
    )
    return fixed + region + pixels


class OcrServerProtocolTests(unittest.TestCase):
    def test_request_round_trip_parse(self):
        now_ms = int(time.time() * 1000)
        body = request_body(deadline_unix_ms=now_ms + 5000)
        parsed = SERVER.parse_request(body, now_ms)

        self.assertEqual(5, parsed.request_id)
        self.assertEqual(7, parsed.epoch_id)
        self.assertEqual(1, len(parsed.regions))
        region = parsed.regions[0]
        self.assertEqual((4, 2, 16), (
            region.width,
            region.height,
            region.stride,
        ))
        self.assertEqual(bytes(range(32)), bytes(region.data))

    def test_parser_rejects_expired_truncated_and_trailing(self):
        now_ms = int(time.time() * 1000)

        with self.assertRaises(SERVER.ProtocolError):
            SERVER.parse_request(
                request_body(deadline_unix_ms=now_ms - 1),
                now_ms,
            )

        body = request_body(deadline_unix_ms=now_ms + 5000)
        with self.assertRaises(SERVER.ProtocolError):
            SERVER.parse_request(body[:-1], now_ms)

        with self.assertRaises(SERVER.ProtocolError):
            SERVER.parse_request(body + b"x", now_ms)

    def test_authentication_binds_body_and_blocks_replay(self):
        key = bytes(range(1, 33))
        body = request_body()
        now = int(time.time())
        nonce = "00112233445566778899aabbccddeeff"
        body_hash = hashlib.sha256(body).hexdigest()
        signature = SERVER.compute_signature(
            key,
            now,
            nonce,
            body_hash,
        )
        headers = Headers(
            {
                "X-LC-KeyId": "v1",
                "X-LC-Timestamp": str(now),
                "X-LC-Nonce": nonce,
                "X-LC-Body-SHA256": body_hash,
                "X-LC-Signature": signature,
            }
        )
        cache = SERVER.ReplayCache()

        SERVER.validate_authentication(
            headers,
            body,
            key,
            cache,
            now,
        )

        with self.assertRaises(SERVER.AuthenticationError):
            SERVER.validate_authentication(
                headers,
                body,
                key,
                cache,
                now,
            )

        changed = body[:-1] + bytes([body[-1] ^ 0xFF])
        fresh_headers = Headers(headers)
        fresh_headers["X-LC-Nonce"] = "ffeeddccbbaa99887766554433221100"
        fresh_headers["X-LC-Signature"] = SERVER.compute_signature(
            key,
            now,
            fresh_headers["X-LC-Nonce"],
            body_hash,
        )

        with self.assertRaises(SERVER.AuthenticationError):
            SERVER.validate_authentication(
                fresh_headers,
                changed,
                key,
                SERVER.ReplayCache(),
                now,
            )

    def test_authentication_rejects_clock_skew(self):
        key = bytes(range(1, 33))
        body = request_body()
        now = int(time.time())
        timestamp = now - SERVER.MAX_CLOCK_SKEW_SECONDS - 1
        nonce = "00112233445566778899aabbccddeeff"
        body_hash = hashlib.sha256(body).hexdigest()
        headers = Headers(
            {
                "X-LC-KeyId": "v1",
                "X-LC-Timestamp": str(timestamp),
                "X-LC-Nonce": nonce,
                "X-LC-Body-SHA256": body_hash,
                "X-LC-Signature": SERVER.compute_signature(
                    key,
                    timestamp,
                    nonce,
                    body_hash,
                ),
            }
        )

        with self.assertRaises(SERVER.AuthenticationError):
            SERVER.validate_authentication(
                headers,
                body,
                key,
                SERVER.ReplayCache(),
                now,
            )

    def test_response_is_bounded_and_utf8_safe(self):
        response = SERVER.build_response(
            SERVER.STATUS_OK,
            5,
            7,
            [(0, "خطا Error")],
        )

        self.assertLessEqual(len(response), SERVER.MAX_RESPONSE_BYTES)
        (
            magic,
            version,
            status,
            request_id,
            epoch_id,
            count,
        ) = struct.unpack_from(">8sHHqqi", response, 0)

        self.assertEqual(SERVER.RESPONSE_MAGIC, magic)
        self.assertEqual(SERVER.PROTOCOL_VERSION, version)
        self.assertEqual(SERVER.STATUS_OK, status)
        self.assertEqual(5, request_id)
        self.assertEqual(7, epoch_id)
        self.assertEqual(1, count)

        region_index, text_length = struct.unpack_from(">ii", response, 32)
        self.assertEqual(0, region_index)
        self.assertEqual(
            "خطا Error",
            response[40 : 40 + text_length].decode("utf-8"),
        )

    def test_contract_constants_match_binary_layout(self):
        SERVER.validate_contract()
        self.assertEqual(
            SERVER.REQUEST_FIXED_HEADER,
            struct.calcsize(">8sHHqqqi"),
        )
        self.assertEqual(
            SERVER.REQUEST_REGION_HEADER,
            struct.calcsize(">iiiHHi"),
        )
        self.assertEqual(
            SERVER.RESPONSE_FIXED_HEADER,
            struct.calcsize(">8sHHqqi"),
        )


if __name__ == "__main__":
    unittest.main()
