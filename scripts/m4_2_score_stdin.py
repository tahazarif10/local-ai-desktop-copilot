#!/usr/bin/env python3
"""Read one OCR hypothesis from stdin and emit aggregate-safe score JSON."""

from __future__ import annotations

import argparse
import json
import sys
from pathlib import Path

from m4_2_ocr_benchmark import score_text


def main() -> int:
    parser = argparse.ArgumentParser()
    parser.add_argument("--truth-file", required=True)
    args = parser.parse_args()

    truth = Path(args.truth_file).read_text(encoding="utf-8-sig")
    hypothesis = sys.stdin.read()
    metrics = score_text(truth, hypothesis)

    print(
        json.dumps(
            {
                "character_edits": metrics.character_edits,
                "reference_code_points": metrics.reference_code_points,
                "word_edits": metrics.word_edits,
                "reference_words": metrics.reference_words,
                "exact_normalized_match": metrics.exact_normalized_match,
            },
            ensure_ascii=True,
            separators=(",", ":"),
        )
    )
    return 0


if __name__ == "__main__":
    raise SystemExit(main())
