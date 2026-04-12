using AppRadar.Models;
using AppRadar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppRadar.Tests;

public sealed class ReelPlannerTests
{
    private static ReelPlanner CreatePlanner() =>
        new(NullLogger<ReelPlanner>.Instance);

    // ── Source helpers ────────────────────────────────────────────────────────────────────────

    private static List<AppSource> CreateFeaturedSources(int count) =>
        Enumerable.Range(1, count).Select(i => new AppSource
        {
            Entry = new AppEntry
            {
                AppName = $"FeaturedApp{i}",
                ImageName = $"featured{i}.png",
                Captions = [$"Generic caption A {i}", $"Generic caption B {i}"],
                Tags = [$"tag{i}", $"category{(i % 3) + 1}"]
            },
            SourceType = AppSourceType.Featured,
            ImagePath = $"/fake/featured{i}.png"
        }).ToList();

    private static List<AppSource> CreateMyAppSources(int count) =>
        Enumerable.Range(1, count).Select(i => new AppSource
        {
            Entry = new AppEntry
            {
                AppName = $"MyApp{i}",
                ImageName = $"myapp{i}.png",
                Captions = [$"Download now {i}", $"Try it today {i}"],
                Tags = ["puzzle", "game"]
            },
            SourceType = AppSourceType.MyApp,
            ImagePath = $"/fake/myapp{i}.png"
        }).ToList();

    /// <summary>Creates a single MyApp source with all four role-specific caption pools.</summary>
    private static AppSource CreateRichMyAppSource(
        string appName,
        string[]? hookCaptions = null,
        string[]? painCaptions = null,
        string[]? credCaptions = null,
        string[]? ctaCaptions = null)
    {
        return new AppSource
        {
            Entry = new AppEntry
            {
                AppName = appName,
                ImageName = $"{appName.Replace(" ", "_")}.png",
                Captions = ["Generic fallback caption"],
                Tags = ["app", "game"],
                HookCaptions = hookCaptions?.ToList(),
                PainPointCaptions = painCaptions?.ToList(),
                CredibilityCaptions = credCaptions?.ToList(),
                CtaCaptions = ctaCaptions?.ToList()
            },
            SourceType = AppSourceType.MyApp,
            ImagePath = $"/fake/{appName}.png"
        };
    }

    private static AppSource CreateRichFeaturedSource(
        string appName,
        string[] tags,
        string[]? hookCaptions = null,
        string[]? painCaptions = null,
        string[]? credCaptions = null)
    {
        return new AppSource
        {
            Entry = new AppEntry
            {
                AppName = appName,
                ImageName = $"{appName.Replace(" ", "_")}.png",
                Captions = ["Generic fallback caption"],
                Tags = [..tags],
                HookCaptions = hookCaptions?.ToList(),
                PainPointCaptions = painCaptions?.ToList(),
                CredibilityCaptions = credCaptions?.ToList()
            },
            SourceType = AppSourceType.Featured,
            ImagePath = $"/fake/{appName}.png"
        };
    }

    // ── Structure tests ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_ReturnsEightSlides()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(
            CreateFeaturedSources(5), CreateMyAppSources(2), new Random(1));

        Assert.Equal(8, plan.Slides.Count);
    }

    [Fact]
    public void Plan_SlidesAreInCorrectRoleOrder()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(
            CreateFeaturedSources(5), CreateMyAppSources(2), new Random(1));

        Assert.Equal(SlideRole.Hook, plan.Slides[0].Role);
        Assert.Equal(SlideRole.Hook, plan.Slides[1].Role);
        Assert.Equal(SlideRole.PainPoint, plan.Slides[2].Role);
        Assert.Equal(SlideRole.PainPoint, plan.Slides[3].Role);
        Assert.Equal(SlideRole.Credibility, plan.Slides[4].Role);
        Assert.Equal(SlideRole.Credibility, plan.Slides[5].Role);
        Assert.Equal(SlideRole.Cta, plan.Slides[6].Role);
        Assert.Equal(SlideRole.Cta, plan.Slides[7].Role);

        Assert.Equal(SlideRole.Hook, plan.Slides[0].NarrationRole);
        Assert.Equal(SlideRole.Hook, plan.Slides[1].NarrationRole);
        Assert.Equal(SlideRole.PainPoint, plan.Slides[2].NarrationRole);
        Assert.Equal(SlideRole.PainPoint, plan.Slides[3].NarrationRole);
        Assert.Equal(SlideRole.Credibility, plan.Slides[4].NarrationRole);
        Assert.Equal(SlideRole.Credibility, plan.Slides[5].NarrationRole);
        Assert.Equal(SlideRole.Cta, plan.Slides[6].NarrationRole);
        Assert.Equal(SlideRole.Cta, plan.Slides[7].NarrationRole);
    }

    [Fact]
    public void Plan_AllSlidesComeFromSameSingleApp()
    {
        // In single-app mode, every slide uses the same AppSource (same app name and image).
        var planner = CreatePlanner();
        var featured = CreateFeaturedSources(5);
        var myApps = CreateMyAppSources(3);

        for (int seed = 0; seed < 20; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            var appNames = plan.Slides.Select(s => s.Source.Entry.AppName).Distinct().ToList();
            Assert.Single(appNames);
        }
    }

    [Fact]
    public void Plan_PreferredSourceIsMyApp_WhenMyAppsAvailable()
    {
        // The planner prefers myAppSources (the promoted app) when they exist.
        var planner = CreatePlanner();
        var featured = CreateFeaturedSources(5);
        var myApps = CreateMyAppSources(3);

        for (int seed = 0; seed < 20; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            // All slides should come from the MyApp pool
            foreach (var slide in plan.Slides)
                Assert.Equal(AppSourceType.MyApp, slide.Source.SourceType);
        }
    }

    [Fact]
    public void Plan_FallsBackToFeaturedSources_WhenMyAppsEmpty()
    {
        var planner = CreatePlanner();
        var featured = CreateFeaturedSources(5);
        var myApps = new List<AppSource>(); // empty

        var plan = planner.Plan(featured, myApps, new Random(1));

        // All slides should come from featured pool
        foreach (var slide in plan.Slides)
            Assert.Equal(AppSourceType.Featured, slide.Source.SourceType);
    }

    [Fact]
    public void Plan_StoryDraftIsPopulated()
    {
        // The ReelPlan must carry a non-null StoryDraft with all 4 stage texts.
        var planner = CreatePlanner();
        var plan = planner.Plan(CreateFeaturedSources(3), CreateMyAppSources(2), new Random(42));

        Assert.NotNull(plan.StoryDraft);
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft!.AppName));
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft.HookText));
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft.PainPointText));
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft.CredibilityText));
        Assert.False(string.IsNullOrWhiteSpace(plan.StoryDraft.CtaText));
    }

    [Fact]
    public void Plan_AllSlidesHaveNonEmptyCaptions()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(
            CreateFeaturedSources(5), CreateMyAppSources(2), new Random(7));

        foreach (var slide in plan.Slides)
        {
            Assert.False(string.IsNullOrWhiteSpace(slide.DisplayCaption),
                $"Slide with role {slide.Role} has an empty caption");
            Assert.False(string.IsNullOrWhiteSpace(slide.NarrationText),
                $"Slide with role {slide.Role} has empty narration text");
        }
    }

    [Fact]
    public void Plan_EachStageCaptionIsDuplicatedAcrossItsTwoSlides()
    {
        var planner = CreatePlanner();
        var featured = CreateFeaturedSources(5);
        var myApps = CreateMyAppSources(2);

        for (int seed = 0; seed < 30; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            Assert.Equal(plan.Slides[0].DisplayCaption, plan.Slides[1].DisplayCaption);
            Assert.Equal(plan.Slides[2].DisplayCaption, plan.Slides[3].DisplayCaption);
            Assert.Equal(plan.Slides[4].DisplayCaption, plan.Slides[5].DisplayCaption);
            Assert.Equal(plan.Slides[6].DisplayCaption, plan.Slides[7].DisplayCaption);
        }
    }

    // ── Determinism ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_WithSameSeed_IsDeterministic()
    {
        var planner = CreatePlanner();
        var featured = CreateFeaturedSources(5);
        var myApps = CreateMyAppSources(2);

        var plan1 = planner.Plan(featured, myApps, new Random(100));
        var plan2 = planner.Plan(featured, myApps, new Random(100));

        Assert.Equal(plan1.Transition, plan2.Transition);
        Assert.Equal(plan1.Slides.Count, plan2.Slides.Count);

        for (int i = 0; i < plan1.Slides.Count; i++)
        {
            Assert.Equal(plan1.Slides[i].Role, plan2.Slides[i].Role);
            Assert.Equal(plan1.Slides[i].DisplayCaption, plan2.Slides[i].DisplayCaption);
            Assert.Equal(plan1.Slides[i].Source.Entry.AppName, plan2.Slides[i].Source.Entry.AppName);
        }
    }

    [Fact]
    public void Plan_WithDifferentSeeds_ProducesDifferentResults()
    {
        var planner = CreatePlanner();
        var featured = CreateFeaturedSources(10);
        var myApps = CreateMyAppSources(3);

        var plan1 = planner.Plan(featured, myApps, new Random(1));
        var plan2 = planner.Plan(featured, myApps, new Random(9999));

        bool allSame = plan1.Slides.Zip(plan2.Slides).All(pair =>
            pair.First.Source.Entry.AppName == pair.Second.Source.Entry.AppName &&
            pair.First.DisplayCaption == pair.Second.DisplayCaption);

        Assert.False(allSame);
    }

    // ── Caption source preference ─────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_PrefersRoleSpecificCaptionsOverGenericCaptions()
    {
        // In single-app mode, one app is selected and all four role-specific caption pools
        // come from that same app.
        var planner = CreatePlanner();

        var richApp = CreateRichMyAppSource(
            "MyPowApp",
            hookCaptions:  ["HOOK: Stop everything right now!"],
            painCaptions:  ["PAIN: Every app wastes your time"],
            credCaptions:  ["CRED: These tools actually work"],
            ctaCaptions:   ["CTA: Download it and win"]);

        var myApps = new List<AppSource> { richApp };
        var featured = CreateFeaturedSources(3); // unused in single-app, but required for API

        var plan = planner.Plan(featured, myApps, new Random(1));

        // Each role should select from its dedicated pool
        Assert.Equal("HOOK: Stop everything right now!", plan.Slides[0].DisplayCaption);
        Assert.Equal("HOOK: Stop everything right now!", plan.Slides[1].DisplayCaption);
        Assert.Equal("PAIN: Every app wastes your time",  plan.Slides[2].DisplayCaption);
        Assert.Equal("PAIN: Every app wastes your time",  plan.Slides[3].DisplayCaption);
        Assert.Equal("CRED: These tools actually work",   plan.Slides[4].DisplayCaption);
        Assert.Equal("CRED: These tools actually work",   plan.Slides[5].DisplayCaption);
        Assert.Equal("CTA: Download it and win",          plan.Slides[6].DisplayCaption);
        Assert.Equal("CTA: Download it and win",          plan.Slides[7].DisplayCaption);
    }

    [Fact]
    public void Plan_RoleSpecificCaptions_VaryAcrossSeeds_WhenPoolHasMultipleOptions()
    {
        var planner = CreatePlanner();

        var richApp = CreateRichMyAppSource(
            "MyPowApp",
            hookCaptions: ["HOOK option A", "HOOK option B", "HOOK option C"],
            painCaptions: ["PAIN option A", "PAIN option B", "PAIN option C"],
            credCaptions: ["CRED option A", "CRED option B", "CRED option C"],
            ctaCaptions: ["CTA option A", "CTA option B", "CTA option C"]);

        var featured = CreateFeaturedSources(3);
        var myApps = new List<AppSource> { richApp };

        var seenHook = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var seenPain = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int seed = 0; seed < 40; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));

            Assert.Contains(plan.Slides[0].DisplayCaption, richApp.Entry.HookCaptions!);
            Assert.Contains(plan.Slides[1].DisplayCaption, richApp.Entry.HookCaptions!);
            Assert.Contains(plan.Slides[2].DisplayCaption, richApp.Entry.PainPointCaptions!);
            Assert.Contains(plan.Slides[3].DisplayCaption, richApp.Entry.PainPointCaptions!);
            Assert.Contains(plan.Slides[4].DisplayCaption, richApp.Entry.CredibilityCaptions!);
            Assert.Contains(plan.Slides[5].DisplayCaption, richApp.Entry.CredibilityCaptions!);
            Assert.Contains(plan.Slides[6].DisplayCaption, richApp.Entry.CtaCaptions!);
            Assert.Contains(plan.Slides[7].DisplayCaption, richApp.Entry.CtaCaptions!);

            seenHook.Add(plan.Slides[0].DisplayCaption);
            seenPain.Add(plan.Slides[2].DisplayCaption);
        }

        Assert.True(seenHook.Count > 1,
            $"Expected hook caption variation across seeds, saw {seenHook.Count} unique value(s).");
        Assert.True(seenPain.Count > 1,
            $"Expected pain caption variation across seeds, saw {seenPain.Count} unique value(s).");
    }

    [Fact]
    public void Plan_FallsBackToGenericCaptionsWhenRoleSpecificAbsent()
    {
        var planner = CreatePlanner();
        // Sources with NO role-specific captions
        var featured = CreateFeaturedSources(3);
        var myApps = CreateMyAppSources(1);

        var plan = planner.Plan(featured, myApps, new Random(42));

        // All captions should still be non-empty (from generic captions or templates)
        foreach (var slide in plan.Slides)
        {
            Assert.False(string.IsNullOrWhiteSpace(slide.DisplayCaption));
        }
    }

    [Fact]
    public void Plan_FallsBackToTemplateCaptionsWhenAllCaptionListsEmpty()
    {
        var planner = CreatePlanner();

        // Sources with NO captions at all — should trigger template fallback
        var featured = Enumerable.Range(1, 3).Select(i => new AppSource
        {
            Entry = new AppEntry
            {
                AppName = $"EmptyApp{i}",
                ImageName = $"empty{i}.png",
                Captions = ["fallback"],  // MetadataValidator requires at least one caption
                Tags = [$"tag{i}"]
            },
            SourceType = AppSourceType.Featured,
            ImagePath = $"/fake/empty{i}.png"
        }).ToList();

        var myApps = new List<AppSource>
        {
            new()
            {
                Entry = new AppEntry
                {
                    AppName = "MyEmptyApp",
                    ImageName = "myempty.png",
                    Captions = ["fallback cta"],
                    Tags = ["app"]
                },
                SourceType = AppSourceType.MyApp,
                ImagePath = "/fake/myempty.png"
            }
        };

        var plan = planner.Plan(featured, myApps, new Random(1));

        foreach (var slide in plan.Slides)
        {
            Assert.False(string.IsNullOrWhiteSpace(slide.DisplayCaption),
                $"Slide {slide.Role} should have a non-empty caption even with no structured data");
        }
    }

    // ── Tag diversity ─────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_SelectsDifferentAppsAcrossSeeds_WhenMultipleMyAppsExist()
    {
        // With multiple myApp entries, different seeds should occasionally pick different apps.
        var planner = CreatePlanner();
        var myApps = CreateMyAppSources(5);
        var featured = CreateFeaturedSources(3);

        var selectedNames = new HashSet<string>();
        for (int seed = 0; seed < 50; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            selectedNames.Add(plan.Slides[0].Source.Entry.AppName);
        }

        // With 5 distinct myApp entries and 50 seeds, we should see more than 1 different app.
        Assert.True(selectedNames.Count > 1,
            "Multiple myApp entries should result in different apps being selected across seeds.");
    }

    // ── Caption scoring ───────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Stop scrolling!", SlideRole.Hook, 100)]      // short + exclamation
    [InlineData("Lorem ipsum placeholder test", SlideRole.Hook, 0)]   // filler — penalised
    [InlineData("Try it today!", SlideRole.Cta, 100)]          // short + action word
    public void ScoreCaption_ReflectsHeuristicPriority(string caption, SlideRole role, int minScore)
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int score = ReelPlanner.ScoreCaption(caption, role, used);
        Assert.True(score >= minScore,
            $"Caption '{caption}' scored {score}, expected >= {minScore}");
    }

    [Fact]
    public void ScoreCaption_LongerCaptionsScoreLowerThanShort()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int shortScore = ReelPlanner.ScoreCaption("Short!", SlideRole.Hook, used);
        int longScore = ReelPlanner.ScoreCaption(
            "This is a very long caption that goes on and on and takes up a lot of space on the screen and nobody reads it", SlideRole.Hook, used);
        Assert.True(shortScore > longScore);
    }

    [Fact]
    public void ScoreCaption_PenalisesDuplicates()
    {
        var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { "Already used!" };
        int score = ReelPlanner.ScoreCaption("Already used!", SlideRole.Hook, used);
        Assert.True(score < 100, "Duplicate captions should score below 100");
    }

    // ── Edge cases ────────────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_WorksWithOnlyOneMyApp()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(CreateFeaturedSources(3), CreateMyAppSources(1), new Random(5));
        Assert.Equal(8, plan.Slides.Count);
        // All slides use the single myApp
        Assert.All(plan.Slides, s => Assert.Equal(AppSourceType.MyApp, s.Source.SourceType));
    }

    [Fact]
    public void Plan_WorksWithOnlyFeaturedSources_WhenMyAppsEmpty()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(CreateFeaturedSources(5), [], new Random(5));
        Assert.Equal(8, plan.Slides.Count);
        // All slides use a featured app as fallback
        Assert.All(plan.Slides, s => Assert.Equal(AppSourceType.Featured, s.Source.SourceType));
    }

    [Fact]
    public void Plan_UsesDifferentPainPointImages_WhenAvailable()
    {
        var planner = CreatePlanner();

        var app = new AppSource
        {
            Entry = new AppEntry
            {
                AppName = "DoublePainApp",
                Tags = ["utility"],
                Hook = new StageContent
                {
                    ImageNames = ["hook.png"],
                    Captions = ["HOOK"]
                },
                PainPoint = new StageContent
                {
                    ImageNames = ["painA.png", "painB.png"],
                    Captions = ["PAIN"]
                },
                Credibility = new StageContent
                {
                    ImageNames = ["cred.png"],
                    Captions = ["CRED"]
                },
                Cta = new StageContent
                {
                    ImageNames = ["cta.png"],
                    Captions = ["CTA"]
                }
            },
            SourceType = AppSourceType.MyApp,
            ImagePath = "/fake/default.png"
        };

        var plan = planner.Plan([], [app], new Random(1));

        Assert.Equal("painA.png", plan.Slides[2].ImageName);
        Assert.Equal("painB.png", plan.Slides[3].ImageName);
    }

    [Fact]
    public void Plan_ReusesPainPointImage_WhenOnlyOneExists()
    {
        var planner = CreatePlanner();

        var app = new AppSource
        {
            Entry = new AppEntry
            {
                AppName = "SinglePainImageApp",
                Tags = ["utility"],
                Hook = new StageContent { ImageNames = ["hook.png"], Captions = ["HOOK"] },
                PainPoint = new StageContent { ImageNames = ["painOnly.png"], Captions = ["PAIN"] },
                Credibility = new StageContent { ImageNames = ["cred.png"], Captions = ["CRED"] },
                Cta = new StageContent { ImageNames = ["cta.png"], Captions = ["CTA"] }
            },
            SourceType = AppSourceType.MyApp,
            ImagePath = "/fake/default.png"
        };

        var plan = planner.Plan([], [app], new Random(2));

        Assert.Equal("painOnly.png", plan.Slides[2].ImageName);
        Assert.Equal("painOnly.png", plan.Slides[3].ImageName);
    }

    [Fact]
    public void Plan_StrategyModeIsStructuredMarketing()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(CreateFeaturedSources(5), CreateMyAppSources(2), new Random(1));
        Assert.Equal("StructuredMarketing", plan.StrategyMode);
    }

    [Fact]
    public void Plan_SlideDurationMatchesSecondsPerSlide()
    {
        var planner = CreatePlanner();
        const int sps = 5;
        var plan = planner.Plan(CreateFeaturedSources(5), CreateMyAppSources(2), new Random(1), sps);
        foreach (var slide in plan.Slides)
            Assert.Equal(sps, slide.DurationSeconds);
    }
}
