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

        // ── Legacy mode: select slides, render, then TTS ─────────────────────────────────────
        if (useLegacy)
        {
            _logger.LogInformation("Using legacy shuffle strategy");
            (slides, transition) = _selectionService.Select(featuredSources, myAppSources, rng);
            strategyMode = "Legacy";

            _logger.LogInformation("Rendering {Count} slides...", slides.Count);
            var slidePaths = new List<string>();
            foreach (var slide in slides)
            {
                var slidePath = _slideRenderer.RenderSlide(slide, outputImagesDir, config, generationId);
                slide.RenderedSlidePath = slidePath;
                slidePaths.Add(slidePath);
            }

            string? audioPath = null;
            bool hasAudio = false;
            int audioDurationMs = 0;
            if (config.Audio.Enabled)
            {
                audioPath = TryGenerateNarration(slides, config, generationId, outputAudioDir, durationSeconds);
                hasAudio = audioPath is not null;
                if (hasAudio && audioPath is not null)
                    audioDurationMs = WavDurationReader.ReadDurationMs(audioPath);
            }

            int finalDurationMs = Math.Max(durationSeconds * 1000, config.CaptionSync.MinVisualDurationMs);
            int finalDurationSeconds = (int)Math.Ceiling(finalDurationMs / 1000.0);

            _logger.LogInformation("Composing video...");
            var videoPath = _videoComposer.ComposeVideo(
                slidePaths, outputVideosDir, generationId,
                finalDurationSeconds, transition, config, audioPath);

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
                AudioDurationMs = audioDurationMs,
                FinalVideoDurationMs = finalDurationMs,
            };
            _manifestWriter.WriteManifest(manifest, outputManifestsDir);
            _logger.LogInformation("Reel {Id} complete (strategy={Strategy}, audio={Audio}, duration={Dur}s)",
                generationId, strategyMode, hasAudio, finalDurationSeconds);
            return;
        }

        // ── Structured mode ───────────────────────────────────────────────────────────────────
        _logger.LogInformation("Using structured single-app marketing strategy");
        AppEntry? selectedAppEntry = null;
        {
            var plan = _reelPlanner.Plan(
                featuredSources, myAppSources, rng, config.Video.SecondsPerSlide);
            transition = plan.Transition; // always TransitionStyle.Slide
            strategyMode = "StructuredMarketing";
            storyDraft = plan.StoryDraft;

            // Capture the selected app entry for keyword caption generation
            selectedAppEntry = plan.Slides.Count > 0 ? plan.Slides[0].Source.Entry : null;

            // ── LLM rewrite ───────────────────────────────────────────────────────────────────
            if (storyDraft is not null)
            {
                var (narrationText, isLlmRewritten, modelUsed, fallbackUsed) =
                    RewriteNarration(storyDraft, config);
                llmFallbackUsed = fallbackUsed;
                llmModelUsed = modelUsed;

                narrationPlan = new ReelNarrationPlan
                {
                    FullNarrationText = narrationText,
                    IsLlmRewritten = isLlmRewritten,
                    LlmModelUsed = modelUsed
                };
                plan.NarrationPlan = narrationPlan;
            }

            // Build 4-role story slides for the manifest (captures the narrative context).
            // SelectedCaption is set to the keyword caption for each stage.
            slides = plan.Slides.Select((p, idx) => new SlideSelection
            {
                Slot = idx + 1,
                Role = p.Role,
                SourceType = p.Source.SourceType,
                AppName = p.Source.Entry.AppName,
                ImageName = p.Source.Entry.ImageName,
                SelectedCaption = KeywordCaptionProvider.GetCaptionForStage(p.Role, p.Source.Entry),
                NarrationText = narrationPlan?.FullNarrationText ?? p.NarrationText,
                SourcePath = p.Source.ImagePath
            }).ToList();
        }

        // ── TTS audio ─────────────────────────────────────────────────────────────────────────
        // Generate audio BEFORE building the reveal timeline so chunk durations use the
        // real measured WAV length, not an estimate.
        string? structuredAudioPath = null;
        bool structuredHasAudio = false;
        int structuredAudioDurationMs = 0;

        if (config.Audio.Enabled && narrationPlan is not null)
        {
            structuredAudioPath = TryGenerateSingleNarration(
                narrationPlan.FullNarrationText, config, generationId, outputAudioDir);
            structuredHasAudio = structuredAudioPath is not null;

            if (structuredHasAudio && structuredAudioPath is not null)
            {
                structuredAudioDurationMs = WavDurationReader.ReadDurationMs(structuredAudioPath);
                _logger.LogInformation("Audio duration: {Ms} ms", structuredAudioDurationMs);
            }
        }

        // ── Final duration ────────────────────────────────────────────────────────────────────
        int finalStructuredDurationMs;
        int finalStructuredDurationSeconds;
        if (structuredAudioDurationMs > 0)
        {
            finalStructuredDurationMs = Math.Max(
                structuredAudioDurationMs + config.CaptionSync.TailHoldMs,
                config.CaptionSync.MinVisualDurationMs);
            finalStructuredDurationSeconds = (int)Math.Ceiling(finalStructuredDurationMs / 1000.0);
            _logger.LogInformation(
                "Narration-driven duration: {Ms} ms ({Seconds} s)",
                finalStructuredDurationMs, finalStructuredDurationSeconds);
        }
        else
        {
            finalStructuredDurationMs = Math.Max(
                durationSeconds * 1000,
                config.CaptionSync.MinVisualDurationMs);
            finalStructuredDurationSeconds = (int)Math.Ceiling(finalStructuredDurationMs / 1000.0);
        }

        // ── Reveal timeline (manifest reference only) ─────────────────────────────────────────
        // Still built and stored in the manifest for reference; NOT used to drive slide rendering.
        if (narrationPlan is not null)
        {
            narrationPlan.ExpectedAudioDurationMs = structuredAudioDurationMs;

            int effectiveAudioMs = ResolveEffectiveAudioMs(
                structuredAudioDurationMs, finalStructuredDurationMs, config.CaptionSync.TailHoldMs);

            var revealChunks = NarrationPlanner.BuildRevealTimeline(
                NarrationPlanner.SplitIntoChunks(narrationPlan.FullNarrationText),
                effectiveAudioMs,
                config.CaptionSync.TailHoldMs);
            narrationPlan.DisplayChunks = revealChunks;
        }

        // ── Render 4 keyword-caption slides ───────────────────────────────────────────────────
        // One PNG per narrative stage — keyword caption (1–3 words), equal time per slide.
        // Narration audio plays over all 4 slides unchanged.
        _logger.LogInformation("Rendering 4 keyword-caption slides...");

        var structuredSlidePaths = new List<string>();
        string sourceImagePath = slides.Count > 0 ? slides[0].SourcePath : string.Empty;
        string appName = storyDraft?.AppName ?? (slides.Count > 0 ? slides[0].AppName : string.Empty);
        string imageName = storyDraft?.ImageName ?? (slides.Count > 0 ? slides[0].ImageName : string.Empty);
        AppSourceType sourceType = slides.Count > 0 ? slides[0].SourceType : AppSourceType.MyApp;

        // Divide total duration equally across the 4 stages; absorb remainder in the last slide.
        int msPerSlide = finalStructuredDurationMs / 4;
        int remainderMs = finalStructuredDurationMs - (msPerSlide * 3);
        var keywordDurationsMs = new List<int>
        {
            msPerSlide,
            msPerSlide,
            msPerSlide,
            remainderMs
        };

        var stageRoles = new[] { SlideRole.Hook, SlideRole.PainPoint, SlideRole.Credibility, SlideRole.Cta };
        for (int i = 0; i < 4; i++)
        {
            var role = stageRoles[i];
            var keyword = selectedAppEntry is not null
                ? KeywordCaptionProvider.GetCaptionForStage(role, selectedAppEntry)
                : KeywordCaptionProvider.GetFallbackCaption(role, appName);

            var keywordSlide = new SlideSelection
            {
                Slot = i + 1,
                Role = role,
                AppName = appName,
                ImageName = imageName,
                SelectedCaption = keyword,
                SourcePath = sourceImagePath,
                SourceType = sourceType
            };
            var path = _slideRenderer.RenderSlide(keywordSlide, outputImagesDir, config, generationId);
            keywordSlide.RenderedSlidePath = path;
            structuredSlidePaths.Add(path);
        }

        // ── Compose video ─────────────────────────────────────────────────────────────────────
        // Pass per-stage durations so the composer gives each slide exactly its allotted window.
        IReadOnlyList<int> chunkDurationsMs = keywordDurationsMs;

        _logger.LogInformation("Composing video...");
        var structuredVideoPath = _videoComposer.ComposeVideo(
            structuredSlidePaths, outputVideosDir, generationId,
            finalStructuredDurationSeconds, transition, config,
            structuredAudioPath, chunkDurationsMs);

        // ── Write manifest ────────────────────────────────────────────────────────────────────
        var structuredManifest = new ReelManifest
        {
            GenerationId = generationId,
            CreatedUtc = DateTime.UtcNow,
            SeedUsed = seed,
            DurationSeconds = finalStructuredDurationSeconds,
            Width = config.Video.Width,
            Height = config.Video.Height,
            Fps = config.Video.Fps,
            TransitionStyle = transition.ToString(),
            Strategy = strategyMode,
            HasAudio = structuredHasAudio,
            VideoPath = structuredVideoPath,
            SlidePaths = structuredSlidePaths,
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
            AudioDurationMs = structuredAudioDurationMs,
            FinalVideoDurationMs = finalStructuredDurationMs,
            LlmFallbackUsed = llmFallbackUsed,
            RevealTimeline = narrationPlan?.DisplayChunks
        };

        _manifestWriter.WriteManifest(structuredManifest, outputManifestsDir);

        _logger.LogInformation("Reel {Id} complete (strategy={Strategy}, audio={Audio}, duration={Dur}s)",
            generationId, strategyMode, structuredHasAudio, finalStructuredDurationSeconds);
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
            // Run the async rewrite on the thread-pool to avoid potential deadlocks when
            // this synchronous method is called from a non-async call chain.
            // In this Windows console app there is no SynchronizationContext, so
            // Task.Run is still the safest pattern here.
            var rewritten = Task.Run(() => provider.RewriteAsync(draft)).GetAwaiter().GetResult();
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
    /// Resolves the effective audio duration to use for proportional chunk allocation.
    /// Uses the real measured audio length when available; falls back to the visual
    /// duration minus the tail hold so chunks fill the screen time proportionally.
    /// </summary>
    private static int ResolveEffectiveAudioMs(int audioDurationMs, int finalDurationMs, int tailHoldMs) =>
        audioDurationMs > 0
            ? audioDurationMs
            : Math.Max(0, finalDurationMs - tailHoldMs);

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
