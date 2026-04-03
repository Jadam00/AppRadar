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

    public void ValidateFfmpegAvailable()
    {
        var path = FindFfmpeg();
        if (path is null)
        {
            throw new InvalidOperationException(
                "FFmpeg not found on PATH or at standard locations. " +
                "Please install FFmpeg: https://ffmpeg.org/download.html");
        }
        _logger.LogInformation("FFmpeg found at: {Path}", path);
    }

    private static string? FindFfmpeg()
    {
        var direct = new[] { "/usr/bin/ffmpeg", "/usr/local/bin/ffmpeg" };
        foreach (var p in direct)
        {
            if (File.Exists(p)) return p;
        }

        var pathVar = Environment.GetEnvironmentVariable("PATH") ?? string.Empty;
        foreach (var dir in pathVar.Split(Path.PathSeparator))
        {
            var full = Path.Combine(dir, "ffmpeg");
            if (File.Exists(full)) return full;
        }

        try
        {
            var psi = new System.Diagnostics.ProcessStartInfo
            {
                FileName = "which",
                Arguments = "ffmpeg",
                RedirectStandardOutput = true,
                UseShellExecute = false
            };
            using var p = System.Diagnostics.Process.Start(psi);
            var found = p?.StandardOutput.ReadLine()?.Trim();
            if (!string.IsNullOrEmpty(found) && File.Exists(found)) return found;
        }
        catch { }

        return null;
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
            _logger.LogDebug("FFmpeg filter graph written to {File}", filterFile);

            var args =
                $"{string.Join(" ", inputArgs)} " +
                $"-filter_complex_script \"{filterFile}\" " +
                $"-map \"[outv]\" " +
                $"-c:v libx264 -pix_fmt yuv420p -crf 23 -preset fast " +
                $"-r {fps} -t {durationSeconds} " +
                $"-movflags +faststart " +
                $"-y \"{outputPath}\"";

            RunFfmpegProcess(args);
        }
        finally
        {
            if (File.Exists(filterFile)) File.Delete(filterFile);
        }

        _logger.LogInformation("Video saved to {Path}", outputPath);
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

    private void RunFfmpegProcess(string args)
    {
        _logger.LogDebug("Executing: ffmpeg {Args}", args);

        var psi = new System.Diagnostics.ProcessStartInfo
        {
            FileName = "ffmpeg",
            Arguments = args,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true
        };

        using var process = System.Diagnostics.Process.Start(psi)
            ?? throw new InvalidOperationException("Failed to start ffmpeg process");

        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            _logger.LogError("FFmpeg stderr:\n{Stderr}", stderr);
            throw new InvalidOperationException(
                $"FFmpeg failed with exit code {process.ExitCode}. See log output for details.");
        }

        _logger.LogDebug("FFmpeg completed successfully");
    }
}
