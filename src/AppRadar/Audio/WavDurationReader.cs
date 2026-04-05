namespace AppRadar.Audio;

/// <summary>
/// Reads the duration of a PCM WAV file from its header without loading the full file.
///
/// WAV files have a standard RIFF header containing a "fmt " chunk that provides
/// the sample rate, channel count, and bit depth.  The "data" chunk size divided by
/// the byte rate gives the duration in seconds.
/// </summary>
public static class WavDurationReader
{
    /// <summary>
    /// Returns the duration of a PCM WAV file in milliseconds.
    /// Returns 0 when the file does not exist, cannot be read, or is not a valid PCM WAV.
    /// </summary>
    public static int ReadDurationMs(string wavPath)
    {
        try
        {
            using var fs = File.OpenRead(wavPath);
            using var br = new BinaryReader(fs);

            // RIFF header: "RIFF" (4) + file size (4) + "WAVE" (4)
            var riff = br.ReadBytes(4);
            if (riff[0] != 'R' || riff[1] != 'I' || riff[2] != 'F' || riff[3] != 'F')
                return 0;

            br.ReadInt32(); // file size — skip
            var wave = br.ReadBytes(4);
            if (wave[0] != 'W' || wave[1] != 'A' || wave[2] != 'V' || wave[3] != 'E')
                return 0;

            int sampleRate = 0;
            int byteRate = 0;
            long dataSizeBytes = 0;

            // Walk chunks until we find "fmt " and "data"
            while (fs.Position < fs.Length - 8)
            {
                var chunkId = new string(br.ReadChars(4));
                int chunkSize = br.ReadInt32();

                if (chunkId == "fmt ")
                {
                    // fmt chunk: AudioFormat(2) ChannelCount(2) SampleRate(4) ByteRate(4) BlockAlign(2) BitsPerSample(2)
                    if (chunkSize < 16) { fs.Seek(chunkSize, SeekOrigin.Current); continue; }
                    br.ReadInt16(); // AudioFormat (1 = PCM)
                    br.ReadInt16(); // ChannelCount
                    sampleRate = br.ReadInt32();
                    byteRate = br.ReadInt32();
                    br.ReadInt16(); // BlockAlign
                    br.ReadInt16(); // BitsPerSample
                    // Skip any extra bytes in fmt chunk
                    int extra = chunkSize - 16;
                    if (extra > 0) fs.Seek(extra, SeekOrigin.Current);
                }
                else if (chunkId == "data")
                {
                    dataSizeBytes = (uint)chunkSize; // treat as unsigned to handle large files
                    break;
                }
                else
                {
                    // Skip unknown chunk
                    fs.Seek(chunkSize, SeekOrigin.Current);
                }
            }

            if (byteRate <= 0 || dataSizeBytes <= 0)
                return 0;

            double durationSeconds = (double)dataSizeBytes / byteRate;
            return (int)Math.Round(durationSeconds * 1000.0);
        }
        catch
        {
            return 0;
        }
    }
}
