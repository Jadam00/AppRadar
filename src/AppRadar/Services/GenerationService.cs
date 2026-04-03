using AppRadar.Config;
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
    private readonly SlideRenderer _slideRenderer;
    private readonly VideoComposer _videoComposer;
    private readonly ManifestWriter _manifestWriter;

    public GenerationService(
        ILogger<GenerationService> logger,
        MetadataLoader metadataLoader,
        MetadataValidator validator,
        SelectionService selectionService,
        SlideRenderer slideRenderer,
        VideoComposer videoComposer,
        ManifestWriter manifestWriter)
    {
        _logger = logger;
        _metadataLoader = metadataLoader;
        _validator = validator;
        _selectionService = selectionService;
        _slideRenderer = slideRenderer;
        _videoComposer = videoComposer;
        _manifestWriter = manifestWriter;
    }

    public void RunGeneration(GenerationOptions options)
    {
        _logger.LogInformation("=== AppRadar Generation Started ===");
        _logger.LogInformation("Count: {Count}, Duration: {Duration}s, Seed: {Seed}",
            options.Count, options.DurationSeconds, options.Seed?.ToString() ?? "random");

        // Validate FFmpeg
        _videoComposer.ValidateFfmpegAvailable();

        // Ensure output directories exist
        EnsureOutputDirectories(options.OutputDirectory);

        // Load config
        var configLoader = new ConfigLoader(
            Microsoft.Extensions.Logging.Abstractions.NullLogger<ConfigLoader>.Instance);
        var config = configLoader.Load(options.InputDirectory);

        // Override config from CLI
        if (options.Fps.HasValue) config.Video.Fps = options.Fps.Value;
        if (options.DurationSeconds > 0) config.Video.DefaultDurationSeconds = options.DurationSeconds;

        // Load metadata
        _logger.LogInformation("Loading metadata...");
        var (featuredDesc, featuredImagesDir) = _metadataLoader.LoadFeaturedApps(options.InputDirectory);
        var (myAppsDesc, myAppsImagesDir) = _metadataLoader.LoadMyApps(options.InputDirectory);

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

        var outputImagesDir = Path.Combine(options.OutputDirectory, "images");
        var outputVideosDir = Path.Combine(options.OutputDirectory, "videos");
        var outputManifestsDir = Path.Combine(options.OutputDirectory, "manifests");

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
                outputImagesDir, outputVideosDir, outputManifestsDir,
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
        int durationSeconds)
    {
        var generationId = $"reel_{DateTime.UtcNow:yyyyMMdd_HHmmss}_{Guid.NewGuid().ToString("N")[..8]}";

        // Selection
        var (slides, transition) = _selectionService.Select(featuredSources, myAppSources, rng);

        // Render slides
        _logger.LogInformation("Rendering {Count} slides...", slides.Count);
        var slidePaths = new List<string>();
        foreach (var slide in slides)
        {
            var slidePath = _slideRenderer.RenderSlide(slide, outputImagesDir, config, generationId);
            slide.RenderedSlidePath = slidePath;
            slidePaths.Add(slidePath);
        }

        // Compose video
        _logger.LogInformation("Composing video...");
        var videoPath = _videoComposer.ComposeVideo(
            slidePaths, outputVideosDir, generationId,
            durationSeconds, transition, config);

        // Write manifest
        var manifest = new ReelManifest
        {
            GenerationId = generationId,
            CreatedUtc = DateTime.UtcNow,
            SeedUsed = seed,
            DurationSeconds = durationSeconds,
            Width = config.Video.Width,
            Height = config.Video.Height,
            Fps = config.Video.Fps,
            TransitionStyle = transition.ToString(),
            VideoPath = videoPath,
            SlidePaths = slidePaths,
            Slides = slides
        };

        _manifestWriter.WriteManifest(manifest, outputManifestsDir);

        _logger.LogInformation("Reel {Id} complete", generationId);
    }

    private static void EnsureOutputDirectories(string outputDirectory)
    {
        Directory.CreateDirectory(Path.Combine(outputDirectory, "images"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "videos"));
        Directory.CreateDirectory(Path.Combine(outputDirectory, "manifests"));
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
}
