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
- PP-OCRv5 Persian path (`lang=fa`) for the Persian/mixed benchmark.

The server driver 596.49 is above PaddlePaddle's documented Windows minimum for the CUDA 12.6 wheel family (550.54.14). The setup script therefore permits that candidate only on `MachineRole=Server`.

The setup prefers Python Install Manager `py install --target` to place Python 3.12 below ignored `.localcopilot/ocr-benchmark`. On machines that only have the legacy `py.exe` launcher, it falls back to the official Python 3.12.10 x64 installer from python.org, verifies the pinned SHA-256 and Python Software Foundation Authenticode signer, and installs into the same project-local directory with launcher/PATH/file-association changes disabled. It does not replace or modify the existing Python 3.14 installation.

## Setup command

From a clean non-elevated PowerShell on the server and the exact feature branch:

```powershell
powershell.exe -NoProfile -ExecutionPolicy Bypass -File .\run-m4-2-ocr-benchmark-setup.ps1 -MachineRole Server -PreparePaddleGpu
```

The setup step downloads benchmark dependencies but does **not** run OCR, create benchmark screenshots, or select a backend.

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

Each local sample must have a stable opaque sample ID, category, ROI image, and exact ground-truth text. PR evidence must never contain the screenshot, OCR output, or ground truth.

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
