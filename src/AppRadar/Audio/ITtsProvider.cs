using AppRadar.Config;

namespace AppRadar.Audio;

/// <summary>
/// One narrated segment corresponding to a single reel slide.
/// </summary>
public sealed class NarrationSegment
{
    /// <summary>Text to synthesise via TTS.</summary>
    public string Text { get; set; } = string.Empty;

    /// <summary>
    /// Desired slide display duration in seconds.
    /// The audio provider will pad with silence if the speech is shorter.
    /// </summary>
    public double TargetDurationSeconds { get; set; }
}

/// <summary>
/// Abstraction for a TTS narration backend.
/// </summary>
public interface ITtsProvider
{
    /// <summary>
    /// Returns true when this provider can run in the current environment.
    /// </summary>
    bool IsAvailable { get; }

    /// <summary>
    /// Generates a single WAV file that contains all <paramref name="segments"/> narrated
    /// in order, with silence gaps inserted between segments according to
    /// <paramref name="config"/>.
    /// </summary>
    /// <param name="segments">Ordered slide narration segments.</param>
    /// <param name="outputWavPath">
    /// Full path where the final WAV file should be written.
    /// Any existing file at this path will be overwritten.
    /// </param>
    /// <param name="config">Audio configuration (rate, voice, gaps, …).</param>
    /// <param name="ffmpegExe">Full path to ffmpeg.exe for post-processing.</param>
    /// <returns>
    /// The path to the generated WAV file (same as <paramref name="outputWavPath"/>).
    /// </returns>
    string GenerateNarration(
        IReadOnlyList<NarrationSegment> segments,
        string outputWavPath,
        AudioConfig config,
        string ffmpegExe);
}
