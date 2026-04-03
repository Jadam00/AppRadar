using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AppRadar.Config;

public sealed class ConfigLoader
{
    private readonly ILogger<ConfigLoader> _logger;

    public ConfigLoader(ILogger<ConfigLoader> logger)
    {
        _logger = logger;
    }

    public AppConfig Load(string inputDirectory)
    {
        var configPath = Path.Combine(inputDirectory, "config.json");

        if (!File.Exists(configPath))
        {
            _logger.LogWarning("No config.json found at {Path}, using defaults", configPath);
            return new AppConfig();
        }

        _logger.LogInformation("Loading config from {Path}", configPath);

        try
        {
            var json = File.ReadAllText(configPath);
            var options = new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true
            };
            var config = JsonSerializer.Deserialize<AppConfig>(json, options);
            if (config is null)
            {
                _logger.LogWarning("Config file was empty or null, using defaults");
                return new AppConfig();
            }

            _logger.LogInformation("Config loaded successfully");
            return config;
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to load config.json, using defaults");
            return new AppConfig();
        }
    }
}
