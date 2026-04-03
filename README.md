# AppRadar

A .NET 10 CLI tool that generates short vertical marketing reels from local app images and metadata.
Produces real H.264 MP4 videos with slide transitions, caption overlays, and subtle drift animation —
ready for Instagram Reels or TikTok-style uploads.

> **Windows Only** — AppRadar is designed and tested for **Windows 11 / Windows Server**.
> Linux and macOS are not supported.

---

## Prerequisites

| Requirement | Details |
|---|---|
| **Windows 11 / Windows Server** | Required platform |
| **.NET 10 SDK** | https://dotnet.microsoft.com/download/dotnet/10.0 |
| **FFmpeg** | Used for video encoding — see [FFmpeg Setup](#ffmpeg-setup) below |

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

# 4. Generate a reel
dotnet run --project src\AppRadar -- generate --count 1 --duration 24
```

---

## FFmpeg Setup

AppRadar uses FFmpeg for video encoding. Install it using one of the following methods:

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

If FFmpeg cannot be found, AppRadar prints a detailed error message listing exactly what was searched
and how to fix it.

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
    "secondsPerSlide": 3
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
  }
}
```

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
│       ├── Commands\
│       ├── Config\
│       ├── Models\
│       ├── Rendering\
│       ├── Services\
│       ├── Utilities\
│       ├── Video\
│       └── Program.cs
├── tests\
│   └── AppRadar.Tests\             # Unit tests
├── input\
│   ├── config.json                 # Optional configuration overrides
│   ├── featuredApps\
│   │   ├── images\                 # Featured app screenshots (.png/.jpg)
│   │   └── description.json        # App metadata and captions
│   └── myApps\
│       ├── images\                 # Your app screenshots
│       └── description.json
├── output\
│   ├── images\                     # Rendered slide PNGs
│   ├── videos\                     # Generated MP4 files
│   └── manifests\                  # JSON manifest per reel
├── temp\                           # Temporary processing files
├── Setup.ps1                       # Windows setup and validation script
└── AppRadar.slnx                   # Solution file
```

---

## Metadata Format

Both `featuredApps\description.json` and `myApps\description.json` use the same format:

```json
{
  "apps": [
    {
      "imageName": "my_app.png",
      "appName": "My App Name",
      "captions": [
        "First caption option",
        "Second caption option",
        "Third caption option"
      ],
      "tags": ["productivity", "utility"],
      "enabled": true
    }
  ]
}
```

**Validation rules:**
- `enabled: false` items are skipped entirely
- `imageName` must be unique within the file
- The image file referenced by `imageName` must exist in the `images\` subfolder
- `captions` must not be empty
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

This reads `description.json` files and generates a PNG for each enabled app entry.

### 3. Generate a reel

```powershell
dotnet run --project src\AppRadar -- generate --count 1 --duration 24
```

Output files are written to `output\videos\`, `output\images\`, and `output\manifests\`.

---

## Commands

### `generate`

Generates one or more reel videos.

```
appradar generate [options]

Options:
  --count <number>      Number of reels to generate (default: 1)
  --duration <seconds>  Target duration per reel in seconds (default: 24)
  --seed <number>       Seed for deterministic output (optional)
  --fps <number>        Frames per second, overrides config (optional)
  --input <path>        Input directory path (default: input)
  --output <path>       Output directory path (default: output)
```

### `setup`

Generates placeholder PNG images from your `description.json` files.

```
appradar setup [--input <path>]
```

---

## Example Commands (PowerShell)

```powershell
# Generate 1 reel, 24 seconds
dotnet run --project src\AppRadar -- generate --count 1 --duration 24

# Generate 5 reels, 30 seconds each
dotnet run --project src\AppRadar -- generate --count 5 --duration 30

# Deterministic output with seed
dotnet run --project src\AppRadar -- generate --count 2 --duration 24 --seed 123

# Custom input/output directories
dotnet run --project src\AppRadar -- generate --count 2 --duration 24 --input .\input --output .\output

# Generate placeholder images for testing
dotnet run --project src\AppRadar -- setup --input input

# Use explicit FFmpeg path via environment variable
$env:FFMPEG_PATH = "C:\ffmpeg\bin\ffmpeg.exe"
dotnet run --project src\AppRadar -- generate --count 1 --duration 24
```

### Running from Visual Studio

Open `AppRadar.slnx` in Visual Studio, set `AppRadar` as the startup project, and configure
launch arguments in the project's debug profile (e.g. `generate --count 1 --duration 24`).
Visual Studio sets the working directory to the project folder by default; use absolute paths or
adjust the working directory in the debug profile if needed.

---

## How Deterministic Mode Works (`--seed`)

When `--seed` is provided:

- All random selections (featured app picks, myApp pick, caption selection, slide shuffle, transition choice) use a seeded `System.Random` instance
- For multiple reels (`--count > 1`), each reel uses `seed + (reelIndex - 1)`, so reel 1 uses `seed`, reel 2 uses `seed + 1`, etc.
- Running the same command with the same `--seed` always produces identical output

Without `--seed`, `Random.Shared.Next()` generates a different seed each run.

---

## Selection Logic

Per generated reel:
1. Load all **enabled** featured apps (requires ≥ 3)
2. Load all **enabled** myApps (requires ≥ 1)
3. Randomly pick **3 unique** featured apps
4. Randomly pick **1 unique** myApp
5. Randomly pick **1 caption** from each selected app's caption list
6. **Shuffle** the 4 slides into a random display order
7. Randomly choose **one transition style** for the whole reel:
   - `Crossfade` — smooth fade between slides
   - `Slide` — horizontal slide transition

---

## Output Structure

Each generation produces:

| File | Location | Description |
|---|---|---|
| Slide PNGs | `output\images\` | Rendered slide images with caption overlays |
| MP4 video | `output\videos\` | Final H.264 vertical reel video |
| Manifest JSON | `output\manifests\` | Full metadata about what was generated |

File names include a timestamp and short random ID, e.g.:
```
reel_20260403_120000_a1b2c3d4.mp4
reel_20260403_120000_a1b2c3d4_slide01.png
reel_20260403_120000_a1b2c3d4_manifest.json
```

### Manifest structure

```json
{
  "GenerationId": "reel_20260403_120000_a1b2c3d4",
  "CreatedUtc": "2026-04-03T12:00:00Z",
  "SeedUsed": 42,
  "DurationSeconds": 24,
  "Width": 1080,
  "Height": 1920,
  "Fps": 30,
  "TransitionStyle": "Crossfade",
  "VideoPath": "output\\videos\\reel_20260403_120000_a1b2c3d4.mp4",
  "SlidePaths": ["..."],
  "Slides": [
    {
      "Slot": 1,
      "SourceType": "Featured",
      "AppName": "Adobe Express",
      "ImageName": "adobe_express.png",
      "SelectedCaption": "Design in minutes",
      "SourcePath": "input\\featuredApps\\images\\adobe_express.png",
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
- **Audio:** None (silent video)

---

## Running Tests

```powershell
dotnet test tests\AppRadar.Tests
```

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

Run with debug-level logging by setting the environment variable:

```powershell
$env:DOTNET_LOGGING__CONSOLE__LOGLEVEL__DEFAULT = "Debug"
dotnet run --project src\AppRadar -- generate --count 1 --duration 24
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

