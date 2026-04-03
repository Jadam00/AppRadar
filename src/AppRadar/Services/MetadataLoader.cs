using System.Text.Json;
using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Services;

public sealed class MetadataLoader
{
    private readonly ILogger<MetadataLoader> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true
    };

    public MetadataLoader(ILogger<MetadataLoader> logger)
    {
        _logger = logger;
    }

    public (AppDescription description, string imagesDir) LoadFeaturedApps(string inputDirectory)
    {
        return LoadFromFolder(Path.Combine(inputDirectory, "featuredApps"));
    }

    public (AppDescription description, string imagesDir) LoadMyApps(string inputDirectory)
    {
        return LoadFromFolder(Path.Combine(inputDirectory, "myApps"));
    }

    private (AppDescription description, string imagesDir) LoadFromFolder(string folderPath)
    {
        _logger.LogInformation("Loading metadata from {Folder}", folderPath);

        if (!Directory.Exists(folderPath))
        {
            throw new DirectoryNotFoundException($"Input folder not found: {folderPath}");
        }

        var descriptionPath = Path.Combine(folderPath, "description.json");
        if (!File.Exists(descriptionPath))
        {
            throw new FileNotFoundException($"description.json not found in: {folderPath}", descriptionPath);
        }

        var json = File.ReadAllText(descriptionPath);
        AppDescription? description;
        try
        {
            description = JsonSerializer.Deserialize<AppDescription>(json, JsonOptions);
        }
        catch (JsonException ex)
        {
            throw new InvalidDataException($"Invalid JSON in {descriptionPath}: {ex.Message}", ex);
        }

        if (description is null)
        {
            throw new InvalidDataException($"description.json is empty or null: {descriptionPath}");
        }

        var imagesDir = Path.Combine(folderPath, "images");
        _logger.LogInformation("Loaded {Count} app entries from {Folder}", description.Apps.Count, folderPath);

        return (description, imagesDir);
    }
}
