using System.Net.Http.Json;
using System.Text;
using System.Text.Json.Serialization;
using AppRadar.Config;
using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Llm;

/// <summary>
/// Generates short per-stage overlay captions using a locally running Ollama model.
/// </summary>
public sealed class OllamaStageCaptionProvider : IStageCaptionProvider
{
    private const string SystemPrompt =
        "You are a direct-response mobile app marketing copywriter. " +
        "Generate one short on-screen caption for a single marketing stage in a vertical reel. " +
        "Return plain text only, no quotes, no bullets, no hashtags, no emojis. " +
        "Keep it punchy and readable in 4 to 8 words.";

    private readonly OllamaConfig _config;
    private readonly HttpClient _http;
    private readonly ILogger<OllamaStageCaptionProvider> _logger;

    public OllamaStageCaptionProvider(
        OllamaConfig config,
        HttpClient http,
        ILogger<OllamaStageCaptionProvider> logger)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public string ProviderName => $"Ollama/{_config.Model}";

    public async Task<string> GenerateCaptionAsync(
        SlideRole stage,
        ReelStoryDraft draft,
        CancellationToken cancellationToken = default)
    {
        var stageText = GetStageText(stage, draft);

        var prompt =
            $"App name: {draft.AppName}\n" +
            $"Tags: {draft.Tags}\n" +
            $"Stage: {stage}\n" +
            $"Stage source text: {stageText}\n\n" +
            "Write one short overlay caption for this stage. Maximum 5 words.";

        var requestBody = new OllamaGenerateRequest
        {
            Model = _config.Model,
            System = SystemPrompt,
            Prompt = prompt,
            Stream = false,
            Options = new OllamaOptions { Temperature = _config.Temperature }
        };

        using var cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
        cts.CancelAfter(TimeSpan.FromSeconds(_config.TimeoutSeconds));

        string rawText;
        try
        {
            var url = _config.BaseUrl.TrimEnd('/') + "/api/generate";
            using var response = await _http.PostAsJsonAsync(url, requestBody, cts.Token);
            response.EnsureSuccessStatusCode();
            var payload = await response.Content.ReadFromJsonAsync<OllamaGenerateResponse>(cts.Token);
            if (payload is null || string.IsNullOrWhiteSpace(payload.Response))
                throw new CaptionRewriteException("Ollama returned an empty stage caption response.");

            rawText = payload.Response;
        }
        catch (OperationCanceledException ex) when (!cancellationToken.IsCancellationRequested)
        {
            throw new CaptionRewriteException(
                $"Ollama stage caption request timed out after {_config.TimeoutSeconds}s.", ex);
        }
        catch (HttpRequestException ex)
        {
            throw new CaptionRewriteException($"Ollama stage caption request failed: {ex.Message}", ex);
        }

        var cleaned = NormalizeCaption(rawText);
        var wordCount = CountWords(cleaned);
        if (wordCount < 4)
            throw new CaptionRewriteException("Generated stage caption is too short after normalization.");

        if (wordCount > 8)
            cleaned = TrimToWordCount(cleaned, 8);

        _logger.LogInformation(
            "Generated stage caption for {Stage}: {Caption}",
            stage,
            cleaned);
        return cleaned;
    }

    internal static string NormalizeCaption(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        var cleaned = StripThinkingBlocks(text);
        cleaned = cleaned.Replace('\r', ' ').Replace('\n', ' ');

        var words = cleaned
            .Split(' ', StringSplitOptions.RemoveEmptyEntries)
            .Where(w => !w.StartsWith('#'));

        cleaned = string.Join(' ', words);
        cleaned = RemoveEmoji(cleaned);
        cleaned = cleaned.Trim().Trim('"', '\'', '.', ',', ';', ':', '!', '?', '-', '_');

        return CollapseWhitespace(cleaned);
    }

    internal static int CountWords(string text) =>
        string.IsNullOrWhiteSpace(text)
            ? 0
            : text.Split(' ', StringSplitOptions.RemoveEmptyEntries).Length;

    internal static string TrimToWordCount(string text, int maxWords)
    {
        if (string.IsNullOrWhiteSpace(text) || maxWords <= 0)
            return string.Empty;

        var words = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length <= maxWords)
            return text;

        return string.Join(' ', words.Take(maxWords));
    }

    private static string GetStageText(SlideRole stage, ReelStoryDraft draft) =>
        stage switch
        {
            SlideRole.Hook => draft.HookText,
            SlideRole.PainPoint => draft.PainPointText,
            SlideRole.Credibility => draft.CredibilityText,
            SlideRole.Cta => draft.CtaText,
            _ => draft.CtaText
        };

    private static string StripThinkingBlocks(string text)
    {
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

    private static string RemoveEmoji(string text)
    {
        var sb = new StringBuilder(text.Length);
        for (int i = 0; i < text.Length; i++)
        {
            char c = text[i];
            if (char.IsHighSurrogate(c))
            {
                if (i + 1 < text.Length && char.IsLowSurrogate(text[i + 1]))
                    i++;
                continue;
            }

            if ((c >= 0x2600 && c <= 0x27FF) || (c >= 0x2B00 && c <= 0x2BFF) || (c >= 0xFE00 && c <= 0xFE0F))
                continue;

            sb.Append(c);
        }

        return sb.ToString();
    }

    private static string CollapseWhitespace(string text)
    {
        var parts = text.Split(' ', StringSplitOptions.RemoveEmptyEntries);
        return string.Join(' ', parts);
    }

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
