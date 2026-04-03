using AppRadar.Models;
using AppRadar.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppRadar.Tests;

public sealed class SelectionServiceTests
{
    private static SelectionService CreateService() =>
        new(NullLogger<SelectionService>.Instance);

    private static List<AppSource> CreateSources(int count, AppSourceType type) =>
        Enumerable.Range(1, count).Select(i => new AppSource
        {
            Entry = new AppEntry
            {
                AppName = $"App{i}",
                ImageName = $"img{i}.png",
                Captions = [$"Caption A for {i}", $"Caption B for {i}"]
            },
            SourceType = type,
            ImagePath = $"/fake/img{i}.png"
        }).ToList();

    [Fact]
    public void Select_Returns4Slides_3FeaturedAnd1MyApp()
    {
        var service = CreateService();
        var featured = CreateSources(5, AppSourceType.Featured);
        var myApps = CreateSources(2, AppSourceType.MyApp);
        var rng = new Random(1);

        var (slides, _) = service.Select(featured, myApps, rng);

        Assert.Equal(4, slides.Count);
        Assert.Equal(3, slides.Count(s => s.SourceType == AppSourceType.Featured));
        Assert.Equal(1, slides.Count(s => s.SourceType == AppSourceType.MyApp));
    }

    [Fact]
    public void Select_AllSlotsUnique()
    {
        var service = CreateService();
        var featured = CreateSources(5, AppSourceType.Featured);
        var myApps = CreateSources(3, AppSourceType.MyApp);
        var rng = new Random(42);

        var (slides, _) = service.Select(featured, myApps, rng);

        var slots = slides.Select(s => s.Slot).ToList();
        Assert.Equal(new[] { 1, 2, 3, 4 }, slots.OrderBy(x => x));
    }

    [Fact]
    public void Select_FeaturedAppsAreUnique()
    {
        var service = CreateService();
        var featured = CreateSources(5, AppSourceType.Featured);
        var myApps = CreateSources(2, AppSourceType.MyApp);
        var rng = new Random(99);

        var (slides, _) = service.Select(featured, myApps, rng);

        var featuredNames = slides
            .Where(s => s.SourceType == AppSourceType.Featured)
            .Select(s => s.AppName)
            .ToList();

        Assert.Equal(3, featuredNames.Distinct().Count());
    }

    [Fact]
    public void Select_WithSeed_IsDeterministic()
    {
        var service = CreateService();
        var featured = CreateSources(5, AppSourceType.Featured);
        var myApps = CreateSources(2, AppSourceType.MyApp);

        var (slides1, transition1) = service.Select(featured, myApps, new Random(123));
        var (slides2, transition2) = service.Select(featured, myApps, new Random(123));

        Assert.Equal(transition1, transition2);
        Assert.Equal(slides1.Count, slides2.Count);
        for (int i = 0; i < slides1.Count; i++)
        {
            Assert.Equal(slides1[i].AppName, slides2[i].AppName);
            Assert.Equal(slides1[i].SelectedCaption, slides2[i].SelectedCaption);
            Assert.Equal(slides1[i].Slot, slides2[i].Slot);
        }
    }

    [Fact]
    public void Select_DifferentSeeds_ProduceDifferentResults()
    {
        var service = CreateService();
        var featured = CreateSources(5, AppSourceType.Featured);
        var myApps = CreateSources(2, AppSourceType.MyApp);

        var (slides1, _) = service.Select(featured, myApps, new Random(1));
        var (slides2, _) = service.Select(featured, myApps, new Random(9999));

        // With different seeds, at least one slide should differ (probability of exact match is very low)
        bool allSame = slides1.Zip(slides2).All(pair =>
            pair.First.AppName == pair.Second.AppName &&
            pair.First.SelectedCaption == pair.Second.SelectedCaption);

        // This could theoretically fail with unlucky seeds, but practically won't
        Assert.False(allSame);
    }

    [Fact]
    public void Select_CaptionIsFromKnownList()
    {
        var service = CreateService();
        var featured = CreateSources(5, AppSourceType.Featured);
        var myApps = CreateSources(2, AppSourceType.MyApp);
        var rng = new Random(77);

        var (slides, _) = service.Select(featured, myApps, rng);

        foreach (var slide in slides)
        {
            var source = featured.Concat(myApps)
                .FirstOrDefault(s => s.Entry.AppName == slide.AppName);
            if (source is not null)
            {
                Assert.Contains(slide.SelectedCaption, source.Entry.Captions);
            }
        }
    }

    [Fact]
    public void Select_ThrowsWhenNotEnoughFeatured()
    {
        var service = CreateService();
        var featured = CreateSources(2, AppSourceType.Featured); // only 2, need 3
        var myApps = CreateSources(2, AppSourceType.MyApp);

        Assert.Throws<InvalidOperationException>(() =>
            service.Select(featured, myApps, new Random(1)));
    }
}
