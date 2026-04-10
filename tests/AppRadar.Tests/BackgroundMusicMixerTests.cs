using AppRadar.Audio;
using AppRadar.Config;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppRadar.Tests;

public sealed class BackgroundMusicMixerTests
{
    [Fact]
    public void AudioConfig_BackgroundMusicDefaults_AreExpected()
    {
        var config = new AudioConfig();

        Assert.True(config.BackgroundMusic.Enabled);
        Assert.Equal("backgroundMusic", config.BackgroundMusic.FolderPath);
        Assert.Equal("random", config.BackgroundMusic.SelectionStrategy);
        Assert.Equal(0.16, config.BackgroundMusic.VolumeMultiplier, 3);
    }

    [Fact]
    public void GetEligibleTracks_IncludesOnlyWavFiles()
    {
        var mixer = new BackgroundMusicMixer(NullLogger<BackgroundMusicMixer>.Instance, new Random(123));
        var tempDir = Path.Combine(Path.GetTempPath(), $"AppRadar_BgmTests_{Guid.NewGuid():N}");
        Directory.CreateDirectory(tempDir);

        try
        {
            var wav = Path.Combine(tempDir, "a.wav");
            var m4a = Path.Combine(tempDir, "b.m4a");
            var txt = Path.Combine(tempDir, "c.txt");
            File.WriteAllText(wav, "x");
            File.WriteAllText(m4a, "x");
            File.WriteAllText(txt, "x");

            var tracks = mixer.GetEligibleTracks(tempDir);

            Assert.Single(tracks);
            Assert.Equal(wav, tracks[0]);
        }
        finally
        {
            if (Directory.Exists(tempDir))
                Directory.Delete(tempDir, recursive: true);
        }
    }

    [Fact]
    public void SelectTrack_Cycle_ReturnsFirstTrack()
    {
        var mixer = new BackgroundMusicMixer(NullLogger<BackgroundMusicMixer>.Instance, new Random(1));
        var tracks = new List<string> { "first.wav", "second.wav" };

        var selected = mixer.SelectTrack(tracks, "cycle");

        Assert.Equal("first.wav", selected);
    }
}
