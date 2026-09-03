# ClickBaitThumbnailGenerator

A standalone .NET 10 command-line tool for creating, processing, reviewing, and exporting original comedy clickbait thumbnails. It has its own solution and projects and does not reference Quizizzo.

The tool generates structured scenarios with an OpenAI text model, creates source images through the OpenAI Image API, centre-crops them to 16:9, and writes exact 512×288 WebP assets. A vision-capable Responses API model then creates exactly two funny, plausible-but-wrong distractor titles for each thumbnail. SQLite tracks every scenario, attempt, title request, review decision, hash, and failure so large runs can resume safely.

## Requirements

- macOS, Linux, or Windows with the .NET 10 SDK
- A newly issued OpenAI API key with access to the configured models
- Optional: Tesseract OCR for automatic unwanted-text checking

Do not put an API key in source code, JSON, shell scripts, command arguments, or this repository. The application reads it only from `OPENAI_API_KEY`, and generation commands fail clearly when it is absent.

## First run on macOS

From this directory:

```bash
read -s OPENAI_API_KEY
export OPENAI_API_KEY

dotnet restore
dotnet build
dotnet test

dotnet run --project src/ClickBaitThumbnailGenerator -- scenarios generate --count 25
dotnet run --project src/ClickBaitThumbnailGenerator -- images generate --count 5 --concurrency 1
dotnet run --project src/ClickBaitThumbnailGenerator -- titles generate --count 5 --concurrency 1
dotnet run --project src/ClickBaitThumbnailGenerator -- review
```

Paste the key after `read -s`, press Return, and keep using that Terminal window. This avoids shell history and sets the variable only for the current shell. Check that it exists without printing it:

```bash
test -n "$OPENAI_API_KEY" && echo "OPENAI_API_KEY is set"
```

## Commands

```bash
# Scenarios
dotnet run --project src/ClickBaitThumbnailGenerator -- scenarios generate --count 2000
dotnet run --project src/ClickBaitThumbnailGenerator -- scenarios import --file scenarios.json
dotnet run --project src/ClickBaitThumbnailGenerator -- scenarios list

# Images
dotnet run --project src/ClickBaitThumbnailGenerator -- images generate --count 1000 --concurrency 3
dotnet run --project src/ClickBaitThumbnailGenerator -- images generate --all --concurrency 3
dotnet run --project src/ClickBaitThumbnailGenerator -- images resume
dotnet run --project src/ClickBaitThumbnailGenerator -- images retry-failed
dotnet run --project src/ClickBaitThumbnailGenerator -- images stats

# AI distractor titles (vision)
dotnet run --project src/ClickBaitThumbnailGenerator -- titles generate --count 5 --concurrency 1
dotnet run --project src/ClickBaitThumbnailGenerator -- titles generate --all --concurrency 3
dotnet run --project src/ClickBaitThumbnailGenerator -- titles resume
dotnet run --project src/ClickBaitThumbnailGenerator -- titles retry-failed
dotnet run --project src/ClickBaitThumbnailGenerator -- titles stats

# Review and export
dotnet run --project src/ClickBaitThumbnailGenerator -- review --port 5099
dotnet run --project src/ClickBaitThumbnailGenerator -- export --output ./export
dotnet run --project src/ClickBaitThumbnailGenerator -- export --output ./export --provenance
```

Press Ctrl+C during a batch to stop gracefully. Completed jobs remain committed, a cancelled active job becomes failed, and any image or title job left as `Generating` after a hard crash is recovered to `Pending` by the corresponding `resume` command or the next generation run.

## AI distractor titles

`titles generate` sends each completed WebP thumbnail to the configured vision model and asks for exactly two short comedy clickbait titles. These are deliberately **distractors**: plausible alternative interpretations of the picture, not the original scenario, canonical answer, or a literal image description. The two titles are validated as non-empty and distinct, saved alongside the image job, shown in the review gallery, and exported as `aiTitles`.

Example game-facing manifest item:

```json
{
  "id": "cb-000001",
  "image": "images/cb-000001.webp",
  "category": "cooking-disaster",
  "width": 512,
  "height": 288,
  "sha256": "...",
  "aiTitles": [
    "I Invented Liquid Rainbows",
    "Never Put a Ladder in Paint"
  ]
}
```

Title generation is independently resumable, so it can be run after a large image batch has already completed. It does not regenerate or modify the images.

## Scenario import format

Import a JSON array. `id` and `createdAt` are optional; omitted IDs are allocated monotonically without overwriting existing records.

```json
[
  {
    "scene": "A chef looking terrified as an enormous baked bean emerges from a saucepan",
    "category": "cooking-disaster",
    "composition": "reaction-and-object",
    "visualStyle": "photographic"
  }
]
```

Exact and obvious token-based near-duplicates are skipped. Existing accepted scenarios are never overwritten by generation or import.

## Review workflow

`review` binds only to `127.0.0.1` and normally opens `http://127.0.0.1:5099`. Use `--no-open` when running without a desktop.

The gallery supports status/category/failure filters, counts, approve, reject, regenerate, edit-and-regenerate, text and duplicate flags, and previous/next navigation. Keyboard shortcuts are `A` approve, `R` reject, `G` regenerate, and the arrow keys to navigate. Generated images remain pending manual review when OCR is unavailable; they are never silently approved.

## OCR on macOS

Install optional Tesseract with Homebrew:

```bash
brew install tesseract
tesseract --version
```

The app invokes `tesseract` without a shell. If it cannot start or OCR fails, the stored result is `CheckUnavailable`. A non-empty OCR result is `TextSuspected`; reviewers make the final decision. OCR is behind `ITextChecker` so another implementation can replace it later.

## Output and persistent state

Defaults are relative to the directory from which the command is run:

```text
data/clickbait-thumbnails.db   Versioned SQLite scenario/job state
generated/*.webp              Processed 512×288 assets
generated/originals/          Optional source responses
tmp/                          Atomic processing and OCR files
export/images/                Approved assets only
export/thumbnails.json        Game-facing manifest without private prompts
export/provenance.json        Optional private model/prompt/date/hash report
```

Keep running commands from the same directory used for image generation. The existing 1,000-job run in this repository was launched from `src/ClickBaitThumbnailGenerator`, so run the title pass from there as well; otherwise relative paths would point at a new database.

These paths, local overrides, databases, logs, temporary files, generated media, and exports are ignored by Git.

## Configuration

Edit `src/ClickBaitThumbnailGenerator/appsettings.json` for checked-in defaults. For personal overrides, create an ignored `appsettings.local.json` beside the built application or change the defaults deliberately before building.

Important settings include:

- `OpenAI:ScenarioModel`, `ImageModel`, `VisionModel`, `ImageSize`, and `ImageQuality`
- `OpenAI:Concurrency`, `MaximumRetries`, and `RequestTimeoutSeconds`
- `OpenAI:EstimatedCostPerImageUsd` (an estimate used only for progress and statistics)
- `Processing:OutputWidth` and `OutputHeight` (must remain exactly 16:9)
- `Processing:WebPQuality`, `KeepOriginalFiles`, and `DuplicateHashThreshold`
- `Generation:DefaultScenarioCount` and `ScenarioBatchSize`
- all `Storage` paths

Configuration is validated at startup. Model names and cost estimates are intentionally configurable because API capabilities and pricing can change.

## Processing and safety guarantees

- Source images are decoded before use and undersized images fail rather than being upscaled.
- Processing uses a centred 16:9 crop, exact resize, metadata removal, configurable WebP encoding, temporary files, and atomic moves.
- Each asset gets a SHA-256 digest and a 64-bit perceptual difference hash. Similar images are flagged, not deleted.
- SQLite uses WAL mode, a version marker, independently resumable image/title job leasing, and stable `cb-000001.webp` filenames.
- Retries cover timeouts, network failures, HTTP 429 with `Retry-After`, and transient 5xx responses with bounded exponential backoff and jitter.
- The API key is applied only to outbound authorization headers and is never persisted or logged.
- Tests use mocked HTTP handlers and never contact OpenAI.

## Cost control

Start with 5 images at concurrency 1. Review quality, crop behavior, unwanted text, and current dashboard spend before increasing either value. Set realistic account/project limits in the OpenAI dashboard. `EstimatedCostPerImageUsd` is not a billing claim; check current official pricing and update it before a large run. A scenario-generation request also has text-token cost that the image estimate does not include.

## Commercial provenance

The normal `thumbnails.json` contains the ID, relative image path, category, dimensions, SHA-256, and the two public `aiTitles` distractors. It never exposes the private scenario prompt. Run export with `--provenance` to retain the configured image model, generation date, full private prompt, SHA-256, and perceptual hash separately. Keep that report with your commercial asset records and review generated output and titles for suitability, brands, real people, copyrighted characters, and unwanted writing before approval.

## Troubleshooting

- **`OPENAI_API_KEY is not set`**: export a newly issued key in the same shell that launches `dotnet run`.
- **HTTP 429**: let automatic retries honor `Retry-After`, reduce concurrency, check project limits, and run `images resume` later.
- **Repeated 5xx/timeouts**: reduce concurrency, confirm network access, then use `images retry-failed`.
- **Failed vision titles**: inspect `titles stats`, then run `titles retry-failed`; completed title jobs are not repeated.
- **`CheckUnavailable`**: install Tesseract or review every affected image manually.
- **Duplicate suspected**: compare the image in the gallery; approve it only when the similarity is acceptable.
- **Corrupt/undersized image**: the job fails with a stored reason and can be retried without losing other completed work.
- **Port already in use**: choose another local port, for example `review --port 5100`.

Run `dotnet run --project src/ClickBaitThumbnailGenerator -- --help` for the command tree.
