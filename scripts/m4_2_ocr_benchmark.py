#!/usr/bin/env python3
"""Local-only M4.2.2 OCR benchmark runner.

The runner intentionally never prints or persists screenshot content, ground truth,
or raw OCR text. Only aggregate metrics leave the local benchmark corpus.
"""

from __future__ import annotations

import argparse
import json
import math
import multiprocessing as mp
import os
from pathlib import Path
import queue
import re
import statistics
import subprocess
import sys
import threading
import time
import unicodedata
from dataclasses import dataclass
from datetime import datetime, timezone
from typing import Any, Iterable, Sequence

SCHEMA_VERSION = 1
DEFAULT_DEVICE = "gpu:0"
DEFAULT_ENGINE = "paddle_static"
DEFAULT_DETECTION_MODEL = "PP-OCRv5_mobile_det"
DEFAULT_RECOGNITION_MODEL = "arabic_PP-OCRv5_mobile_rec"
DEFAULT_WARM_RUNS = 3
DEFAULT_INFERENCE_TIMEOUT_SECONDS = 15.0
DEFAULT_INITIALIZATION_TIMEOUT_SECONDS = 300.0

REQUIRED_CATEGORIES = (
    "persian-ui",
    "english-ui",
    "mixed-fa-en",
    "terminal-console",
    "dialog",
    "browser-ui",
    "desktop-app-ui",
)

OPAQUE_SAMPLE_ID = re.compile(r"^[a-z0-9][a-z0-9_-]{2,31}$")


@dataclass(frozen=True)
class TextMetrics:
    reference_code_points: int
    hypothesis_code_points: int
    character_edits: int
    character_error_rate: float
    reference_words: int
    hypothesis_words: int
    word_edits: int
    word_error_rate: float
    exact_normalized_match: bool


def normalize_text(value: str) -> str:
    return unicodedata.normalize(
        "NFC",
        value.replace("\r\n", "\n").replace("\r", "\n"),
    ).strip()


def _levenshtein(left: Sequence[Any], right: Sequence[Any]) -> int:
    if not left:
        return len(right)
    if not right:
        return len(left)

    previous = list(range(len(right) + 1))
    current = [0] * (len(right) + 1)

    for row, left_item in enumerate(left, start=1):
        current[0] = row
        for column, right_item in enumerate(right, start=1):
            substitution_cost = 0 if left_item == right_item else 1
            current[column] = min(
                previous[column] + 1,
                current[column - 1] + 1,
                previous[column - 1] + substitution_cost,
            )
        previous, current = current, previous

    return previous[len(right)]


def _rate(edits: int, reference_count: int) -> float:
    if reference_count == 0:
        return 0.0 if edits == 0 else 1.0
    return edits / reference_count


def score_text(reference: str, hypothesis: str) -> TextMetrics:
    reference_normalized = normalize_text(reference)
    hypothesis_normalized = normalize_text(hypothesis)

    reference_code_points = list(reference_normalized)
    hypothesis_code_points = list(hypothesis_normalized)
    reference_words = reference_normalized.split()
    hypothesis_words = hypothesis_normalized.split()

    character_edits = _levenshtein(
        reference_code_points,
        hypothesis_code_points,
    )
    word_edits = _levenshtein(reference_words, hypothesis_words)

    return TextMetrics(
        reference_code_points=len(reference_code_points),
        hypothesis_code_points=len(hypothesis_code_points),
        character_edits=character_edits,
        character_error_rate=_rate(
            character_edits,
            len(reference_code_points),
        ),
        reference_words=len(reference_words),
        hypothesis_words=len(hypothesis_words),
        word_edits=word_edits,
        word_error_rate=_rate(word_edits, len(reference_words)),
        exact_normalized_match=(
            reference_normalized == hypothesis_normalized
        ),
    )


def percentile(values: Sequence[float], percentile_value: float) -> float:
    if not values:
        return 0.0
    ordered = sorted(values)
    if len(ordered) == 1:
        return ordered[0]

    rank = (len(ordered) - 1) * percentile_value
    lower = math.floor(rank)
    upper = math.ceil(rank)

    if lower == upper:
        return ordered[lower]

    fraction = rank - lower
    return ordered[lower] + (
        ordered[upper] - ordered[lower]
    ) * fraction


def directory_size_bytes(path: Path) -> int:
    if not path.exists():
        return 0

    total = 0
    for entry in path.rglob("*"):
        if entry.is_file():
            try:
                total += entry.stat().st_size
            except OSError:
                pass
    return total


def _resolve_local_child(root: Path, relative_value: str) -> Path:
    relative = Path(relative_value)
    if relative.is_absolute():
        raise ValueError("Corpus paths must be relative.")

    resolved_root = root.resolve()
    candidate = (resolved_root / relative).resolve()

    try:
        candidate.relative_to(resolved_root)
    except ValueError as exc:
        raise ValueError("Corpus path escaped its local root.") from exc

    return candidate


def load_manifest(
    manifest_path: Path,
    require_files: bool,
) -> list[dict[str, Any]]:
    data = json.loads(manifest_path.read_text(encoding="utf-8-sig"))

    if data.get("schema") != SCHEMA_VERSION:
        raise ValueError("Unexpected corpus manifest schema.")

    samples = data.get("samples")
    if not isinstance(samples, list) or not samples:
        raise ValueError("Corpus manifest must contain samples.")

    corpus_root = manifest_path.parent
    seen_ids: set[str] = set()
    categories: set[str] = set()
    validated: list[dict[str, Any]] = []

    for sample in samples:
        if not isinstance(sample, dict):
            raise ValueError("Every corpus sample must be an object.")

        sample_id = sample.get("id")
        category = sample.get("category")
        image_value = sample.get("image")
        truth_value = sample.get("ground_truth_file")

        if not isinstance(sample_id, str) or not OPAQUE_SAMPLE_ID.fullmatch(
            sample_id
        ):
            raise ValueError("Sample IDs must be opaque lowercase identifiers.")

        if sample_id in seen_ids:
            raise ValueError("Sample IDs must be unique.")
        seen_ids.add(sample_id)

        if category not in REQUIRED_CATEGORIES:
            raise ValueError(f"Unexpected corpus category for {sample_id}.")
        categories.add(category)

        if not isinstance(image_value, str) or not isinstance(
            truth_value,
            str,
        ):
            raise ValueError("Image and ground-truth paths must be strings.")

        image_path = _resolve_local_child(corpus_root, image_value)
        truth_path = _resolve_local_child(corpus_root, truth_value)

        if require_files:
            if not image_path.is_file():
                raise ValueError(
                    f"Missing local image for sample {sample_id}."
                )
            if not truth_path.is_file():
                raise ValueError(
                    f"Missing local ground truth for sample {sample_id}."
                )
            if not truth_path.read_text(encoding="utf-8-sig").strip():
                raise ValueError(
                    f"Ground truth is empty for sample {sample_id}."
                )

        validated.append(
            {
                "id": sample_id,
                "category": category,
                "image_path": image_path,
                "truth_path": truth_path,
            }
        )

    missing_categories = set(REQUIRED_CATEGORIES) - categories
    if missing_categories:
        raise ValueError(
            "Corpus is missing required categories: "
            + ", ".join(sorted(missing_categories))
        )

    return validated


def _extract_text(result_items: Iterable[Any]) -> str:
    texts: list[str] = []
    for result in result_items:
        result_texts = result["rec_texts"]
        for value in result_texts:
            text = str(value).strip()
            if text:
                texts.append(text)
    return "\n".join(texts)


def _worker_main(
    command_queue: mp.Queue,
    result_queue: mp.Queue,
    config: dict[str, Any],
) -> None:
    os.environ["PADDLE_PDX_CACHE_HOME"] = config["model_cache_root"]

    started = time.perf_counter()
    try:
        import importlib.metadata as metadata
        import paddle
        from paddleocr import PaddleOCR

        ocr = PaddleOCR(
            device=config["device"],
            engine=config["engine"],
            text_detection_model_name=config["detection_model"],
            text_recognition_model_name=config["recognition_model"],
            use_doc_orientation_classify=False,
            use_doc_unwarping=False,
            use_textline_orientation=False,
        )

        result_queue.put(
            {
                "kind": "ready",
                "initialization_ms": (
                    time.perf_counter() - started
                )
                * 1000.0,
                "python_version": sys.version.split()[0],
                "paddle_version": str(paddle.__version__),
                "paddleocr_version": metadata.version("paddleocr"),
                "device": str(paddle.device.get_device()),
                "compiled_with_cuda": bool(
                    paddle.device.is_compiled_with_cuda()
                ),
            }
        )
    except BaseException as exc:
        result_queue.put(
            {
                "kind": "fatal",
                "error_type": type(exc).__name__,
                "error": str(exc)[:500],
            }
        )
        return

    while True:
        command = command_queue.get()
        if command.get("kind") == "stop":
            return
        if command.get("kind") != "predict":
            continue

        started = time.perf_counter()
        try:
            output = ocr.predict(command["image_path"])
            text = _extract_text(output)
            result_queue.put(
                {
                    "kind": "prediction",
                    "token": command["token"],
                    "elapsed_ms": (
                        time.perf_counter() - started
                    )
                    * 1000.0,
                    "text": text,
                }
            )
        except BaseException as exc:
            result_queue.put(
                {
                    "kind": "prediction_error",
                    "token": command["token"],
                    "elapsed_ms": (
                        time.perf_counter() - started
                    )
                    * 1000.0,
                    "error_type": type(exc).__name__,
                    "error": str(exc)[:500],
                }
            )


def _gpu_used_memory_mib() -> float | None:
    try:
        completed = subprocess.run(
            [
                "nvidia-smi",
                "--query-gpu=memory.used",
                "--format=csv,noheader,nounits",
            ],
            capture_output=True,
            text=True,
            timeout=3.0,
            check=False,
        )
        if completed.returncode != 0:
            return None
        first_line = completed.stdout.strip().splitlines()[0]
        return float(first_line.strip())
    except (OSError, ValueError, IndexError, subprocess.TimeoutExpired):
        return None


class _GpuMonitor:
    def __init__(self) -> None:
        self.baseline_mib = _gpu_used_memory_mib()
        self.peak_mib = self.baseline_mib
        self._stop = threading.Event()
        self._thread = threading.Thread(
            target=self._run,
            name="m4-2-gpu-monitor",
            daemon=True,
        )

    def start(self) -> None:
        self._thread.start()

    def stop(self) -> None:
        self._stop.set()
        self._thread.join(timeout=2.0)

    def _run(self) -> None:
        while not self._stop.wait(0.25):
            value = _gpu_used_memory_mib()
            if value is None:
                continue
            if self.peak_mib is None or value > self.peak_mib:
                self.peak_mib = value


def _process_rss_mib(pid: int) -> float | None:
    try:
        import psutil

        return psutil.Process(pid).memory_info().rss / (1024.0 * 1024.0)
    except BaseException:
        return None


def _timeout_guard_self_test() -> bool:
    process = subprocess.Popen(
        [
            sys.executable,
            "-c",
            "import time; time.sleep(5)",
        ]
    )
    try:
        process.wait(timeout=0.2)
        return False
    except subprocess.TimeoutExpired:
        process.kill()
        process.wait(timeout=5.0)
        return process.returncode is not None


def _start_worker(
    config: dict[str, Any],
    initialization_timeout: float,
) -> tuple[mp.Process, mp.Queue, mp.Queue, dict[str, Any]]:
    ctx = mp.get_context("spawn")
    command_queue: mp.Queue = ctx.Queue(maxsize=2)
    result_queue: mp.Queue = ctx.Queue(maxsize=2)
    process = ctx.Process(
        target=_worker_main,
        args=(command_queue, result_queue, config),
        daemon=True,
    )
    process.start()

    try:
        message = result_queue.get(timeout=initialization_timeout)
    except queue.Empty:
        process.kill()
        process.join(timeout=5.0)
        raise RuntimeError("OCR worker initialization timed out.")

    if message.get("kind") == "fatal":
        process.join(timeout=5.0)
        raise RuntimeError(
            "OCR worker initialization failed: "
            + message.get("error_type", "UnknownError")
            + ": "
            + message.get("error", "")
        )

    if message.get("kind") != "ready":
        process.kill()
        process.join(timeout=5.0)
        raise RuntimeError("OCR worker returned an unexpected startup message.")

    return process, command_queue, result_queue, message


def _stop_worker(
    process: mp.Process,
    command_queue: mp.Queue,
) -> None:
    if process.is_alive():
        try:
            command_queue.put({"kind": "stop"}, timeout=1.0)
        except queue.Full:
            pass
        process.join(timeout=5.0)

    if process.is_alive():
        process.kill()
        process.join(timeout=5.0)


def _predict(
    process: mp.Process,
    command_queue: mp.Queue,
    result_queue: mp.Queue,
    image_path: Path,
    token: str,
    timeout_seconds: float,
) -> dict[str, Any]:
    command_queue.put(
        {
            "kind": "predict",
            "token": token,
            "image_path": str(image_path),
        },
        timeout=1.0,
    )

    try:
        message = result_queue.get(timeout=timeout_seconds)
    except queue.Empty as exc:
        if process.is_alive():
            process.kill()
            process.join(timeout=5.0)
        raise TimeoutError("OCR prediction exceeded the benchmark deadline.") from exc

    if message.get("token") != token:
        raise RuntimeError("OCR worker result token mismatch.")

    return message


def _config_from_args(args: argparse.Namespace) -> dict[str, Any]:
    benchmark_root = Path(args.benchmark_root).resolve()
    model_cache_root = benchmark_root / "models"
    model_cache_root.mkdir(parents=True, exist_ok=True)

    return {
        "device": args.device,
        "engine": args.engine,
        "detection_model": args.detection_model,
        "recognition_model": args.recognition_model,
        "model_cache_root": str(model_cache_root),
    }


def prepare_models(args: argparse.Namespace) -> dict[str, Any]:
    config = _config_from_args(args)
    monitor = _GpuMonitor()
    monitor.start()

    process = None
    command_queue = None
    try:
        process, command_queue, _, ready = _start_worker(
            config,
            args.initialization_timeout_seconds,
        )
        rss_mib = _process_rss_mib(process.pid)
    finally:
        if process is not None and command_queue is not None:
            _stop_worker(process, command_queue)
        monitor.stop()

    if not ready["compiled_with_cuda"]:
        raise RuntimeError("Paddle is not CUDA-enabled.")
    if not str(ready["device"]).lower().startswith("gpu"):
        raise RuntimeError("Paddle did not select a GPU device.")

    return {
        "schema": SCHEMA_VERSION,
        "mode": "prepare-models",
        "environment_prepared": True,
        "ocr_executed": False,
        "benchmark_content_created": False,
        "backend_selected": False,
        "detection_model": config["detection_model"],
        "recognition_model": config["recognition_model"],
        "engine": config["engine"],
        "device": ready["device"],
        "compiled_with_cuda": ready["compiled_with_cuda"],
        "python_version": ready["python_version"],
        "paddle_version": ready["paddle_version"],
        "paddleocr_version": ready["paddleocr_version"],
        "initialization_ms": round(ready["initialization_ms"], 3),
        "worker_rss_mib": None if rss_mib is None else round(rss_mib, 3),
        "gpu_vram_baseline_mib": monitor.baseline_mib,
        "gpu_vram_peak_mib": monitor.peak_mib,
        "model_cache_mib": round(
            directory_size_bytes(Path(config["model_cache_root"]))
            / (1024.0 * 1024.0),
            3,
        ),
    }


def run_benchmark(args: argparse.Namespace) -> dict[str, Any]:
    benchmark_root = Path(args.benchmark_root).resolve()
    manifest_path = Path(args.manifest).resolve()
    samples = load_manifest(manifest_path, require_files=True)
    config = _config_from_args(args)

    timeout_guard_passed = _timeout_guard_self_test()
    if not timeout_guard_passed:
        raise RuntimeError("Benchmark timeout guard self-test failed.")

    gpu_monitor = _GpuMonitor()
    gpu_monitor.start()

    process = None
    command_queue = None
    result_queue = None

    character_edits = 0
    reference_code_points = 0
    word_edits = 0
    reference_words = 0
    exact_matches = 0
    failures = 0
    timeouts = 0
    latencies: list[float] = []
    rss_peak_mib: float | None = None
    category_counts: dict[str, int] = {
        category: 0 for category in REQUIRED_CATEGORIES
    }

    try:
        process, command_queue, result_queue, ready = _start_worker(
            config,
            args.initialization_timeout_seconds,
        )

        first_sample = samples[0]
        warmup = _predict(
            process,
            command_queue,
            result_queue,
            first_sample["image_path"],
            "warmup",
            args.inference_timeout_seconds,
        )
        if warmup.get("kind") != "prediction":
            raise RuntimeError("OCR warm-up failed.")

        for sample_index, sample in enumerate(samples):
            category_counts[sample["category"]] += 1
            truth = sample["truth_path"].read_text(encoding="utf-8-sig")
            first_hypothesis: str | None = None

            for run_index in range(args.warm_runs):
                token = f"{sample_index}-{run_index}"
                try:
                    message = _predict(
                        process,
                        command_queue,
                        result_queue,
                        sample["image_path"],
                        token,
                        args.inference_timeout_seconds,
                    )
                except TimeoutError:
                    failures += 1
                    timeouts += 1
                    raise

                if message.get("kind") != "prediction":
                    failures += 1
                    raise RuntimeError(
                        "OCR prediction failed for an opaque benchmark sample."
                    )

                latencies.append(float(message["elapsed_ms"]))

                rss_mib = _process_rss_mib(process.pid)
                if rss_mib is not None:
                    if rss_peak_mib is None or rss_mib > rss_peak_mib:
                        rss_peak_mib = rss_mib

                if first_hypothesis is None:
                    first_hypothesis = str(message["text"])

            assert first_hypothesis is not None
            metrics = score_text(truth, first_hypothesis)
            character_edits += metrics.character_edits
            reference_code_points += metrics.reference_code_points
            word_edits += metrics.word_edits
            reference_words += metrics.reference_words
            exact_matches += 1 if metrics.exact_normalized_match else 0

    finally:
        if process is not None and command_queue is not None:
            _stop_worker(process, command_queue)
        gpu_monitor.stop()

    if reference_code_points == 0:
        cer = 0.0 if character_edits == 0 else 1.0
    else:
        cer = character_edits / reference_code_points

    if reference_words == 0:
        wer = 0.0 if word_edits == 0 else 1.0
    else:
        wer = word_edits / reference_words

    exact_rate = exact_matches / len(samples)

    python_root = benchmark_root / "python312-nuget"

    return {
        "schema": SCHEMA_VERSION,
        "mode": "controlled-benchmark",
        "sample_count": len(samples),
        "category_counts": category_counts,
        "raw_content_persisted": False,
        "raw_ocr_logged": False,
        "backend_selected": False,
        "timeout_guard_self_test": "PASS",
        "failure_count": failures,
        "timeout_count": timeouts,
        "strict_character_error_rate": round(cer, 8),
        "strict_word_error_rate": round(wer, 8),
        "exact_normalized_match_rate": round(exact_rate, 8),
        "warm_run_count_per_sample": args.warm_runs,
        "warm_latency_p50_ms": round(percentile(latencies, 0.50), 3),
        "warm_latency_p95_ms": round(percentile(latencies, 0.95), 3),
        "warm_latency_max_ms": round(max(latencies), 3),
        "cold_initialization_ms": round(ready["initialization_ms"], 3),
        "worker_rss_peak_mib": (
            None if rss_peak_mib is None else round(rss_peak_mib, 3)
        ),
        "gpu_vram_baseline_mib": gpu_monitor.baseline_mib,
        "gpu_vram_peak_mib": gpu_monitor.peak_mib,
        "gpu_vram_delta_mib": (
            None
            if gpu_monitor.baseline_mib is None
            or gpu_monitor.peak_mib is None
            else round(
                gpu_monitor.peak_mib - gpu_monitor.baseline_mib,
                3,
            )
        ),
        "python_runtime_footprint_mib": round(
            directory_size_bytes(python_root) / (1024.0 * 1024.0),
            3,
        ),
        "model_cache_footprint_mib": round(
            directory_size_bytes(Path(config["model_cache_root"]))
            / (1024.0 * 1024.0),
            3,
        ),
        "python_version": ready["python_version"],
        "paddle_version": ready["paddle_version"],
        "paddleocr_version": ready["paddleocr_version"],
        "engine": config["engine"],
        "device": ready["device"],
        "compiled_with_cuda": ready["compiled_with_cuda"],
        "detection_model": config["detection_model"],
        "recognition_model": config["recognition_model"],
        "inference_timeout_seconds": args.inference_timeout_seconds,
    }


def write_aggregate_result(
    benchmark_root: Path,
    result: dict[str, Any],
) -> Path:
    results_root = benchmark_root / "results"
    results_root.mkdir(parents=True, exist_ok=True)
    stamp = datetime.now(timezone.utc).strftime("%Y%m%dT%H%M%SZ")
    output_path = results_root / f"aggregate-{stamp}.json"
    output_path.write_text(
        json.dumps(result, ensure_ascii=True, indent=2) + "\n",
        encoding="utf-8",
    )
    return output_path


def build_parser() -> argparse.ArgumentParser:
    parser = argparse.ArgumentParser(
        description="M4.2.2 local-only OCR benchmark runner."
    )
    parser.add_argument("--benchmark-root", required=True)
    parser.add_argument("--manifest")
    parser.add_argument("--device", default=DEFAULT_DEVICE)
    parser.add_argument("--engine", default=DEFAULT_ENGINE)
    parser.add_argument(
        "--detection-model",
        default=DEFAULT_DETECTION_MODEL,
    )
    parser.add_argument(
        "--recognition-model",
        default=DEFAULT_RECOGNITION_MODEL,
    )
    parser.add_argument(
        "--warm-runs",
        type=int,
        default=DEFAULT_WARM_RUNS,
    )
    parser.add_argument(
        "--inference-timeout-seconds",
        type=float,
        default=DEFAULT_INFERENCE_TIMEOUT_SECONDS,
    )
    parser.add_argument(
        "--initialization-timeout-seconds",
        type=float,
        default=DEFAULT_INITIALIZATION_TIMEOUT_SECONDS,
    )
    parser.add_argument("--prepare-models", action="store_true")
    parser.add_argument("--run", action="store_true")
    parser.add_argument("--validate-only", action="store_true")
    return parser


def validate_static_contract(args: argparse.Namespace) -> None:
    if DEFAULT_RECOGNITION_MODEL != "arabic_PP-OCRv5_mobile_rec":
        raise RuntimeError("Persian recognition model pin changed.")
    if DEFAULT_DETECTION_MODEL != "PP-OCRv5_mobile_det":
        raise RuntimeError("Detection model pin changed.")
    if DEFAULT_DEVICE != "gpu:0":
        raise RuntimeError("Benchmark device pin changed.")
    if args.warm_runs < 1 or args.warm_runs > 20:
        raise ValueError("warm-runs must be between 1 and 20.")
    if args.inference_timeout_seconds <= 0:
        raise ValueError("Inference timeout must be positive.")
    if args.initialization_timeout_seconds <= 0:
        raise ValueError("Initialization timeout must be positive.")


def main() -> int:
    parser = build_parser()
    args = parser.parse_args()
    validate_static_contract(args)

    if args.validate_only:
        print("M4.2.2 OCR benchmark runner validation: PASS")
        return 0

    if args.prepare_models == args.run:
        parser.error("Choose exactly one of --prepare-models or --run.")

    benchmark_root = Path(args.benchmark_root).resolve()
    benchmark_root.mkdir(parents=True, exist_ok=True)

    if args.run:
        if not args.manifest:
            parser.error("--manifest is required with --run.")
        result = run_benchmark(args)
    else:
        result = prepare_models(args)

    output_path = write_aggregate_result(benchmark_root, result)

    print("M4.2.2 OCR BENCHMARK: PASS")
    print(json.dumps(result, ensure_ascii=True, indent=2))
    print(f"aggregate_result={output_path}")
    return 0


if __name__ == "__main__":
    mp.freeze_support()
    raise SystemExit(main())
