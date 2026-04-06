using System.Net;
using System.Text;
using System.Text.Json;
using AppRadar.Config;
using AppRadar.Llm;
using AppRadar.Models;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppRadar.Tests;

public sealed class OllamaStageCaptionProviderTests
{
    private static ReelStoryDraft MakeDraft() =>
        new()
        {
            AppName = "NeuroMaze",
            Tags = "logic, puzzle",
            HookText = "Most puzzle games stop challenging you after level 5",
            PainPointText = "Most puzzle apps get boring fast",
            CredibilityText = "NeuroMaze generates a new challenge every time you play",
            CtaText = "Try NeuroMaze if you dare"
        };

    private static OllamaStageCaptionProvider CreateProvider(HttpClient http, int timeoutSeconds = 30) =>
        new(
            new OllamaConfig
            {
                BaseUrl = "http://localhost:11434",
                Model = "qwen3:8b",
                TimeoutSeconds = timeoutSeconds,
                Temperature = 0.4
            },
            http,
            NullLogger<OllamaStageCaptionProvider>.Instance);

    [Fact]
    public async Task GenerateCaptionAsync_ReturnsCleanedShortCaption_OnSuccess()
    {
        var payload = JsonSerializer.Serialize(new { response = "<think>reasoning</think>  #promo Think faster solve harder today now" });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, payload);
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        var result = await provider.GenerateCaptionAsync(SlideRole.Hook, MakeDraft());

        Assert.Equal("Think faster solve harder today now", result);
        Assert.InRange(OllamaStageCaptionProvider.CountWords(result), 4, 8);
    }

    [Fact]
    public async Task GenerateCaptionAsync_TrimsToEightWords_WhenModelReturnsTooLong()
    {
        var payload = JsonSerializer.Serialize(new { response = "This logic challenge keeps your brain sharp every single day forever" });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, payload);
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        var result = await provider.GenerateCaptionAsync(SlideRole.Credibility, MakeDraft());

        Assert.Equal(8, OllamaStageCaptionProvider.CountWords(result));
        Assert.Equal("This logic challenge keeps your brain sharp every", result);
    }

    [Fact]
    public async Task GenerateCaptionAsync_Throws_WhenTooShortAfterNormalization()
    {
        var payload = JsonSerializer.Serialize(new { response = "Try now" });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, payload);
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        await Assert.ThrowsAsync<CaptionRewriteException>(
            () => provider.GenerateCaptionAsync(SlideRole.Cta, MakeDraft()));
    }

    [Fact]
    public async Task GenerateCaptionAsync_Throws_OnHttpError()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, "Server error");
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        await Assert.ThrowsAsync<CaptionRewriteException>(
            () => provider.GenerateCaptionAsync(SlideRole.PainPoint, MakeDraft()));
    }

    private sealed class FakeHttpHandler(HttpStatusCode statusCode, string content)
        : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromResult(new HttpResponseMessage(statusCode)
            {
                Content = new StringContent(content, Encoding.UTF8, "application/json")
            });
    }
}
