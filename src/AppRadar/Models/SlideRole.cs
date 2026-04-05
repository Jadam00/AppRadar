namespace AppRadar.Models;

/// <summary>
/// The narrative role a slide plays in the structured 4-stage marketing reel.
/// </summary>
public enum SlideRole
{
    /// <summary>Slide 1: Hard hook — stop the scroll, create tension or curiosity.</summary>
    Hook,

    /// <summary>Slide 2: Pain point / curiosity — identify a problem or intriguing angle.</summary>
    PainPoint,

    /// <summary>Slide 3: Comparison / credibility — evidence, contrast, or build-up.</summary>
    Credibility,

    /// <summary>Slide 4: App / CTA — present the featured app as the payoff.</summary>
    Cta
}
