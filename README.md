# AppRadar

A .NET 10 CLI tool that generates short vertical marketing reels from local app images and metadata.
Produces H.264 MP4 videos with a structured narrative arc, slide transitions, caption overlays,
subtle drift animation, and optional Windows TTS narration audio —
ready for Instagram Reels or TikTok-style uploads.

> **Windows Only** — AppRadar is designed and tested for **Windows 11 / Windows Server**.
> Linux and macOS are not supported.

---

## What's New

### Structured Narrative Engine (v2)

AppRadar no longer produces a random slideshow.  
Each reel now follows a deliberate **4-stage marketing flow**:

| Slide | Role | Purpose |
|---|---|---|
| 1 | **Hook** | Stop the scroll — bold claim, tension, or curiosity |
| 2 | **Pain Point** | Identify a problem or intriguing angle |
| 3 | **Credibility** | Evidence, contrast, build-up toward the reveal |
| 4 | **CTA** | Your app as the payoff with a clear call-to-action |

### TTS Narration (Windows)

Enable audio to generate spoken narration from slide captions and mux it into the MP4:

```powershell
dotnet run --project src\AppRadar -- generate --count 1 --with-audio true
```

Narration uses the Windows SAPI `SpeechSynthesizer` (no external API, no cloud).

---

## Prerequisites

| Requirement | Details |
|---|---|
| **Windows 11 / Windows Server** | Required platform |
| **.NET 10 SDK** | https://dotnet.microsoft.com/download/dotnet/10.0 |
| **FFmpeg** | Used for video encoding and audio muxing — see [FFmpeg Setup](#ffmpeg-setup) |

Verify your .NET SDK version in PowerShell:

```powershell
dotnet --version   # must be 10.x
```

---

## Quick Start (PowerShell)

```powershell
# 1. Clone and open repo
git clone <repo-url>
cd AppRadar

# 2. Run the setup script to validate all prerequisites
.\Setup.ps1

# 3. Generate placeholder images for testing (no real screenshots needed)
dotnet run --project src\AppRadar -- setup --input input

# 4. Generate a structured marketing reel
dotnet run --project src\AppRadar -- generate --count 1 --duration 16

# 5. Generate a reel with narration audio (Windows only)
dotnet run --project src\AppRadar -- generate --count 1 --duration 16 --with-audio true
```

---

## FFmpeg Setup

AppRadar uses FFmpeg for video encoding and audio muxing. Install it using one of the following methods:

### Option 1 — winget (recommended)

```powershell
winget install Gyan.FFmpeg
```

Restart your terminal after installation, then verify:

```powershell
ffmpeg -version
```

### Option 2 — Chocolatey

```powershell
choco install ffmpeg
```

### Option 3 — Scoop

```powershell
scoop install ffmpeg
```

### Option 4 — Manual install

1. Download a Windows build from https://ffmpeg.org/download.html#build-windows
2. Extract to `C:\ffmpeg`
3. Add `C:\ffmpeg\bin` to your `PATH` environment variable, **or** set the explicit path in `input\config.json`:

```json
{
  "tools": {
    "ffmpegPath": "C:\\ffmpeg\\bin\\ffmpeg.exe"
  }
}
```

### Option 5 — Environment variable

```powershell
$env:FFMPEG_PATH = "C:\ffmpeg\bin\ffmpeg.exe"
```

### How FFmpeg is discovered

AppRadar searches in this order and uses the first match:

1. `tools.ffmpegPath` value in `input\config.json`
2. `FFMPEG_PATH` environment variable
3. Common Windows install locations:
   - `C:\ffmpeg\bin\ffmpeg.exe`
   - `C:\Program Files\ffmpeg\bin\ffmpeg.exe`
   - Chocolatey: `%ProgramData%\chocolatey\bin\ffmpeg.exe`
   - Scoop: `%USERPROFILE%\scoop\shims\ffmpeg.exe`
   - WinGet: `%LOCALAPPDATA%\Microsoft\WinGet\Packages\Gyan.FFmpeg_*\...\bin\ffmpeg.exe`
4. Each directory listed in the `PATH` environment variable (checks `ffmpeg.exe` and `ffmpeg`)
5. Direct execution probe (covers shims and wrappers not represented as plain files)

---

## Setup Script

Run `Setup.ps1` from the repository root to validate your environment:

```powershell
.\Setup.ps1
```

It checks:
- Windows platform
- .NET 10 SDK presence
- FFmpeg presence and version
- Required input/output folders (creates them if missing)
- Input metadata files
- Project build

To also generate placeholder images during setup:

```powershell
.\Setup.ps1 -GeneratePlaceholders
```

---

## Configuration

`input\config.json` — all sections are optional (shown with defaults):

```json
{
  "video": {
    "width": 1080,
    "height": 1920,
    "fps": 30,
    "defaultDurationSeconds": 24,
    "secondsPerSlide": 4
  },
  "overlay": {
    "fontFamily": "Arial",
    "fontSize": 72,
    "fontColor": "#FFFFFF",
    "padding": 64,
    "maxTextWidthPercent": 0.82,
    "bottomGradientOpacity": 0.55,
    "textShadow": true
  },
  "animation": {
    "verticalDriftPixels": 60,
    "transitionDurationMs": 600
  },
  "tools": {
    "ffmpegPath": null,
    "ffprobePath": null
  },
  "audio": {
    "enabled": false,
    "ttsProvider": "SystemSpeech",
    "voiceName": null,
    "rate": 0,
    "volume": 100,
    "leadInMs": 150,
    "gapBetweenSlidesMs": 300,
    "normalizeAudio": true
  },
  "strategy": {
    "mode": "StructuredMarketing",
    "allowLegacyShuffleMode": true
  }
}
```

### Audio configuration

| Field | Default | Description |
|---|---|---|
| `enabled` | `false` | Generate and mux narration audio into the MP4 |
| `ttsProvider` | `"SystemSpeech"` | TTS backend to use. Currently: `SystemSpeech` (Windows SAPI) |
| `voiceName` | `null` | SAPI voice name (e.g. `"Microsoft Zira Desktop"`). `null` = system default |
| `rate` | `0` | Speaking rate: `-10` (slowest) to `10` (fastest) |
| `volume` | `100` | Volume 0–100 |
| `leadInMs` | `150` | Silence before first word of each slide (ms) |
| `gapBetweenSlidesMs` | `300` | Silence between narrated slide segments (ms) |
| `normalizeAudio` | `true` | Apply FFmpeg `loudnorm` filter to even out audio levels |

### Strategy configuration

| Field | Default | Description |
|---|---|---|
| `mode` | `"StructuredMarketing"` | `"StructuredMarketing"` (4-stage narrative) or `"Legacy"` (original random shuffle) |
| `allowLegacyShuffleMode` | `true` | When `true`, `--strategy legacy` is accepted on the CLI |

### Supported environment variables

| Variable | Purpose |
|---|---|
| `FFMPEG_PATH` | Full path to `ffmpeg.exe` — overrides automatic discovery |
| `FFPROBE_PATH` | Full path to `ffprobe.exe` — not currently used in the pipeline |

---

## Folder Structure

```
AppRadar\
├── src\
│   └── AppRadar\                   # Main CLI application
│       ├── Audio\                  # ITtsProvider, SystemSpeechTtsProvider, TtsProviderFactory
│       ├── Commands\
│       ├── Config\
│       ├── Models\
│       ├── Rendering\
│       ├── Services\               # ReelPlanner, SelectionService, GenerationService, …
│       ├── Utilities\
│       ├── Video\
│       └── Program.cs
├── tests\
│   └── AppRadar.Tests\             # Unit tests (44 tests)
├── input\
│   ├── config.json                 # Optional configuration overrides
│   ├── featuredApps\
│   │   ├── images\                 # Featured app screenshots (.png/.jpg)
│   │   └── description.json        # App metadata with structured caption fields
│   └── myApps\
│       ├── images\                 # Your app screenshots
│       └── description.json        # Your app metadata with CTA captions
├── output\
│   ├── audio\                      # Generated narration WAV files
│   ├── images\                     # Rendered slide PNGs
│   ├── videos\                     # Generated MP4 files
│   └── manifests\                  # JSON manifest per reel
├── temp\                           # Temporary processing files
├── Setup.ps1                       # Windows setup and validation script
└── AppRadar.slnx                   # Solution file
```

---

## Metadata Format

Both `featuredApps\description.json` and `myApps\description.json` use the same format.

### Backwards-compatible extended schema

```json
{
  "apps": [
    {
      "imageName": "my_app.png",
      "appName": "My App Name",
      "captions": [
        "Generic fallback caption"
      ],
      "hookCaptions": [
        "Hard hook line 1",
        "Hard hook line 2"
      ],
      "painPointCaptions": [
        "Pain point or curiosity line"
      ],
      "credibilityCaptions": [
        "Credibility or comparison line"
      ],
      "ctaCaptions": [
        "Download now",
        "Try it today"
      ],
      "tags": ["productivity", "utility"],
      "enabled": true
    }
  ]
}
```

**Role-specific caption fields are optional.**  
When a role-specific list is absent or empty, the planner falls back to the generic
`captions` list, then to built-in templates derived from the app's tags and name.

**Validation rules:**
- `enabled: false` items are skipped entirely
- `imageName` must be unique within the file
- The image file referenced by `imageName` must exist in the `images\` subfolder
- `captions` must not be empty (at least one generic caption is required as fallback)
- Role-specific caption fields (`hookCaptions`, `painPointCaptions`, etc.) are optional
- At least **3 enabled** featured apps are required
- At least **1 enabled** myApp is required

---

## Getting Started

### 1. Clone and build

```powershell
git clone <repo-url>
cd AppRadar
dotnet build
```

### 2. Generate placeholder images (optional)

If you don't have real app screenshots yet, generate colorful placeholder images for testing:

```powershell
dotnet run --project src\AppRadar -- setup --input input
```

### 3. Generate a structured marketing reel

```powershell
dotnet run --project src\AppRadar -- generate --count 1 --duration 16
```

### 4. Generate a reel with narration audio (Windows)

```powershell
dotnet run --project src\AppRadar -- generate --count 1 --duration 16 --with-audio true
```

Output files are written to `output\videos\`, `output\images\`, `output\audio\`, and `output\manifests\`.

---

## Commands

### `generate`

Generates one or more reel videos.

```
appradar generate [options]

Options:
  --count <number>         Number of reels to generate (default: 1)
  --duration <seconds>     Target duration per reel in seconds (default: 24)
  --seed <number>          Seed for deterministic output (optional)
  --fps <number>           Frames per second, overrides config (optional)
  --input <path>           Input directory path (default: input)
  --output <path>          Output directory path (default: output)
  --with-audio <bool>      Generate narration audio: true or false (overrides config audio.enabled)
  --strategy <name>        Narrative strategy: structured (default) or legacy
```

### `setup`

Generates placeholder PNG images from your `description.json` files.

```
appradar setup [--input <path>]
```

---

## Example Commands (PowerShell)

```powershell
# Generate 1 structured reel, 16 seconds
dotnet run --project src\AppRadar -- generate --count 1 --duration 16

# Generate a reel with TTS narration audio
dotnet run --project src\AppRadar -- generate --count 1 --duration 16 --with-audio true

# Use the original legacy shuffle mode
dotnet run --project src\AppRadar -- generate --count 1 --duration 24 --strategy legacy

# Deterministic output with seed
dotnet run --project src\AppRadar -- generate --count 2 --duration 16 --seed 123

# Generate 5 reels
dotnet run --project src\AppRadar -- generate --count 5 --duration 16

# Custom input/output directories
dotnet run --project src\AppRadar -- generate --count 1 --input .\input --output .\output

# Generate placeholder images for testing
dotnet run --project src\AppRadar -- setup --input input

# Use explicit FFmpeg path via environment variable
$env:FFMPEG_PATH = "C:\ffmpeg\bin\ffmpeg.exe"
dotnet run --project src\AppRadar -- generate --count 1 --duration 16
```

---

## How the Narrative Engine Works

### Structured Marketing mode (default)

The `ReelPlanner` service builds each reel in four intentional stages:

**1. HOOK** — Pick a featured app whose hook captions feel punchy and short.  
**2. PAIN POINT** — Pick a different featured app (preferring distinct tags) whose pain-point copy creates tension.  
**3. CREDIBILITY** — Pick a third featured app that builds toward the payoff.  
**4. CTA** — Place your app as the final slide with a clear call to action.

Caption selection uses a heuristic scorer that:
- Prefers short captions (≤ 50 chars ideal)
- Rewards captions with exclamation points or questions
- Penalises very long captions (> 90 chars)
- Avoids duplicate wording across slides
- Prefers apps with distinct tags across the reel

### Legacy mode

Passing `--strategy legacy` restores the original behaviour:
- 3 random featured apps selected
- 1 random myApp selected
- Order shuffled randomly
- Transition style chosen at random

### Determinism

When `--seed` is provided:
- All random selections use a seeded `System.Random` instance
- For multiple reels (`--count > 1`), reel N uses `seed + (N - 1)`
- Same seed always produces identical output

---

## How TTS Narration Works

When audio is enabled, the pipeline adds two steps after slide rendering:

1. **Narration generation** — The `SystemSpeechTtsProvider` uses the Windows SAPI
   `SpeechSynthesizer` (from `System.Speech`) to synthesise each slide's narration text
   into a per-segment WAV file.  FFmpeg then concatenates the segments — inserting
   configurable silence gaps — and optionally normalises the audio with `loudnorm`.

2. **Video + audio muxing** — The video is rendered as a silent MP4 first, then FFmpeg
   muxes it with the narration WAV (`-c:a aac -b:a 128k -shortest`) to produce the
   final file.

**TTS voice selection:**  
Set `audio.voiceName` in config to use a specific SAPI voice, e.g.:
```json
"audio": { "voiceName": "Microsoft Zira Desktop" }
```
Leave it `null` to use the Windows system default voice.

**Why System.Speech?**  
`System.Speech.Synthesis.SpeechSynthesizer` is a built-in Windows API — no cloud service,
no API keys, no internet required.  It runs entirely locally and produces standard WAV output
that FFmpeg can mux directly.

---

## Output Structure

Each generation produces:

| File | Location | Description |
|---|---|---|
| Slide PNGs | `output\images\` | Rendered slide images with caption overlays |
| MP4 video | `output\videos\` | Final H.264 vertical reel (with or without audio) |
| Narration WAV | `output\audio\` | Raw TTS audio (only when `--with-audio true`) |
| Manifest JSON | `output\manifests\` | Full metadata about what was generated |

File names include a timestamp and short random ID, e.g.:
```
reel_20260404_120000_a1b2c3d4.mp4
reel_20260404_120000_a1b2c3d4_narration.wav
reel_20260404_120000_a1b2c3d4_slide01.png
reel_20260404_120000_a1b2c3d4_manifest.json
```

### Manifest structure

```json
{
  "GenerationId": "reel_20260404_120000_a1b2c3d4",
  "CreatedUtc": "2026-04-04T12:00:00Z",
  "SeedUsed": 42,
  "DurationSeconds": 16,
  "Width": 1080,
  "Height": 1920,
  "Fps": 30,
  "TransitionStyle": "Crossfade",
  "Strategy": "StructuredMarketing",
  "HasAudio": true,
  "VideoPath": "output\\videos\\reel_20260404_120000_a1b2c3d4.mp4",
  "SlidePaths": ["..."],
  "Slides": [
    {
      "Slot": 1,
      "Role": "Hook",
      "SourceType": "Featured",
      "AppName": "Canva",
      "ImageName": "canva.jpg",
      "SelectedCaption": "Stop settling for boring visuals",
      "NarrationText": "Stop settling for boring visuals",
      "SourcePath": "input\\featuredApps\\images\\canva.jpg",
      "RenderedSlidePath": "output\\images\\reel_..._slide01.png"
    }
  ]
}
```

---

## Video Encoding

- **Container:** MP4 (H.264, `libx264`)
- **Pixel format:** `yuv420p` (maximum compatibility)
- **CRF:** 23 (good quality / file size balance)
- **Preset:** `fast`
- **Resolution:** 1080×1920 (vertical, matches Instagram Reels / TikTok)
- **Frame rate:** 30 fps (configurable)
- **Audio:** AAC 128 kbps (when enabled); silent by default

---

## Running Tests

```powershell
dotnet test tests\AppRadar.Tests
```

The test suite covers:
- `ReelPlannerTests` — narrative engine, scoring heuristics, tag diversity, determinism
- `SelectionServiceTests` — legacy selection logic
- `VideoComposerTests` — FFmpeg discovery and cycle calculation
- `MetadataValidatorTests` — validation rules
- `ManifestWriterTests` — manifest serialisation

---

## Troubleshooting

### `FFmpeg not found`

AppRadar prints a detailed message listing every location that was searched and three options to fix it.
The most common fix is to install FFmpeg via winget:

```powershell
winget install Gyan.FFmpeg
```

Then restart your terminal. Or set the path explicitly in `input\config.json`:

```json
"tools": { "ffmpegPath": "C:\\ffmpeg\\bin\\ffmpeg.exe" }
```

### `TTS not available on this platform`

`SystemSpeechTtsProvider` requires Windows.  If you see this warning on a non-Windows machine,
audio is automatically skipped and a silent video is produced.  On Windows this should not occur
under normal circumstances.

### `At least 3 enabled featured apps are required`

Add more entries to `input\featuredApps\description.json` with `"enabled": true`.

### `Image file not found`

Ensure image files listed in `description.json` exist in the corresponding `images\` subfolder.
Run `dotnet run --project src\AppRadar -- setup --input input` to generate placeholder images.

### `Could not find a usable font`

On Windows, `Arial` is a built-in system font and should always be found.
If you see this error, verify the font family name in `input\config.json`:

```json
"overlay": { "fontFamily": "Arial" }
```

### `FFmpeg failed with exit code N`

Run with debug-level logging:

```powershell
$env:DOTNET_LOGGING__CONSOLE__LOGLEVEL__DEFAULT = "Debug"
dotnet run --project src\AppRadar -- generate --count 1 --duration 16
```

The full FFmpeg command and stderr output will be included in the logs.

Common causes:
- `libx264` encoder not included in your FFmpeg build — use a full build from https://ffmpeg.org/download.html#build-windows
- Output path contains unsupported characters
- Insufficient disk space

### Paths with spaces

AppRadar quotes all file paths passed to FFmpeg. Paths with spaces are supported.
If you experience issues, try moving the repository to a path without spaces (e.g. `C:\AppRadar`).

### Slides look distorted

All input images are scaled using "cover fit" with centered crop — they are never stretched.
If output looks unexpected, verify source images are valid PNGs or JPEGs.

### Running from a different working directory

AppRadar resolves `--input` and `--output` to absolute paths and logs them at startup.
If relative paths resolve to unexpected locations, use absolute paths:

```powershell
dotnet run --project src\AppRadar -- generate --input C:\AppRadar\input --output C:\AppRadar\output
```

