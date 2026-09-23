#!/usr/bin/env python3
"""Local-only M4.2.2 Tesseract baseline benchmark.

Raw OCR text, ground truth, and screenshots never leave the local corpus.
Only aggregate metrics are printed and persisted.
"""

from __future__ import annotations

import argparse
import ctypes
from ctypes import wintypes
import json
from pathlib import Path
import subprocess
import time
from datetime import datetime, timezone
from typing import Any

from m4_2_ocr_benchmark import (
    REQUIRED_CATEGORIES,
    directory_size_bytes,
    load_manifest,
    percentile,
    score_text,
    select_samples,
)

SCHEMA_VERSION = 1
DEFAULT_WARM_RUNS = 3
DEFAULT_TIMEOUT_SECONDS = 15.0
DEFAULT_LANGUAGE = "fas+eng"
DEFAULT_OEM = "1"
DEFAULT_PSM = "6"
DEFAULT_TESSDATA_VARIANT = "fast"


class PROCESS_MEMORY_COUNTERS(ctypes.Structure):
    _fields_ = [
        ("cb", wintypes.DWORD),
        ("PageFaultCount", wintypes.DWORD),
        ("PeakWorkingSetSize", ctypes.c_size_t),
        ("WorkingSetSize", ctypes.c_size_t),
        ("QuotaPeakPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPagedPoolUsage", ctypes.c_size_t),
        ("QuotaPeakNonPagedPoolUsage", ctypes.c_size_t),
        ("QuotaNonPagedPoolUsage", ctypes.c_size_t),
        ("PagefileUsage", ctypes.c_size_t),
        ("PeakPagefileUsage", ctypes.c_size_t),
    ]


def _process_rss_mib(pid: int) -> float | None:
    if not hasattr(ctypes, "windll"):
        return None
    kernel32 = ctypes.windll.kernel32
    psapi = ctypes.windll.psapi
    process_query_limited_information = 0x1000
    handle = kernel32.OpenProcess(
        process_query_limited_information,
        False,
        int(pid),
    )
    if not handle:
        return None
    try:
        counters = PROCESS_MEMORY_COUNTERS()
        counters.cb = ctypes.sizeof(counters)
        ok = psapi.GetProcessMemoryInfo(
            handle,
            ctypes.byref(counters),
            counters.cb,
        )
        if not ok:
            return None
        return float(counters.WorkingSetSize) / (1024.0 * 1024.0)
    finally:
        kernel32.CloseHandle(handle)


def _run_tesseract(
    executable: Path,
    image_path: Path,
    tessdata_dir: Path,
    language: str,
    oem: str,
    psm: str,
    timeout_seconds: float,
) -> tuple[str, float, float | None]:
    command = [
        str(executable),
        str(image_path),
        "stdout",
        "--tessdata-dir",
        str(tessdata_dir),
        "-l",
        language,
        "--oem",
        oem,
        "--psm",
        psm,
    ]

    started = time.perf_counter()
    process = subprocess.Popen(
        command,
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
    )
    peak_rss: float | None = None

    try:
        while process.poll() is None:
            elapsed = time.perf_counter() - started
            if elapsed > timeout_seconds:
                process.kill()
                process.communicate()
                raise TimeoutError("Tesseract inference exceeded benchmark deadline.")
            rss = _process_rss_mib(process.pid)
            if rss is not None and (peak_rss is None or rss > peak_rss):
                peak_rss = rss
            time.sleep(0.005)

        stdout, stderr = process.communicate()
    finally:
        if process.poll() is None:
            process.kill()
            process.communicate()

    elapsed_ms = (time.perf_counter() - started) * 1000.0
    if process.returncode != 0:
        raise RuntimeError(
            f"Tesseract exited with code {process.returncode}; stderr suppressed."
        )
    return stdout, elapsed_ms, peak_rss


def _version(executable: Path) -> str:
    completed = subprocess.run(
        [str(executable), "--version"],
        stdout=subprocess.PIPE,
        stderr=subprocess.PIPE,
        text=True,
        encoding="utf-8",
        errors="replace",
        timeout=10.0,
        creationflags=getattr(subprocess, "CREATE_NO_WINDOW", 0),
        check=False,
    )
    if completed.returncode != 0:
        return "unavailable"
    first = (completed.stdout or completed.stderr).splitlines()
    return first[0].strip() if first else "unavailable"


def run(args: argparse.Namespace) -> dict[str, Any]:
    executable = Path(args.tesseract_exe).resolve()
    tessdata_dir = Path(args.tessdata_dir).resolve()
    benchmark_root = Path(args.benchmark_root).resolve()
    manifest_path = Path(args.manifest).resolve()

    if not executable.is_file():
        raise ValueError("Tesseract executable is missing.")
    if not tessdata_dir.is_dir():
        raise ValueError("Tesseract tessdata directory is missing.")
    for required in ("fas.traineddata", "eng.traineddata"):
        if not (tessdata_dir / required).is_file():
            raise ValueError(f"Required Tesseract language data is missing: {required}")

    samples = load_manifest(manifest_path, require_files=True)
    samples = select_samples(samples, args.include_category)
    included_categories = list(
        dict.fromkeys(args.include_category or REQUIRED_CATEGORIES)
    )

    first_sample = samples[0]
    cold_text, cold_ms, cold_rss = _run_tesseract(
        executable,
        first_sample["image_path"],
        tessdata_dir,
        args.language,
        args.oem,
        args.psm,
        args.timeout_seconds,
    )
    del cold_text

    latencies: list[float] = []
    rss_peak = cold_rss
    character_edits = 0
    reference_code_points = 0
    word_edits = 0
    reference_words = 0
    exact_matches = 0
    failures = 0
    timeouts = 0
    category_counts: dict[str, int] = {}

    for sample in samples:
        category_counts[sample["category"]] = (
            category_counts.get(sample["category"], 0) + 1
        )
        truth = sample["truth_path"].read_text(encoding="utf-8-sig")
        first_hypothesis: str | None = None

        for _ in range(args.warm_runs):
            try:
                hypothesis, elapsed_ms, rss = _run_tesseract(
                    executable,
                    sample["image_path"],
                    tessdata_dir,
                    args.language,
                    args.oem,
                    args.psm,
                    args.timeout_seconds,
                )
            except TimeoutError:
                failures += 1
                timeouts += 1
                raise
            except Exception:
                failures += 1
                raise

            latencies.append(elapsed_ms)
            if rss is not None and (rss_peak is None or rss > rss_peak):
                rss_peak = rss
            if first_hypothesis is None:
                first_hypothesis = hypothesis

        assert first_hypothesis is not None
        metrics = score_text(truth, first_hypothesis)
        character_edits += metrics.character_edits
        reference_code_points += metrics.reference_code_points
        word_edits += metrics.word_edits
        reference_words += metrics.reference_words
        exact_matches += 1 if metrics.exact_normalized_match else 0

    cer = (
        0.0 if character_edits == 0 else 1.0
        if reference_code_points == 0
        else character_edits / reference_code_points
    )
    wer = (
        0.0 if word_edits == 0 else 1.0
        if reference_words == 0
        else word_edits / reference_words
    )

    install_root = executable.parent
    return {
        "schema": SCHEMA_VERSION,
        "mode": "controlled-benchmark",
        "candidate": f"tesseract-5-fas-eng-{args.tessdata_variant}",
        "tessdata_variant": args.tessdata_variant,
        "scope": (
            "matched-category-subset"
            if args.include_category
            else "full-seven-category"
        ),
        "sample_count": len(samples),
        "included_categories": included_categories,
        "category_counts": category_counts,
        "raw_content_persisted": False,
        "raw_ocr_logged": False,
        "backend_selected": False,
        "failure_count": failures,
        "timeout_count": timeouts,
        "strict_character_error_rate": round(cer, 8),
        "strict_word_error_rate": round(wer, 8),
        "exact_normalized_match_rate": round(exact_matches / len(samples), 8),
        "warm_run_count_per_sample": args.warm_runs,
        "warm_latency_p50_ms": round(percentile(latencies, 0.50), 3),
        "warm_latency_p95_ms": round(percentile(latencies, 0.95), 3),
        "warm_latency_max_ms": round(max(latencies), 3),
        "cold_first_ocr_ms": round(cold_ms, 3),
        "process_rss_peak_mib": (
            None if rss_peak is None else round(rss_peak, 3)
        ),
        "gpu_vram_baseline_mib": None,
        "gpu_vram_peak_mib": None,
        "gpu_vram_delta_mib": None,
        "tesseract_install_footprint_mib": round(
            directory_size_bytes(install_root) / (1024.0 * 1024.0),
            3,
        ),
        "tessdata_footprint_mib": round(
            directory_size_bytes(tessdata_dir) / (1024.0 * 1024.0),
            3,
        ),
        "tesseract_version": _version(executable),
        "language": args.language,
        "oem": args.oem,
        "psm": args.psm,
        "device": "cpu",
        "inference_timeout_seconds": args.timeout_seconds,
    }


def write_result(benchmark_root: Path, result: dict[str, Any]) -> Path:
    root = benchmark_root / "results"
    root.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    variant = result.get("tessdata_variant", "unknown")
    path = root / f"aggregate-tesseract-{variant}-{stamp}.json"
    path.write_text(
        json.dumps(result, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    return path


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--benchmark-root", required=True)
    parser.add_argument("--manifest", required=True)
    parser.add_argument(
        "--include-category",
        action="append",
        choices=REQUIRED_CATEGORIES,
        default=[],
    )
    parser.add_argument("--tesseract-exe", required=True)
    parser.add_argument("--tessdata-dir", required=True)
    parser.add_argument(
        "--tessdata-variant",
        choices=("fast", "best"),
        default=DEFAULT_TESSDATA_VARIANT,
    )
    parser.add_argument("--language", default=DEFAULT_LANGUAGE)
    parser.add_argument("--oem", default=DEFAULT_OEM)
    parser.add_argument("--psm", default=DEFAULT_PSM)
    parser.add_argument("--warm-runs", type=int, default=DEFAULT_WARM_RUNS)
    parser.add_argument(
        "--timeout-seconds",
        type=float,
        default=DEFAULT_TIMEOUT_SECONDS,
    )
    parser.add_argument("--validate-only", action="store_true")
    args = parser.parse_args()

    if args.warm_runs < 1 or args.warm_runs > 20:
        raise ValueError("warm-runs must be between 1 and 20.")
    if args.timeout_seconds <= 0:
        raise ValueError("timeout-seconds must be positive.")
    if args.language != "fas+eng":
        raise ValueError("M4.2.2 Tesseract baseline must use fas+eng.")
    if args.oem != "1":
        raise ValueError("M4.2.2 Tesseract baseline must use LSTM OEM 1.")
    if args.psm != "6":
        raise ValueError("M4.2.2 Tesseract baseline must use PSM 6.")

    if args.validate_only:
        print("M4.2.2 Tesseract benchmark validation: PASS")
        return 0

    result = run(args)
    output = write_result(Path(args.benchmark_root).resolve(), result)
    print("M4.2.2 TESSERACT BENCHMARK: PASS")
    print(json.dumps(result, ensure_ascii=True, indent=2))
    print(f"aggregate_result={output}")
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
