using AppRadar.Models;
using AppRadar.Services;
using Xunit;

namespace AppRadar.Tests;

public sealed class KeywordCaptionProviderTests
{
    // ── Explicit keyword fields ───────────────────────────────────────────────────────────────

    [Fact]
    public void GetCaptionForStage_ReturnsHookKeywords_WhenPresent()
    {
        var entry = MakeEntry(hookKeywords: "Stop scrolling");
        Assert.Equal("Stop scrolling", KeywordCaptionProvider.GetCaptionForStage(SlideRole.Hook, entry));
    }

    [Fact]
    public void GetCaptionForStage_ReturnsPainKeywords_WhenPresent()
    {
        var entry = MakeEntry(painKeywords: "Manual work");
        Assert.Equal("Manual work", KeywordCaptionProvider.GetCaptionForStage(SlideRole.PainPoint, entry));
    }

    [Fact]
    public void GetCaptionForStage_ReturnsCredibilityKeywords_WhenPresent()
    {
        var entry = MakeEntry(credibilityKeywords: "Smart automation");
        Assert.Equal("Smart automation", KeywordCaptionProvider.GetCaptionForStage(SlideRole.Credibility, entry));
    }

    [Fact]
    public void GetCaptionForStage_ReturnsCtaKeywords_WhenPresent()
    {
        var entry = MakeEntry(ctaKeywords: "Try MyApp");
        Assert.Equal("Try MyApp", KeywordCaptionProvider.GetCaptionForStage(SlideRole.Cta, entry));
    }

    [Fact]
    public void GetCaptionForStage_TrimsWhitespace_FromExplicitKeywords()
    {
        var entry = MakeEntry(hookKeywords: "  Too slow  ");
        Assert.Equal("Too slow", KeywordCaptionProvider.GetCaptionForStage(SlideRole.Hook, entry));
    }

    // ── Tag-derived captions ──────────────────────────────────────────────────────────────────

    [Fact]
    public void GetCaptionForStage_UsesTagDerived_WhenNoExplicitKeyword_Hook()
    {
        var entry = MakeEntry(tags: ["productivity"]);
        var caption = KeywordCaptionProvider.GetCaptionForStage(SlideRole.Hook, entry);
        Assert.Contains("productivity", caption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCaptionForStage_UsesTagDerived_WhenNoExplicitKeyword_Pain()
    {
        var entry = MakeEntry(tags: ["productivity"]);
        var caption = KeywordCaptionProvider.GetCaptionForStage(SlideRole.PainPoint, entry);
        Assert.Contains("productivity", caption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCaptionForStage_UsesTagDerived_WhenNoExplicitKeyword_Credibility()
    {
        var entry = MakeEntry(tags: ["automation"]);
        var caption = KeywordCaptionProvider.GetCaptionForStage(SlideRole.Credibility, entry);
        Assert.Contains("automation", caption, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public void GetCaptionForStage_CtaUsesTryAppName_ForSingleWordAppName()
    {
        var entry = MakeEntry(appName: "MyApp", tags: ["productivity"]);
        var caption = KeywordCaptionProvider.GetCaptionForStage(SlideRole.Cta, entry);
        Assert.Equal("Try MyApp", caption);
    }

    [Fact]
    public void GetCaptionForStage_CtaUsesAppNameDirectly_ForMultiWordAppName()
    {
        var entry = MakeEntry(appName: "My Cool App", tags: ["productivity"]);
        var caption = KeywordCaptionProvider.GetCaptionForStage(SlideRole.Cta, entry);
        Assert.Equal("My Cool App", caption);
    }

    // ── Deterministic fallback ────────────────────────────────────────────────────────────────

    [Fact]
    public void GetCaptionForStage_FallsBackToDeterministic_WhenNoTagsOrKeywords()
    {
        var entry = MakeEntry(); // no tags, no keywords
        var hook = KeywordCaptionProvider.GetCaptionForStage(SlideRole.Hook, entry);
        var pain = KeywordCaptionProvider.GetCaptionForStage(SlideRole.PainPoint, entry);
        var cred = KeywordCaptionProvider.GetCaptionForStage(SlideRole.Credibility, entry);

        Assert.False(string.IsNullOrWhiteSpace(hook));
        Assert.False(string.IsNullOrWhiteSpace(pain));
        Assert.False(string.IsNullOrWhiteSpace(cred));
    }

    [Fact]
    public void GetFallbackCaption_Hook_ReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(KeywordCaptionProvider.GetFallbackCaption(SlideRole.Hook, "MyApp")));
    }

    [Fact]
    public void GetFallbackCaption_Pain_ReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(KeywordCaptionProvider.GetFallbackCaption(SlideRole.PainPoint, "MyApp")));
    }

    [Fact]
    public void GetFallbackCaption_Credibility_ReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(KeywordCaptionProvider.GetFallbackCaption(SlideRole.Credibility, "MyApp")));
    }

    [Fact]
    public void GetFallbackCaption_Cta_ContainsAppName_WhenSingleWord()
    {
        var cta = KeywordCaptionProvider.GetFallbackCaption(SlideRole.Cta, "MyApp");
        Assert.Contains("MyApp", cta, StringComparison.Ordinal);
    }

    [Fact]
    public void GetFallbackCaption_Cta_EmptyAppName_ReturnsNonEmpty()
    {
        Assert.False(string.IsNullOrWhiteSpace(KeywordCaptionProvider.GetFallbackCaption(SlideRole.Cta, "")));
    }

    // ── Priority: explicit > tag-derived > fallback ───────────────────────────────────────────

    [Fact]
    public void GetCaptionForStage_ExplicitKeyword_TakesPriorityOverTags()
    {
        var entry = MakeEntry(hookKeywords: "Too slow", tags: ["productivity"]);
        var caption = KeywordCaptionProvider.GetCaptionForStage(SlideRole.Hook, entry);
        Assert.Equal("Too slow", caption);
    }

    [Fact]
    public void GetCaptionForStage_TagDerived_TakesPriorityOverFallback()
    {
        var entry = MakeEntry(tags: ["productivity"]);
        var caption = KeywordCaptionProvider.GetCaptionForStage(SlideRole.Hook, entry);
        // Tag-derived result must differ from the pure fallback "Too slow" only if tag ≠ "slow"
        // Just assert it's non-empty and contains the tag.
        Assert.False(string.IsNullOrWhiteSpace(caption));
        Assert.Contains("productivity", caption, StringComparison.OrdinalIgnoreCase);
    }

    // ── All outputs are non-empty ─────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(SlideRole.Hook)]
    [InlineData(SlideRole.PainPoint)]
    [InlineData(SlideRole.Credibility)]
    [InlineData(SlideRole.Cta)]
    public void GetCaptionForStage_NeverReturnsEmpty_ForAnyRole(SlideRole role)
    {
        var entry = MakeEntry(); // minimal entry
        Assert.False(string.IsNullOrWhiteSpace(
            KeywordCaptionProvider.GetCaptionForStage(role, entry)));
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static AppEntry MakeEntry(
        string appName = "TestApp",
        string[]? tags = null,
        string? hookKeywords = null,
        string? painKeywords = null,
        string? credibilityKeywords = null,
        string? ctaKeywords = null) =>
        new()
        {
            AppName = appName,
            ImageName = "test.png",
            Tags = tags?.ToList() ?? [],
            HookKeywords = hookKeywords,
            PainKeywords = painKeywords,
            CredibilityKeywords = credibilityKeywords,
            CtaKeywords = ctaKeywords
        };
}
