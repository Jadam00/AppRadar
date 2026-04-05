using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using AppRadar.Config;
using AppRadar.Llm;
using AppRadar.Models;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppRadar.Tests;

/// <summary>
/// Tests for OllamaCaptionRewriteProvider using a hand-rolled fake HTTP handler.
/// </summary>
public sealed class OllamaCaptionRewriteProviderTests
{
    private static ReelStoryDraft MakeDraft(string hook = "Hook", string pain = "Pain",
        string cred = "Cred", string cta = "CTA") =>
        new()
        {
            AppName = "TestApp",
            Tags = "puzzle, logic",
            HookText = hook,
            PainPointText = pain,
            CredibilityText = cred,
            CtaText = cta
        };

    private static OllamaCaptionRewriteProvider CreateProvider(
        HttpClient http,
        string model = "qwen3:8b",
        int timeoutSeconds = 30) =>
        new(
            new OllamaConfig { BaseUrl = "http://localhost:11434", Model = model, TimeoutSeconds = timeoutSeconds, Temperature = 0.4 },
            http,
            NullLogger<OllamaCaptionRewriteProvider>.Instance);

    // ── Success path ──────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RewriteAsync_ReturnsResponseText_OnSuccess()
    {
        var responsePayload = JsonSerializer.Serialize(new { response = "Great narration paragraph." });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, responsePayload);
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        var result = await provider.RewriteAsync(MakeDraft());

        Assert.Equal("Great narration paragraph.", result);
    }

    [Fact]
    public async Task RewriteAsync_StripsThinkingBlocks()
    {
        var raw = "<think>Some reasoning</think>Final narration text.";
        var responsePayload = JsonSerializer.Serialize(new { response = raw });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, responsePayload);
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        var result = await provider.RewriteAsync(MakeDraft());

        Assert.Equal("Final narration text.", result);
    }

    // ── Failure paths ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RewriteAsync_ThrowsCaptionRewriteException_OnHttpError()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.InternalServerError, "Server error");
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        await Assert.ThrowsAsync<CaptionRewriteException>(() => provider.RewriteAsync(MakeDraft()));
    }

    [Fact]
    public async Task RewriteAsync_ThrowsCaptionRewriteException_OnTimeout()
    {
        var handler = new SlowHttpHandler(delayMs: 5000);
        using var http = new HttpClient(handler);
        // Very short timeout to trigger cancellation quickly
        var provider = CreateProvider(http, timeoutSeconds: 1);

        await Assert.ThrowsAsync<CaptionRewriteException>(() => provider.RewriteAsync(MakeDraft()));
    }

    [Fact]
    public async Task RewriteAsync_ThrowsCaptionRewriteException_OnEmptyResponse()
    {
        var responsePayload = JsonSerializer.Serialize(new { response = "   " });
        var handler = new FakeHttpHandler(HttpStatusCode.OK, responsePayload);
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        await Assert.ThrowsAsync<CaptionRewriteException>(() => provider.RewriteAsync(MakeDraft()));
    }

    [Fact]
    public async Task RewriteAsync_ThrowsCaptionRewriteException_OnConnectionRefused()
    {
        var handler = new ThrowingHttpHandler(new HttpRequestException("Connection refused"));
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http);

        await Assert.ThrowsAsync<CaptionRewriteException>(() => provider.RewriteAsync(MakeDraft()));
    }

    // ── Provider name ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void ProviderName_IncludesModel()
    {
        var handler = new FakeHttpHandler(HttpStatusCode.OK, "{}");
        using var http = new HttpClient(handler);
        var provider = CreateProvider(http, model: "llama3.2");

        Assert.Equal("Ollama/llama3.2", provider.ProviderName);
    }

    // ── Fake HTTP handlers ────────────────────────────────────────────────────────────────────

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

    private sealed class SlowHttpHandler(int delayMs) : HttpMessageHandler
    {
        protected override async Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken)
        {
            await Task.Delay(delayMs, cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("{\"response\":\"ok\"}", Encoding.UTF8, "application/json")
            };
        }
    }

    private sealed class ThrowingHttpHandler(Exception exception) : HttpMessageHandler
    {
        protected override Task<HttpResponseMessage> SendAsync(
            HttpRequestMessage request,
            CancellationToken cancellationToken) =>
            Task.FromException<HttpResponseMessage>(exception);
    }
}
