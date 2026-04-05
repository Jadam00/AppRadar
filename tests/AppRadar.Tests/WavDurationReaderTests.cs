using AppRadar.Audio;
using Xunit;

namespace AppRadar.Tests;

public sealed class WavDurationReaderTests
{
    // ── Helper to build a minimal valid PCM WAV byte array ────────────────────────────────────

    private static byte[] BuildWav(int sampleRate = 16000, int channels = 1, int bitsPerSample = 16, int durationMs = 1000)
    {
        int byteRate = sampleRate * channels * (bitsPerSample / 8);
        int dataSize = (int)(byteRate * (durationMs / 1000.0));

        using var ms = new MemoryStream();
        using var bw = new BinaryWriter(ms);

        // RIFF header
        bw.Write("RIFF"u8.ToArray());
        bw.Write(36 + dataSize);   // file size - 8
        bw.Write("WAVE"u8.ToArray());

        // fmt chunk
        bw.Write("fmt "u8.ToArray());
        bw.Write(16);              // chunk size
        bw.Write((short)1);        // PCM
        bw.Write((short)channels);
        bw.Write(sampleRate);
        bw.Write(byteRate);
        bw.Write((short)(channels * bitsPerSample / 8));   // blockAlign
        bw.Write((short)bitsPerSample);

        // data chunk
        bw.Write("data"u8.ToArray());
        bw.Write(dataSize);
        bw.Write(new byte[dataSize]);

        return ms.ToArray();
    }

    // ── Read from temp file ───────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData(1000)]
    [InlineData(3500)]
    [InlineData(500)]
    public void ReadDurationMs_ReturnsMeasuredDuration(int durationMs)
    {
        var wavBytes = BuildWav(durationMs: durationMs);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, wavBytes);
            int measured = WavDurationReader.ReadDurationMs(path);

            // Allow ±50ms tolerance for rounding
            Assert.InRange(measured, durationMs - 50, durationMs + 50);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadDurationMs_ReturnsZero_ForNonExistentFile()
    {
        var result = WavDurationReader.ReadDurationMs("/nonexistent/file.wav");
        Assert.Equal(0, result);
    }

    [Fact]
    public void ReadDurationMs_ReturnsZero_ForInvalidWavContent()
    {
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, [0, 1, 2, 3, 4, 5]);
            var result = WavDurationReader.ReadDurationMs(path);
            Assert.Equal(0, result);
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void ReadDurationMs_Works_WithStereoWav()
    {
        var wavBytes = BuildWav(sampleRate: 44100, channels: 2, bitsPerSample: 16, durationMs: 2000);
        var path = Path.GetTempFileName();
        try
        {
            File.WriteAllBytes(path, wavBytes);
            int measured = WavDurationReader.ReadDurationMs(path);
            Assert.InRange(measured, 1950, 2050);
        }
        finally
        {
            File.Delete(path);
        }
    }
}
