# ADR 0013: Evidence-gated OCR benchmark protocol

- Status: Accepted
- Date: 2026-09-23
- Accepted: 2026-09-23
- Supersedes: none
- Extends: ADR 0003, ADR 0012

## Context

M4.1 accepted a geometry-only region-of-interest planner. M4.2 is the first
milestone allowed to choose and integrate an OCR backend.

The project requires useful OCR for Persian, English, and mixed Persian-English
desktop text while preserving the existing privacy model:

- OCR must remain local;
- product OCR requires `CapturePixels + RunOcr` in the same current epoch;
- OCR receives only an allowed bounded ROI;
- stale/cancelled work is not published;
- raw OCR text is never logged;
- an empty or rejected ROI must not silently widen to a full frame;
- backend names are not architectural commitments until measured on the fixed
  client/server hardware.

The fixed client has an Intel i7-6700K, 32 GB RAM, and AMD Radeon R9 M395X.
The fixed local AI server has an Intel i5-12450HX, 16 GB RAM, and an RTX 3050
Laptop GPU with 6 GB VRAM.

Public-source review on 2026-09-23 narrows the benchmark set without selecting a
winner:

1. Microsoft's newer Windows AI Text Recognition API remains NPU-only. The
   fixed machines do not have the required NPU, so this API is excluded from the
   current target-hardware benchmark rather than treated as a failing candidate.
   Source:
   https://learn.microsoft.com/windows/ai/apis/text-recognition
2. Legacy `Windows.Media.Ocr.OcrEngine` remains available on ordinary Windows
   systems. Its usable recognizer languages depend on OCR language packs
   installed on the device, so Persian/English eligibility must be measured from
   `AvailableRecognizerLanguages` on the target machine.
   Sources:
   https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine
   https://learn.microsoft.com/en-us/uwp/api/windows.media.ocr.ocrengine.availablerecognizerlanguages
3. Tesseract 5 is a CPU-oriented local baseline with official `eng` and
   `fas` traineddata. The official project exposes `tessdata_fast` as the
   speed-oriented model set and `tessdata_best` as the slower
   accuracy-oriented set. Both may be benchmarked, but no Tesseract variant is
   accepted before target-hardware evidence.
   Sources:
   https://github.com/tesseract-ocr/tessdoc/blob/main/Data-Files.md
   https://github.com/tesseract-ocr/tessdata_fast
   https://github.com/tesseract-ocr/tessdata_best
4. PaddleOCR PP-OCRv5 exposes multilingual OCR including a mobile Arabic-script
   recognition model supporting Persian and English. It is therefore a valid
   benchmark candidate, especially for the local server GPU path, but it is not
   selected until full-pipeline accuracy, latency, memory, packaging, and
   cancellation are measured.
   Source:
   https://github.com/PaddlePaddle/PaddleOCR/blob/main/docs/version3.x/algorithm/PP-OCRv5/PP-OCRv5_multi_languages.en.md

## Decision

Split M4.2 into evidence-first slices.

### M4.2.1 — benchmark contract and target-machine preflight

Before installing or integrating any OCR engine:

- add portable OCR scoring for strict NFC-normalized character error rate (CER),
  word error rate (WER), and exact normalized match;
- add a one-command, non-elevated Windows preflight that records only hardware,
  runtime/version, and language-availability metadata;
- do not capture pixels, execute OCR, download packages/models, modify language
  packs, or make a backend selection during preflight;
- keep the evidence under ignored `.localcopilot/benchmarks`;
- parse and validate the preflight runner in CI before physical use.

The initial candidate groups are:

- `windows-media-ocr`;
- `tesseract-5-fas-eng`;
- `paddleocr-ppocrv5-fa-en`.

The newer Windows AI Text Recognition API is recorded as excluded by the fixed
hardware requirement, not as an OCR candidate.

### M4.2.2 — controlled benchmark

After preflight determines what is available or must be installed explicitly,
benchmark the viable candidates using local-only fixtures and exact target
hardware.

The minimum benchmark matrix must cover:

- Persian;
- English;
- mixed Persian-English technical text;
- terminal/console style;
- dialog style;
- browser UI style;
- desktop application UI style.

The benchmark corpus stays local and must not be committed if it contains real
screenshots. Ground-truth text and OCR output are content-bearing benchmark
data and must not enter normal diagnostic logs or PR evidence.

For each candidate/configuration record content-free aggregates only:

- strict CER;
- strict WER;
- exact-match rate;
- cold initialization latency;
- warm per-ROI latency with warmup excluded;
- p50 and p95 latency;
- process working-set delta or peak where measurable;
- GPU VRAM delta/peak where measurable;
- package/model footprint;
- language/model configuration;
- cancellation/timeout behavior;
- failure count.

Benchmark reports may contain sample IDs/categories and aggregate metrics, but
not OCR text, screenshot pixels, local usernames, or arbitrary filesystem paths.

### M4.2.3 — backend selection and product integration

No backend is integrated into the product until the benchmark evidence is
reviewed.

The selected backend must satisfy all hard gates:

- Persian and English support, including the mixed benchmark set;
- fully local execution for the chosen topology;
- no silent network/model download during product operation;
- bounded ROI input only;
- same-epoch `CapturePixels + RunOcr` revalidation before OCR;
- cancellation/timeout with stale result rejection;
- bounded memory/queue ownership and deterministic teardown;
- no OCR text in diagnostics;
- packaging that can be reproduced on the target deployment.

If OCR runs on the local AI server, sending ROI pixels across the LAN additionally
requires the existing `SendPixelsToLocalServer` capability and an explicit
authenticated local transport design. The benchmark itself does not grant or
implement that egress path.

## Scoring rules

Benchmark text scoring is deliberately strict:

- normalize line endings;
- normalize Unicode to NFC;
- trim outer whitespace only;
- preserve internal whitespace;
- compute CER over Unicode code points, not UTF-16 code units;
- compute WER over Unicode-whitespace-separated tokens;
- do not silently canonicalize Persian/Arabic letter variants or digits in the
  primary score.

A later secondary readability metric may be added, but it must never replace
the strict score or hide script/digit substitutions.

## Consequences

- Candidate availability is measured before dependency installation.
- Windows Media OCR may be rejected early if Persian is not actually available
  on the target Windows installation.
- Tesseract and PaddleOCR remain replaceable benchmark candidates rather than
  product dependencies.
- The benchmark can compare client CPU and server GPU evidence without deciding
  topology in advance.
- Real OCR integration remains blocked until controlled physical measurements
  exist.
- No change is made to M4.1 ROI budgets, privacy capabilities, or product
  defaults.

## Acceptance for M4.2.1

M4.2.1 is accepted only when:

- portable scoring tests pass on Ubuntu and Windows;
- the PowerShell preflight parses under Windows PowerShell 5.1 and
  `-ValidateOnly` passes in CI;
- the strict packaged WinUI build remains green;
- the preflight is executed on the physical client and its content-free summary
  is reviewed;
- server preflight is executed before any server/GPU candidate is used in the
  controlled benchmark;
- no backend selection is claimed from preflight alone.

## M4.2.1 acceptance evidence

Accepted implementation/preflight head:
`d8f12b00524934138115f22cc8bd7149e20f4452`.

[CI #137](https://github.com/tahazarif10/local-ai-desktop-copilot/actions/runs/35854783290)
passed the portable Core suite, Windows Core suite, Windows PowerShell parsing,
the M4.2 preflight `-ValidateOnly` gate, prior M3.4 runner/provider regressions,
and the strict WinUI `Debug/win-x64` build.

Physical client preflight on clean head `ec7b5de740e7825835c5c6bb16bb3f13f5111d02`
reported i7-6700K, 31.9 GiB RAM, AMD R9 M395X, legacy Windows OCR with only
`en-US`, no Tesseract, Python 3.14.7, and no Paddle/PaddleOCR. No content was
captured or OCR executed.

Physical server preflight on clean head `d8f12b00524934138115f22cc8bd7149e20f4452`
reported i5-12450HX, 15.7 GiB RAM, RTX 3050 6GB Laptop GPU, NVIDIA driver
596.49, legacy Windows OCR with only `en-US`, no Tesseract, Python 3.14.0b2,
and no Paddle/PaddleOCR. Python 3.14 is explicitly classified unsupported for
the current Paddle Windows wheel requirement. No content was captured or OCR
executed.

The two preflights therefore establish environment facts only. They do not
select an OCR backend or topology. M4.2.2 controlled benchmarking remains
required.


## M4.2.2 acceptance evidence

The controlled benchmark slice completed on the fixed server using the local seven-category OS-rendered corpus. Raw screenshots, ground truth, and OCR hypotheses remained local; repository and PR evidence contains aggregate metrics only.

Measured multilingual candidates:

- PaddleOCR `PP-OCRv5_mobile_det + arabic_PP-OCRv5_mobile_rec`: CER 0.28873239, WER 0.31343284, warm p50 43.513 ms.
- PaddleOCR `PP-OCRv5_server_det + arabic_PP-OCRv5_mobile_rec`: CER 0.23004695, WER 0.22388060, warm p50 91.392 ms, measured GPU VRAM delta 562 MiB.
- Tesseract 5.5.3 `fas+eng` with pinned `tessdata_fast`: CER 0.50938967, WER 1.0, warm p50 285.938 ms.
- Tesseract 5.5.3 `fas+eng` with pinned `tessdata_best`: CER 0.52347418, WER 1.07462687, warm p50 397.424 ms.

Legacy Windows Media OCR was retained only as English-only evidence because the fixed machines expose `en-US` but no Persian recognizer. Its matched three-sample English run measured CER 0.31963470, WER 0.39285714, and 6.732 ms recognition p50 / 12.162 ms end-to-end p50.

The bounded Paddle tuning pass showed that the server detector, not the English-specific recognizer alone, produced the material accuracy improvement. The multilingual server-detector pair is therefore the evidence-backed selection candidate for M4.2.3.

M4.2.2 closes candidate benchmarking without integrating a backend. M4.2.3 must make the explicit backend/topology selection, reapply M4.1 ROI and same-epoch capability gates, prove cancellation/stale rejection and deterministic teardown in product integration, and preserve the recorded cuDNN compatibility warning until an upstream-compatible runtime changes the evidence.
