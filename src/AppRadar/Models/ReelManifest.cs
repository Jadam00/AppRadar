namespace AppRadar.Models;

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

    /// <summary>The single image used throughout the reel (structured mode only).</summary>
    public string? SelectedImageName { get; set; }

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
