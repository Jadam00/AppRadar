using AppRadar.Llm;
using AppRadar.Models;
using Xunit;

namespace AppRadar.Tests;

public sealed class DeterministicCaptionJoinerTests
{
    // ── Basic join behaviour ──────────────────────────────────────────────────────────────────

    [Fact]
    public void Join_CombinesFourStagesToOneSentence()
    {
        var result = DeterministicCaptionJoiner.Join(
            "Hook text",
            "Pain text",
            "Credibility text",
            "CTA text");

        Assert.Equal("Hook text. Pain text. Credibility text. CTA text.", result);
    }

    [Fact]
    public void Join_RemovesTrailingPunctuationBeforeJoining()
    {
        var result = DeterministicCaptionJoiner.Join(
            "Catchy hook!",
            "Painful reality.",
            "Strong proof?",
            "Download now.");

        Assert.Equal("Catchy hook. Painful reality. Strong proof. Download now.", result);
    }

    [Fact]
    public void Join_SkipsEmptyParts()
    {
        var result = DeterministicCaptionJoiner.Join(
            "Hook only",
            "",
            "",
            "CTA only");

        Assert.Equal("Hook only. CTA only.", result);
    }

    [Fact]
    public void Join_ReturnsEmptyString_WhenAllPartsEmpty()
    {
        var result = DeterministicCaptionJoiner.Join("", "", "", "");
        Assert.Equal(string.Empty, result);
    }

    [Fact]
    public void Join_WorksWithWhitespaceOnlyParts()
    {
        var result = DeterministicCaptionJoiner.Join(
            "  Hook  ",
            "  ",
            "  Cred  ",
            "  ");

        Assert.Equal("Hook. Cred.", result);
    }

    // ── Interface contract ────────────────────────────────────────────────────────────────────

    [Fact]
    public async Task RewriteAsync_ReturnsDeterministicJoin()
    {
        var joiner = new DeterministicCaptionJoiner();
        var draft = new ReelStoryDraft
        {
            AppName = "TestApp",
            HookText = "Hook",
            PainPointText = "Pain",
            CredibilityText = "Cred",
            CtaText = "CTA"
        };

        var result = await joiner.RewriteAsync(draft);
        Assert.Equal("Hook. Pain. Cred. CTA.", result);
    }

    [Fact]
    public void ProviderName_IsDeterministicFallback()
    {
        var joiner = new DeterministicCaptionJoiner();
        Assert.Equal("DeterministicFallback", joiner.ProviderName);
    }
}
