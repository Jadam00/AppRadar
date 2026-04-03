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

            if (string.IsNullOrWhiteSpace(entry.ImageName))
            {
                errors.Add($"Entry '{entry.AppName}' has an empty imageName");
                continue;
            }

            if (!seenNames.Add(entry.ImageName))
            {
                errors.Add($"Duplicate imageName '{entry.ImageName}' found in description.json");
                continue;
            }

            if (entry.Captions is null || entry.Captions.Count == 0)
            {
                errors.Add($"Entry '{entry.AppName}' (image: {entry.ImageName}) has no captions");
                continue;
            }

            var imagePath = Path.Combine(imagesDir, entry.ImageName);
            if (!File.Exists(imagePath))
            {
                errors.Add($"Image file not found: {imagePath}");
                continue;
            }

            sources.Add(new AppSource
            {
                Entry = entry,
                SourceType = sourceType,
                ImagePath = imagePath
            });
        }

        if (errors.Count > 0)
        {
            var combined = string.Join(Environment.NewLine + "  - ", errors);
            throw new ValidationException($"Validation errors:{Environment.NewLine}  - {combined}");
        }

        return sources;
    }

    public void ValidateFeaturedCount(List<AppSource> sources)
    {
        if (sources.Count < 3)
        {
            throw new ValidationException(
                $"At least 3 enabled featured apps are required, but only {sources.Count} valid entries found.");
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
