# M4.2.2 Controlled OCR benchmark

This document defines the first content-bearing OCR benchmark slice after the accepted M4.2.1 preflight.

## Fixed evidence from M4.2.1

- Client: Intel i7-6700K, 31.9 GiB RAM, AMD Radeon R9 M395X.
- Server: Intel i5-12450HX, 15.7 GiB RAM, NVIDIA RTX 3050 Laptop GPU 6 GB, driver 596.49.
- Legacy Windows OCR is installed on both machines but currently exposes only `en-US`; it is therefore an English-only baseline unless a separate Persian language-pack change is approved.
- Tesseract is absent on both machines.
- Paddle/PaddleOCR is absent on both machines.
- Existing Python 3.14 installations are outside the current supported Windows range for the pinned Paddle benchmark and must not be modified.

## First controlled environment

The first prepared environment is the **server-local PaddleOCR GPU candidate**.

Pinned benchmark dependencies:

- isolated Python 3.12;
- `paddlepaddle-gpu==3.2.0`;
- CUDA 12.6 Paddle wheel index;
- `paddleocr==3.7.0`;
- `arabic_PP-OCRv5_mobile_rec` explicitly pinned for Persian/mixed recognition; the official PP-OCRv5 multilingual model table lists Persian and English support for this model.

The server driver 596.49 is above PaddlePaddle's documented Windows minimum for the CUDA 12.6 wheel family (550.54.14). The setup script therefore permits that candidate only on `MachineRole=Server`.

The setup prefers Python Install Manager `py install --target`. When the machine only exposes the legacy launcher, it now uses the official CPython `python` NuGet package 3.12.10, which Python's own Windows documentation recommends for CI/build scenarios and allows to be placed under an arbitrary output directory. The NuGet CLI and Python runtime are both kept below ignored `.localcopilot/ocr-benchmark`; the existing Python installations on C: are not modified.

## Setup command

From a clean non-elevated PowerShell on the server and the exact feature branch:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-benchmark-setup.ps1 -MachineRole Server -PreparePaddleGpu -BenchmarkRoot D:\LocalAI-Prerequisites
```

The setup step downloads benchmark dependencies but does **not** run OCR, create benchmark screenshots, or select a backend. `-BenchmarkRoot` may point outside the repository; on the fixed server use `D:\LocalAI-Prerequisites` so benchmark prerequisites stay off C: and are not mixed with source files.

## Accepted environment evidence

Physical server acceptance on exact clean head `f8275451084af12637e66169ac3c3c06bf78f7cb`:

- CI #166 PASS on the same head;
- root `D:\LocalAI-Prerequisites`;
- Python 3.12.10;
- PaddlePaddle GPU 3.2.0;
- PaddleOCR 3.7.0;
- NVIDIA driver 596.49;
- `gpu:0`;
- CUDA compiled = true;
- no product/system Python mutation;
- no OCR or benchmark content created;
- no backend selected.

## Controlled runner

The runner is split into two entry points:

- `run-m4-2-ocr-benchmark.ps1` is the Windows/branch/privacy wrapper.
- `scripts/m4_2_ocr_benchmark.py` owns the local OCR worker, strict scoring, timeout guard, resource sampling, and aggregate result generation.

On the fixed server, all benchmark state lives below:

```text
D:\LocalAI-Prerequisites
```

The wrapper supports three physical modes:

```powershell
.\run-m4-2-ocr-benchmark.ps1 -InitializeCorpus
.\run-m4-2-ocr-benchmark.ps1 -PrepareModels
.\run-m4-2-ocr-benchmark.ps1 -Run
```

`-InitializeCorpus` creates seven opaque local sample IDs and empty local ground-truth files without capturing any pixels. The user populates bounded ROI PNGs and exact visible-text ground truth locally. Full-screen screenshots are not part of the corpus contract.

`-PrepareModels` does not read corpus content. It pins `PP-OCRv5_mobile_det` plus `arabic_PP-OCRv5_mobile_rec`, forces `gpu:0`, disables document orientation/unwarping/text-line orientation extras, and directs the PaddleX model cache to `D:\LocalAI-Prerequisites\models`.

`-Run` validates all seven categories, rejects absolute/path-escaping corpus entries, keeps OCR hypotheses in memory only, and writes aggregate metrics without raw content.

## Zero-touch controlled OS-rendered corpus

`run-m4-2-ocr-controlled-ui.ps1` is the normal physical entry point for this gate. It replaces the earlier synthetic bitmap smoke corpus with seven **visible, OS-rendered WinForms client-area captures** covering the same required categories, writes exact ground truth only below the local benchmark root, records local provenance, and immediately invokes the aggregate-only benchmark runner.

The harness is deliberately distinct from `run-m4-2-ocr-auto-smoke.ps1`: the smoke runner proves pipeline determinism using direct bitmap text generation, while the controlled-UI runner exercises actual Windows font shaping, DPI, controls, compositing, and screen capture. It still represents a controlled fixture corpus rather than uncontrolled user content, so its evidence is suitable for candidate measurement without leaking real application text.

From a clean, non-elevated PowerShell on the fixed server:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-controlled-ui.ps1 -BenchmarkRoot D:\LocalAI-Prerequisites
```

No manual screenshot or ground-truth editing is required. The command rewrites only the seven local corpus sample files and manifest; model/cache/result directories are preserved. It never prints ground-truth strings and never performs a full-screen capture.

## Controlled benchmark corpus

Real benchmark content stays local below ignored `.localcopilot/ocr-benchmark`.

Minimum categories:

1. Persian UI text.
2. English UI text.
3. Mixed Persian-English technical UI text.
4. Terminal/console text.
5. Dialog text.
6. Browser UI text.
7. Desktop application UI text.

Each local sample must have a stable opaque sample ID, category, ROI image, and exact ground-truth text. PR evidence must never contain the screenshot, OCR output, or ground truth. The preferred acceptance corpus for this branch is generated by the zero-touch OS-rendered harness above; manual corpus population is no longer required for the controlled fixture gate.


## First controlled PaddleOCR physical result

The zero-touch OS-rendered corpus was executed on the fixed server at clean head `9fa4ba26daf4becc011f6e1ccc6cf7eeb81d0156` after CI #181 passed.

Aggregate-only physical evidence:

- seven samples / all seven required categories;
- failures: 0;
- timeouts: 0;
- timeout guard self-test: PASS;
- strict CER: 0.28873239;
- strict WER: 0.31343284;
- exact normalized match rate: 0.0;
- warm latency p50: 43.513 ms;
- warm latency p95: 78.132 ms;
- warm latency max: 80.619 ms;
- cold initialization: 3389.084 ms;
- worker RSS peak: 1494.766 MiB;
- GPU VRAM baseline/peak/delta: 275 / 499 / 224 MiB;
- Python runtime footprint: 3762.326 MiB;
- model cache footprint: 12.511 MiB;
- no raw OCR text logged or persisted;
- backend remains unselected.

The run also reproduced the environment warning that Paddle is compiled against cuDNN 9.9 while the installed/runtime cuDNN package is 9.5.1.17 / 90501. The benchmark completed successfully, so this is recorded as a compatibility risk rather than silently treated as resolved.

This result is sufficient evidence that the pinned Paddle pipeline executes locally and within the measured latency/resource envelope on the controlled Windows-rendered corpus. Its accuracy is not sufficient, by itself, to select the product backend. Candidate comparison remains required.

## Tesseract comparison baseline

## Tesseract controlled physical result

The pinned Tesseract 5.5.3 `fas+eng` baseline was executed on the exact existing controlled OS-rendered corpus at clean head `3a2386442b357a92829f619c0b12250b6b394179`.

Aggregate-only physical evidence:

- seven samples / all seven required categories;
- failures: 0;
- timeouts: 0;
- strict CER: 0.50938967;
- strict WER: 1.0;
- exact normalized match rate: 0.0;
- warm latency p50: 285.938 ms;
- warm latency p95: 651.489 ms;
- warm latency max: 656.062 ms;
- cold first OCR: 489.071 ms;
- process RSS peak: 38.27 MiB;
- install footprint: 110.986 MiB;
- `tessdata_fast` footprint: 4.334 MiB;
- raw OCR text logged/persisted: false;
- backend remains unselected.

On this shared full seven-category corpus, PaddleOCR has the lower aggregate error and lower warm latency: Tesseract CER is about 1.76x Paddle CER, Tesseract WER is about 3.19x Paddle WER, and Tesseract warm p50 latency is about 6.57x Paddle p50 latency. These are descriptive measurements on this controlled corpus, not a backend-selection claim.


The next controlled candidate reuses the **exact same local corpus** rather than recapturing it. The baseline is pinned to Tesseract 5.5.3, `fas+eng`, LSTM OEM 1, PSM 6, and `tessdata_fast` at commit `87416418657359cb625c412a48b6e1d6d41c29bd`.

The one-command physical entry point is:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-tesseract.ps1 -BenchmarkRoot D:\LocalAI-Prerequisites
```

If Tesseract is absent, the wrapper first uses Windows Package Manager when available. If `winget.exe` is unavailable, it downloads the pinned official Tesseract 5.5.3 installer directly from the upstream GitHub release, verifies its SHA-256 against the pinned Microsoft winget manifest value, and launches the verified installer silently. Because the package is machine-scope, Windows may require a UAC confirmation. The benchmark itself remains non-elevated. Persian and English traineddata are downloaded into the benchmark root and raw OCR output remains memory-only.


## Windows Media OCR English-only baseline

The fixed machines expose legacy Windows Media OCR only for `en-US`, so this candidate is not eligible for the Persian or mixed Persian-English portions of the seven-category matrix. It is retained as a descriptive English-only baseline rather than scored as a failing multilingual engine.

The runner reuses the exact existing controlled corpus and selects only these categories:

- `english-ui`;
- `terminal-console`;
- `browser-ui`.

It never recaptures pixels and never prints or persists OCR text. The first hypothesis for each sample is passed in-memory to the strict scorer helper; only aggregate metrics are emitted.

Physical entry point:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-windows-media.ps1 -BenchmarkRoot D:\LocalAI-Prerequisites
```

Because this baseline covers only three English-only samples, its accuracy numbers are not directly comparable to the full seven-category PaddleOCR/Tesseract aggregates without a matched subset rerun. Its purpose is to retain platform-native English evidence and confirm the target-machine limitation that Persian support is absent.


## Windows Media OCR physical result

The corrected WinRT bridge completed successfully on the fixed server at clean head `563212ed9f8712057d25f6af1fc1d77893224cb3`.

English-only aggregate evidence:

- scope: `english-ui`, `terminal-console`, `browser-ui`;
- sample count: 3;
- failures: 0;
- timeouts: 0;
- strict CER: 0.31963470;
- strict WER: 0.39285714;
- exact normalized match rate: 0.0;
- warm recognition p50/p95/max: 7.028 / 7.619 / 7.667 ms;
- warm end-to-end p50/p95: 12.235 / 22.830 ms;
- cold engine creation: 2.378 ms;
- cold first OCR / end-to-end: 15.944 / 71.798 ms;
- raw OCR text logged/persisted: false;
- Persian support: false;
- mixed Persian-English eligibility: false;
- backend remains unselected.

These English-only accuracy numbers are not directly comparable with the seven-category PaddleOCR/Tesseract aggregates. A final matched-subset run therefore executes all three candidates on the same three English samples before the comparison gate is closed.

The one-command matched comparison is:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-english-comparison.ps1 -BenchmarkRoot D:\LocalAI-Prerequisites
```

This command reuses the existing corpus; it does not recapture pixels or modify ground truth.


## Matched English-subset physical comparison

The one-command matched comparison passed at clean head `133a6fba18731371f86ff8d26916ba974dcdbcf7` on the same three controlled samples: `english-ui`, `terminal-console`, and `browser-ui`.

| Candidate | CER | WER | Exact | Warm p50 |
| --- | ---: | ---: | ---: | ---: |
| PaddleOCR PP-OCRv5 multilingual | 0.28310502 | 0.35714286 | 0.0 | 52.599 ms |
| Tesseract 5.5.3 `fas+eng` | 0.37442922 | 0.92857143 | 0.0 | 280.316 ms |
| Windows Media OCR `en-US` | 0.31963470 | 0.39285714 | 0.0 | 6.732 ms recognition / 12.162 ms end-to-end |

All three completed with zero failures and zero timeouts; none logged or persisted raw OCR output.

On this matched subset, PaddleOCR measured the lowest CER and WER, Tesseract measured both higher error and higher latency, and Windows Media OCR measured much lower latency but remains English-only. This evidence still does **not** select a backend: PaddleOCR's absolute error remains high enough to justify one bounded model/configuration tuning pass before M4.2.2 is closed.


## Bounded Paddle model/configuration tuning pass

The candidate comparison leaves PaddleOCR as the only currently measured engine with Persian support and materially better multilingual evidence than the Tesseract baseline, but the absolute strict error remains too high to select it without one bounded tuning pass.

The tuning matrix is intentionally small and evidence-driven:

1. `PP-OCRv5_server_det + arabic_PP-OCRv5_mobile_rec` on the full seven-category corpus. PaddleOCR documents the server detector as the higher-accuracy PP-OCRv5 detection model for high-performance servers, while retaining the multilingual recognizer required for Persian.
2. `PP-OCRv5_mobile_det + en_PP-OCRv5_mobile_rec` on the matched English-only subset. The official multilingual documentation identifies the English PP-OCRv5 recognizer as an English-optimized model with higher recognition accuracy for English scenarios.
3. `PP-OCRv5_server_det + en_PP-OCRv5_mobile_rec` on the same matched English-only subset to isolate whether the more accurate detector adds useful desktop-UI accuracy.

Official references:

- https://github.com/PaddlePaddle/PaddleOCR/blob/main/docs/version3.x/module_usage/text_detection.en.md
- https://github.com/PaddlePaddle/PaddleOCR/blob/main/docs/version3.x/algorithm/PP-OCRv5/PP-OCRv5_multi_languages.en.md

No server recognition model is substituted for Persian because the measured multilingual requirement remains Persian + English. The tuning pass does not expand into arbitrary model search, PP-OCRv6, custom training, preprocessing heuristics, or product integration.

One-command physical entry point:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-paddle-variants.ps1 -BenchmarkRoot D:\LocalAI-Prerequisites
```

The command reuses the existing controlled corpus and local model cache, prepares only the pinned variant models, and emits aggregate-only evidence.


## Bounded Paddle variant physical result

The bounded Paddle variant matrix passed at clean head `3c4f300ec852a681fa5f0a5fda0587e8027ba393` on the existing controlled corpus.

Full seven-category multilingual comparison:

| Configuration | CER | WER | Exact | Warm p50 | Warm p95 | GPU VRAM delta | Model cache |
| --- | ---: | ---: | ---: | ---: | ---: | ---: | ---: |
| `PP-OCRv5_mobile_det + arabic_PP-OCRv5_mobile_rec` | 0.28873239 | 0.31343284 | 0.0 | 43.513 ms | 78.132 ms | 224 MiB | 12.511 MiB |
| `PP-OCRv5_server_det + arabic_PP-OCRv5_mobile_rec` | 0.23004695 | 0.22388060 | 0.0 | 91.392 ms | 117.272 ms | 562 MiB | 96.775 MiB |

The server detector reduced strict CER by about 20.3% and strict WER by about 28.6% relative to the original multilingual configuration, at roughly 2.10x warm p50 latency and about 2.51x GPU VRAM delta. The measured VRAM delta remains below 1 GiB on the fixed 6 GiB server GPU.

Matched English-subset variant evidence:

| Configuration | CER | WER | Exact | Warm p50 |
| --- | ---: | ---: | ---: | ---: |
| mobile detector + multilingual recognizer | 0.28310502 | 0.35714286 | 0.0 | 52.599 ms |
| mobile detector + English recognizer | 0.28310502 | 0.35714286 | 0.0 | 49.892 ms |
| server detector + English recognizer | 0.21461187 | 0.21428571 | 0.0 | 94.660 ms |

The English-specific recognizer did not improve strict accuracy when the mobile detector was held constant. The server detector again produced the material accuracy change. The evidence therefore points to `PP-OCRv5_server_det + arabic_PP-OCRv5_mobile_rec` as the strongest measured Paddle multilingual configuration, without yet selecting the product backend.

The cuDNN 9.9-compiled versus 9.5.1.17 runtime warning remained present in every GPU run. An open upstream Paddle issue reports the same warning on official Windows cu126 wheels while `pip check`, Paddle verification, and inference still succeed; no upstream resolution is recorded there. The benchmark environment therefore remains pinned rather than manually overriding Paddle's cuDNN dependency:
https://github.com/PaddlePaddle/Paddle/issues/79388

## Final accuracy-focused Tesseract check

Before backend selection, one final bounded baseline uses official `tessdata_best` rather than the already measured speed-oriented `tessdata_fast`. This is warranted because ADR 0013 explicitly identifies both official model sets and `tessdata_best` is the accuracy-oriented Tesseract option.

Pinned source:

- repository: `tesseract-ocr/tessdata_best`;
- commit: `e12c65a915945e4c28e237a9b52bc4a8f39a0cec`;
- language set: `fas+eng`;
- Tesseract: 5.5.3;
- OEM: 1;
- PSM: 6.

Physical command:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-tesseract.ps1 -BenchmarkRoot D:\LocalAI-Prerequisites -TessdataVariant best
```

It reuses the same seven local samples and emits aggregate-only evidence. If this accuracy-focused baseline still does not materially close the gap to the strongest multilingual Paddle configuration, M4.2.2 has enough candidate evidence to proceed to backend-selection review rather than expanding into an open-ended benchmark search.


## Final Tesseract accuracy-focused physical result

The pinned `tessdata_best` run passed on the same seven-sample controlled corpus at clean head `4ffac27a29e8260ca958f4c8ba5f703de88493c7`.

Aggregate evidence:

- candidate: Tesseract 5.5.3, `fas+eng`, OEM 1, PSM 6, `tessdata_best`;
- sample count: 7 / all required categories;
- failures: 0;
- timeouts: 0;
- strict CER: 0.52347418;
- strict WER: 1.07462687;
- exact normalized match rate: 0.0;
- warm p50/p95/max: 397.424 / 791.157 / 812.740 ms;
- cold first OCR: 454.762 ms;
- process RSS peak: 58.031 MiB;
- tessdata footprint: 17.859 MiB;
- raw OCR log/persistence: false.

This accuracy-oriented Tesseract dataset did not improve the controlled result relative to `tessdata_fast` (CER 0.50938967, WER 1.0, warm p50 285.938 ms). It therefore does not justify expanding the Tesseract search further for M4.2.2.

## M4.2.2 benchmark conclusion

The controlled candidate search is complete.

Full multilingual seven-category evidence:

| Candidate/configuration | CER | WER | Warm p50 | Key constraint |
| --- | ---: | ---: | ---: | --- |
| Paddle `mobile_det + arabic_mobile_rec` | 0.28873239 | 0.31343284 | 43.513 ms | lower GPU/resource cost |
| Paddle `server_det + arabic_mobile_rec` | **0.23004695** | **0.22388060** | 91.392 ms | 562 MiB measured VRAM delta |
| Tesseract 5.5.3 `tessdata_fast fas+eng` | 0.50938967 | 1.00000000 | 285.938 ms | CPU-only |
| Tesseract 5.5.3 `tessdata_best fas+eng` | 0.52347418 | 1.07462687 | 397.424 ms | CPU-only, larger/slower |

Legacy Windows Media OCR remains an English-only platform baseline and is not eligible for the Persian/mixed hard gate.

The evidence supports carrying `PP-OCRv5_server_det + arabic_PP-OCRv5_mobile_rec` forward as the M4.2.3 selection candidate. M4.2.2 itself does not integrate or mark a product backend selected; that architecture/product decision belongs to M4.2.3.

The selected-candidate evidence already includes deterministic timeout-guard PASS for the Paddle runner, zero physical failures/timeouts, bounded local model/cache ownership, aggregate-only diagnostics, and local GPU execution. The repeated cuDNN version warning remains an explicit compatibility risk to preserve and monitor during integration acceptance.

## Required aggregate measurements

For every engine/configuration:

- strict Unicode-code-point CER;
- strict WER;
- exact normalized match rate;
- cold initialization latency;
- warm per-ROI latency, including p50 and p95;
- failure count;
- cancellation/timeout result;
- working-set delta/peak where measurable;
- GPU VRAM delta/peak where measurable;
- package/model footprint;
- engine/model/language configuration.

## Candidate order

1. Prepare and validate PaddleOCR GPU on the fixed server.
2. Build the benchmark runner/corpus against that environment.
3. Add a Tesseract 5 Persian+English baseline through a separately approved Windows package installation.
4. Retain legacy Windows OCR as English-only evidence unless Persian OCR language support is explicitly added.
5. Compare aggregate evidence before accepting any backend/topology.

No result from a single engine is sufficient for product selection.

## Privacy and product boundary

M4.2.2 is benchmark infrastructure only.

- No product runtime OCR wiring.
- No LAN pixel transport.
- No change to `CapturePixels`, `RunOcr`, or `SendPixelsToLocalServer`.
- No whole-frame fallback.
- No benchmark content in normal diagnostics or Git.
- No backend selection until the candidate comparison is reviewed.

Official references used for the pinned setup are retained in ADR 0013. PaddlePaddle currently documents `paddlepaddle-gpu==3.2.0` with the CUDA 12.6 index for Windows drivers at or above 550.54.14; PaddleOCR 3.7.0 is the pinned package version for this benchmark branch.
