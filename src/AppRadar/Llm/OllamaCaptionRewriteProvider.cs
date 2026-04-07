using System.Net.Http.Json;
using System.Text.Json;
using System.Text.Json.Serialization;
using AppRadar.Config;
using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Llm;

/// <summary>
/// Caption rewrite provider that calls a locally running Ollama instance.
///
/// Calls POST {baseUrl}/api/generate with a carefully designed system+user prompt
/// that instructs the model to combine the 4 stage texts into one short spoken paragraph.
///
/// If Ollama is unavailable the <see cref="CaptionRewriteException"/> is thrown
/// and the caller should fall back to <see cref="DeterministicCaptionJoiner"/>.
/// </summary>
public sealed class OllamaCaptionRewriteProvider : ICaptionRewriteProvider
{
    private const string SystemPrompt =
        "You are a direct-response mobile app marketing copywriter. " +
        "Rewrite four short marketing stages into one short spoken paragraph for a vertical social media reel. " +
        "Keep the same order of ideas: hook, pain point, credibility, CTA. " +
        "Be natural, punchy, and easy to narrate. " +
        "Form a single cohesive story that flows well when spoken aloud. " +
        "Do not invent claims. " +
        "Do not use bullet points. " +
        "Do not use hashtags. " +
        "Do not use emojis. " +
        "Start with 'Ever wondered...'. " +
        "Return plain text only. Provide for emphasis and pauses between sentences, but do not use special markup for them.";

    /// <summary>
    /// Maximum number of non-empty lines accepted in an Ollama response before it is
    /// treated as bullet-point output (and the first line is taken instead).
    /// </summary>
    private const int MaxAcceptableResponseLines = 4;

    private readonly OllamaConfig _config;
    private readonly HttpClient _http;
    private readonly ILogger<OllamaCaptionRewriteProvider> _logger;

    public OllamaCaptionRewriteProvider(
        OllamaConfig config,
        HttpClient http,
        ILogger<OllamaCaptionRewriteProvider> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <inheritdoc />
    public string ProviderName => $"Ollama/{_config.Model}";

    /// <inheritdoc />
    public async Task<string> RewriteAsync(ReelStoryDraft draft, CancellationToken cancellationToken = default)
    {
        var userMessage =
            $"App name: {draft.AppName}\n" +
            $"Tags: {draft.Tags}\n" +
            $"Hook: {draft.HookText}\n" +
            $"Pain point: {draft.PainPointText}\n" +
            $"Credibility: {draft.CredibilityText}\n" +
            $"CTA: {draft.CtaText}\n\n" +
            "Rewrite these into one short paragraph suitable for narration in a vertical marketing reel. " +
            "Emphasize natural, conversational language that flows well when spoken aloud." ;
            // "Add a comma after every period .";
            // "Rewrite these into one short paragraph suitable for narration in a vertical marketing reel.";

        var requestBody = new OllamaGenerateRequest
        {
            Model = _config.Model,
            System = SystemPrompt,
            Prompt = userMessage,
            Stream = false,
            Options = new OllamaOptions { Temperature = _config.Temperature }
        };

        _logger.LogInformation("Calling Ollama ({BaseUrl}) with model {Model}", _config.BaseUrl, _config.Model);

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_config.TimeoutSeconds));

        string responseText;
        try
        {
            var url = _config.BaseUrl.TrimEnd('/') + "/api/generate";
            using var response = await _http.PostAsJsonAsync(url, requestBody, cts.Token);
            response.EnsureSuccessStatusCode();
            var responseJson = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cts.Token);

            if (responseJson is null || string.IsNullOrWhiteSpace(responseJson.Response))
                throw new CaptionRewriteException("Ollama returned an empty response.");

            responseText = responseJson.Response.Trim();
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CaptionRewriteException(
                $"Ollama request timed out after {_config.TimeoutSeconds}s.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new CaptionRewriteException(
                $"Ollama request failed: {ex.Message}", ex);
        }

        // Strip any <think>...</think> blocks that some models emit (e.g. qwen3 reasoning traces)
        responseText = StripThinkingBlocks(responseText);

        // Validate: reject obvious bullet-point / multi-line responses
        var lines = responseText
            .Split('\n', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        if (lines.Length > MaxAcceptableResponseLines)
        {
            _logger.LogWarning(
                "Ollama returned {Lines} lines — looks like bullet points. Falling back to first line.",
                lines.Length);
            responseText = lines[0];
        }

        // Reject if emojis or hashtags slipped through
        if (ContainsEmoji(responseText) || responseText.Contains('#'))
        {
            _logger.LogWarning("Ollama response contains prohibited content (emojis/hashtags). Cleaning.");
            responseText = RemoveProhibitedContent(responseText);
        }

        if (string.IsNullOrWhiteSpace(responseText))
            throw new CaptionRewriteException("Ollama returned unusable content after cleaning.");

        _logger.LogInformation("Ollama rewrite successful ({Chars} chars)", responseText.Length);
        return responseText;
    }

    private static string StripThinkingBlocks(string text)
    {
        // Remove <think>...</think> blocks emitted by some reasoning models
        while (true)
        {
            int start = text.IndexOf("<think>", StringComparison.OrdinalIgnoreCase);
            if (start < 0) break;
            int end = text.IndexOf("</think>", start, StringComparison.OrdinalIgnoreCase);
            if (end < 0) break;
            text = text[..start] + text[(end + "</think>".Length)..];
        }
        return text.Trim();
    }

    private static bool ContainsEmoji(string text)
    {
        // Modern emoji are above U+FFFF and represented as UTF-16 surrogate pairs.
        // High surrogates (U+D800–U+DBFF) indicate a codepoint above U+FFFF.
        // Also check common BMP emoji/symbol ranges:
        //   U+2600–U+27FF  Miscellaneous Symbols, Dingbats
        //   U+2B00–U+2BFF  Miscellaneous Symbols and Arrows
        //   U+FE00–U+FE0F  Variation Selectors (often follow emoji)
        foreach (char c in text)
        {
            if (char.IsHighSurrogate(c)) return true;
            if (c >= 0x2600 && c <= 0x27FF) return true;
            if (c >= 0x2B00 && c <= 0x2BFF) return true;
            if (c >= 0xFE00 && c <= 0xFE0F) return true;
        }
        return false;
    }

    private static string RemoveProhibitedContent(string text)
    {
        // Remove hashtag words
        var words = text.Split(' ')
            .Where(w => !w.StartsWith('#'))
            .ToArray();
        var withoutHashtags = string.Join(' ', words);

        // Remove emoji characters (BMP symbols and high-surrogate sequences)
        var sb = new System.Text.StringBuilder(withoutHashtags.Length);
        for (int i = 0; i < withoutHashtags.Length; i++)
        {
            char c = withoutHashtags[i];
            if (char.IsHighSurrogate(c))
            {
                // Skip surrogate pair (emoji above BMP)
                if (i + 1 < withoutHashtags.Length && char.IsLowSurrogate(withoutHashtags[i + 1]))
                    i++;
                continue;
            }
            if ((c >= 0x2600 && c <= 0x27FF) || (c >= 0x2B00 && c <= 0x2BFF) || (c >= 0xFE00 && c <= 0xFE0F))
                continue;
            sb.Append(c);
        }
        return sb.ToString().Trim();
    }

    // ── Ollama API DTOs ─────────────────────────────────────────────────────────────────────

    private sealed class OllamaGenerateRequest
    {
        [JsonPropertyName("model")]
        public string Model { get; set; } = string.Empty;

        [JsonPropertyName("system")]
        public string System { get; set; } = string.Empty;

        [JsonPropertyName("prompt")]
        public string Prompt { get; set; } = string.Empty;

        [JsonPropertyName("stream")]
        public bool Stream { get; set; }

        [JsonPropertyName("options")]
        public OllamaOptions Options { get; set; } = new();
    }

    private sealed class OllamaOptions
    {
        [JsonPropertyName("temperature")]
        public double Temperature { get; set; }
    }

    private sealed class OllamaGenerateResponse
    {
        [JsonPropertyName("response")]
        public string Response { get; set; } = string.Empty;
    }
}
