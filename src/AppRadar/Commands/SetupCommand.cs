using System.CommandLine;
using System.Text.Json;
using AppRadar.Models;
using AppRadar.Utilities;
using Microsoft.Extensions.Logging;

namespace AppRadar.Commands;

public static class SetupCommand
{
    public static Command Build(ILoggerFactory loggerFactory)
    {
        var inputOption = new Option<string>(
            name: "--input",
            description: "Input directory path",
            getDefaultValue: () => "input");

        var cmd = new Command("setup",
            "Generate sample placeholder images for testing (when real screenshots are not available)")
        {
            inputOption
        };

        cmd.SetHandler((string input) =>
        {
            var logger = loggerFactory.CreateLogger("AppRadar.Commands.Setup");
            logger.LogInformation("Generating sample placeholder images into {Input}", input);

            GenerateSampleImages(input, logger);

            logger.LogInformation("Setup complete. You can now run: appradar generate --count 1 --duration 24");
        }, inputOption);

        return cmd;
    }

    private static void GenerateSampleImages(string inputDir, ILogger logger)
    {
        var featuredImagesDir = Path.Combine(inputDir, "featuredApps", "images");
        var myAppsImagesDir = Path.Combine(inputDir, "myApps", "images");

        Directory.CreateDirectory(featuredImagesDir);
        Directory.CreateDirectory(myAppsImagesDir);

        // Load descriptions to know which images to generate
        var featuredDescPath = Path.Combine(inputDir, "featuredApps", "description.json");
        var myAppsDescPath = Path.Combine(inputDir, "myApps", "description.json");

        var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

        if (File.Exists(featuredDescPath))
        {
            var desc = JsonSerializer.Deserialize<AppDescription>(
                File.ReadAllText(featuredDescPath), options);

            if (desc?.Apps != null)
            {
                foreach (var app in desc.Apps.Where(a => a.Enabled))
                {
                    var imgPath = Path.Combine(featuredImagesDir, app.ImageName);
                    if (!File.Exists(imgPath))
                    {
                        logger.LogInformation("Generating placeholder: {Name}", app.ImageName);
                        PlaceholderImageGenerator.Generate(imgPath, app.AppName);
                    }
                    else
                    {
                        logger.LogInformation("Image already exists: {Name}", app.ImageName);
                    }
                }
            }
        }

        if (File.Exists(myAppsDescPath))
        {
            var desc = JsonSerializer.Deserialize<AppDescription>(
                File.ReadAllText(myAppsDescPath), options);

            if (desc?.Apps != null)
            {
                foreach (var app in desc.Apps.Where(a => a.Enabled))
                {
                    var imgPath = Path.Combine(myAppsImagesDir, app.ImageName);
                    if (!File.Exists(imgPath))
                    {
                        logger.LogInformation("Generating placeholder: {Name}", app.ImageName);
                        PlaceholderImageGenerator.Generate(imgPath, app.AppName);
                    }
                    else
                    {
                        logger.LogInformation("Image already exists: {Name}", app.ImageName);
                    }
                }
            }
        }
    }
}
