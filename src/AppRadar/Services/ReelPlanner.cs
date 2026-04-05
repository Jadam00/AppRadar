using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Services;

/// <summary>
/// Builds a deterministic, structured 4-stage marketing <see cref="ReelPlan"/> from the
/// available app sources.
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
    /// Builds a complete <see cref="ReelPlan"/> for one reel.
    /// </summary>
    /// <param name="featuredSources">All valid featured app sources (≥ 3 required).</param>
    /// <param name="myAppSources">All valid myApp sources (≥ 1 required).</param>
    /// <param name="rng">Seeded RNG for deterministic output.</param>
    /// <param name="secondsPerSlide">Target display duration per slide.</param>
    public ReelPlan Plan(
        List<AppSource> featuredSources,
        List<AppSource> myAppSources,
        Random rng,
        int secondsPerSlide = 4)
    {
        _logger.LogInformation("Building structured reel plan ({FeaturedCount} featured, {MyAppCount} myApps)",
            featuredSources.Count, myAppSources.Count);

        // --- Pick apps for each role --------------------------------------------------------

        // Prefer apps with distinct tags across the reel for variety.
        var featuredPool = featuredSources.ToList();
        var hookApp = PickWithTagDiversity(featuredPool, [], rng);
        featuredPool.Remove(hookApp);

        var painApp = PickWithTagDiversity(featuredPool, [hookApp], rng);
        featuredPool.Remove(painApp);

        var credApp = PickWithTagDiversity(featuredPool, [hookApp, painApp], rng);

        var ctaApp = PickUnique(myAppSources, rng);

        _logger.LogInformation(
            "Role assignments — Hook: {Hook}, PainPoint: {Pain}, Credibility: {Cred}, CTA: {Cta}",
            hookApp.Entry.AppName, painApp.Entry.AppName,
            credApp.Entry.AppName, ctaApp.Entry.AppName);

        // --- Select captions for each role --------------------------------------------------

        var usedTexts = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        var hookCaption = SelectCaption(hookApp, SlideRole.Hook, rng, usedTexts);
        var painCaption = SelectCaption(painApp, SlideRole.PainPoint, rng, usedTexts);
        var credCaption = SelectCaption(credApp, SlideRole.Credibility, rng, usedTexts);
        var ctaCaption = SelectCaption(ctaApp, SlideRole.Cta, rng, usedTexts);

        _logger.LogInformation("Hook caption     : \"{Caption}\"", hookCaption);
        _logger.LogInformation("PainPoint caption: \"{Caption}\"", painCaption);
        _logger.LogInformation("Credibility caption: \"{Caption}\"", credCaption);
        _logger.LogInformation("CTA caption      : \"{Caption}\"", ctaCaption);

        // --- Choose transition --------------------------------------------------------------

        var transition = (TransitionStyle)rng.Next(0, 2);
        _logger.LogInformation("Transition: {Transition}", transition);

        // --- Assemble plan ------------------------------------------------------------------

        return new ReelPlan
        {
            Transition = transition,
            StrategyMode = "StructuredMarketing",
            Slides =
            [
                MakeSlidePlan(SlideRole.Hook, hookApp, hookCaption, secondsPerSlide),
                MakeSlidePlan(SlideRole.PainPoint, painApp, painCaption, secondsPerSlide),
                MakeSlidePlan(SlideRole.Credibility, credApp, credCaption, secondsPerSlide),
                MakeSlidePlan(SlideRole.Cta, ctaApp, ctaCaption, secondsPerSlide),
            ]
        };
    }

    // ── Caption selection ────────────────────────────────────────────────────────────────────

    /// <summary>
    /// Selects the best caption for a given role from the app's caption pools.
    /// Priority: role-specific pool → generic captions → built-in template.
    /// Among valid candidates, the highest-scoring caption is chosen.
    /// </summary>
    internal string SelectCaption(
        AppSource source,
        SlideRole role,
        Random rng,
        ISet<string> usedTexts)
    {
        var candidates = BuildCandidatePool(source, role, rng);

        // Score and rank; filter already-used text
        var ranked = candidates
            .Where(c => !string.IsNullOrWhiteSpace(c))
            .Select(c => (text: c, score: ScoreCaption(c, role, usedTexts)))
            .OrderByDescending(x => x.score)
            .ToList();

        // Best unique caption, or best if all duplicates
        var chosen = ranked.FirstOrDefault(x => !usedTexts.Contains(x.text)).text
            ?? ranked.FirstOrDefault().text
            ?? $"Check out {source.Entry.AppName}";

        usedTexts.Add(chosen);
        return chosen;
    }

    private List<string> BuildCandidatePool(AppSource source, SlideRole role, Random rng)
    {
        var entry = source.Entry;
        var pool = new List<string>();

        // 1. Role-specific structured captions (highest priority)
        var roleSpecific = GetRoleSpecificCaptions(entry, role);
        if (roleSpecific is { Count: > 0 })
            pool.AddRange(roleSpecific);

        // 2. Generic captions as fallback
        if (entry.Captions is { Count: > 0 })
            pool.AddRange(entry.Captions);

        // 3. Built-in templates as last resort
        pool.Add(BuildTemplate(entry, role, rng));

        return pool;
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

    /// <summary>
    /// Picks an app from <paramref name="pool"/> that shares the fewest tags with
    /// the already-selected apps, promoting reel-level tag diversity.
    /// Falls back to random pick when all apps have equal tag overlap.
    /// </summary>
    private static AppSource PickWithTagDiversity(
        List<AppSource> pool,
        IReadOnlyList<AppSource> alreadySelected,
        Random rng)
    {
        if (pool.Count == 0)
            throw new InvalidOperationException("App source pool is empty — cannot pick an app.");

        var selectedTags = alreadySelected
            .SelectMany(s => s.Entry.Tags)
            .ToHashSet(StringComparer.OrdinalIgnoreCase);

        // Score each candidate by number of new (non-overlapping) tags it brings
        var scored = pool
            .Select(src =>
            {
                int newTags = src.Entry.Tags.Count(t =>
                    !selectedTags.Contains(t, StringComparer.OrdinalIgnoreCase));
                return (src, newTags);
            })
            .ToList();

        int maxNew = scored.Max(x => x.newTags);
        var best = scored.Where(x => x.newTags == maxNew).Select(x => x.src).ToList();

        return best[rng.Next(best.Count)];
    }

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
