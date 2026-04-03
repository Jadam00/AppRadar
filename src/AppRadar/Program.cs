using System.CommandLine;
using AppRadar.Commands;
using Microsoft.Extensions.Logging;

var loggerFactory = LoggerFactory.Create(builder =>
{
    builder
        .AddConsole()
        .SetMinimumLevel(LogLevel.Information);
});

var rootCommand = new RootCommand("AppRadar - Generate vertical marketing reels from local images");
rootCommand.AddCommand(GenerateCommand.Build(loggerFactory));
rootCommand.AddCommand(SetupCommand.Build(loggerFactory));

return await rootCommand.InvokeAsync(args);
