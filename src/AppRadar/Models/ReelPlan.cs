namespace AppRadar.Models;

/// <summary>
/// The complete plan for one reel, built by <see cref="AppRadar.Services.ReelPlanner"/>
/// before any rendering happens.
/// </summary>
public sealed class ReelPlan
{
    public List<ReelSlidePlan> Slides { get; set; } = new();
    public TransitionStyle Transition { get; set; }
    public int SeedUsed { get; set; }
    public string StrategyMode { get; set; } = string.Empty;

    /// <summary>
    /// Pre-LLM structured 4-stage story draft for single-app reels.
    /// Null in legacy strategy mode.
    /// </summary>
    public ReelStoryDraft? StoryDraft { get; set; }

    /// <summary>
    /// Post-LLM narration plan.  Populated after the LLM rewrite step.
    /// Null until the rewrite step completes (or falls back).
    /// </summary>
    public ReelNarrationPlan? NarrationPlan { get; set; }
}

/// <summary>
/// The plan for a single slide within a <see cref="ReelPlan"/>.
/// </summary>
public sealed class ReelSlidePlan
{
    /// <summary>Which narrative role this slide plays.</summary>
    public SlideRole Role { get; set; }

    /// <summary>The app source (image + metadata) to use for this slide.</summary>
    public AppSource Source { get; set; } = null!;

    /// <summary>Selected image file name for this stage.</summary>
    public string ImageName { get; set; } = string.Empty;

    /// <summary>Resolved full image path for this stage.</summary>
    public string SourcePath { get; set; } = string.Empty;

    /// <summary>
    /// The caption text to display visually on the slide.
    /// This may differ from <see cref="NarrationText"/> when the narration is
    /// a longer version of the short on-screen copy.
    /// </summary>
    public string DisplayCaption { get; set; } = string.Empty;

    /// <summary>
    /// The text fed to the TTS engine for this slide's narration.
    /// Defaults to <see cref="DisplayCaption"/> when not set separately.
    /// </summary>
    public string NarrationText { get; set; } = string.Empty;

    /// <summary>Target duration for this slide in seconds.</summary>
    public int DurationSeconds { get; set; }
}
