using AppRadar.Audio;
using AppRadar.Config;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AppRadar.Tests;

// ── TtsProviderFactory tests ──────────────────────────────────────────────────────────────────

public sealed class TtsProviderFactoryTests
{
    private sealed class CapturingLoggerProvider : ILoggerProvider
    {
        public List<string> WarningMessages { get; } = [];

        public ILogger CreateLogger(string categoryName) => new CapturingLogger(WarningMessages);
        public void Dispose() { }

        private sealed class CapturingLogger(List<string> warningMessages) : ILogger
        {
            private readonly List<string> _warningMessages = warningMessages;

            public IDisposable BeginScope<TState>(TState state) where TState : notnull => NullScope.Instance;
            public bool IsEnabled(LogLevel logLevel) => true;

            public void Log<TState>(
                LogLevel logLevel,
                EventId eventId,
                TState state,
                Exception? exception,
                Func<TState, Exception?, string> formatter)
            {
                if (logLevel == LogLevel.Warning)
                    _warningMessages.Add(formatter(state, exception));
            }
        }

        private sealed class NullScope : IDisposable
        {
            public static readonly NullScope Instance = new();
            public void Dispose() { }
        }
    }

    [Fact]
    public void Create_WithPiperProvider_ReturnsPiperTtsProvider()
    {
        var config = new AudioConfig { TtsProvider = "Piper" };
        var provider = TtsProviderFactory.Create(config, NullLoggerFactory.Instance);
        Assert.IsType<PiperTtsProvider>(provider);
    }

    [Fact]
    public void Create_WithPiperProviderCaseInsensitive_ReturnsPiperTtsProvider()
    {
        var config = new AudioConfig { TtsProvider = "piper" };
        var provider = TtsProviderFactory.Create(config, NullLoggerFactory.Instance);
        Assert.IsType<PiperTtsProvider>(provider);
    }

    [Fact]
    public void Create_WithPiperAndSystemSpeechOnlyOptions_LogsWarnings()
    {
        var loggerProvider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider));

        var config = new AudioConfig
        {
            TtsProvider = "Piper",
            VoiceName = "Microsoft Zira Desktop",
            Rate = -3,
            Volume = 80
        };

        var provider = TtsProviderFactory.Create(config, loggerFactory);

        Assert.IsType<PiperTtsProvider>(provider);
        Assert.Contains(loggerProvider.WarningMessages,
            m => m.Contains("audio.voiceName is ignored", StringComparison.Ordinal));
        Assert.Contains(loggerProvider.WarningMessages,
            m => m.Contains("audio.rate is ignored", StringComparison.Ordinal));
        Assert.Contains(loggerProvider.WarningMessages,
            m => m.Contains("audio.volume is ignored", StringComparison.Ordinal));
    }

    [Fact]
    public void Create_WithPiperAndDefaultSystemSpeechOptions_DoesNotLogWarnings()
    {
        var loggerProvider = new CapturingLoggerProvider();
        using var loggerFactory = LoggerFactory.Create(b => b.AddProvider(loggerProvider));

        var config = new AudioConfig
        {
            TtsProvider = "Piper",
            VoiceName = null,
            Rate = 0,
            Volume = 100
        };

        var provider = TtsProviderFactory.Create(config, loggerFactory);

        Assert.IsType<PiperTtsProvider>(provider);
        Assert.Empty(loggerProvider.WarningMessages);
    }

    [Fact]
    public void Create_WithSystemSpeechProvider_ReturnsSystemSpeechTtsProvider()
    {
        if (!OperatingSystem.IsWindows()) return; // SystemSpeech is Windows-only

        var config = new AudioConfig { TtsProvider = "SystemSpeech" };
        var provider = TtsProviderFactory.Create(config, NullLoggerFactory.Instance);
        Assert.IsType<SystemSpeechTtsProvider>(provider);
    }

    [Fact]
    public void Create_WithEmptyProvider_FallsBackToSystemSpeech()
    {
        if (!OperatingSystem.IsWindows()) return; // SystemSpeech fallback is Windows-only

        var config = new AudioConfig { TtsProvider = string.Empty };
        var provider = TtsProviderFactory.Create(config, NullLoggerFactory.Instance);
        Assert.IsType<SystemSpeechTtsProvider>(provider);
    }

    [Fact]
    public void Create_WithNullProvider_FallsBackToSystemSpeech()
    {
        if (!OperatingSystem.IsWindows()) return; // SystemSpeech fallback is Windows-only

        var config = new AudioConfig { TtsProvider = null! };
        var provider = TtsProviderFactory.Create(config, NullLoggerFactory.Instance);
        Assert.IsType<SystemSpeechTtsProvider>(provider);
    }

    [Fact]
    public void Create_WithUnknownProvider_ThrowsNotSupportedException()
    {
        var config = new AudioConfig { TtsProvider = "GoogleCloud" };
        Assert.Throws<NotSupportedException>(
            () => TtsProviderFactory.Create(config, NullLoggerFactory.Instance));
    }

    [Fact]
    public void Create_ErrorMessage_ListsBothProviders()
    {
        var config = new AudioConfig { TtsProvider = "Unknown" };
        var ex = Assert.Throws<NotSupportedException>(
            () => TtsProviderFactory.Create(config, NullLoggerFactory.Instance));
        Assert.Contains("SystemSpeech", ex.Message);
        Assert.Contains("Piper", ex.Message);
    }
}

// ── PiperTtsProvider tests ────────────────────────────────────────────────────────────────────

public sealed class PiperTtsProviderTests
{
    private static NullLogger<PiperTtsProvider> Logger =>
        NullLogger<PiperTtsProvider>.Instance;

    private static Func<string, string, string, int, (int, string)> MockRunner(
        int exitCode, string stderr = "") =>
        (_, _, _, _) => (exitCode, stderr);

    // ── IsAvailable ───────────────────────────────────────────────────────────────────────────

    [Fact]
    public void IsAvailable_WhenExePathIsEmpty_ReturnsFalse()
    {
        var config = new PiperConfig { ExePath = string.Empty, ModelPath = "model.onnx" };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void IsAvailable_WhenModelPathIsEmpty_ReturnsFalse()
    {
        var config = new PiperConfig { ExePath = "piper.exe", ModelPath = string.Empty };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        Assert.False(provider.IsAvailable);
    }

    [Fact]
    public void IsAvailable_WhenExeFileDoesNotExist_ReturnsFalse()
    {
        var config = new PiperConfig
        {
            ExePath  = @"C:\does-not-exist\piper.exe",
            ModelPath = @"C:\does-not-exist\model.onnx"
        };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        Assert.False(provider.IsAvailable);
    }

    // ── Config validation ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void GenerateNarration_WhenExePathIsEmpty_ThrowsInvalidOperationException()
    {
        var config = new PiperConfig { ExePath = string.Empty, ModelPath = "model.onnx" };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            provider.GenerateNarration([], "out.wav", new AudioConfig(), "ffmpeg.exe"));

        Assert.Contains("exePath", ex.Message);
    }

    [Fact]
    public void GenerateNarration_WhenModelPathIsEmpty_ThrowsInvalidOperationException()
    {
        var config = new PiperConfig { ExePath = "piper.exe", ModelPath = string.Empty };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));

        var ex = Assert.Throws<InvalidOperationException>(() =>
            provider.GenerateNarration([], "out.wav", new AudioConfig(), "ffmpeg.exe"));

        Assert.Contains("modelPath", ex.Message);
    }

    [Fact]
    public void GenerateNarration_WhenExeFileNotFound_ThrowsFileNotFoundException()
    {
        var config = new PiperConfig
        {
            ExePath   = @"C:\does-not-exist\piper.exe",
            ModelPath = @"C:\does-not-exist\model.onnx"
        };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));

        Assert.Throws<FileNotFoundException>(() =>
            provider.GenerateNarration([], "out.wav", new AudioConfig(), "ffmpeg.exe"));
    }

    [Fact]
    public void GenerateNarration_WhenPiperFails_ThrowsInvalidOperationException()
    {
        // Use real files for path validation, then simulate a piper failure
        var exePath    = CreateTempFile();
        var modelPath  = CreateTempFile();
        var modelJson  = modelPath + ".json";
        File.WriteAllText(modelJson, "{}"); // create required .onnx.json sidecar

        try
        {
            var config = new PiperConfig { ExePath = exePath, ModelPath = modelPath };
            var provider = new PiperTtsProvider(
                config, Logger,
                MockRunner(exitCode: 1, stderr: "Piper error: bad model"));

            var segments = new List<NarrationSegment>
            {
                new() { Text = "Hello world", TargetDurationSeconds = 3.0 }
            };

            var ex = Assert.Throws<InvalidOperationException>(() =>
                provider.GenerateNarration(segments, "out.wav", new AudioConfig(), "ffmpeg.exe"));

            Assert.Contains("exit code 1", ex.Message);
            Assert.Contains("Piper error: bad model", ex.Message);
        }
        finally
        {
            TryDelete(exePath);
            TryDelete(modelPath);
            TryDelete(modelJson);
        }
    }

    // ── Argument building ─────────────────────────────────────────────────────────────────────

    [Fact]
    public void BuildArguments_ContainsModelPath()
    {
        var config = new PiperConfig
        {
            ExePath   = "piper.exe",
            ModelPath = @"C:\models\en.onnx"
        };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        var args = provider.BuildArguments(@"C:\out\seg.wav");

        Assert.Contains(@"--model ""C:\models\en.onnx""", args);
    }

    [Fact]
    public void BuildArguments_ContainsOutputFile()
    {
        var config = new PiperConfig
        {
            ExePath   = "piper.exe",
            ModelPath = @"C:\models\en.onnx"
        };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        var args = provider.BuildArguments(@"C:\out\seg.wav");

        Assert.Contains(@"--output_file ""C:\out\seg.wav""", args);
    }

    [Fact]
    public void BuildArguments_DoesNotContainTextArg()
    {
        var config = new PiperConfig
        {
            ExePath   = "piper.exe",
            ModelPath = @"C:\models\en.onnx"
        };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        var args = provider.BuildArguments(@"C:\out\seg.wav");

        Assert.DoesNotContain("--text", args);
    }

    [Fact]
    public void BuildArguments_ContainsLengthScaleNoiseScaleNoiseW()
    {
        var config = new PiperConfig
        {
            ExePath    = "piper.exe",
            ModelPath  = @"C:\models\en.onnx",
            LengthScale = 1.2,
            NoiseScale  = 0.5,
            NoiseW      = 0.9
        };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        var args = provider.BuildArguments(@"C:\out\seg.wav");

        Assert.Contains("--length_scale 1.200", args);
        Assert.Contains("--noise_scale 0.500", args);
        Assert.Contains("--noise_w 0.900", args);
    }

    [Fact]
    public void BuildArguments_WhenSpeakerSet_IncludesSpeakerFlag()
    {
        var config = new PiperConfig
        {
            ExePath   = "piper.exe",
            ModelPath = @"C:\models\en.onnx",
            Speaker   = 2
        };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        var args = provider.BuildArguments(@"C:\out\seg.wav");

        Assert.Contains("--speaker 2", args);
    }

    [Fact]
    public void BuildArguments_WhenSpeakerNotSet_DoesNotIncludeSpeakerFlag()
    {
        var config = new PiperConfig
        {
            ExePath   = "piper.exe",
            ModelPath = @"C:\models\en.onnx",
            Speaker   = null
        };
        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));
        var args = provider.BuildArguments(@"C:\out\seg.wav");

        Assert.DoesNotContain("--speaker", args);
    }

    [Fact]
    public void ValidateText_WhenTextExceedsMaxLength_ThrowsArgumentException()
    {
        var longText = new string('x', PiperTtsProvider.MaxTextLengthChars + 1);

        Assert.Throws<ArgumentException>(() =>
            PiperTtsProvider.ValidateText(longText));
    }

    // ── Text sanitisation ─────────────────────────────────────────────────────────────────────

    [Theory]
    [InlineData("Hello world",      "Hello world")]
    [InlineData("  spaced  text  ", "spaced text")]
    [InlineData("line1\nline2",     "line1 line2")]
    [InlineData("line1\r\nline2",   "line1 line2")]
    [InlineData("tab\there",        "tab here")]
    [InlineData("say \"hello\"",    "say \"hello\"")]
    [InlineData("",                 "")]
    [InlineData("   ",              "")]
    public void SanitiseText_NormalisesInputCorrectly(string input, string expected)
    {
        Assert.Equal(expected, PiperTtsProvider.SanitiseText(input));
    }

    [Theory]
    [InlineData("Hello [[pause]] world", "Hello. world")]
    [InlineData("Hello [[pause=150]] world", "Hello, world")]
    [InlineData("Hello [[pause=750]] world", "Hello... world")]
    [InlineData("Hello [[pause=1500]] world", "Hello.... world")]
    [InlineData("Hello [[pause=30]] world", "Hello, world")]
    public void ExpandPauseMarkers_MapsPauseDurationsToPacingPunctuation(string input, string expected)
    {
        var result = PiperTtsProvider.ExpandPauseMarkers(
            input,
            defaultPauseMs: 320,
            minPauseMs: 120,
            maxPauseMs: 1200);

        Assert.Equal(expected, PiperTtsProvider.SanitiseText(result));
    }

    [Fact]
    public void SanitiseTextForSynthesis_WhenPauseMarkersDisabled_DoesNotExpandMarkers()
    {
        var config = new PiperConfig
        {
            ExePath = "piper.exe",
            ModelPath = "model.onnx",
            EnablePauseMarkers = false
        };

        var provider = new PiperTtsProvider(config, Logger, MockRunner(0));

        var result = provider.SanitiseTextForSynthesis("Hook [[pause=400]] CTA");

        Assert.Equal("Hook [[pause=400]] CTA", result);
    }

    // ── Helpers ───────────────────────────────────────────────────────────────────────────────

    private static string CreateTempFile()
    {
        var path = Path.GetTempFileName();
        return path;
    }

    private static void TryDelete(string path)
    {
        try { File.Delete(path); } catch { }
    }
}
