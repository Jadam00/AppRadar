namespace AppRadar.Models;

using System.Text.Json.Serialization;

public sealed class ReelManifest
{
    public string GenerationId { get; set; } = string.Empty;
    public DateTime CreatedUtc { get; set; }
    public int? SeedUsed { get; set; }
    public int DurationSeconds { get; set; }
    public int Width { get; set; }
    public int Height { get; set; }
    public int Fps { get; set; }
    public string TransitionStyle { get; set; } = string.Empty;
    public string Strategy { get; set; } = string.Empty;
    public bool HasAudio { get; set; }
    public string VideoPath { get; set; } = string.Empty;
    public List<string> SlidePaths { get; set; } = new();
    public List<SlideSelection> Slides { get; set; } = new();

    // ── Single-app fields ──────────────────────────────────────────────────────────────────

    /// <summary>The single app this reel promotes (structured mode only).</summary>
    public string? SelectedAppName { get; set; }

    /// <summary>Slugified app folder used for output paths.</summary>
    public string? OutputAppSlug { get; set; }

    /// <summary>The selected image file name for each stage (structured mode only).</summary>
    public Dictionary<string, string>? StageImageNames { get; set; }

    [JsonIgnore]
    public string? SelectedImageName
    {
        get => StageImageNames is null ? null : StageImageNames.GetValueOrDefault(SlideRole.Hook.ToString());
        set
        {
            if (string.IsNullOrWhiteSpace(value))
                return;

            StageImageNames = new Dictionary<string, string>
            {
                [SlideRole.Hook.ToString()] = value,
                [SlideRole.PainPoint.ToString()] = value,
                [SlideRole.Credibility.ToString()] = value,
                [SlideRole.Cta.ToString()] = value
            };
        }
    }

    // ── 4-stage pre-LLM texts ─────────────────────────────────────────────────────────────

    public string? HookText { get; set; }
    public string? PainPointText { get; set; }
    public string? CredibilityText { get; set; }
    public string? CtaText { get; set; }

    // ── Narration / LLM ──────────────────────────────────────────────────────────────────

    /// <summary>The final narration paragraph (LLM-rewritten or deterministic fallback).</summary>
    public string? NarrationText { get; set; }

    /// <summary>LLM model used for rewrite, or null when fallback was used.</summary>
    public string? LlmModelUsed { get; set; }

    /// <summary>LLM model/provider used for per-slide short overlay captions.</summary>
    public string? SlideCaptionModelUsed { get; set; }

    /// <summary>True when at least one slide overlay caption used LLM output.</summary>
    public bool UsedLlmSlideCaptions { get; set; }

    /// <summary>TTS provider used for audio generation.</summary>
    public string? TtsProvider { get; set; }

    /// <summary>Measured duration of the generated narration audio, in milliseconds.</summary>
    public int AudioDurationMs { get; set; }

    /// <summary>Actual final video duration in milliseconds.</summary>
    public int FinalVideoDurationMs { get; set; }

    /// <summary>Whether the LLM rewrite fallback was used (Ollama unavailable/failed).</summary>
    public bool LlmFallbackUsed { get; set; }

    /// <summary>Progressive caption reveal timeline.</summary>
    public List<DisplayChunk>? RevealTimeline { get; set; }
}
