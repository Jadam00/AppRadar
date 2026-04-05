using AppRadar.Audio;
using AppRadar.Config;
using AppRadar.Llm;
using AppRadar.Models;
using AppRadar.Rendering;
using AppRadar.Video;
using Microsoft.Extensions.Logging;

namespace AppRadar.Services;

public sealed class GenerationService
{
    private readonly ILogger<GenerationService> _logger;
    private readonly MetadataLoader _metadataLoader;
    private readonly MetadataValidator _validator;
    private readonly SelectionService _selectionService;
    private readonly ReelPlanner _reelPlanner;
    private readonly SlideRenderer _slideRenderer;
    private readonly VideoComposer _videoComposer;
    private readonly ManifestWriter _manifestWriter;
    private readonly ILoggerFactory _loggerFactory;

    public GenerationService(
        ILogger<GenerationService> logger,
        MetadataLoader metadataLoader,
        MetadataValidator validator,
        SelectionService selectionService,
        ReelPlanner reelPlanner,
        SlideRenderer slideRenderer,
        VideoComposer videoComposer,
        ManifestWriter manifestWriter,
        ILoggerFactory loggerFactory)
    {
        _logger = logger;
        _metadataLoader = metadataLoader;
        _validator = validator;
        _selectionService = selectionService;
        _reelPlanner = reelPlanner;
        _slideRenderer = slideRenderer;
        _videoComposer = videoComposer;
        _manifestWriter = manifestWriter;
        _loggerFactory = loggerFactory;
    }

    public void RunGeneration(GenerationOptions options)
    {
        _logger.LogInformation("=== AppRadar Generation Started ===");
        _logger.LogInformation("Count: {Count}, Duration: {Duration}s, Seed: {Seed}",
            options.Count, options.DurationSeconds, options.Seed?.ToString() ?? "random");

        // Resolve and log directories so Windows path issues are immediately visible
        var workingDir = Directory.GetCurrentDirectory();
        var inputDir = Path.GetFullPath(options.InputDirectory);
        var outputDir = Path.GetFullPath(options.OutputDirectory);
        _logger.LogInformation("Working directory : {Dir}", workingDir);
        _logger.LogInformation("Input directory   : {Dir}", inputDir);
        _logger.LogInformation("Output directory  : {Dir}", outputDir);

        // Ensure output directories exist
        EnsureOutputDirectories(outputDir);

        // Load config (must happen before FFmpeg validation so config paths are available)
        var configLoader = new ConfigLoader(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigLoader>.Instance);
        var config = configLoader.Load(inputDir);

        // Apply CLI overrides
        if (options.Fps.HasValue) config.Video.Fps = options.Fps.Value;
        if (options.DurationSeconds > 0) config.Video.DefaultDurationSeconds = options.DurationSeconds;

        // CLI audio override: --with-audio true|false
        if (options.WithAudio.HasValue) config.Audio.Enabled = options.WithAudio.Value;

        // CLI strategy override: --strategy structured|legacy
        if (!string.IsNullOrWhiteSpace(options.Strategy))
        {
            config.Strategy.Mode = options.Strategy.Equals("legacy", StringComparison.OrdinalIgnoreCase)
                ? "Legacy"
                : "StructuredMarketing";
        }

        _logger.LogInformation("Strategy: {Strategy}, Audio: {Audio}",
            config.Strategy.Mode, config.Audio.Enabled ? "enabled" : "disabled");

        // Validate FFmpeg (after config load so explicit tool paths in config are respected)
        _videoComposer.ValidateFfmpegAvailable(config);

        // Load metadata
        _logger.LogInformation("Loading metadata...");
        var (featuredDesc, featuredImagesDir) = _metadataLoader.LoadFeaturedApps(inputDir);
        var (myAppsDesc, myAppsImagesDir) = _metadataLoader.LoadMyApps(inputDir);

        // Validate
        _logger.LogInformation("Validating metadata...");
        var featuredSources = _validator.ValidateAndBuildSources(
            featuredDesc, featuredImagesDir, AppSourceType.Featured);
        var myAppSources = _validator.ValidateAndBuildSources(
            myAppsDesc, myAppsImagesDir, AppSourceType.MyApp);

        _validator.ValidateFeaturedCount(featuredSources);
        _validator.ValidateMyAppsCount(myAppSources);

        _logger.LogInformation("Validation passed: {FeaturedCount} featured, {MyAppCount} myApps",
            featuredSources.Count, myAppSources.Count);

        var outputImagesDir = Path.Combine(outputDir, "images");
        var outputVideosDir = Path.Combine(outputDir, "videos");
        var outputManifestsDir = Path.Combine(outputDir, "manifests");
        var outputAudioDir = Path.Combine(outputDir, "audio");
        Directory.CreateDirectory(outputAudioDir);

        // Generate each reel
        for (int i = 1; i <= options.Count; i++)
        {
            _logger.LogInformation("--- Generating reel {Index}/{Total} ---", i, options.Count);

            int seed = options.Seed.HasValue ? options.Seed.Value + i - 1 : Random.Shared.Next();
            var rng = new Random(seed);
            _logger.LogInformation("Using seed: {Seed}", seed);

            GenerateReel(
                rng, seed,
                featuredSources, myAppSources,
                config,
                outputImagesDir, outputVideosDir, outputManifestsDir, outputAudioDir,
                options.DurationSeconds);
        }

        _logger.LogInformation("=== AppRadar Generation Complete ===");
    }

    private void GenerateReel(
        Random rng,
        int seed,
        List<AppSource> featuredSources,
        List<AppSource> myAppSources,
        AppConfig config,
        string outputImagesDir,
        string outputVideosDir,
        string outputManifestsDir,
        string outputAudioDir,
        int durationSeconds)
    {
        var generationId = $"reel_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";

        List<SlideSelection> slides;
        TransitionStyle transition;
        string strategyMode;
        ReelStoryDraft? storyDraft = null;
        ReelNarrationPlan? narrationPlan = null;
        bool llmFallbackUsed = false;
        string? llmModelUsed = null;

        bool useLegacy = config.Strategy.Mode.Equals("Legacy", StringComparison.OrdinalIgnoreCase);

        if (useLegacy)
        {
            _logger.LogInformation("Using legacy shuffle strategy");
            (slides, transition) = _selectionService.Select(featuredSources, myAppSources, rng);
            strategyMode = "Legacy";
        }
        else
        {
            _logger.LogInformation("Using structured single-app marketing strategy");
            var plan = _reelPlanner.Plan(
                featuredSources, myAppSources, rng, config.Video.SecondsPerSlide);
            transition = plan.Transition;
            strategyMode = "StructuredMarketing";
            storyDraft = plan.StoryDraft;

            // ── LLM rewrite step ─────────────────────────────────────────────────────
            if (storyDraft is not null)
            {
                var (narrationText, isLlmRewritten, modelUsed, fallbackUsed) =
                    RewriteNarration(storyDraft, config);
                llmFallbackUsed = fallbackUsed;
                llmModelUsed = modelUsed;

                // Update all slide display captions and narration text to reflect the
                // rewritten narration — split into display chunks per slide.
                // The chunks are assigned to slides in order; the full narration paragraph
                // is also stored on the plan.
                var chunkTexts = NarrationPlanner.SplitIntoChunks(narrationText);
                for (int i = 0; i < plan.Slides.Count; i++)
                {
                    var chunkText = i < chunkTexts.Count ? chunkTexts[i] : string.Empty;
                    plan.Slides[i].DisplayCaption = chunkText;
                    plan.Slides[i].NarrationText = narrationText; // full paragraph for TTS
                }

                // Build initial narration plan (audio duration will be filled in after TTS)
                narrationPlan = new ReelNarrationPlan
                {
                    FullNarrationText = narrationText,
                    IsLlmRewritten = isLlmRewritten,
                    LlmModelUsed = modelUsed
                };

                plan.NarrationPlan = narrationPlan;
            }

            slides = plan.Slides.Select((p, idx) => new SlideSelection
            {
                Slot = idx + 1,
                Role = p.Role,
                SourceType = p.Source.SourceType,
                AppName = p.Source.Entry.AppName,
                ImageName = p.Source.Entry.ImageName,
                SelectedCaption = p.DisplayCaption,
                NarrationText = p.NarrationText,
                SourcePath = p.Source.ImagePath
            }).ToList();
        }

        // Render slides (all slides use the same source image in structured mode)
        _logger.LogInformation("Rendering {Count} slides...", slides.Count);
        var slidePaths = new List<string>();
        foreach (var slide in slides)
        {
            var slidePath = _slideRenderer.RenderSlide(slide, outputImagesDir, config, generationId);
            slide.RenderedSlidePath = slidePath;
            slidePaths.Add(slidePath);
        }

        // Optionally generate TTS narration
        string? audioPath = null;
        bool hasAudio = false;
        int audioDurationMs = 0;

        if (config.Audio.Enabled && narrationPlan is not null)
        {
            // Single-narration TTS: pass the full paragraph once (not per-slide segments)
            audioPath = TryGenerateSingleNarration(
                narrationPlan.FullNarrationText, config, generationId, outputAudioDir);
            hasAudio = audioPath is not null;

            if (hasAudio && audioPath is not null)
            {
                audioDurationMs = WavDurationReader.ReadDurationMs(audioPath);
                _logger.LogInformation("Audio duration: {Ms} ms", audioDurationMs);
            }
        }
        else if (config.Audio.Enabled)
        {
            // Legacy path: per-slide segments
            audioPath = TryGenerateNarration(
                slides, config, generationId, outputAudioDir, durationSeconds);
            hasAudio = audioPath is not null;

            if (hasAudio && audioPath is not null)
                audioDurationMs = WavDurationReader.ReadDurationMs(audioPath);
        }

        // Build final narration plan with measured audio duration and reveal timeline
        if (narrationPlan is not null)
        {
            narrationPlan.ExpectedAudioDurationMs = audioDurationMs;
            var revealChunks = NarrationPlanner.BuildRevealTimeline(
                NarrationPlanner.SplitIntoChunks(narrationPlan.FullNarrationText),
                audioDurationMs,
                config.CaptionSync.TailHoldMs);
            narrationPlan.DisplayChunks = revealChunks;

            // Update slide captions to match reveal chunks (may have changed from initial split)
            for (int i = 0; i < slides.Count; i++)
            {
                if (i < revealChunks.Count)
                    slides[i].SelectedCaption = revealChunks[i].Text;
            }
        }

        // Determine final video duration from narration (narration-driven) or config
        int finalDurationMs;
        int finalDurationSeconds;
        if (audioDurationMs > 0)
        {
            finalDurationMs = Math.Max(
                audioDurationMs + config.CaptionSync.TailHoldMs,
                config.CaptionSync.MinVisualDurationMs);
            finalDurationSeconds = (int)Math.Ceiling(finalDurationMs / 1000.0);
            _logger.LogInformation(
                "Narration-driven duration: {Ms} ms ({Seconds} s)", finalDurationMs, finalDurationSeconds);
        }
        else
        {
            finalDurationMs = Math.Max(
                durationSeconds * 1000,
                config.CaptionSync.MinVisualDurationMs);
            finalDurationSeconds = (int)Math.Ceiling(finalDurationMs / 1000.0);
        }

        // Compose video (with optional audio mux)
        _logger.LogInformation("Composing video...");
        var videoPath = _videoComposer.ComposeVideo(
            slidePaths, outputVideosDir, generationId,
            finalDurationSeconds, transition, config, audioPath);

        // Write manifest
        var manifest = new ReelManifest
        {
            GenerationId = generationId,
            CreatedUtc = DateTime.UtcNow,
            SeedUsed = seed,
            DurationSeconds = finalDurationSeconds,
            Width = config.Video.Width,
            Height = config.Video.Height,
            Fps = config.Video.Fps,
            TransitionStyle = transition.ToString(),
            Strategy = strategyMode,
            HasAudio = hasAudio,
            VideoPath = videoPath,
            SlidePaths = slidePaths,
            Slides = slides,

            // Single-app fields
            SelectedAppName = storyDraft?.AppName,
            SelectedImageName = storyDraft?.ImageName,
            HookText = storyDraft?.HookText,
            PainPointText = storyDraft?.PainPointText,
            CredibilityText = storyDraft?.CredibilityText,
            CtaText = storyDraft?.CtaText,

            // Narration / LLM fields
            NarrationText = narrationPlan?.FullNarrationText,
            LlmModelUsed = llmModelUsed,
            TtsProvider = config.Audio.Enabled ? config.Audio.TtsProvider : null,
            AudioDurationMs = audioDurationMs,
            FinalVideoDurationMs = finalDurationMs,
            LlmFallbackUsed = llmFallbackUsed,
            RevealTimeline = narrationPlan?.DisplayChunks
        };

        _manifestWriter.WriteManifest(manifest, outputManifestsDir);

        _logger.LogInformation("Reel {Id} complete (strategy={Strategy}, audio={Audio}, duration={Dur}s)",
            generationId, strategyMode, hasAudio, finalDurationSeconds);
    }

    /// <summary>
    /// Calls the configured LLM provider to rewrite the 4-stage draft into one narration
    /// paragraph.  Falls back to deterministic join when LLM is disabled, unavailable, or fails.
    /// Returns (text, isLlmRewritten, modelUsed, fallbackUsed).
    /// </summary>
    private (string text, bool isLlmRewritten, string? modelUsed, bool fallbackUsed) RewriteNarration(
        ReelStoryDraft draft, AppConfig config)
    {
        var provider = CaptionRewriteProviderFactory.Create(config.Llm, _loggerFactory);

        if (provider is DeterministicCaptionJoiner)
        {
            var fallbackText = DeterministicCaptionJoiner.Join(
                draft.HookText, draft.PainPointText, draft.CredibilityText, draft.CtaText);
            return (fallbackText, false, null, false);
        }

        try
        {
            var rewritten = provider.RewriteAsync(draft).GetAwaiter().GetResult();
            return (rewritten, true, provider.ProviderName, false);
        }
        catch (CaptionRewriteException ex)
        {
            _logger.LogWarning(ex, "LLM rewrite failed — falling back to deterministic join");
            var fallbackText = DeterministicCaptionJoiner.Join(
                draft.HookText, draft.PainPointText, draft.CredibilityText, draft.CtaText);
            return (fallbackText, false, provider.ProviderName, true);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unexpected error in LLM rewrite — falling back to deterministic join");
            var fallbackText = DeterministicCaptionJoiner.Join(
                draft.HookText, draft.PainPointText, draft.CredibilityText, draft.CtaText);
            return (fallbackText, false, null, true);
        }
    }

    /// <summary>
    /// Generates TTS narration from a single full narration paragraph.
    /// Used in structured single-app mode.
    /// </summary>
    private string? TryGenerateSingleNarration(
        string narrationText,
        AppConfig config,
        string generationId,
        string outputAudioDir)
    {
        try
        {
            var provider = TtsProviderFactory.Create(config.Audio, _loggerFactory);
            if (!provider.IsAvailable)
            {
                _logger.LogWarning("TTS provider '{Provider}' is not available on this platform; skipping audio",
                    config.Audio.TtsProvider);
                return null;
            }

            var segments = new List<NarrationSegment>
            {
                new() { Text = narrationText, TargetDurationSeconds = 0 }
            };

            var ffmpegExe = _videoComposer.GetFfmpegPath(config);
            var audioPath = Path.Combine(outputAudioDir, $"{generationId}_narration.wav");

            return provider.GenerateNarration(segments, audioPath, config.Audio, ffmpegExe);
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogWarning(ex, "TTS not available on this platform; producing silent video");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS generation failed; producing silent video");
            return null;
        }
    }

    private string? TryGenerateNarration(
        List<SlideSelection> slides,
        AppConfig config,
        string generationId,
        string outputAudioDir,
        int durationSeconds)
    {
        try
        {
            var provider = TtsProviderFactory.Create(config.Audio, _loggerFactory);
            if (!provider.IsAvailable)
            {
                _logger.LogWarning("TTS provider '{Provider}' is not available on this platform; skipping audio",
                    config.Audio.TtsProvider);
                return null;
            }

            var segments = slides.Select(s => new NarrationSegment
            {
                Text = string.IsNullOrWhiteSpace(s.NarrationText) ? s.SelectedCaption : s.NarrationText,
                TargetDurationSeconds = config.Video.SecondsPerSlide
            }).ToList();

            var ffmpegExe = _videoComposer.GetFfmpegPath(config);
            var audioPath = Path.Combine(outputAudioDir, $"{generationId}_narration.wav");

            return provider.GenerateNarration(segments, audioPath, config.Audio, ffmpegExe);
        }
        catch (PlatformNotSupportedException ex)
        {
            _logger.LogWarning(ex, "TTS not available on this platform; producing silent video");
            return null;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "TTS generation failed; producing silent video");
            return null;
        }
    }

    private static void EnsureOutputDirectories(string outputDirectory)
    {
        Directory.CreateDirectory(Path.Combine(outputDirectory, "images"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "videos"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "manifests"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "audio"));
        Directory.CreateDirectory("temp");
        Directory.CreateDirectory("logs");
    }
}

public sealed class GenerationOptions
{
    public int Count { get; set; } = 1;
    public int DurationSeconds { get; set; } = 24;
    public int? Seed { get; set; }
    public int? Fps { get; set; }
    public string InputDirectory { get; set; } = "input";
    public string OutputDirectory { get; set; } = "output";

    /// <summary>
    /// When set, overrides <see cref="AppConfig.Audio.Enabled"/> from config.
    /// </summary>
    public bool? WithAudio { get; set; }

    /// <summary>
    /// When set, overrides <see cref="AppConfig.Strategy.Mode"/> from config.
    /// Accepted values: "structured", "legacy".
    /// </summary>
    public string? Strategy { get; set; }
}
