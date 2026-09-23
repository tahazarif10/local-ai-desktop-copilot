import importlib.util
import json
from pathlib import Path
import tempfile
import unittest
import sys


SCRIPT_PATH = Path(__file__).with_name("m4_2_ocr_benchmark.py")
SPEC = importlib.util.spec_from_file_location("m4_2_ocr_benchmark", SCRIPT_PATH)
BENCH = importlib.util.module_from_spec(SPEC)
sys.modules[SPEC.name] = BENCH
assert SPEC.loader is not None
SPEC.loader.exec_module(BENCH)


class OcrBenchmarkScoringTests(unittest.TestCase):
    def test_exact_persian_english_is_zero_error(self):
        text = "خطای Servo ER01 در Axis A1"
        result = BENCH.score_text(text, text)
        self.assertEqual(0, result.character_edits)
        self.assertEqual(0.0, result.character_error_rate)
        self.assertEqual(0, result.word_edits)
        self.assertEqual(0.0, result.word_error_rate)
        self.assertTrue(result.exact_normalized_match)

    def test_unicode_nfc_and_line_endings_are_normalized(self):
        result = BENCH.score_text(
            "Cafe\u0301\r\nدستگاه",
            "Café\nدستگاه",
        )
        self.assertEqual(0, result.character_edits)
        self.assertEqual(0, result.word_edits)
        self.assertTrue(result.exact_normalized_match)

    def test_persian_substitution_is_one_code_point_edit(self):
        result = BENCH.score_text("فشار جک", "فشار حک")
        self.assertEqual(1, result.character_edits)
        self.assertGreater(result.character_error_rate, 0.0)
        self.assertEqual(1, result.word_edits)
        self.assertFalse(result.exact_normalized_match)

    def test_internal_whitespace_is_preserved_for_exact_match(self):
        result = BENCH.score_text("alpha   beta\tgamma", "alpha beta gamma")
        self.assertEqual(0, result.word_edits)
        self.assertFalse(result.exact_normalized_match)

    def test_empty_reference_rates_match_core_contract(self):
        empty = BENCH.score_text("", "")
        self.assertEqual(0.0, empty.character_error_rate)
        self.assertEqual(0.0, empty.word_error_rate)

        nonempty = BENCH.score_text("", "unexpected")
        self.assertEqual(1.0, nonempty.character_error_rate)
        self.assertEqual(1.0, nonempty.word_error_rate)

    def test_unicode_code_points_not_utf16_units(self):
        result = BENCH.score_text("A😀B", "A😃B")
        self.assertEqual(3, result.reference_code_points)
        self.assertEqual(3, result.hypothesis_code_points)
        self.assertEqual(1, result.character_edits)

    def test_percentile_interpolates(self):
        self.assertAlmostEqual(2.5, BENCH.percentile([1, 2, 3, 4], 0.5))
        self.assertAlmostEqual(3.85, BENCH.percentile([1, 2, 3, 4], 0.95))


class ManifestValidationTests(unittest.TestCase):
    def _write_manifest(self, root: Path, mutate=None):
        images = root / "images"
        truth = root / "ground-truth"
        images.mkdir()
        truth.mkdir()

        samples = []
        for index, category in enumerate(BENCH.REQUIRED_CATEGORIES, start=1):
            sample_id = f"s{index:03d}"
            (images / f"{sample_id}.png").write_bytes(b"not-an-image")
            (truth / f"{sample_id}.txt").write_text(
                f"ground truth {index}",
                encoding="utf-8",
            )
            samples.append(
                {
                    "id": sample_id,
                    "category": category,
                    "image": f"images/{sample_id}.png",
                    "ground_truth_file": f"ground-truth/{sample_id}.txt",
                }
            )

        manifest = {"schema": 1, "samples": samples}
        if mutate is not None:
            mutate(manifest)

        path = root / "manifest.json"
        path.write_text(json.dumps(manifest), encoding="utf-8")
        return path

    def test_manifest_requires_all_categories_and_local_files(self):
        with tempfile.TemporaryDirectory() as temp:
            path = self._write_manifest(Path(temp))
            samples = BENCH.load_manifest(path, require_files=True)
            self.assertEqual(len(BENCH.REQUIRED_CATEGORIES), len(samples))

    def test_manifest_rejects_path_escape(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = self._write_manifest(
                root,
                lambda manifest: manifest["samples"][0].update(
                    {"image": "../escape.png"}
                ),
            )
            with self.assertRaises(ValueError):
                BENCH.load_manifest(path, require_files=False)

    def test_manifest_rejects_missing_category(self):
        with tempfile.TemporaryDirectory() as temp:
            root = Path(temp)
            path = self._write_manifest(
                root,
                lambda manifest: manifest["samples"].pop(),
            )
            with self.assertRaises(ValueError):
                BENCH.load_manifest(path, require_files=False)


if __name__ == "__main__":
    unittest.main()
