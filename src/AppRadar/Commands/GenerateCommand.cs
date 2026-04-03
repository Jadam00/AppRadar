using System.CommandLine;
using AppRadar.Config;
using AppRadar.Rendering;
using AppRadar.Services;
using AppRadar.Video;
using Microsoft.Extensions.Logging;

namespace AppRadar.Commands;

public static class GenerateCommand
{
    public static Command Build(ILoggerFactory loggerFactory)
    {
        var countOption = new Option<int>(
            name: "--count",
            description: "Number of reels to generate",
            getDefaultValue: () => 1);

        var durationOption = new Option<int>(
            name: "--duration",
            description: "Target duration in seconds for each reel",
            getDefaultValue: () => 24);

        var seedOption = new Option<int?>(
            name: "--seed",
            description: "Optional seed for deterministic random selection");
        seedOption.IsRequired = false;

        var fpsOption = new Option<int?>(
            name: "--fps",
            description: "Frames per second (overrides config)");
        fpsOption.IsRequired = false;

        var inputOption = new Option<string>(
            name: "--input",
            description: "Input directory path",
            getDefaultValue: () => "input");

        var outputOption = new Option<string>(
            name: "--output",
            description: "Output directory path",
            getDefaultValue: () => "output");

        var generateCommand = new Command("generate", "Generate marketing reel videos")
        {
            countOption,
            durationOption,
            seedOption,
            fpsOption,
            inputOption,
            outputOption
        };

        generateCommand.SetHandler(
            (int count, int duration, int? seed, int? fps, string input, string output) =>
            {
                var logger = loggerFactory.CreateLogger("AppRadar.Commands.Generate");
                logger.LogInformation("Starting generation: count={Count}, duration={Duration}s", count, duration);

                var service = new GenerationService(
                    loggerFactory.CreateLogger<GenerationService>(),
                    new MetadataLoader(loggerFactory.CreateLogger<MetadataLoader>()),
                    new MetadataValidator(loggerFactory.CreateLogger<MetadataValidator>()),
                    new SelectionService(loggerFactory.CreateLogger<SelectionService>()),
                    new SlideRenderer(loggerFactory.CreateLogger<SlideRenderer>()),
                    new VideoComposer(loggerFactory.CreateLogger<VideoComposer>()),
                    new ManifestWriter(loggerFactory.CreateLogger<ManifestWriter>()));

                var options = new GenerationOptions
                {
                    Count = count,
                    DurationSeconds = duration,
                    Seed = seed,
                    Fps = fps,
                    InputDirectory = input,
                    OutputDirectory = output
                };

                service.RunGeneration(options);
            },
            countOption, durationOption, seedOption, fpsOption, inputOption, outputOption);

        return generateCommand;
    }
}
