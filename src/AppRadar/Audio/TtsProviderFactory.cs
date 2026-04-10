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
        ArgumentNullException.ThrowIfNull(config);
        ArgumentNullException.ThrowIfNull(loggerFactory);

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
            WarnIfSystemSpeechOnlyOptionsConfiguredForPiper(config, loggerFactory);

            return new PiperTtsProvider(
                config.Piper,
                loggerFactory.CreateLogger<PiperTtsProvider>());
        }

        if (name.Equals("Xtts", StringComparison.OrdinalIgnoreCase))
        {
            WarnIfSystemSpeechOnlyOptionsConfiguredForXtts(config, loggerFactory);

            return new XttsTtsProvider(
                config.Xtts,
                loggerFactory.CreateLogger<XttsTtsProvider>());
        }

        throw new NotSupportedException(
            $"Unknown TTS provider: '{name}'. Supported values: SystemSpeech, Piper, Xtts");
    }

    private static void WarnIfSystemSpeechOnlyOptionsConfiguredForPiper(
        AudioConfig config,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(TtsProviderFactory));

        if (!string.IsNullOrWhiteSpace(config.VoiceName))
        {
            logger.LogWarning(
                "audio.voiceName is ignored when ttsProvider is Piper. " +
                "Set ttsProvider to SystemSpeech to use a SAPI voice name.");
        }

        if (config.Rate != 0)
        {
            logger.LogWarning(
                "audio.rate is ignored when ttsProvider is Piper. " +
                "Use audio.piper.lengthScale to tune speech pace.");
        }

        if (config.Volume != 100)
        {
            logger.LogWarning(
                "audio.volume is ignored when ttsProvider is Piper. " +
                "Use normalizeAudio and post-processing for loudness control.");
        }
    }

    private static void WarnIfSystemSpeechOnlyOptionsConfiguredForXtts(
        AudioConfig config,
        ILoggerFactory loggerFactory)
    {
        var logger = loggerFactory.CreateLogger(typeof(TtsProviderFactory));

        if (!string.IsNullOrWhiteSpace(config.VoiceName))
        {
            logger.LogWarning(
                "audio.voiceName is ignored when ttsProvider is Xtts. " +
                "Set voice references in audio.xtts.voicePath instead.");
        }

        if (config.Rate != 0)
        {
            logger.LogWarning(
                "audio.rate is ignored when ttsProvider is Xtts. " +
                "Tune pacing in text and service/model settings instead.");
        }

        if (config.Volume != 100)
        {
            logger.LogWarning(
                "audio.volume is ignored when ttsProvider is Xtts. " +
                "Use normalizeAudio and post-processing for loudness control.");
        }
    }
}
