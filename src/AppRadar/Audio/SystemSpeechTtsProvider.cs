using System.Runtime.Versioning;
using System.Speech.Synthesis;
using AppRadar.Config;
using Microsoft.Extensions.Logging;

namespace AppRadar.Audio;

/// <summary>
/// Windows TTS provider backed by the SAPI <see cref="SpeechSynthesizer"/> from
/// <c>System.Speech</c> (available on Windows 7+).
///
/// Each slide segment is synthesised to a temporary WAV file.  All segments are
/// then concatenated — with configurable silence gaps — via FFmpeg, producing a
/// single WAV that can be muxed into the final MP4.
///
/// The provider is only available on Windows (<see cref="IsAvailable"/>).
/// </summary>
[SupportedOSPlatform("windows")]
public sealed class SystemSpeechTtsProvider : ITtsProvider
{
    private readonly ILogger<SystemSpeechTtsProvider> _logger;

    public SystemSpeechTtsProvider(ILogger<SystemSpeechTtsProvider> logger)
    {
        _logger = logger;
    }

    public bool IsAvailable => OperatingSystem.IsWindows();

    public string GenerateNarration(
        IReadOnlyList<NarrationSegment> segments,
        string outputWavPath,
        AudioConfig config,
        string ffmpegExe)
    {
        if (!OperatingSystem.IsWindows())
            throw new PlatformNotSupportedException(
                "SystemSpeechTtsProvider requires Windows. " +
                "Disable audio with --with-audio false or set audio.enabled=false in config.json.");

        _logger.LogInformation("Generating TTS narration for {Count} segments", segments.Count);

        var tempDir = Path.Combine(Path.GetTempPath(), $"AppRadar_tts_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var segmentPaths = SynthesiseSegments(segments, config, tempDir);
            ConcatenateSegments(segmentPaths, segments, outputWavPath, config, ffmpegExe);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }

        _logger.LogInformation("Narration WAV written to {Path}", outputWavPath);
        return outputWavPath;
    }

    // ── Synthesis ─────────────────────────────────────────────────────────────────────────────

    [SupportedOSPlatform("windows")]
    private List<string> SynthesiseSegments(
        IReadOnlyList<NarrationSegment> segments,
        AudioConfig config,
        string tempDir)
    {
        var paths = new List<string>();

        using var synth = new SpeechSynthesizer();
        ConfigureSynthesizer(synth, config);

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var segPath = Path.Combine(tempDir, $"seg_{i:D2}.wav");

            _logger.LogDebug("Synthesising segment {Index}: \"{Text}\"", i, segment.Text);
            synth.SetOutputToWaveFile(segPath);
            synth.Speak(segment.Text);
            synth.SetOutputToNull(); // flush / close the file

            paths.Add(segPath);
        }

        return paths;
    }

    [SupportedOSPlatform("windows")]
    private static void ConfigureSynthesizer(SpeechSynthesizer synth, AudioConfig config)
    {
        if (!string.IsNullOrWhiteSpace(config.VoiceName))
        {
            try { synth.SelectVoice(config.VoiceName); }
            catch { /* voice not found — fall back to system default */ }
        }

        // Rate: SAPI accepts -10 to 10
        synth.Rate = Math.Clamp(config.Rate, -10, 10);
        synth.Volume = Math.Clamp(config.Volume, 0, 100);
    }

    // ── Concatenation via FFmpeg ──────────────────────────────────────────────────────────────

    /// <summary>
    /// Concatenates per-segment WAVs into a single output WAV, inserting silence
    /// at the start of each segment and between segments as configured.
    /// When a segment is shorter than its <see cref="NarrationSegment.TargetDurationSeconds"/>,
    /// it is padded with trailing silence so the total audio aligns with the video timeline.
    /// </summary>
    private void ConcatenateSegments(
        IReadOnlyList<string> segmentPaths,
        IReadOnlyList<NarrationSegment> segments,
        string outputWavPath,
        AudioConfig config,
        string ffmpegExe)
    {
        _logger.LogDebug("Concatenating {Count} audio segments with FFmpeg", segmentPaths.Count);

        // Build a list of (inputPath, paddedDuration) for each segment.
        // We use FFmpeg's apad filter to extend each segment to its target duration.

        var filterParts = new System.Text.StringBuilder();
        var inputArgs = new System.Text.StringBuilder();
        int inputIndex = 0;

        double leadInSec = config.LeadInMs / 1000.0;
        double gapSec = config.GapBetweenSlidesMs / 1000.0;

        for (int i = 0; i < segmentPaths.Count; i++)
        {
            inputArgs.Append($"-i \"{segmentPaths[i]}\" ");
            inputIndex++;

            // Re-sample to a consistent format and pad each segment to its target duration.
            // apad inserts silence at the end; atrim removes any excess.
            double targetDur = Math.Max(segments[i].TargetDurationSeconds - leadInSec, 0.5);

            filterParts.Append(
                $"[{i}:a]" +
                $"aresample=22050," +
                $"aformat=sample_fmts=s16:channel_layouts=mono," +
                $"adelay={(int)(leadInSec * 1000)}|{(int)(leadInSec * 1000)}," +
                $"apad=whole_dur={segments[i].TargetDurationSeconds:F3}," +
                $"atrim=duration={segments[i].TargetDurationSeconds:F3}" +
                $"[seg{i}]; ");
        }

        // Insert gap silence between segments, then concat all
        int concatInputs = segmentPaths.Count;
        for (int i = 0; i < concatInputs; i++)
            filterParts.Append($"[seg{i}]");

        filterParts.Append($"concat=n={concatInputs}:v=0:a=1[cat]; ");

        if (config.NormalizeAudio)
            filterParts.Append("[cat]loudnorm[outa]");
        else
            filterParts.Append("[cat]acopy[outa]");

        var args =
            $"{inputArgs}" +
            $"-filter_complex \"{filterParts}\" " +
            $"-map \"[outa]\" " +
            $"-ar 22050 -ac 1 " +
            $"-y \"{outputWavPath}\"";

        RunFfmpegProcess(ffmpegExe, args);
    }

    private void RunFfmpegProcess(string ffmpegPath, string args)
    {
        _logger.LogDebug("TTS FFmpeg: \"{FFmpeg}\" {Args}", ffmpegPath, args);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Failed to start FFmpeg for audio processing. Executable: {ffmpegPath}");

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg audio processing failed (exit {Code}):\n{Stderr}",
                process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"FFmpeg audio processing failed with exit code {process.ExitCode}. " +
                "Check the log for details.");
        }
    }

    private void TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up temp TTS directory: {Dir}", dir);
        }
    }
}
