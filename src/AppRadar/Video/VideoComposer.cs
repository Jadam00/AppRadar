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

    public string ComposeVideo(
        List<string> slidePaths,
        string outputVideosDir,
        string generationId,
        int durationSeconds,
        TransitionStyle transition,
        AppConfig config)
    {
        _logger.LogInformation("Composing video for reel {Id} with transition: {Transition}",
            generationId, transition);

        var ffmpegExe = ResolveFfmpegPath(config);
        _logger.LogDebug("Using FFmpeg executable: {Path}", ffmpegExe);

        var outputPath = Path.Combine(outputVideosDir, $"{generationId}.mp4");

        int fps = config.Video.Fps;
        int secondsPerSlide = config.Video.SecondsPerSlide;
        int width = config.Video.Width;
        int height = config.Video.Height;
        int driftPixels = config.Animation.VerticalDriftPixels;
        int transitionMs = config.Animation.TransitionDurationMs;

        int cycleCount = CalculateCycleCount(durationSeconds, secondsPerSlide, slidePaths.Count,
            transitionMs / 1000.0);
        int totalSlides = slidePaths.Count * cycleCount;

        var inputArgs = new List<string>();
        for (int cycle = 0; cycle < cycleCount; cycle++)
        {
            foreach (var slidePath in slidePaths)
            {
                inputArgs.Add($"-loop 1 -t {secondsPerSlide} -i \"{slidePath}\"");
            }
        }

        var filterScript = BuildFilterGraph(
            totalSlides, fps, secondsPerSlide, durationSeconds,
            width, height, driftPixels, transitionMs, transition);

        var filterFile = Path.GetTempFileName();
        try
        {
            File.WriteAllText(filterFile, filterScript);
            _logger.LogDebug("FFmpeg filter graph written to: {File}", filterFile);

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
        finally
        {
            if (File.Exists(filterFile)) File.Delete(filterFile);
        }

        _logger.LogInformation("Video saved to: {Path}", outputPath);
        return outputPath;
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

    private static string BuildFilterGraph(
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
            if (offset < 0.001) offset = 0.001;

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

