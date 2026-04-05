using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Services;

/// <summary>
/// Builds a deterministic, structured 4-stage marketing <see cref="ReelPlan"/> from a
/// single selected app.
///
/// Single-app mode (default):
///   Selects exactly ONE app and ONE image. All four narrative stages
///   (Hook, PainPoint, Credibility, CTA) are drawn from that single app's caption
///   pools. The result is a cohesive one-app mini-advert.
///
/// The four stages are:
///   1. Hook        — Stop the scroll; tension / curiosity / bold claim
///   2. PainPoint   — A problem, frustration, or intriguing angle
///   3. Credibility — Evidence, contrast, build-up toward the CTA
///   4. Cta         — My app as the payoff with a clear next step
///
/// Caption selection uses a scoring heuristic that prefers:
///   - Short punchy text (≤ 50 chars ideal)
///   - Text containing question marks or exclamation points
///   - Avoiding exact duplicate wording across slides
///   - Avoiding very long captions (> 90 chars penalised)
///
/// When a role-specific caption list is empty or absent, the planner falls back to
/// the generic <c>captions</c> list, then to a built-in template derived from the
/// app's tags and name.
/// </summary>
public sealed class ReelPlanner
{
    private readonly ILogger<ReelPlanner> _logger;

    // Built-in template pools keyed by role.  {tag} and {app} are substituted at runtime.
    private static readonly string[] HookTemplates =
    [
        "Stop scrolling — these {tag} apps are worth it",
        "Most people sleep on the best {tag} tools",
        "You're missing out if you haven't tried these",
        "These {tag} apps are way more useful than you think",
        "Still using the same old {tag} apps? Think again",
        "Not all {tag} apps are created equal",
        "The {tag} apps nobody talks about",
        "This changes how you think about {tag}",
    ];

    private static readonly string[] PainPointTemplates =
    [
        "Finding great {tag} tools takes forever",
        "Most {tag} apps are way too complicated",
        "Nobody has time to dig through endless {tag} options",
        "The obvious {tag} picks are rarely the best ones",
        "You've probably settled for less in {tag} without knowing it",
        "Generic {tag} apps waste your time",
        "Too many {tag} apps promise a lot and deliver little",
        "Most people only know the big names in {tag} — and miss the gems",
    ];

    private static readonly string[] CredibilityTemplates =
    [
        "3 {tag} tools actually worth checking out",
        "Better than the usual {tag} defaults",
        "Simple, useful, and actually worth trying",
        "Underrated {tag} apps that deserve more attention",
        "These {tag} picks are genuinely good",
        "Tried and tested — the best of {tag}",
        "The {tag} apps that hold up",
        "If you care about {tag}, these are for you",
    ];

    private static readonly string[] CtaTemplates =
    [
        "Try {app} next",
        "Want the real deal? Start with {app}",
        "Download {app} and see for yourself",
        "Think you're good? Prove it with {app}",
        "{app} — give it a go",
        "The last {tag} app you'll need: {app}",
        "Ready? {app} is waiting",
        "Don't sleep on {app}",
    ];

    public ReelPlanner(ILogger<ReelPlanner> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Builds a complete single-app <see cref="ReelPlan"/> for one reel.
    ///
    /// Selects exactly ONE app and ONE image.  All four stages draw captions from
    /// that same app.  Prefers <paramref name="myAppSources"/> (the app being promoted)
    /// and falls back to <paramref name="featuredSources"/> when myApps is empty.
    /// </summary>
    /// <param name="featuredSources">Available featured app sources.</param>
    /// <param name="myAppSources">Available myApp sources (preferred for the reel app).</param>
    /// <param name="rng">Seeded RNG for deterministic output.</param>
    /// <param name="secondsPerSlide">Target display duration per caption chunk (default 4 s).</param>
    public ReelPlan Plan(
        List<AppSource> featuredSources,
        List<AppSource> myAppSources,
        Random rng,
        int secondsPerSlide = 4)
    {
        // Prefer myAppSources as the "your app" being advertised; fall back to featured
        var candidatePool = myAppSources.Count > 0 ? myAppSources : featuredSources;

        if (candidatePool.Count == 0)
            throw new InvalidOperationException(
                "No app sources available — cannot build a reel plan.");

        // Select exactly ONE app entry (and therefore ONE image)
        var selectedSource = PickUnique(candidatePool, rng);

        _logger.LogInformation(
            "Single-app reel: selected '{App}' (image: {Image})",
            selectedSource.Entry.AppName, selectedSource.Entry.ImageName);

        // Build 4 stage captions from the SAME app
        var usedTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hookCaption   = SelectCaption(selectedSource, SlideRole.Hook,         rng, usedTexts);
        var painCaption   = SelectCaption(selectedSource, SlideRole.PainPoint,    rng, usedTexts);
        var credCaption   = SelectCaption(selectedSource, SlideRole.Credibility,  rng, usedTexts);
        var ctaCaption    = SelectCaption(selectedSource, SlideRole.Cta,          rng, usedTexts);

        _logger.LogInformation("Hook caption       : \"{Caption}\"", hookCaption);
        _logger.LogInformation("PainPoint caption  : \"{Caption}\"", painCaption);
        _logger.LogInformation("Credibility caption: \"{Caption}\"", credCaption);
        _logger.LogInformation("CTA caption        : \"{Caption}\"", ctaCaption);

        // Build story draft (pre-LLM structured model)
        var storyDraft = new ReelStoryDraft
        {
            AppName        = selectedSource.Entry.AppName,
            ImageName      = selectedSource.Entry.ImageName,
            Tags           = string.Join(", ", selectedSource.Entry.Tags),
            HookText       = hookCaption,
            PainPointText  = painCaption,
            CredibilityText = credCaption,
            CtaText        = ctaCaption
        };

        // Structured marketing reels always use horizontal scroll (slideleft xfade).
        var transition = TransitionStyle.Slide;
        _logger.LogInformation("Transition: {Transition}", transition);

        // Assemble plan — all slides use the SAME source image
        return new ReelPlan
        {
            Transition   = transition,
            StrategyMode = "StructuredMarketing",
            StoryDraft   = storyDraft,
            Slides =
            [
                MakeSlidePlan(SlideRole.Hook,        selectedSource, hookCaption,  secondsPerSlide),
                MakeSlidePlan(SlideRole.PainPoint,   selectedSource, painCaption,  secondsPerSlide),
                MakeSlidePlan(SlideRole.Credibility, selectedSource, credCaption,  secondsPerSlide),
                MakeSlidePlan(SlideRole.Cta,         selectedSource, ctaCaption,   secondsPerSlide),
            ]
        };
    }

    // ── Caption selection ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the best caption for a given role from the app's caption pools.
    /// Priority: role-specific pool → generic captions → built-in template.
    /// Within each pool, the highest-scoring caption is chosen.
    /// Role-specific captions are always preferred over generic ones when a non-duplicate
    /// candidate exists in the role-specific pool.
    /// </summary>
    internal string SelectCaption(
        AppSource source,
        SlideRole role,
        Random rng,
        ISet<string> usedTexts)
    {
        // 1. Try role-specific pool first (hard priority — best caption from this pool wins
        //    over any generic caption, regardless of score).
        var roleSpecific = GetRoleSpecificCaptions(source.Entry, role);
        if (roleSpecific is { Count: > 0 })
        {
            var rsRanked = roleSpecific
                .Where(c => !string.IsNullOrWhiteSpace(c))
                .Select(c => (text: c, score: ScoreCaption(c, role, usedTexts)))
                .OrderByDescending(x => x.score)
                .ToList();

            var bestRs = rsRanked.FirstOrDefault(x => !usedTexts.Contains(x.text)).text;
            if (bestRs is not null)
            {
                usedTexts.Add(bestRs);
                return bestRs;
            }
        }

        // 2. Fall back to generic captions and templates scored together.
        var fallbackPool = new List<string>();
        if (source.Entry.Captions is { Count: > 0 })
            fallbackPool.AddRange(source.Entry.Captions);
        fallbackPool.Add(BuildTemplate(source.Entry, role, rng));

        var ranked = fallbackPool
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => (text: c, score: ScoreCaption(c, role, usedTexts)))
            .OrderByDescending(x => x.score)
            .ToList();

        var chosen = ranked.FirstOrDefault(x => !usedTexts.Contains(x.text)).text
            ?? ranked.FirstOrDefault().text
            ?? $"Check out {source.Entry.AppName}";

        usedTexts.Add(chosen);
        return chosen;
    }

    private static List<string>? GetRoleSpecificCaptions(AppEntry entry, SlideRole role) =>
        role switch
        {
            SlideRole.Hook => entry.HookCaptions,
            SlideRole.PainPoint => entry.PainPointCaptions,
            SlideRole.Credibility => entry.CredibilityCaptions,
            SlideRole.Cta => entry.CtaCaptions,
            _ => null
        };

    private static string BuildTemplate(AppEntry entry, SlideRole role, Random rng)
    {
        var tag = entry.Tags.Count > 0
            ? entry.Tags[rng.Next(entry.Tags.Count)]
            : "app";
        var app = entry.AppName;

        var templates = role switch
        {
            SlideRole.Hook => HookTemplates,
            SlideRole.PainPoint => PainPointTemplates,
            SlideRole.Credibility => CredibilityTemplates,
            SlideRole.Cta => CtaTemplates,
            _ => CtaTemplates
        };

        var tmpl = templates[rng.Next(templates.Length)];
        return tmpl.Replace("{tag}", tag, StringComparison.OrdinalIgnoreCase)
                   .Replace("{app}", app, StringComparison.OrdinalIgnoreCase);
    }

    // ── Scoring heuristics ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Returns a heuristic score for how effective a caption is for its role.
    /// Higher is better.
    /// </summary>
    internal static int ScoreCaption(string text, SlideRole role, ISet<string> usedTexts)
    {
        int score = 100;

        // Penalise exact duplicates (handled by the caller but keep as guard)
        if (usedTexts.Contains(text)) score -= 50;

        // Prefer shorter text — ideal for short-form reel readability
        int len = text.Length;
        if (len <= 30) score += 30;
        else if (len <= 50) score += 20;
        else if (len <= 70) score += 5;
        else if (len > 90) score -= 20;

        // Reward emotional/energy indicators
        if (text.Contains('!')) score += 15;
        if (text.Contains('?')) score += 10;

        // Role-specific boosts
        switch (role)
        {
            case SlideRole.Hook:
                // Hook should feel urgent / challenging
                if (ContainsAny(text, ["stop", "miss", "wast", "ignor", "sleep", "think"]))
                    score += 10;
                break;
            case SlideRole.PainPoint:
                // Pain point should identify a problem
                if (ContainsAny(text, ["too", "never", "forever", "time", "hard", "complic", "endless"]))
                    score += 10;
                break;
            case SlideRole.Credibility:
                // Credibility should feel substantiated
                if (ContainsAny(text, ["worth", "best", "top", "great", "proven", "tried", "actually"]))
                    score += 10;
                break;
            case SlideRole.Cta:
                // CTA should feel actionable
                if (ContainsAny(text, ["try", "download", "start", "get", "go", "now", "today", "prove"]))
                    score += 10;
                break;
        }

        // Penalise low-information filler
        if (ContainsAny(text, ["lorem", "placeholder", "test"]))
            score -= 40;

        return score;
    }

    private static bool ContainsAny(string text, IEnumerable<string> fragments)
    {
        var lower = text.ToLowerInvariant();
        return fragments.Any(f => lower.Contains(f, StringComparison.OrdinalIgnoreCase));
    }

    // ── App selection helpers ─────────────────────────────────────────────────────────────────

    private static AppSource PickUnique(List<AppSource> pool, Random rng)
    {
        if (pool.Count == 0)
            throw new InvalidOperationException("App source pool is empty — cannot pick an app.");
        return pool[rng.Next(pool.Count)];
    }

    // ── Plan assembly ─────────────────────────────────────────────────────────────────────────

    private static ReelSlidePlan MakeSlidePlan(
        SlideRole role,
        AppSource source,
        string caption,
        int durationSeconds)
    {
        return new ReelSlidePlan
        {
            Role = role,
            Source = source,
            DisplayCaption = caption,
            NarrationText = caption,   // default: narration mirrors the on-screen copy
            DurationSeconds = durationSeconds
        };
    }
}
