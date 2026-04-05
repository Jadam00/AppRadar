using AppRadar.Config;
using Microsoft.Extensions.Logging;

namespace AppRadar.Llm;

/// <summary>
/// Creates the appropriate <see cref="ICaptionRewriteProvider"/> based on the
/// <see cref="LlmConfig"/> settings.
///
/// When LLM is disabled or <paramref name="config"/> requests an unknown provider,
/// returns a <see cref="DeterministicCaptionJoiner"/> so the pipeline always has
/// a working rewrite implementation.
/// </summary>
public static class CaptionRewriteProviderFactory
{
    /// <summary>
    /// Creates the configured provider.
    /// If <see cref="LlmConfig.Enabled"/> is false the deterministic joiner is returned directly.
    /// </summary>
    public static ICaptionRewriteProvider Create(LlmConfig config, ILoggerFactory loggerFactory)
    {
        if (!config.Enabled)
            return new DeterministicCaptionJoiner();

        if (config.Provider.Equals("Ollama", StringComparison.OrdinalIgnoreCase))
        {
            var http = new HttpClient();
            var logger = loggerFactory.CreateLogger<OllamaCaptionRewriteProvider>();
            return new OllamaCaptionRewriteProvider(config.Ollama, http, logger);
        }

        // Unknown provider — fall back to deterministic joiner
        loggerFactory.CreateLogger(nameof(CaptionRewriteProviderFactory))
            .LogWarning("Unknown LLM provider '{Provider}'; falling back to deterministic join.", config.Provider);
        return new DeterministicCaptionJoiner();
    }
}
