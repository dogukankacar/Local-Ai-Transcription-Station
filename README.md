# 🔒 Local Transcription Station

**A fully offline, privacy-first transcription and anonymization platform for qualitative research.**

Built for a psychology academic who needed to transcribe and anonymize clinical interview recordings — without a single byte of audio, video, or personal data ever leaving the machine. No cloud APIs, no third-party AI services, no compliance gray areas. Just local compute, doing exactly what it's told.

[![.NET](https://img.shields.io/badge/.NET-8.0-512BD4?logo=dotnet)](https://dotnet.microsoft.com/)
[![Python](https://img.shields.io/badge/Python-3.14-3776AB?logo=python&logoColor=white)](https://www.python.org/)
[![React](https://img.shields.io/badge/React-18-61DAFB?logo=react&logoColor=black)](https://react.dev/)
[![PostgreSQL](https://img.shields.io/badge/PostgreSQL-16-4169E1?logo=postgresql&logoColor=white)](https://www.postgresql.org/)
[![License: MIT](https://img.shields.io/badge/License-MIT-yellow.svg)](#license)

---

## Why this exists

Academic and clinical researchers routinely record interviews containing sensitive personal information — names, locations, medical details. Commercial transcription APIs (Whisper API, AssemblyAI, Otter.ai, etc.) require uploading that audio to a third party, which is frequently disallowed by ethics-committee approvals and data-protection regulations (GDPR, and in this case Turkey's KVKK).

**Local Transcription Station** solves this by running the entire pipeline — speech-to-text, speaker diarization, and PII redaction — on a single local machine, using only free, open-source models. The only network calls it ever makes are one-time model downloads from Hugging Face on first run; after that, it is provably offline.

## Key Features

- 🎙️ **Speech-to-text** via [faster-whisper](https://github.com/SYSTRAN/faster-whisper) (`large-v3`), running on a consumer GPU with an int8 quantized runtime to fit a 6GB VRAM budget.
- 🕵️ **Automatic PII redaction** — a Turkish NER model detects and censors person names and locations in-line, before the transcript ever touches disk.
- 🗣️ **Optional speaker diarization** (pyannote.audio) — labels turns as `[Konuşmacı 1]`, `[Konuşmacı 2]`, etc. Off by default and fully skippable per-job, since it's the most CPU-expensive stage.
- 🧩 **Silence-aware chunking for long recordings** — audio is split at real silence boundaries (via `ffmpeg silencedetect`), never mid-word, keeping multi-hour files stable on modest hardware. Whisper's own context (`initial_prompt`) is carried across chunk boundaries to preserve sentence continuity.
- ⚙️ **Async, resumable job processing** — uploads return immediately (`202 Accepted`); a Hangfire-backed queue (persisted in PostgreSQL) processes jobs in the background, survives app restarts, and gives a full dashboard of job history and failures.
- 🖥️ **Native desktop app** — React + Tailwind UI wrapped in Tauri, so end users double-click an icon rather than touch a terminal.
- 📄 **Research-ready exports** — `.srt` subtitles and `.docx` transcripts (both redacted and original), generated client-side, ready to drop into MAXQDA / NVivo.
- 🔐 **Defense-in-depth privacy** — Postgres bound to `127.0.0.1` only, redacted text is the default persisted artifact, and the original (unredacted) transcript is an explicit opt-in export with an in-app warning label.

## Architecture

Clean Architecture on the .NET side, with a fully independent Python microservice for the AI workload:

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
                                 │   PostgreSQL    │      │  Hangfire Queue   │      │  Python FastAPI  │
                                 │ (jobs, isolated  │      │ (persisted in     │      │  AI microservice │
                                 │  to localhost)   │      │  the same DB)     │      │  (127.0.0.1:8500)│
                                 └────────────────┘      └──────────────────┘      └────────┬─────────┘
                                                                                              │
                                                                          ┌───────────────────┼───────────────────┐
                                                                          ▼                    ▼                    ▼
                                                                  faster-whisper        Turkish NER          pyannote.audio
                                                                  (GPU, CUDA)          (BERT, CPU)          (diarization, CPU)
```

**Backend layers** (`src/WebAPI`):
- `Domain` — entities (`TranscriptionJob`), enums, no external dependencies.
- `Application` — CQRS commands/queries (MediatR), interfaces, DTOs. Knows nothing about EF Core, Hangfire, or HTTP.
- `Infrastructure` — EF Core + Npgsql, the Hangfire job runner, the FFmpeg wrapper, and the HTTP client that talks to the Python engine.
- `WebAPI` — controllers, DI composition root, Swagger.

## Tech Stack

| Layer | Technology |
|---|---|
| Desktop shell | Tauri (Rust) |
| Frontend | React 18, TypeScript, Vite, Tailwind CSS |
| Backend API | ASP.NET Core 8, Clean Architecture, MediatR (CQRS) |
| Background jobs | Hangfire (PostgreSQL storage) |
| Database | PostgreSQL 16 (Docker, `127.0.0.1`-only) |
| AI engine | FastAPI (Python), faster-whisper, 🤗 Transformers, pyannote.audio |
| Media processing | FFmpeg (extraction, silence detection, chunking) |

## Getting Started

### Prerequisites

- .NET 8 SDK
- Python 3.11+ with pip
- Node.js 18+
- Docker Desktop (for PostgreSQL)
- FFmpeg (system PATH) + an FFmpeg "shared" build (DLLs, for the diarization audio decoder)
- An NVIDIA GPU with 6GB+ VRAM (CUDA 12.x) — CPU-only fallback is possible but slow
- A free [Hugging Face](https://huggingface.co) account + access token (for the diarization models)

### 1. Start PostgreSQL

```bash
docker compose up -d
```

### 2. Run the AI engine

```bash
pip install faster-whisper transformers pyannote.audio
pip install nvidia-cublas-cu12==12.4.5.8 nvidia-cudnn-cu12==9.1.0.70 nvidia-nvjitlink-cu12==12.4.127

# one-time: accept model terms at huggingface.co/pyannote/speaker-diarization-3.1
#           and huggingface.co/pyannote/segmentation-3.0, then:
setx HF_TOKEN "hf_xxx"

uvicorn transcribe_censor_service:app --host 127.0.0.1 --port 8500
```

### 3. Run the API

```bash
cd src/WebAPI
dotnet ef database update
dotnet run
```

Swagger UI: `http://localhost:5169/swagger` · Hangfire dashboard: `http://localhost:5169/hangfire`

### 4. Run the desktop app

```bash
cd desktop-ui
npm install
npm run dev        # or: npm run tauri dev, once Tauri is initialized
```

## Configuration

| Variable | Where | Purpose |
|---|---|---|
| `HF_TOKEN` | Python env | Hugging Face token for gated diarization models |
| `FFMPEG_SHARED_DIR` | Python env | Path to FFmpeg's shared-library build (required by the diarization decoder) |
| `WHISPER_MODEL` | Python env | Swap models without code changes, e.g. `large-v3-turbo` |
| `DIARIZATION_DEVICE` | Python env | `cpu` (default, stable) or `gpu` (faster, requires a CUDA-enabled torch build) |
| `ConnectionStrings:Postgres` | `appsettings.json` | Database connection string |
| `PythonEngine:BaseUrl` | `appsettings.json` | Where the API reaches the AI engine |

## Privacy & Compliance Notes

This project was built against Turkey's KVKK (data protection law) and standard university ethics-committee requirements for handling clinical interview data:

- No audio, video, or transcript data is ever transmitted to a third-party API.
- PostgreSQL and the AI engine are both bound exclusively to `127.0.0.1`.
- The default, persisted artifact is the **redacted** transcript; the original is a separate, explicitly-labeled export.
- Temporary audio files are deleted immediately after processing, success or failure.

## Known Limitations / Roadmap

- GPU-accelerated diarization is currently blocked on Python 3.14 (no official CUDA-enabled PyTorch wheels for Windows yet); the fallback CPU path is parallelized against the Whisper GPU pass to minimize wall-clock impact.
- No checkpoint/resume for partially-completed jobs yet — a failed multi-hour job currently restarts from the beginning rather than the last completed chunk.
- Single-machine design by choice (no distributed workers) — matches the target deployment (one researcher, one workstation).

## License

MIT — see [LICENSE](./LICENSE).

## Author

Built by [Doğukan](https://github.com/) — self-taught full-stack developer based in Denizli, Turkey.
