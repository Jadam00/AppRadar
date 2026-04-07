using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Services;

public sealed class MetadataValidator
{
    private readonly ILogger<MetadataValidator> _logger;

    public MetadataValidator(ILogger<MetadataValidator> logger)
    {
        _logger = logger;
    }

    public List<AppSource> ValidateAndBuildSources(
        AppDescription description,
        string imagesDir,
        AppSourceType sourceType)
    {
        var sources = new List<AppSource>();
        var seenNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var errors = new List<string>();

        foreach (var entry in description.Apps)
        {
            if (!entry.Enabled)
            {
                _logger.LogDebug("Skipping disabled entry: {AppName}", entry.AppName);
                continue;
            }

            if (string.IsNullOrWhiteSpace(entry.AppName))
            {
                errors.Add("An enabled entry has an empty appName");
                continue;
            }

            var stageCandidates = new Dictionary<SlideRole, List<StageImageCandidate>>();
            ValidateStage(entry, SlideRole.Hook, entry.Hook, imagesDir, seenNames, stageCandidates, errors);
            ValidateStage(entry, SlideRole.PainPoint, entry.PainPoint, imagesDir, seenNames, stageCandidates, errors);
            ValidateStage(entry, SlideRole.Credibility, entry.Credibility, imagesDir, seenNames, stageCandidates, errors);
            ValidateStage(entry, SlideRole.Cta, entry.Cta, imagesDir, seenNames, stageCandidates, errors);

            if (HasStageErrorsForEntry(errors, entry.AppName))
                continue;

            var defaultImagePath = stageCandidates[SlideRole.Hook][0].ImagePath;

            sources.Add(new AppSource
            {
                Entry = entry,
                SourceType = sourceType,
                ImagePath = defaultImagePath,
                StageImageCandidates = stageCandidates
            });
        }

        if (errors.Count > 0)
        {
            var combined = string.Join(Environment.NewLine + "  - ", errors);
            throw new ValidationException($"Validation errors:{Environment.NewLine}  - {combined}");
        }

        return sources;
    }

    private static bool HasStageErrorsForEntry(List<string> errors, string appName) =>
        errors.Any(e => e.Contains($"Entry '{appName}'", StringComparison.OrdinalIgnoreCase));

    private static void ValidateStage(
        AppEntry entry,
        SlideRole role,
        StageContent stage,
        string imagesDir,
        HashSet<string> seenNames,
        Dictionary<SlideRole, List<StageImageCandidate>> stageCandidates,
        List<string> errors)
    {
        var stageName = role.ToString();

        if (stage.Captions is null || stage.Captions.Count == 0)
        {
            errors.Add($"Entry '{entry.AppName}' has no captions for stage '{stageName}'");
            return;
        }

        if (stage.ImageNames is null || stage.ImageNames.Count == 0)
        {
            errors.Add($"Entry '{entry.AppName}' has no imageNames for stage '{stageName}'");
            return;
        }

        var candidates = new List<StageImageCandidate>();
        foreach (var imageName in stage.ImageNames)
        {
            if (string.IsNullOrWhiteSpace(imageName))
            {
                errors.Add($"Entry '{entry.AppName}' has an empty imageName in stage '{stageName}'");
                continue;
            }

            var uniqueKey = $"{entry.AppName}:{stageName}:{imageName}";
            if (!seenNames.Add(uniqueKey))
            {
                errors.Add($"Duplicate stage imageName '{imageName}' found for entry '{entry.AppName}' stage '{stageName}'");
                continue;
            }

            var imagePath = Path.Combine(imagesDir, imageName);
            if (!File.Exists(imagePath))
            {
                errors.Add($"Image file not found for entry '{entry.AppName}' stage '{stageName}': {imagePath}");
                continue;
            }

            candidates.Add(new StageImageCandidate
            {
                ImageName = imageName,
                ImagePath = imagePath
            });
        }

        if (candidates.Count == 0)
        {
            errors.Add($"Entry '{entry.AppName}' stage '{stageName}' has no valid images");
            return;
        }

        stageCandidates[role] = candidates;
    }

    public void ValidateFeaturedCount(List<AppSource> sources)
    {
        // Featured apps are optional in single-app mode (the reel app comes from myApps).
        // Log a warning when empty so the operator knows, but do not block the pipeline.
        if (sources.Count == 0)
        {
            _logger.LogWarning(
                "No valid featured app sources found. " +
                "In StructuredMarketing mode the reel will be built from myApps only.");
        }
    }

    public void ValidateMyAppsCount(List<AppSource> sources)
    {
        if (sources.Count < 1)
        {
            throw new ValidationException(
                "At least 1 enabled myApp is required, but no valid entries found.");
        }
    }
}

public sealed class ValidationException : Exception
{
    public ValidationException(string message) : base(message) { }
}
