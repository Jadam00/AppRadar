using AppRadar.Config;
using Microsoft.Extensions.Logging;

namespace AppRadar.Audio;

/// <summary>
/// Creates the correct <see cref="ITtsProvider"/> for the current platform and config.
/// </summary>
public static class TtsProviderFactory
{
    /// <summary>
    /// Returns an <see cref="ITtsProvider"/> appropriate for the configured provider name.
    /// Throws <see cref="InvalidOperationException"/> when the requested provider is not
    /// available in the current environment.
    /// Supported provider names: <c>"SystemSpeech"</c>, <c>"Piper"</c>.
    /// </summary>
    public static ITtsProvider Create(AudioConfig config, ILoggerFactory loggerFactory)
    {
        var name = (config.TtsProvider ?? string.Empty).Trim();

        if (string.IsNullOrEmpty(name) ||
            name.Equals("SystemSpeech", StringComparison.OrdinalIgnoreCase))
        {
            if (!OperatingSystem.IsWindows())
            {
                throw new PlatformNotSupportedException(
                    "TTS provider 'SystemSpeech' is only available on Windows. " +
                    "Run AppRadar on Windows or disable audio: --with-audio false");
            }

            return new SystemSpeechTtsProvider(
                loggerFactory.CreateLogger<SystemSpeechTtsProvider>());
        }

        if (name.Equals("Piper", StringComparison.OrdinalIgnoreCase))
        {
            return new PiperTtsProvider(
                config.Piper,
                loggerFactory.CreateLogger<PiperTtsProvider>());
        }

        throw new NotSupportedException(
            $"Unknown TTS provider: '{name}'. Supported values: SystemSpeech, Piper");
    }
}
