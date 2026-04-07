using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Services;

public sealed class SelectionService
{
    private readonly ILogger<SelectionService> _logger;

    public SelectionService(ILogger<SelectionService> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Selects 3 unique featured apps, 1 unique myApp, assigns captions, shuffles order,
    /// and chooses a random transition style.
    /// </summary>
    public (List<SlideSelection> slides, TransitionStyle transition) Select(
        List<AppSource> featuredSources,
        List<AppSource> myAppSources,
        Random rng)
    {
        // Pick 3 unique featured
        var selectedFeatured = PickUnique(featuredSources, 3, rng);
        _logger.LogInformation("Selected featured apps: {Apps}",
            string.Join(", ", selectedFeatured.Select(s => s.Entry.AppName)));

        // Pick 1 unique myApp
        var selectedMyApp = PickUnique(myAppSources, 1, rng);
        _logger.LogInformation("Selected myApp: {App}", selectedMyApp[0].Entry.AppName);

        // Build slides (not yet shuffled)
        var allSelected = selectedFeatured.Concat(selectedMyApp).ToList();

        var slides = allSelected.Select(source =>
        {
            var hookCaptions = source.Entry.Hook.Captions;
            var caption = hookCaptions[rng.Next(hookCaptions.Count)];
            var hookImages = source.Entry.Hook.ImageNames;
            var imageName = hookImages[rng.Next(hookImages.Count)];
            var imagePath = Path.Combine(Path.GetDirectoryName(source.ImagePath) ?? string.Empty, imageName);
            _logger.LogInformation("  {AppName} → caption: \"{Caption}\"",
                source.Entry.AppName, caption);

            return new SlideSelection
            {
                AppName = source.Entry.AppName,
                SourceType = source.SourceType,
                ImageName = imageName,
                SelectedCaption = caption,
                SourcePath = imagePath
            };
        }).ToList();

        // Shuffle order (always true per requirements)
        Shuffle(slides, rng);

        // Assign slots after shuffle
        for (int i = 0; i < slides.Count; i++)
        {
            slides[i].Slot = i + 1;
        }

        _logger.LogInformation("Final slide order: {Order}",
            string.Join(" → ", slides.Select(s => $"[{s.Slot}] {s.AppName}")));

        // Choose transition style
        var transition = (TransitionStyle)rng.Next(0, 2);
        _logger.LogInformation("Transition style: {Transition}", transition);

        return (slides, transition);
    }

    private static List<AppSource> PickUnique(List<AppSource> pool, int count, Random rng)
    {
        if (pool.Count < count)
        {
            throw new InvalidOperationException(
                $"Not enough sources: need {count}, have {pool.Count}");
        }

        var copy = pool.ToList();
        var result = new List<AppSource>(count);
        for (int i = 0; i < count; i++)
        {
            int idx = rng.Next(copy.Count);
            result.Add(copy[idx]);
            copy.RemoveAt(idx);
        }
        return result;
    }

    private static void Shuffle<T>(List<T> list, Random rng)
    {
        for (int i = list.Count - 1; i > 0; i--)
        {
            int j = rng.Next(i + 1);
            (list[i], list[j]) = (list[j], list[i]);
        }
    }
}
