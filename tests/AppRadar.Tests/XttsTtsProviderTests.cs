using System.Diagnostics;
using System.Net;
using System.Net.Http;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AppRadar.Audio;
using AppRadar.Config;
using Microsoft.Extensions.Logging.Abstractions;

namespace AppRadar.Tests;

public sealed class XttsTtsProviderTests
{
    [Fact]
    public void GenerateNarration_RetriesOnTransientFailure_ThenSucceeds()
    {
        var outputPath = CreateTempFilePath();
        File.WriteAllText(outputPath, "stub");

        try
        {
            var attempts = 0;
            string? capturedBody = null;

            var handler = new StubHttpMessageHandler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri.AbsolutePath.Equals("/tts", StringComparison.OrdinalIgnoreCase))
                {
                    attempts++;
                    capturedBody = request.Content!.ReadAsStringAsync().GetAwaiter().GetResult();

                    if (attempts == 1)
                    {
                        return new HttpResponseMessage(HttpStatusCode.InternalServerError)
                        {
                            Content = new StringContent("{\"error\":\"temporary\"}", Encoding.UTF8, "application/json")
                        };
                    }

                    var payload = JsonSerializer.Serialize(new
                    {
                        success = true,
                        durationSeconds = 1.23,
                        filePath = outputPath
                    });

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var http = new HttpClient(handler);
            var config = new XttsConfig
            {
                BaseUrl = "http://localhost:8020",
                MaxRetries = 2,
                RetryBaseDelayMs = 1,
                TimeoutSeconds = 3,
                StartupWaitSeconds = 1
            };

            var provider = new XttsTtsProvider(
                config,
                http,
                NullLogger<XttsTtsProvider>.Instance,
                _ => Process.GetCurrentProcess(),
                (_, _) => Task.CompletedTask);

            var resultPath = provider.GenerateNarration(
                [new NarrationSegment { Text = "Hello retry world", TargetDurationSeconds = 0 }],
                outputPath,
                new AudioConfig(),
                "ffmpeg.exe");

            Assert.Equal(outputPath, resultPath);
            Assert.Equal(2, attempts);
            Assert.NotNull(capturedBody);
            Assert.Contains("Hello retry world", capturedBody, StringComparison.Ordinal);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    [Fact]
    public void GenerateNarration_DoesNotStartService_WhenHealthRecoversWithinGracePeriod()
    {
        var outputPath = CreateTempFilePath();
        File.WriteAllText(outputPath, "stub");

        var startupScript = CreateTempScriptPath(".cmd");
        File.WriteAllText(startupScript, "@echo off");

        try
        {
            var healthCalls = 0;
            var processStarts = 0;

            var handler = new StubHttpMessageHandler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    healthCalls++;
                    var status = healthCalls == 1 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
                    return new HttpResponseMessage(status)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri.AbsolutePath.Equals("/tts", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        success = true,
                        durationSeconds = 2.0,
                        filePath = outputPath
                    });

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var http = new HttpClient(handler);
            var config = new XttsConfig
            {
                BaseUrl = "http://localhost:8020",
                StartupScriptPath = startupScript,
                TimeoutSeconds = 3,
                StartupWaitSeconds = 2,
                RetryBaseDelayMs = 1
            };

            var provider = new XttsTtsProvider(
                config,
                http,
                NullLogger<XttsTtsProvider>.Instance,
                _ =>
                {
                    processStarts++;
                    return Process.GetCurrentProcess();
                },
                (_, _) => Task.CompletedTask);

            provider.GenerateNarration(
                [new NarrationSegment { Text = "Hello auto start", TargetDurationSeconds = 0 }],
                outputPath,
                new AudioConfig(),
                "ffmpeg.exe");

            Assert.Equal(0, processStarts);
            Assert.True(healthCalls >= 2);
        }
        finally
        {
            TryDelete(outputPath);
            TryDelete(startupScript);
        }
    }

    [Fact]
    public void GenerateNarration_StartsService_WhenHealthStaysUnavailable_AndNoListenerExists()
    {
        var outputPath = CreateTempFilePath();
        File.WriteAllText(outputPath, "stub");

        try
        {
            var healthCalls = 0;
            var processStarts = 0;
            var serviceStarted = false;

            var handler = new StubHttpMessageHandler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    healthCalls++;
                    var status = serviceStarted ? HttpStatusCode.OK : HttpStatusCode.ServiceUnavailable;
                    return new HttpResponseMessage(status)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri.AbsolutePath.Equals("/tts", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        success = true,
                        durationSeconds = 2.0,
                        filePath = outputPath
                    });

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var http = new HttpClient(handler);
            var config = new XttsConfig
            {
                BaseUrl = "http://localhost:8020",
                TimeoutSeconds = 3,
                StartupWaitSeconds = 3,
                RetryBaseDelayMs = 1
            };

            var provider = new XttsTtsProvider(
                config,
                http,
                NullLogger<XttsTtsProvider>.Instance,
                _ =>
                {
                    processStarts++;
                    serviceStarted = true;
                    return Process.GetCurrentProcess();
                },
                (_, _) => Task.CompletedTask);

            provider.GenerateNarration(
                [new NarrationSegment { Text = "Hello auto start", TargetDurationSeconds = 0 }],
                outputPath,
                new AudioConfig(),
                "ffmpeg.exe");

            Assert.Equal(1, processStarts);
            Assert.True(healthCalls >= 2);
        }
        finally
        {
            TryDelete(outputPath);
        }
    }

    [Fact]
    public void GenerateNarration_DoesNotStartService_WhenPortAlreadyListening_AndHealthRecovers()
    {
        var outputPath = CreateTempFilePath();
        File.WriteAllText(outputPath, "stub");

        var listener = new TcpListener(IPAddress.Loopback, 0);
        listener.Start();
        var port = ((IPEndPoint)listener.LocalEndpoint).Port;

        try
        {
            var healthCalls = 0;
            var processStarts = 0;

            var handler = new StubHttpMessageHandler((request, _) =>
            {
                if (request.RequestUri!.AbsolutePath.Equals("/health", StringComparison.OrdinalIgnoreCase))
                {
                    healthCalls++;
                    var status = healthCalls <= 2 ? HttpStatusCode.ServiceUnavailable : HttpStatusCode.OK;
                    return new HttpResponseMessage(status)
                    {
                        Content = new StringContent("{}", Encoding.UTF8, "application/json")
                    };
                }

                if (request.RequestUri.AbsolutePath.Equals("/tts", StringComparison.OrdinalIgnoreCase))
                {
                    var payload = JsonSerializer.Serialize(new
                    {
                        success = true,
                        durationSeconds = 2.0,
                        filePath = outputPath
                    });

                    return new HttpResponseMessage(HttpStatusCode.OK)
                    {
                        Content = new StringContent(payload, Encoding.UTF8, "application/json")
                    };
                }

                return new HttpResponseMessage(HttpStatusCode.NotFound);
            });

            using var http = new HttpClient(handler);
            var config = new XttsConfig
            {
                BaseUrl = $"http://127.0.0.1:{port}",
                TimeoutSeconds = 3,
                StartupWaitSeconds = 3,
                RetryBaseDelayMs = 1
            };

            var provider = new XttsTtsProvider(
                config,
                http,
                NullLogger<XttsTtsProvider>.Instance,
                _ =>
                {
                    processStarts++;
                    return Process.GetCurrentProcess();
                },
                (_, _) => Task.CompletedTask);

            provider.GenerateNarration(
                [new NarrationSegment { Text = "Hello existing service", TargetDurationSeconds = 0 }],
                outputPath,
                new AudioConfig(),
                "ffmpeg.exe");

            Assert.Equal(0, processStarts);
            Assert.True(healthCalls >= 3);
        }
        finally
        {
            listener.Stop();
            TryDelete(outputPath);
        }
    }

    private static string CreateTempFilePath()
    {
        var path = Path.Combine(Path.GetTempPath(), $"appradar_xtts_{Guid.NewGuid():N}.wav");
        return path;
    }

    private static string CreateTempScriptPath(string extension)
    {
        return Path.Combine(Path.GetTempPath(), $"appradar_xtts_{Guid.NewGuid():N}{extension}");
    }

    private static void TryDelete(string path)
    {
        try { if (File.Exists(path)) File.Delete(path); } catch { }
    }

    private sealed class StubHttpMessageHandler(Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> handler)
        : HttpMessageHandler
    {
        private readonly Func<HttpRequestMessage, CancellationToken, HttpResponseMessage> _handler = handler;

        protected override Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            return Task.FromResult(_handler(request, cancellationToken));
        }
    }
}
