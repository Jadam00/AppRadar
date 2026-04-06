using AppRadar.Config;
using AppRadar.Models;
using Microsoft.Extensions.Logging;

namespace AppRadar.Video;

public sealed class VideoComposer
{
    private readonly ILogger<VideoComposer> _logger;

    public VideoComposer(ILogger<VideoComposer> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Validates that FFmpeg is available and logs where it was found.
    /// Throws <see cref="InvalidOperationException"/> with actionable Windows-specific
    /// instructions if FFmpeg cannot be located.
    /// </summary>
    public void ValidateFfmpegAvailable(AppConfig config)
    {
        var path = ResolveFfmpegPath(config);
        _logger.LogInformation("FFmpeg resolved to: {Path}", path);
    }

    /// <summary>
    /// Returns the resolved FFmpeg path for use by other pipeline components (e.g. TTS).
    /// Throws <see cref="InvalidOperationException"/> when FFmpeg cannot be found.
    /// </summary>
    public string GetFfmpegPath(AppConfig config) => ResolveFfmpegPath(config);

    private string ResolveFfmpegPath(AppConfig config)
    {
        var path = FindFfmpegExe(config, _logger);
        if (path is null)
        {
            throw new InvalidOperationException(BuildNotFoundMessage(config));
        }
        return path;
    }

    /// <summary>
    /// Locates ffmpeg.exe using the following Windows-native strategy (in order):
    /// 1. Explicit <c>Tools:FFmpegPath</c> config value
    /// 2. <c>FFMPEG_PATH</c> environment variable
    /// 3. Common Windows install locations (manual, WinGet, Chocolatey, Scoop)
    /// 4. Each directory on the <c>PATH</c> environment variable, checking both
    ///    <c>ffmpeg.exe</c> and <c>ffmpeg</c>
    /// 5. Direct execution probe — lets the OS resolve the name via PATH / shims
    /// </summary>
    internal static string? FindFfmpegExe(AppConfig config, ILogger? logger = null)
    {
        // 1. Explicit config value
        if (!string.IsNullOrWhiteSpace(config.Tools.FFmpegPath))
        {
            var configured = config.Tools.FFmpegPath.Trim().Trim('"');
            if (File.Exists(configured))
            {
                logger?.LogDebug("FFmpeg found via Tools:FFmpegPath config: {Path}", configured);
                return configured;
            }

            // If the user explicitly configured a path but it does not exist, fail fast
            // with a clear message rather than silently falling back.
            logger?.LogWarning(
                "Tools:FFmpegPath is configured as '{Path}' but the file does not exist.",
                configured);
            return null;
        }

        // 2. FFMPEG_PATH environment variable
        var envValue = Environment.GetEnvironmentVariable("FFMPEG_PATH");
        if (!string.IsNullOrWhiteSpace(envValue))
        {
            var envPath = envValue.Trim().Trim('"');
            if (File.Exists(envPath))
            {
                logger?.LogDebug("FFmpeg found via FFMPEG_PATH environment variable: {Path}", envPath);
                return envPath;
            }

            logger?.LogWarning(
                "FFMPEG_PATH environment variable is set to '{Path}' but the file does not exist.",
                envPath);
        }

        // 3. Common Windows install locations
        var commonLocations = BuildCommonWindowsLocations();
        foreach (var candidate in commonLocations)
        {
            if (File.Exists(candidate))
            {
                logger?.LogDebug("FFmpeg found at common Windows location: {Path}", candidate);
                return candidate;
            }
        }

        // 4. PATH environment variable — check each directory for ffmpeg.exe / ffmpeg
        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        var exeNames = new[] { "ffmpeg.exe", "ffmpeg" };
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var trimmedDir = dir.Trim().Trim('"');
            if (string.IsNullOrWhiteSpace(trimmedDir)) continue;

            foreach (var exe in exeNames)
            {
                var full = Path.Combine(trimmedDir, exe);
                if (File.Exists(full))
                {
                    logger?.LogDebug("FFmpeg found on PATH: {Path}", full);
                    return full;
                }
            }
        }

        // 5. Direct execution probe — covers PATH shims (e.g. Scoop) that are not plain files
        foreach (var exeName in new[] { "ffmpeg.exe", "ffmpeg" })
        {
            try
            {
                var psi = new System.Diagnostics.ProcessStartInfo
                {
                    FileName = exeName,
                    Arguments = "-version",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true
                };
                using var probe = System.Diagnostics.Process.Start(psi);
                if (probe is not null)
                {
                    probe.WaitForExit(5000);
                    if (probe.ExitCode == 0)
                    {
                        logger?.LogDebug("FFmpeg responds to direct execution as '{Name}'", exeName);
                        return exeName;
                    }
                }
            }
            catch { /* not on PATH or not executable */ }
        }

        return null;
    }

    private static IEnumerable<string> BuildCommonWindowsLocations()
    {
        // Static well-known locations
        yield return @"C:\ffmpeg\bin\ffmpeg.exe";
        yield return @"C:\Program Files\ffmpeg\bin\ffmpeg.exe";
        yield return @"C:\Program Files (x86)\ffmpeg\bin\ffmpeg.exe";

        // Chocolatey
        var programData = Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData);
        yield return Path.Combine(programData, "chocolatey", "bin", "ffmpeg.exe");

        // Scoop (user and global)
        var userProfile = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        yield return Path.Combine(userProfile, "scoop", "shims", "ffmpeg.exe");
        yield return @"C:\ProgramData\scoop\shims\ffmpeg.exe";

        // WinGet installs (user-level LocalAppData)
        var localAppData = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
        var wingetBase = Path.Combine(localAppData, "Microsoft", "WinGet", "Packages");
        if (Directory.Exists(wingetBase))
        {
            foreach (var pkg in Directory.EnumerateDirectories(wingetBase, "Gyan.FFmpeg_*"))
            {
                foreach (var binDir in Directory.EnumerateDirectories(pkg, "ffmpeg-*"))
                {
                    var candidate = Path.Combine(binDir, "bin", "ffmpeg.exe");
                    if (File.Exists(candidate)) yield return candidate;
                }
            }
        }

        // Additional user-level installs
        yield return Path.Combine(localAppData, "Programs", "ffmpeg", "bin", "ffmpeg.exe");
    }

    private static string BuildNotFoundMessage(AppConfig config)
    {
        var configuredPath = config.Tools.FFmpegPath;
        var envValue = Environment.GetEnvironmentVariable("FFMPEG_PATH");

        var sb = new System.Text.StringBuilder();
        sb.AppendLine("FFmpeg not found. AppRadar searched the following locations on Windows:");
        sb.AppendLine();

        if (!string.IsNullOrWhiteSpace(configuredPath))
            sb.AppendLine($"  - Tools:FFmpegPath config  : '{configuredPath}' (file does not exist)");
        else
            sb.AppendLine("  - Tools:FFmpegPath config  : (not set)");

        if (!string.IsNullOrWhiteSpace(envValue))
            sb.AppendLine($"  - FFMPEG_PATH env variable : '{envValue}' (file does not exist)");
        else
            sb.AppendLine("  - FFMPEG_PATH env variable : (not set)");

        sb.AppendLine("  - Common Windows install locations (C:\\ffmpeg\\bin, Program Files, Chocolatey, Scoop, WinGet)");
        sb.AppendLine("  - Each directory listed in the PATH environment variable");
        sb.AppendLine();
        sb.AppendLine("To fix, choose one of the following options:");
        sb.AppendLine();
        sb.AppendLine("  Option 1 — Install FFmpeg and ensure it is on PATH:");
        sb.AppendLine("    winget install Gyan.FFmpeg");
        sb.AppendLine("    choco install ffmpeg");
        sb.AppendLine("    scoop install ffmpeg");
        sb.AppendLine("    Then restart your terminal and verify:  ffmpeg -version");
        sb.AppendLine();
        sb.AppendLine("  Option 2 — Set an explicit path in input\\config.json:");
        sb.AppendLine("    \"tools\": { \"ffmpegPath\": \"C:\\\\ffmpeg\\\\bin\\\\ffmpeg.exe\" }");
        sb.AppendLine();
        sb.AppendLine("  Option 3 — Set the FFMPEG_PATH environment variable:");
        sb.AppendLine("    $env:FFMPEG_PATH = 'C:\\ffmpeg\\bin\\ffmpeg.exe'");
        sb.AppendLine();
        sb.AppendLine("Download FFmpeg for Windows: https://ffmpeg.org/download.html#build-windows");

        return sb.ToString();
    }

    // Minimum xfade offset in seconds — prevents an offset of 0 which FFmpeg rejects.
    private const double MinXfadeOffsetSeconds = 0.001;

    // A chunk must be displayed for at least half the xfade transition duration, otherwise
    // the transition would start before the chunk is even fully visible.
    private const double MinChunkDisplayFraction = 0.5;

    public string ComposeVideo(
        List<string> slidePaths,
        string outputVideosDir,
        string generationId,
        int durationSeconds,
        TransitionStyle transition,
        AppConfig config,
        string? audioPath = null,
        IReadOnlyList<int>? chunkDurationsMs = null)
    {
        _logger.LogInformation("Composing video for reel {Id} with transition: {Transition}",
            generationId, transition);

        var ffmpegExe = ResolveFfmpegPath(config);
        _logger.LogDebug("Using FFmpeg executable: {Path}", ffmpegExe);

        var outputPath = Path.Combine(outputVideosDir, $"{generationId}.mp4");

        int fps = config.Video.Fps;
        int width = config.Video.Width;
        int height = config.Video.Height;
        int driftPixels = config.Animation.VerticalDriftPixels;
        int transitionMs = config.Animation.TransitionDurationMs;

        string filterScript;
        List<string> inputArgs;

        if (chunkDurationsMs is not null && chunkDurationsMs.Count == slidePaths.Count)
        {
            // Narration-driven chunked composition: one input per chunk, variable durations.
            // Input durations are inflated by the xfade transition length (except the last)
            // so that total output exactly matches the sum of chunk display durations.
            double transitionSec = transitionMs / 1000.0;
            double minDisplaySec = transitionSec * MinChunkDisplayFraction;

            // Compute clamped display durations once; reuse for both input args and filter graph.
            var displayDurSec = chunkDurationsMs
                .Select(ms => Math.Max(ms / 1000.0, minDisplaySec))
                .ToList();

            inputArgs = new List<string>(slidePaths.Count);
            for (int i = 0; i < slidePaths.Count; i++)
            {
                bool isLast = i == slidePaths.Count - 1;
                double inputSec = isLast ? displayDurSec[i] : displayDurSec[i] + transitionSec;
                inputArgs.Add($"-loop 1 -t {inputSec:F3} -i \"{slidePaths[i]}\"");
            }

            filterScript = BuildFilterGraphChunked(
                slidePaths.Count, fps, durationSeconds,
                width, height, driftPixels, transitionMs, displayDurSec);
        }
        else
        {
            // Fixed-duration cycling composition (legacy and fallback path).
            int secondsPerSlide = config.Video.SecondsPerSlide;
            int cycleCount = CalculateCycleCount(durationSeconds, secondsPerSlide, slidePaths.Count,
                transitionMs / 1000.0);
            int totalSlides = slidePaths.Count * cycleCount;

            inputArgs = new List<string>(totalSlides);
            for (int cycle = 0; cycle < cycleCount; cycle++)
            {
                foreach (var slidePath in slidePaths)
                    inputArgs.Add($"-loop 1 -t {secondsPerSlide} -i \"{slidePath}\"");
            }

            filterScript = BuildFilterGraph(
                totalSlides, fps, secondsPerSlide, durationSeconds,
                width, height, driftPixels, transitionMs, transition);
        }

        var filterFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filterFile, filterScript);
            _logger.LogDebug("FFmpeg filter graph written to: {File}", filterFile);

            ExecuteComposition(ffmpegExe, inputArgs, filterFile, outputPath, fps, durationSeconds, audioPath, outputVideosDir, generationId);
        }
        finally
        {
            if (File.Exists(filterFile)) File.Delete(filterFile);
        }

        _logger.LogInformation("Video saved to: {Path}", outputPath);
        return outputPath;
    }

    private void ExecuteComposition(
        string ffmpegExe,
        List<string> inputArgs,
        string filterFile,
        string outputPath,
        int fps,
        int durationSeconds,
        string? audioPath,
        string outputVideosDir,
        string generationId)
    {
        if (audioPath is not null && File.Exists(audioPath))
        {
            // Two-pass: (1) render silent video, (2) mux audio
            var silentPath = Path.Combine(outputVideosDir, $"{generationId}_silent.mp4");
            try
            {
                var videoArgs =
                    $"{string.Join(" ", inputArgs)} " +
                    $"-filter_complex_script \"{filterFile}\" " +
                    $"-map \"[outv]\" " +
                    $"-c:v libx264 -pix_fmt yuv420p -crf 23 -preset fast " +
                    $"-r {fps} -t {durationSeconds} " +
                    $"-movflags +faststart " +
                    $"-an " +
                    $"-y \"{silentPath}\"";

                RunFfmpegProcess(ffmpegExe, videoArgs);

                // Mux audio into final MP4.
                // Do NOT use -shortest here: the video duration is already computed to
                // match the narration audio length (narration-driven duration), so both
                // streams should end at approximately the same time.
                // Use -c:a aac to encode the audio and map both streams explicitly.
                var muxArgs =
                    $"-i \"{silentPath}\" " +
                    $"-i \"{audioPath}\" " +
                    $"-c:v copy " +
                    $"-c:a aac -b:a 128k " +
                    $"-movflags +faststart " +
                    $"-y \"{outputPath}\"";

                RunFfmpegProcess(ffmpegExe, muxArgs);
                _logger.LogInformation("Audio muxed into video successfully");
            }
            finally
            {
                if (File.Exists(silentPath)) File.Delete(silentPath);
            }
        }
        else
        {
            if (audioPath is not null)
                _logger.LogWarning("Audio file not found at {Path}; producing silent video", audioPath);

            var args =
                $"{string.Join(" ", inputArgs)} " +
                $"-filter_complex_script \"{filterFile}\" " +
                $"-map \"[outv]\" " +
                $"-c:v libx264 -pix_fmt yuv420p -crf 23 -preset fast " +
                $"-r {fps} -t {durationSeconds} " +
                $"-movflags +faststart " +
                $"-y \"{outputPath}\"";

            RunFfmpegProcess(ffmpegExe, args);
        }
    }

    public static int CalculateCycleCount(
        int durationSeconds, int secondsPerSlide, int slideCount, double transitionDurationSeconds = 0.6)
    {
        // With xfade, total output duration for N total slides is:
        // N * secondsPerSlide - (N - 1) * transitionDuration
        // We need this >= durationSeconds, so:
        // N * (secondsPerSlide - transitionDuration) >= durationSeconds - transitionDuration
        double effectivePerSlide = secondsPerSlide - transitionDurationSeconds;
        if (effectivePerSlide <= 0) effectivePerSlide = secondsPerSlide;
        double totalSlidesNeeded = (durationSeconds - transitionDurationSeconds) / effectivePerSlide;
        int cyclesNeeded = (int)Math.Ceiling(totalSlidesNeeded / slideCount);
        return Math.Max(cyclesNeeded, 1);
    }

    internal static string BuildFilterGraph(
        int totalSlides,
        int fps,
        int secondsPerSlide,
        int durationSeconds,
        int width,
        int height,
        int driftPixels,
        int transitionMs,
        TransitionStyle transition)
    {
        var sb = new System.Text.StringBuilder();

        if (driftPixels > 0)
        {
            // Vertical drift: scale to extra height then animate a vertical crop upward.
            int scaledHeight = height + driftPixels * 2;
            for (int i = 0; i < totalSlides; i++)
            {
                sb.AppendLine(
                    $"[{i}:v]" +
                    $"scale={width}:{scaledHeight}:force_original_aspect_ratio=increase," +
                    $"crop={width}:{scaledHeight}," +
                    $"crop={width}:{height}:0:'min(t/{secondsPerSlide}*{driftPixels},{driftPixels})'," +
                    $"setpts=PTS-STARTPTS" +
                    $"[v{i}];");
            }
        }
        else
        {
            // No vertical motion: simple cover-fit scale and center crop.
            for (int i = 0; i < totalSlides; i++)
            {
                sb.AppendLine(
                    $"[{i}:v]" +
                    $"scale={width}:{height}:force_original_aspect_ratio=increase," +
                    $"crop={width}:{height}," +
                    $"setpts=PTS-STARTPTS" +
                    $"[v{i}];");
            }
        }

        if (totalSlides == 1)
        {
            sb.AppendLine($"[v0]trim=duration={durationSeconds},setpts=PTS-STARTPTS[outv]");
            return sb.ToString();
        }

        double segDuration = secondsPerSlide;
        double transitionDuration = transitionMs / 1000.0;
        string transitionType = transition == TransitionStyle.Crossfade ? "fade" : "slideleft";

        string prev = "v0";
        for (int i = 1; i < totalSlides; i++)
        {
            double offset = i * (segDuration - transitionDuration);
            if (offset < MinXfadeOffsetSeconds) offset = MinXfadeOffsetSeconds;

            bool isLast = (i == totalSlides - 1);
            string outLabel = isLast ? "outv_raw" : $"tmp{i}";

            sb.AppendLine(
                $"[{prev}][v{i}]xfade=transition={transitionType}:" +
                $"duration={transitionDuration:F3}:offset={offset:F3}[{outLabel}];");
            prev = outLabel;
        }

        sb.AppendLine($"[outv_raw]trim=duration={durationSeconds},setpts=PTS-STARTPTS[outv]");
        return sb.ToString();
    }

    /// <summary>
    /// Builds an FFmpeg filter-graph for narration-driven (chunked) composition.
    ///
    /// Each chunk slide has its own display duration derived from the reveal timeline.
    /// Non-last inputs are inflated by <paramref name="transitionMs"/> so the output
    /// after xfade chaining equals the sum of <paramref name="displayDurSec"/>.
    ///
    /// Transition is always <c>slideleft</c> (horizontal scroll).
    /// Offsets are the cumulative sum of display durations, giving precise per-chunk timing.
    /// </summary>
    internal static string BuildFilterGraphChunked(
        int totalSlides,
        int fps,
        int durationSeconds,
        int width,
        int height,
        int driftPixels,
        int transitionMs,
        IReadOnlyList<double> displayDurSec)
    {
        var sb = new System.Text.StringBuilder();
        double transitionSec = transitionMs / 1000.0;

        if (driftPixels > 0)
        {
            // Vertical drift: scale to extra height then animate a vertical crop upward.
            int scaledHeight = height + driftPixels * 2;
            for (int i = 0; i < totalSlides; i++)
            {
                bool isLast = i == totalSlides - 1;
                double inputSec = isLast ? displayDurSec[i] : displayDurSec[i] + transitionSec;

                sb.AppendLine(
                    $"[{i}:v]" +
                    $"scale={width}:{scaledHeight}:force_original_aspect_ratio=increase," +
                    $"crop={width}:{scaledHeight}," +
                    $"crop={width}:{height}:0:'min(t/{inputSec:F3}*{driftPixels},{driftPixels})'," +
                    $"setpts=PTS-STARTPTS" +
                    $"[v{i}];");
            }
        }
        else
        {
            // No vertical motion: simple cover-fit scale and center crop.
            for (int i = 0; i < totalSlides; i++)
            {
                sb.AppendLine(
                    $"[{i}:v]" +
                    $"scale={width}:{height}:force_original_aspect_ratio=increase," +
                    $"crop={width}:{height}," +
                    $"setpts=PTS-STARTPTS" +
                    $"[v{i}];");
            }
        }

        if (totalSlides == 1)
        {
            sb.AppendLine($"[v0]trim=duration={durationSeconds},setpts=PTS-STARTPTS[outv]");
            return sb.ToString();
        }

        // xfade offsets: for inflated inputs (D[i] = displayDurSec[i] + transitionSec for
        // non-last slides), the chained output length after k xfades is sum(d[0..k]) + t.
        // Each new xfade starts when the previous output ends (minus one transition length):
        //   offset[i] = (sum(d[0..i-1]) + t) - t = sum(d[0..i-1])
        // So the offset is simply the cumulative sum of display durations — matching the comment.
        double cumulative = 0.0;
        string prev = "v0";
        for (int i = 1; i < totalSlides; i++)
        {
            cumulative += displayDurSec[i - 1];
            double offset = Math.Max(cumulative, MinXfadeOffsetSeconds);

            bool isLast = i == totalSlides - 1;
            string outLabel = isLast ? "outv_raw" : $"tmp{i}";

            sb.AppendLine(
                $"[{prev}][v{i}]xfade=transition=slideleft:" +
                $"duration={transitionSec:F3}:offset={offset:F3}[{outLabel}];");
            prev = outLabel;
        }

        sb.AppendLine($"[outv_raw]trim=duration={durationSeconds},setpts=PTS-STARTPTS[outv]");
        return sb.ToString();
    }

    private void RunFfmpegProcess(string ffmpegPath, string args)
    {
        _logger.LogDebug("Executing: \"{FFmpeg}\" {Args}", ffmpegPath, args);

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
                $"Failed to start FFmpeg process. Executable: {ffmpegPath}");

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg exited with code {Code}. Stderr:\n{Stderr}",
                process.ExitCode, stderr);
            throw new InvalidOperationException(
                $"FFmpeg failed with exit code {process.ExitCode}. " +
                "Check the log output above for details.");
        }

        _logger.LogDebug("FFmpeg completed successfully");
    }
}

