# 🔒 Lokal Deşifre İstasyonu

**A fully offline, privacy-first transcription and anonymization platform for qualitative research.**

Built for a psychology academic who needed to transcribe and anonymize clinical interview recordings — without a single byte of audio, video, or personal data ever leaving the machine. No cloud APIs, no third-party AI services, no compliance gray areas. Just local compute, doing exactly what it's told.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.14-3776AB?logo=python&logoColor=white)](https://www.python.org/)
[![React](https://img.shields.io/badge/React-18-61DAFB?logo=react&logoColor=black)](https://react.dev/)
[![Tauri](https://img.shields.io/badge/Tauri-2-FFC131?logo=tauri&logoColor=black)](https://tauri.app/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](#license)

---

## 📥 Just want to try it?

This repo is the **source code** (for developers). If you just want to *use* the app, download the ready-to-run desktop build (no setup, no dependencies) here:

**[⬇ Download the packaged desktop app (Google Drive)](https://drive.google.com/file/d/1MiTRgsg8HqNhFnOYYFSwiYN1ORz3HJ6_/view?usp=sharing)**

Unzip, double-click `Baslat.vbs`, done. See [Kullanim-Kilavuzu.txt](./Kullanim-Kilavuzu.txt) (Turkish user guide) for details.

---

## Why this exists

Academic and clinical researchers routinely record interviews containing sensitive personal information — names, locations, medical details. Commercial transcription APIs (Whisper API, AssemblyAI, Otter.ai, etc.) require uploading that audio to a third party, which is frequently disallowed by ethics-committee approvals and data-protection regulations (GDPR, and in this case Turkey's KVKK).

**Lokal Deşifre İstasyonu** solves this by running the entire pipeline — speech-to-text, speaker diarization, and PII redaction — on a single local machine, using only free, open-source models, packaged as a standalone desktop app. The only network calls it ever makes are one-time model downloads from Hugging Face on first setup; after that, it is provably offline.

## Key Features

- 🎙️ **Speech-to-text** via [faster-whisper](https://github.com/SYSTRAN/faster-whisper) (`large-v3`), GPU-accelerated with automatic CPU fallback if no compatible NVIDIA GPU is present — the app degrades gracefully instead of crashing on unsupported hardware.
- 🕵️ **Automatic PII redaction** — a Turkish NER model detects and censors person names and locations in-line, before the transcript ever touches disk.
- 🗣️ **Optional speaker diarization** (pyannote.audio) — labels turns as `[Konuşmacı 1]`, `[Konuşmacı 2]`, etc., running in parallel with the GPU transcription pass so it doesn't add to wall-clock time. Off by default, opt-in per job.
- 🧩 **Silence-aware chunking for long recordings** — audio is split at real silence boundaries (via `ffmpeg silencedetect`), never mid-word, keeping multi-hour files stable on modest hardware. Whisper's own context (`initial_prompt`) is carried across chunk boundaries to preserve sentence continuity.
- ⚙️ **Async, cancellable job processing** — uploads return immediately (`202 Accepted`); a background queue processes jobs one at a time, reports live progress (real percentage, not a fake animation), and can be cancelled mid-run.
- 📚 **Searchable job history** — every past job is browsable, searchable by filename, paginated, and deletable (two-step confirmation) without touching the database by hand.
- 🖥️ **Native desktop app** — React + Tailwind UI wrapped in Tauri, packaged alongside a self-contained .NET API and a PyInstaller-bundled AI engine into a single portable folder. One shortcut, no terminal, no installed runtimes required on the target machine.
- 📄 **Research-ready exports** — `.srt` subtitles, and `.docx` / `.txt` / `.xlsx` transcripts (both redacted and original), generated client-side, ready to drop into MAXQDA / NVivo.
- 🔐 **Defense-in-depth privacy** — the local API is bound to `127.0.0.1` only, redacted text is the default persisted artifact, and the original (unredacted) transcript is an explicit opt-in export with an in-app warning label.

## Architecture

Clean Architecture on the .NET side, with a fully independent Python microservice for the AI workload — all three (plus the desktop UI) ship as prebuilt executables in the packaged distribution.

```
┌─────────────────────┐        HTTP (multipart)        ┌──────────────────────────┐
│   React + Tauri UI   │ ──────────────────────────────▶│   ASP.NET Core 8 API     │
│  (desktop-ui/)        │◀────────────────────────────── │   Clean Architecture     │
└─────────────────────┘        202 Accepted + polling    │   (src/WebAPI)           │
                                                          │                          │
                                                          │  Domain → Application →  │
                                                          │  Infrastructure → WebAPI │
                                                          └────────┬─────────────────┘
                                                                   │
                                          ┌────────────────────────┼────────────────────────┐
                                          ▼                        ▼                        ▼
                                 ┌────────────────┐      ┌──────────────────┐      ┌─────────────────┐
                                 │     SQLite       │      │  In-memory Queue  │      │  Python FastAPI  │
                                 │ (single .db file, │      │  + BackgroundService│    │  AI microservice │
                                 │  no server needed)│      │  (Channel<Guid>)   │      │  (127.0.0.1:8500)│
                                 └────────────────┘      └──────────────────┘      └────────┬─────────┘
                                                                                              │
                                                                          ┌───────────────────┼───────────────────┐
                                                                          ▼                    ▼                    ▼
                                                                  faster-whisper        Turkish NER          pyannote.audio
                                                              (GPU, auto CPU-fallback)  (BERT, CPU)          (diarization, CPU,
                                                                                                               parallel to whisper)
```

**Backend layers** (`src/WebAPI`):
- `Domain` — entities (`TranscriptionJob`), enums, no external dependencies.
- `Application` — CQRS commands/queries (MediatR), interfaces, DTOs. Knows nothing about EF Core, HTTP, or the queue implementation.
- `Infrastructure` — EF Core + SQLite, the background job queue/worker, the FFmpeg wrapper, and the HTTP client that talks to the Python engine.
- `WebAPI` — controllers, DI composition root, Serilog file logging (no console window in the packaged build).

## Tech Stack

| Layer | Technology |
|---|---|
| Desktop shell | Tauri 2 (Rust), packaged as a single portable folder |
| Frontend | React 18, TypeScript, Vite, Tailwind CSS |
| Backend API | ASP.NET Core 8, Clean Architecture, MediatR (CQRS), self-contained single-file publish |
| Background jobs | In-memory `Channel<Guid>` + `BackgroundService` (no external broker) |
| Database | SQLite — a single file, zero server setup |
| AI engine | FastAPI (Python), faster-whisper, 🤗 Transformers, pyannote.audio, packaged with PyInstaller |
| Media processing | FFmpeg (extraction, silence detection, chunking) |

## Getting Started (building from source)

> Just want to run the app? Use the [prebuilt download](#-just-want-to-try-it) instead — everything below is for building/modifying the source.

### Prerequisites

- .NET 8 SDK
- Python 3.11+ with pip
- Node.js 18+ and Rust (for Tauri)
- FFmpeg (system PATH) + an FFmpeg "shared" build (DLLs, for the diarization audio decoder)
- An NVIDIA GPU with 6GB+ VRAM (CUDA 12.x) for GPU acceleration — fully optional, the engine auto-detects and falls back to CPU if unavailable
- A free [Hugging Face](https://huggingface.co) account + access token (only needed if you want speaker diarization)

### 1. Run the AI engine

```bash
pip install faster-whisper transformers pyannote.audio
pip install nvidia-cublas-cu12==12.4.5.8 nvidia-cudnn-cu12==9.1.0.70 nvidia-nvjitlink-cu12==12.4.127

# optional, only for speaker diarization: accept model terms at
# huggingface.co/pyannote/speaker-diarization-3.1 and
# huggingface.co/pyannote/segmentation-3.0, then:
setx HF_TOKEN "hf_xxx"

python transcribe_censor_service.py
# (or: uvicorn transcribe_censor_service:app --host 127.0.0.1 --port 8500)
```

### 2. Run the API

```bash
cd src/WebAPI
dotnet run
```

The SQLite database and its schema are created automatically on first launch — no manual migration step needed. Swagger UI: `http://localhost:5169/swagger` (development mode only).

### 3. Run the desktop app

```bash
cd desktop-ui
npm install
npm run dev        # browser-based dev server
# or, for the native window:
npx tauri dev
```

### Building the portable distribution

```bash
# AI engine
pip install pyinstaller
pyinstaller transcribe_censor_service.spec

# API (self-contained, no .NET runtime needed on target machine)
cd src/WebAPI
dotnet publish -c Release -r win-x64 --self-contained true -p:PublishSingleFile=true -o publish

# Desktop UI
cd desktop-ui
npx tauri build
```

Combine the three build outputs plus an FFmpeg "shared" build into one folder (see `start-all.bat` / `Baslat.vbs` for the launcher pattern used in this project) to produce a single double-click-to-run package.

## Configuration

| Variable | Where | Purpose |
|---|---|---|
| `HF_TOKEN` | Python env | Hugging Face token for gated diarization models (optional — diarization silently disables without it) |
| `FFMPEG_SHARED_DIR` | Python env | Path to FFmpeg's shared-library build; auto-detected next to the executable if unset |
| `WHISPER_MODEL` | Python env | Swap models without code changes, e.g. `large-v3-turbo` |
| `WHISPER_DEVICE` | Python env | Force `cuda` or `cpu`; auto-detects with graceful fallback if unset |
| `DIARIZATION_DEVICE` | Python env | `cpu` (default, stable) or `gpu` (experimental — see Known Limitations) |
| `ConnectionStrings:Default` | `appsettings.json` | SQLite file path |
| `PythonEngine:BaseUrl` | `appsettings.json` | Where the API reaches the AI engine |

## Privacy & Compliance Notes

This project was built against Turkey's KVKK (data protection law) and standard university ethics-committee requirements for handling clinical interview data:

- No audio, video, or transcript data is ever transmitted to a third-party API.
- The database and the AI engine are both bound exclusively to `127.0.0.1`.
- The default, persisted artifact is the **redacted** transcript; the original is a separate, explicitly-labeled export with an in-app warning.
- Temporary audio files are deleted immediately after processing, success or failure.
- Closing the desktop app terminates the background API and AI engine processes — nothing keeps running unattended.

## Known Limitations / Roadmap

- GPU-accelerated diarization was evaluated and found to add more overhead (model unload/reload per request) than it saved on short files; CPU diarization running in parallel with GPU transcription is the current default and performed better in testing. `DIARIZATION_DEVICE=gpu` remains available for experimentation.
- No checkpoint/resume for partially-completed jobs — a failed multi-hour job restarts from the beginning rather than the last completed chunk.
- Single-machine design by choice (no distributed workers) — matches the target deployment (one researcher, one workstation).
- AMD/Intel GPUs are not accelerated (ctranslate2 has no ROCm/DirectML backend on Windows); the app runs correctly on CPU in that case, just slower.

## License

MIT — see [LICENSE](./LICENSE).

## Author

Built by [Doğukan](https://github.com/dogukankacar) — self-taught full-stack developer based in Denizli, Turkey.
