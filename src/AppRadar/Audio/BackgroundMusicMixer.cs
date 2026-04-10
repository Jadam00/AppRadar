using System.Diagnostics;
using System.Globalization;
using AppRadar.Config;
using Microsoft.Extensions.Logging;

namespace AppRadar.Audio;

/// <summary>
/// Selects a background music track and mixes it with narration.
/// </summary>
public sealed class BackgroundMusicMixer
{
    private readonly ILogger<BackgroundMusicMixer> _logger;
    private readonly Random _random;

    public BackgroundMusicMixer(ILogger<BackgroundMusicMixer> logger, Random? random = null)
    {
        _logger = logger;
        _random = random ?? Random.Shared;
    }

    public string? TryMixNarrationWithBackground(
        string narrationPath,
        AudioConfig audioConfig,
        string ffmpegExe,
        string outputAudioDir,
        string generationId)
    {
        var bgmConfig = audioConfig.BackgroundMusic;
        if (!bgmConfig.Enabled)
            return null;

        if (!File.Exists(narrationPath))
        {
            _logger.LogWarning("Narration file not found for BGM mix: {Path}", narrationPath);
            return null;
        }

        var narrationMs = WavDurationReader.ReadDurationMs(narrationPath);
        if (narrationMs <= 0)
        {
            _logger.LogWarning("Unable to determine narration duration; skipping BGM mix");
            return null;
        }

        var musicDirectory = ResolveMusicDirectory(bgmConfig.FolderPath);
        var tracks = GetEligibleTracks(musicDirectory);
        if (tracks.Count == 0)
        {
            _logger.LogWarning("No .wav files found in background music folder: {Dir}", musicDirectory);
            return null;
        }

        var selectedTrack = SelectTrack(tracks, bgmConfig.SelectionStrategy);
        if (selectedTrack is null)
            return null;

        Directory.CreateDirectory(outputAudioDir);
        var mixedPath = Path.Combine(outputAudioDir, $"{generationId}_mixed.wav");

        var durationSeconds = narrationMs / 1000.0;
        var durationArg = durationSeconds.ToString("0.###", CultureInfo.InvariantCulture);
        var volumeArg = bgmConfig.VolumeMultiplier.ToString("0.###", CultureInfo.InvariantCulture);

        var args =
            $"-i \"{narrationPath}\" " +
            $"-stream_loop -1 -t {durationArg} -i \"{selectedTrack}\" " +
            $"-filter_complex \"[0:a]aformat=sample_fmts=fltp:channel_layouts=mono[nar];" +
            $"[1:a]aformat=sample_fmts=fltp:channel_layouts=mono,volume={volumeArg}[bg];" +
            $"[nar][bg]amix=inputs=2:duration=first:dropout_transition=0[mix]\" " +
            $"-map \"[mix]\" -ac 1 -ar 22050 -c:a pcm_s16le -y \"{mixedPath}\"";

        _logger.LogInformation("Mixing narration with BGM track: {Track}", Path.GetFileName(selectedTrack));

        if (!RunFfmpegProcess(ffmpegExe, args))
            return null;

        return mixedPath;
    }

    internal string ResolveMusicDirectory(string configuredPath)
    {
        if (string.IsNullOrWhiteSpace(configuredPath))
            configuredPath = "backgroundMusic";

        return Path.IsPathRooted(configuredPath)
            ? configuredPath
            : Path.GetFullPath(Path.Combine(Directory.GetCurrentDirectory(), configuredPath));
    }

    internal IReadOnlyList<string> GetEligibleTracks(string musicDirectory)
    {
        if (!Directory.Exists(musicDirectory))
            return [];

        return Directory.EnumerateFiles(musicDirectory, "*.wav", SearchOption.TopDirectoryOnly)
            .OrderBy(path => path, StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    internal string? SelectTrack(IReadOnlyList<string> tracks, string? selectionStrategy)
    {
        if (tracks.Count == 0)
            return null;

        var strategy = (selectionStrategy ?? "random").Trim();
        if (strategy.Equals("cycle", StringComparison.OrdinalIgnoreCase))
            return tracks[0];

        var index = _random.Next(tracks.Count);
        return tracks[index];
    }

    private bool RunFfmpegProcess(string ffmpegPath, string args)
    {
        using var process = new Process();
        process.StartInfo = new ProcessStartInfo
        {
            FileName = ffmpegPath,
            Arguments = args,
            RedirectStandardError = true,
            RedirectStandardOutput = true,
            UseShellExecute = false,
            CreateNoWindow = true
        };

        process.Start();
        var stdout = process.StandardOutput.ReadToEnd();
        var stderr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode == 0)
            return true;

        _logger.LogWarning(
            "FFmpeg background mix failed (exit={ExitCode}). stderr={Stderr} stdout={Stdout}",
            process.ExitCode,
            stderr,
            stdout);

        return false;
    }
}
