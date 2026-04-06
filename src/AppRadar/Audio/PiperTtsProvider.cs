using System.Diagnostics;
using System.Text;
using System.Text.RegularExpressions;
using AppRadar.Config;
using Microsoft.Extensions.Logging;

namespace AppRadar.Audio;

/// <summary>
/// TTS provider backed by the Piper neural TTS engine, invoked as an external executable.
///
/// Each slide segment is synthesised to a temporary WAV file by calling <c>piper.exe</c>.
/// All segments are then concatenated — with configurable silence gaps — via FFmpeg,
/// producing a single WAV that can be muxed into the final MP4.
///
/// Requires Windows and a locally installed Piper build with a voice model.
/// See README.md → "Piper TTS Setup" for installation instructions.
/// </summary>
public sealed class PiperTtsProvider : ITtsProvider
{
    /// <summary>Maximum number of characters allowed per narration segment.</summary>
    internal const int MaxTextLengthChars = 5000;

    /// <summary>Seconds to wait for a single Piper process before killing it.</summary>
    internal const int DefaultTimeoutSeconds = 30;
    private static readonly Regex PauseMarkerRegex =
        new(@"\[\[\s*pause(?:\s*=\s*(\d{1,5}))?\s*\]\]", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    private const double MinimumSegmentDurationSeconds = 0.5;

    private readonly PiperConfig _config;
    private readonly ILogger<PiperTtsProvider> _logger;

    // Injected for unit testing so no real process needs to be spawned.
    // Parameters: exe, args, stdinText, timeoutSeconds → (exitCode, stderr)
    private readonly Func<string, string, string, int, (int ExitCode, string Stderr)> _processRunner;

    /// <summary>
    /// Creates a <see cref="PiperTtsProvider"/> that runs Piper as a real subprocess.
    /// </summary>
    public PiperTtsProvider(PiperConfig config, ILogger<PiperTtsProvider> logger)
        : this(config, logger, RunProcess)
    {
    }

    /// <summary>
    /// Creates a <see cref="PiperTtsProvider"/> with an injected process runner.
    /// Intended for unit testing — use the public constructor in production.
    /// </summary>
    internal PiperTtsProvider(
        PiperConfig config,
        ILogger<PiperTtsProvider> logger,
        Func<string, string, string, int, (int ExitCode, string Stderr)> processRunner)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processRunner = processRunner ?? throw new ArgumentNullException(nameof(processRunner));
    }

    /// <inheritdoc />
    /// <remarks>
    /// Returns <see langword="false"/> on non-Windows platforms — AppRadar is Windows-only.
    /// Also returns <see langword="false"/> if any required file (exe, model, or model config)
    /// is absent.
    /// </remarks>
    public bool IsAvailable =>
        OperatingSystem.IsWindows() &&
        !string.IsNullOrWhiteSpace(_config.ExePath) &&
        !string.IsNullOrWhiteSpace(_config.ModelPath) &&
        File.Exists(_config.ExePath) &&
        File.Exists(_config.ModelPath) &&
        File.Exists(_config.ModelPath + ".json");

    /// <inheritdoc />
    public string GenerateNarration(
        IReadOnlyList<NarrationSegment> segments,
        string outputWavPath,
        AudioConfig config,
        string ffmpegExe)
    {
        ValidateConfig();

        _logger.LogInformation(
            "Generating TTS narration for {Count} segment(s) using Piper", segments.Count);
        _logger.LogDebug("Piper executable: {ExePath}", _config.ExePath);
        _logger.LogDebug("Piper model: {ModelPath}", _config.ModelPath);

        var tempDir = Path.Combine(Path.GetTempPath(), $"AppRadar_piper_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var segmentPaths = SynthesiseSegments(segments, tempDir);
            ConcatenateSegments(segmentPaths, segments, outputWavPath, config, ffmpegExe);
        }
        finally
        {
            TryDeleteDirectory(tempDir);
        }

        _logger.LogInformation("Narration WAV written to {Path}", outputWavPath);
        return outputWavPath;
    }

    // ── Validation ────────────────────────────────────────────────────────────────────────────

    private void ValidateConfig()
    {
        if (string.IsNullOrWhiteSpace(_config.ExePath))
            throw new InvalidOperationException(
                "Piper TTS is missing 'audio.piper.exePath' in config.json. " +
                "Set it to the full path of piper.exe.");

        if (string.IsNullOrWhiteSpace(_config.ModelPath))
            throw new InvalidOperationException(
                "Piper TTS is missing 'audio.piper.modelPath' in config.json. " +
                "Set it to the full path of your .onnx voice model.");

        if (!File.Exists(_config.ExePath))
            throw new FileNotFoundException(
                $"Piper executable not found: '{_config.ExePath}'. " +
                "Download Piper from https://github.com/rhasspy/piper/releases " +
                "and update audio.piper.exePath in config.json.",
                _config.ExePath);

        if (!File.Exists(_config.ModelPath))
            throw new FileNotFoundException(
                $"Piper voice model not found: '{_config.ModelPath}'. " +
                "Download a voice model (.onnx) from https://huggingface.co/rhasspy/piper-voices " +
                "and update audio.piper.modelPath in config.json.",
                _config.ModelPath);

        var modelConfigPath = _config.ModelPath + ".json";
        if (!File.Exists(modelConfigPath))
            throw new FileNotFoundException(
                $"Piper model config not found: '{modelConfigPath}'. " +
                "The .onnx.json sidecar file must be present in the same folder as the .onnx model. " +
                "Download it alongside the .onnx file from https://huggingface.co/rhasspy/piper-voices.",
                modelConfigPath);
    }

    // ── Synthesis ─────────────────────────────────────────────────────────────────────────────

    private List<string> SynthesiseSegments(
        IReadOnlyList<NarrationSegment> segments,
        string tempDir)
    {
        var paths = new List<string>(segments.Count);

        for (int i = 0; i < segments.Count; i++)
        {
            var segment = segments[i];
            var segPath = Path.Combine(tempDir, $"seg_{i:D2}.wav");
            var safeText = SanitiseTextForSynthesis(segment.Text);
            ValidateText(safeText);
            var args = BuildArguments(segPath);

            _logger.LogDebug(
                "Piper synthesising segment {Index}: \"{Text}\"", i, segment.Text);
            _logger.LogDebug(
                "Piper command: \"{Exe}\" {Args} (text via stdin)", _config.ExePath, args);

            var (exitCode, stderr) = _processRunner(_config.ExePath, args, safeText, DefaultTimeoutSeconds);

            if (exitCode != 0)
                throw new InvalidOperationException(
                    $"Piper TTS failed on segment {i} (exit code {exitCode}).\n" +
                    $"Command: \"{_config.ExePath}\" {args}\n" +
                    $"Stderr: {stderr}");

            _logger.LogDebug("Piper wrote WAV segment: {Path}", segPath);
            paths.Add(segPath);
        }

        return paths;
    }

    /// <summary>
    /// Validates that <paramref name="sanitisedText"/> does not exceed the maximum allowed length.
    /// </summary>
    /// <exception cref="ArgumentException">Thrown when the text is too long.</exception>
    internal static void ValidateText(string sanitisedText)
    {
        if (sanitisedText.Length > MaxTextLengthChars)
            throw new ArgumentException(
                $"Narration segment exceeds the maximum allowed length of {MaxTextLengthChars} " +
                $"characters ({sanitisedText.Length} chars). Shorten the caption for this slide.",
                nameof(sanitisedText));
    }

    /// <summary>
    /// Builds the <c>piper.exe</c> argument string for a single segment.
    /// Narration text is not included here; it is sent to Piper via stdin.
    /// </summary>
    internal string BuildArguments(string outputPath)
    {
        var sb = new StringBuilder();
        sb.Append($"--model \"{_config.ModelPath}\" ");
        sb.Append($"--output_file \"{outputPath}\" ");
        sb.Append($"--length_scale {_config.LengthScale:F3} ");
        sb.Append($"--noise_scale {_config.NoiseScale:F3} ");
        sb.Append($"--noise_w {_config.NoiseW:F3}");

        if (_config.Speaker.HasValue)
            sb.Append($" --speaker {_config.Speaker.Value}");

        return sb.ToString();
    }

    /// <summary>
    /// Sanitises narration text before it is written to Piper's stdin.
    /// Line endings and tabs are collapsed to spaces so Piper receives a single clean line.
    /// </summary>
    internal static string SanitiseText(string text)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        // Normalise line endings and tabs to spaces — Piper expects a single line via stdin
        text = text.Replace('\n', ' ').Replace('\r', ' ').Replace('\t', ' ');

        // Collapse multiple spaces and trim
        return string.Join(" ", text.Split(' ', StringSplitOptions.RemoveEmptyEntries));
    }

    /// <summary>
    /// Expands optional pause markers and then normalises whitespace for synthesis.
    /// Supported markers:
    /// <c>[[pause]]</c> and <c>[[pause=400]]</c> (milliseconds).
    /// </summary>
    internal string SanitiseTextForSynthesis(string text)
    {
        if (!_config.EnablePauseMarkers)
            return SanitiseText(text);

        var expanded = ExpandPauseMarkers(
            text,
            _config.PauseMarkerDefaultMs,
            _config.PauseMarkerMinMs,
            _config.PauseMarkerMaxMs);

        return SanitiseText(expanded);
    }

    internal static string ExpandPauseMarkers(
        string text,
        int defaultPauseMs,
        int minPauseMs,
        int maxPauseMs)
    {
        if (string.IsNullOrWhiteSpace(text))
            return string.Empty;

        if (minPauseMs <= 0)
            minPauseMs = 1;
        if (maxPauseMs < minPauseMs)
            maxPauseMs = minPauseMs;

        defaultPauseMs = Math.Clamp(defaultPauseMs, minPauseMs, maxPauseMs);

        return PauseMarkerRegex.Replace(text, m =>
        {
            var requestedMs = defaultPauseMs;
            if (m.Groups[1].Success && int.TryParse(m.Groups[1].Value, out var parsed))
                requestedMs = Math.Clamp(parsed, minPauseMs, maxPauseMs);

            return requestedMs switch
            {
                <= 220 => ", ",
                <= 500 => ". ",
                <= 900 => "... ",
                _ => ".... "
            };
        });
    }

    // ── Concatenation via FFmpeg ──────────────────────────────────────────────────────────────

    private void ConcatenateSegments(
        IReadOnlyList<string> segmentPaths,
        IReadOnlyList<NarrationSegment> segments,
        string outputWavPath,
        AudioConfig config,
        string ffmpegExe)
    {
        _logger.LogDebug("Concatenating {Count} audio segment(s) with FFmpeg", segmentPaths.Count);

        var filterParts = new StringBuilder();
        var inputArgs = new StringBuilder();

        double leadInSec = config.LeadInMs / 1000.0;

        for (int i = 0; i < segmentPaths.Count; i++)
        {
            inputArgs.Append($"-i \"{segmentPaths[i]}\" ");

            filterParts.Append(
                $"[{i}:a]" +
                $"aresample=22050," +
                $"aformat=sample_fmts=s16:channel_layouts=mono," +
                $"adelay={(int)(leadInSec * 1000)}|{(int)(leadInSec * 1000)}," +
                $"apad=whole_dur={segments[i].TargetDurationSeconds:F3}," +
                $"atrim=duration={segments[i].TargetDurationSeconds:F3}" +
                $"[seg{i}]; ");
        }

        int concatInputs = segmentPaths.Count;
        for (int i = 0; i < concatInputs; i++)
            filterParts.Append($"[seg{i}]");

        filterParts.Append($"concat=n={concatInputs}:v=0:a=1[cat]; ");

        if (config.NormalizeAudio)
            filterParts.Append("[cat]loudnorm[outa]");
        else
            filterParts.Append("[cat]acopy[outa]");

        var ffmpegArgs =
            $"{inputArgs}" +
            $"-filter_complex \"{filterParts}\" " +
            $"-map \"[outa]\" " +
            $"-ar 22050 -ac 1 " +
            $"-y \"{outputWavPath}\"";

        RunFfmpegProcess(ffmpegExe, ffmpegArgs);
    }

    private void RunFfmpegProcess(string ffmpegPath, string args)
    {
        _logger.LogDebug("TTS FFmpeg: \"{FFmpeg}\" {Args}", ffmpegPath, args);

        var psi = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using var process = Process.Start(psi)
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

    // ── Process execution ─────────────────────────────────────────────────────────────────────

    private static (int ExitCode, string Stderr) RunProcess(
        string exe, string args, string text, int timeoutSeconds)
    {
        var psi = new ProcessStartInfo
        {
            FileName = exe,
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardInput = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardInputEncoding = Encoding.UTF8,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(psi)
            ?? throw new InvalidOperationException(
                $"Failed to start Piper process. Executable: {exe}");

        // Send narration text via stdin; closing signals end of input to Piper
        process.StandardInput.WriteLine(text);
        process.StandardInput.Close();

        // Read stderr asynchronously to prevent deadlock if the buffer fills before exit
        var stderrTask = process.StandardError.ReadToEndAsync();
        var completed = process.WaitForExit(TimeSpan.FromSeconds(timeoutSeconds));

        if (!completed)
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException(
                $"Piper process timed out after {timeoutSeconds} seconds. " +
                $"Executable: {exe}");
        }

        return (process.ExitCode, stderrTask.GetAwaiter().GetResult());
    }

    // ── Cleanup ───────────────────────────────────────────────────────────────────────────────

    private void TryDeleteDirectory(string dir)
    {
        try { Directory.Delete(dir, recursive: true); }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Could not clean up temp Piper directory: {Dir}", dir);
        }
    }
}
