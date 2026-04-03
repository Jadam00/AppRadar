# AppRadar

A .NET 10 CLI tool that generates short vertical marketing reels from local app images and metadata. Produces real H.264 MP4 videos with slide transitions, caption overlays, and subtle drift animation — ready for Instagram Reels or TikTok-style uploads.

---

## Requirements

| Requirement | Details |
|---|---|
| **.NET 10 SDK** | https://dotnet.microsoft.com/download/dotnet/10.0 |
| **FFmpeg** | Used for video encoding — see installation below |

---

## FFmpeg Installation

AppRadar validates that FFmpeg is available on `PATH` before running. Install it for your platform:

**Ubuntu/Debian**
```bash
sudo apt-get install -y ffmpeg
```

**macOS (Homebrew)**
```bash
brew install ffmpeg
```

**Windows**
Download from https://ffmpeg.org/download.html and add the `bin/` folder to your `PATH`.

**Verify installation:**
```bash
ffmpeg -version
```

---

## .NET Version

This project targets **net10.0**. Make sure you have .NET 10 SDK installed:

```bash
dotnet --version  # should be 10.x
```

---

## Folder Structure

```
AppRadar/
├── src/
│   └── AppRadar/                   # Main CLI application
│       ├── Commands/
│       ├── Config/
│       ├── Models/
│       ├── Rendering/
│       ├── Services/
│       ├── Utilities/
│       ├── Video/
│       └── Program.cs
├── tests/
│   └── AppRadar.Tests/             # Unit tests
├── input/
│   ├── config.json                 # Optional configuration overrides
│   ├── featuredApps/
│   │   ├── images/                 # Featured app screenshots (.png/.jpg)
│   │   └── description.json        # App metadata and captions
│   └── myApps/
│       ├── images/                 # Your app screenshots
│       └── description.json
├── output/
│   ├── images/                     # Rendered slide PNGs
│   ├── videos/                     # Generated MP4 files
│   └── manifests/                  # JSON manifest per reel
├── temp/                           # Temporary processing files
└── logs/                           # Log files (if configured)
```

---

## Metadata Format

Both `featuredApps/description.json` and `myApps/description.json` use the same format:

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
- `imageName` file must exist in the `images/` subfolder
- `captions` must not be empty
- At least **3 enabled** featured apps are required
- At least **1 enabled** myApp is required

---

## Config Format

`input/config.json` — all fields are optional (shown with defaults):

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
  }
}
```

CLI flags (`--fps`, `--duration`) override config values where applicable.

---

## Getting Started

### 1. Clone and build

```bash
git clone <repo>
cd AppRadar
dotnet build
```

### 2. Generate sample placeholder images

If you don't yet have real app screenshots, generate colorful placeholder images for testing:

```bash
dotnet run --project src/AppRadar -- setup --input input
```

This reads `description.json` files and generates a PNG for each app entry.

### 3. Generate a reel

```bash
dotnet run --project src/AppRadar -- generate --count 1 --duration 24
```

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

Generates placeholder PNG images from your `description.json` files. Useful for testing the pipeline without real screenshots.

```
appradar setup [--input <path>]
```

---

## Example Commands

```bash
# Generate 1 reel, 24 seconds
dotnet run --project src/AppRadar -- generate --count 1 --duration 24

# Generate 5 reels, 30 seconds each
dotnet run --project src/AppRadar -- generate --count 5 --duration 30

# Deterministic output with seed
dotnet run --project src/AppRadar -- generate --count 2 --duration 24 --seed 123

# Custom input/output directories
dotnet run --project src/AppRadar -- generate --count 2 --duration 24 \
  --input ./input --output ./output

# Generate placeholder images for testing
dotnet run --project src/AppRadar -- setup --input input
```

---

## How Deterministic Mode Works (`--seed`)

When `--seed` is provided:

- All random selections (featured app picks, myApp pick, caption selection, slide shuffle, transition choice) use a seeded `System.Random` instance
- For multiple reels (`--count > 1`), each reel uses `seed + (reelIndex - 1)`, so reel 1 uses `seed`, reel 2 uses `seed + 1`, etc.
- Running the same command with the same `--seed` will always produce identical output

Without `--seed`, `Random.Shared.Next()` is used to generate a different seed each run.

---

## Selection Logic

Per generated reel:
1. Load all **enabled** featured apps (requires ≥ 3)
2. Load all **enabled** myApps (requires ≥ 1)
3. Randomly pick **3 unique** featured apps
4. Randomly pick **1 unique** myApp
5. Randomly pick **1 caption** from each selected app's caption list
6. **Shuffle** the 4 slides into a random display order (always enabled)
7. Randomly choose **one transition style** for the whole reel:
   - `Crossfade` — smooth fade between slides
   - `Slide` — horizontal slide transition

---

## Output Structure

Each generation produces:

| File | Location | Description |
|---|---|---|
| Slide PNGs | `output/images/` | Rendered slide images with caption overlays |
| MP4 video | `output/videos/` | Final H.264 vertical reel video |
| Manifest JSON | `output/manifests/` | Full metadata about what was generated |

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
  "VideoPath": "output/videos/reel_20260403_120000_a1b2c3d4.mp4",
  "SlidePaths": ["..."],
  "Slides": [
    {
      "Slot": 1,
      "SourceType": "Featured",
      "AppName": "Adobe Express",
      "ImageName": "adobe_express.png",
      "SelectedCaption": "Design in minutes",
      "SourcePath": "input/featuredApps/images/adobe_express.png",
      "RenderedSlidePath": "output/images/reel_..._slide01.png"
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
- **Resolution:** 1080×1920 (vertical, matches Instagram Reels)
- **Frame rate:** 30 fps (configurable)
- **Audio:** None (silent video, consistent with marketing reel style)

---

## Running Tests

```bash
dotnet test tests/AppRadar.Tests
```

---

## Troubleshooting

**`FFmpeg not found on PATH`**
Install FFmpeg and ensure it's accessible as `ffmpeg` on your `PATH`.

**`At least 3 enabled featured apps are required`**
Add more entries to `input/featuredApps/description.json` with `"enabled": true`.

**`Image file not found`**
Ensure image files listed in `description.json` exist in the corresponding `images/` subfolder. Run `appradar setup` to generate placeholder images if needed.

**`Could not find a usable font`**
AppRadar tries several system fonts. Install `fonts-dejavu-core` or `fonts-liberation`:
```bash
sudo apt-get install -y fonts-dejavu-core
```

**`FFmpeg failed with exit code N`**
Run with debug logging or check the FFmpeg stderr output in the log. Common causes: missing libx264 encoder, unsupported filter version.

**Slides look distorted**
All input images are scaled using "cover fit" with centered crop — they are never stretched. If output looks odd, verify your source images are valid PNGs or JPEGs.
