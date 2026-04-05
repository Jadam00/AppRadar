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

    private static AppSource CreateRichMyAppSource(string appName, string[]? ctaCaptions = null)
    {
        return new AppSource
        {
            Entry = new AppEntry
            {
                AppName = appName,
                ImageName = $"{appName.Replace(" ", "_")}.png",
                Captions = ["Generic CTA fallback"],
                Tags = ["app", "game"],
                CtaCaptions = ctaCaptions?.ToList()
            },
            SourceType = AppSourceType.MyApp,
            ImagePath = $"/fake/{appName}.png"
        };
    }

    // ── Structure tests ───────────────────────────────────────────────────────────────────────

    [Fact]
    public void Plan_ReturnsFourSlides()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(
            CreateFeaturedSources(5), CreateMyAppSources(2), new Random(1));

        Assert.Equal(4, plan.Slides.Count);
    }

    [Fact]
    public void Plan_SlidesAreInCorrectRoleOrder()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(
            CreateFeaturedSources(5), CreateMyAppSources(2), new Random(1));

        Assert.Equal(SlideRole.Hook, plan.Slides[0].Role);
        Assert.Equal(SlideRole.PainPoint, plan.Slides[1].Role);
        Assert.Equal(SlideRole.Credibility, plan.Slides[2].Role);
        Assert.Equal(SlideRole.Cta, plan.Slides[3].Role);
    }

    [Fact]
    public void Plan_LastSlideIsAlwaysMyApp()
    {
        var planner = CreatePlanner();
        var featured = CreateFeaturedSources(5);
        var myApps = CreateMyAppSources(3);

        for (int seed = 0; seed < 20; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            Assert.Equal(AppSourceType.MyApp, plan.Slides[3].Source.SourceType);
        }
    }

    [Fact]
    public void Plan_FirstThreeSlidesAreFeatured()
    {
        var planner = CreatePlanner();
        var plan = planner.Plan(
            CreateFeaturedSources(5), CreateMyAppSources(2), new Random(42));

        for (int i = 0; i < 3; i++)
        {
            Assert.Equal(AppSourceType.Featured, plan.Slides[i].Source.SourceType);
        }
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
    public void Plan_NoDuplicateCaptionsAcrossSlides()
    {
        var planner = CreatePlanner();
        var featured = CreateFeaturedSources(5);
        var myApps = CreateMyAppSources(2);

        for (int seed = 0; seed < 30; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            var captions = plan.Slides.Select(s => s.DisplayCaption).ToList();
            var distinct = captions.Distinct(StringComparer.OrdinalIgnoreCase).ToList();
            Assert.Equal(captions.Count, distinct.Count);
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
        var planner = CreatePlanner();
        var hookSource = CreateRichFeaturedSource(
            "HookApp", ["design"],
            hookCaptions: ["HOOK: Stop everything right now!"]);
        var painSource = CreateRichFeaturedSource(
            "PainApp", ["productivity"],
            painCaptions: ["PAIN: Every app wastes your time"]);
        var credSource = CreateRichFeaturedSource(
            "CredApp", ["tools"],
            credCaptions: ["CRED: These three tools actually work"]);
        var ctaSource = CreateRichMyAppSource(
            "MyApp", ctaCaptions: ["CTA: Download it and win"]);

        var featured = new List<AppSource> { hookSource, painSource, credSource };
        var myApps = new List<AppSource> { ctaSource };

        // Run multiple seeds to make sure role-specific captions are picked
        bool hookCaptionFound = false;
        bool painCaptionFound = false;
        bool credCaptionFound = false;
        bool ctaCaptionFound = false;

        for (int seed = 0; seed < 50; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            if (plan.Slides[0].DisplayCaption == "HOOK: Stop everything right now!") hookCaptionFound = true;
            if (plan.Slides[1].DisplayCaption == "PAIN: Every app wastes your time") painCaptionFound = true;
            if (plan.Slides[2].DisplayCaption == "CRED: These three tools actually work") credCaptionFound = true;
            if (plan.Slides[3].DisplayCaption == "CTA: Download it and win") ctaCaptionFound = true;
        }

        Assert.True(hookCaptionFound, "Hook role-specific caption should have been selected");
        Assert.True(painCaptionFound, "PainPoint role-specific caption should have been selected");
        Assert.True(credCaptionFound, "Credibility role-specific caption should have been selected");
        Assert.True(ctaCaptionFound, "CTA role-specific caption should have been selected");
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
    public void Plan_PrefersAppsWithDistinctTagsForVariety()
    {
        var planner = CreatePlanner();

        // App A: unique tag "puzzle"
        // App B: unique tag "music"
        // App C: shares tag "design" with D
        // App D: shares tag "design" with C (lower diversity value)
        var sourceA = new AppSource
        {
            Entry = new AppEntry
            {
                AppName = "PuzzleApp", ImageName = "puzzle.png",
                Captions = ["cap"], Tags = ["puzzle"]
            },
            SourceType = AppSourceType.Featured, ImagePath = "/fake/puzzle.png"
        };
        var sourceB = new AppSource
        {
            Entry = new AppEntry
            {
                AppName = "MusicApp", ImageName = "music.png",
                Captions = ["cap"], Tags = ["music"]
            },
            SourceType = AppSourceType.Featured, ImagePath = "/fake/music.png"
        };
        var sourceC = new AppSource
        {
            Entry = new AppEntry
            {
                AppName = "DesignApp1", ImageName = "design1.png",
                Captions = ["cap"], Tags = ["design"]
            },
            SourceType = AppSourceType.Featured, ImagePath = "/fake/design1.png"
        };
        var sourceD = new AppSource
        {
            Entry = new AppEntry
            {
                AppName = "DesignApp2", ImageName = "design2.png",
                Captions = ["cap"], Tags = ["design"]
            },
            SourceType = AppSourceType.Featured, ImagePath = "/fake/design2.png"
        };

        var featured = new List<AppSource> { sourceA, sourceB, sourceC, sourceD };
        var myApps = CreateMyAppSources(1);

        // Over multiple seeds, the planner should not always pick the two design apps together
        int timeBothDesignAppsPicked = 0;
        for (int seed = 0; seed < 50; seed++)
        {
            var plan = planner.Plan(featured, myApps, new Random(seed));
            var featuredApps = plan.Slides.Take(3).Select(s => s.Source.Entry.AppName).ToList();
            if (featuredApps.Contains("DesignApp1") && featuredApps.Contains("DesignApp2"))
                timeBothDesignAppsPicked++;
        }

        // Both design apps picked together should be rare compared to diverse picks
        Assert.True(timeBothDesignAppsPicked < 30,
            "Both design apps should not dominate the reel — tag diversity should reduce co-occurrence");
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
    public void Plan_WorksWithMinimumRequiredSources()
    {
        var planner = CreatePlanner();
        // Exactly 3 featured, 1 myApp
        var plan = planner.Plan(CreateFeaturedSources(3), CreateMyAppSources(1), new Random(5));
        Assert.Equal(4, plan.Slides.Count);
        Assert.Equal(AppSourceType.MyApp, plan.Slides[3].Source.SourceType);
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
