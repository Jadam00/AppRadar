using System.Text.Json;
using System.Text.Json.Serialization;
using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Services;

public sealed class ManifestWriter
{
    private readonly ILogger<ManifestWriter> _logger;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    public ManifestWriter(ILogger<ManifestWriter> logger)
    {
        _logger = logger;
    }

    public string WriteManifest(ReelManifest manifest, string outputManifestsDir)
    {
        var outputPath = Path.Combine(outputManifestsDir, $"{manifest.GenerationId}_manifest.json");
        _logger.LogInformation("Writing manifest to {Path}", outputPath);

        var json = JsonSerializer.Serialize(manifest, JsonOptions);
        File.WriteAllText(outputPath, json);

        _logger.LogInformation("Manifest saved");
        return outputPath;
    }
}
