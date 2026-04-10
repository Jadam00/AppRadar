using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Text.Json;
using AppRadar.Config;
using Microsoft.Extensions.Logging;

namespace AppRadar.Audio;

/// <summary>
/// TTS provider backed by a local XTTS v2 Python microservice.
/// </summary>
public sealed class XttsTtsProvider : ITtsProvider
{
    private readonly XttsConfig _config;
    private readonly ILogger<XttsTtsProvider> _logger;
    private readonly HttpClient _http;
    private readonly Func<ProcessStartInfo, Process?> _processStarter;
    private readonly Func<TimeSpan, CancellationToken, Task> _delayAsync;

    private readonly object _startupLock = new();
    private Process? _serviceProcess;

    /// <summary>
    /// Creates a provider with a dedicated HttpClient instance.
    /// </summary>
    public XttsTtsProvider(XttsConfig config, ILogger<XttsTtsProvider> logger)
        : this(config, new HttpClient(), logger, startInfo => Process.Start(startInfo),
            (delay, cancellationToken) => Task.Delay(delay, cancellationToken))
    {
    }

    /// <summary>
    /// Internal constructor for tests.
    /// </summary>
    internal XttsTtsProvider(
        XttsConfig config,
        HttpClient http,
        ILogger<XttsTtsProvider> logger,
        Func<ProcessStartInfo, Process?> processStarter,
        Func<TimeSpan, CancellationToken, Task> delayAsync)
    {
        _config = config ?? throw new ArgumentNullException(nameof(config));
        _http = http ?? throw new ArgumentNullException(nameof(http));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
        _processStarter = processStarter ?? throw new ArgumentNullException(nameof(processStarter));
        _delayAsync = delayAsync ?? throw new ArgumentNullException(nameof(delayAsync));
    }

    /// <inheritdoc />
    public bool IsAvailable => OperatingSystem.IsWindows() && !string.IsNullOrWhiteSpace(_config.BaseUrl);

    /// <inheritdoc />
    public string GenerateNarration(
        IReadOnlyList<NarrationSegment> segments,
        string outputWavPath,
        AudioConfig config,
        string ffmpegExe)
    {
        ArgumentNullException.ThrowIfNull(segments);
        ArgumentException.ThrowIfNullOrWhiteSpace(outputWavPath);
        ArgumentNullException.ThrowIfNull(config);

        if (segments.Count == 0)
            throw new ArgumentException("At least one narration segment is required for XTTS.", nameof(segments));

        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(outputWavPath))!);

        var mergedText = JoinSegments(segments);
        if (string.IsNullOrWhiteSpace(mergedText))
            throw new InvalidOperationException("Narration text is empty after sanitization.");

        var voicePath = ResolvePath(_config.VoicePath);

        _logger.LogInformation("Generating XTTS narration using service {BaseUrl}", _config.BaseUrl);
        _logger.LogDebug("XTTS voice path: {VoicePath}", voicePath);

        return Task.Run(async () =>
        {
            await EnsureServiceReadyAsync(CancellationToken.None);

            var payload = new XttsRequest(mergedText, voicePath, outputWavPath);
            var response = await PostWithRetryAsync(payload, CancellationToken.None);

            if (!response.Success)
                throw new InvalidOperationException("XTTS service returned success=false.");

            var finalPath = string.IsNullOrWhiteSpace(response.FilePath)
                ? outputWavPath
                : response.FilePath;

            if (!Path.IsPathRooted(finalPath))
                finalPath = Path.GetFullPath(finalPath);

            if (!File.Exists(finalPath))
                throw new FileNotFoundException(
                    $"XTTS service reported output path '{finalPath}', but the WAV file was not found.",
                    finalPath);

            _logger.LogInformation(
                "XTTS narration generated at {Path} ({Duration:F2}s)",
                finalPath,
                response.DurationSeconds);

            return finalPath;
        }).GetAwaiter().GetResult();
    }

    private async Task<XttsResponse> PostWithRetryAsync(XttsRequest payload, CancellationToken cancellationToken)
    {
        Exception? lastException = null;
        var attempts = Math.Max(0, _config.MaxRetries) + 1;

        for (int attempt = 1; attempt <= attempts; attempt++)
        {
            try
            {
                using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
                timeoutCts.CancelAfter(TimeSpan.FromSeconds(Math.Max(1, _config.TimeoutSeconds)));

                var endpoint = BuildEndpoint(_config.TtsPath);
                var requestJson = JsonSerializer.Serialize(
                    payload,
                    new JsonSerializerOptions { PropertyNamingPolicy = JsonNamingPolicy.CamelCase });

                using var requestContent = new StringContent(requestJson, Encoding.UTF8, "application/json");
                using var httpResponse = await _http.PostAsync(endpoint, requestContent, timeoutCts.Token);
                var body = await httpResponse.Content.ReadAsStringAsync(timeoutCts.Token);

                if (!httpResponse.IsSuccessStatusCode)
                {
                    var message = $"XTTS service returned HTTP {(int)httpResponse.StatusCode} ({httpResponse.StatusCode}). Body: {body}";
                    if (IsTransientStatus(httpResponse.StatusCode) && attempt < attempts)
                    {
                        lastException = new HttpRequestException(message);
                        await BackoffAsync(attempt, cancellationToken);
                        continue;
                    }

                    throw new InvalidOperationException(message);
                }

                var parsed = JsonSerializer.Deserialize<XttsResponse>(
                    body,
                    new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

                if (parsed is null)
                    throw new InvalidOperationException("XTTS service returned an empty or invalid JSON response.");

                return parsed;
            }
            catch (Exception ex) when (IsRetryable(ex) && attempt < attempts)
            {
                lastException = ex;
                _logger.LogWarning(
                    ex,
                    "XTTS request attempt {Attempt}/{Total} failed. Retrying...",
                    attempt,
                    attempts);
                await BackoffAsync(attempt, cancellationToken);
            }
            catch (Exception ex)
            {
                lastException = ex;
                break;
            }
        }

        throw new InvalidOperationException(
            $"XTTS request failed after {attempts} attempt(s).",
            lastException);
    }

    private async Task EnsureServiceReadyAsync(CancellationToken cancellationToken)
    {
        if (await IsServiceHealthyAsync(cancellationToken))
            return;

        // Give an already-running external service a short grace window before auto-start.
        if (await WaitForHealthyAsync(TimeSpan.FromSeconds(3), cancellationToken))
            return;

        if (IsServicePortListening())
        {
            _logger.LogInformation(
                "XTTS endpoint {BaseUrl} has a listening port but is not healthy yet. Waiting up to {StartupWaitSeconds}s before auto-start.",
                _config.BaseUrl,
                _config.StartupWaitSeconds);

            if (await WaitForHealthyAsync(TimeSpan.FromSeconds(Math.Max(1, _config.StartupWaitSeconds)), cancellationToken))
                return;

            throw new InvalidOperationException(
                $"XTTS endpoint '{_config.BaseUrl}' is listening but did not become healthy in time.");
        }

        lock (_startupLock)
        {
            if (_serviceProcess is { HasExited: false })
                return;

            StartServiceProcess();
        }

        if (await WaitForHealthyAsync(TimeSpan.FromSeconds(Math.Max(1, _config.StartupWaitSeconds)), cancellationToken))
            return;

        var exitCodeHint = _serviceProcess is { HasExited: true }
            ? $"Service process exited with code {_serviceProcess.ExitCode}."
            : "Service process did not become healthy in time.";

        throw new InvalidOperationException(
            $"Unable to reach XTTS service at '{_config.BaseUrl}'. {exitCodeHint}");
    }

    private async Task<bool> IsServiceHealthyAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var timeoutCts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            timeoutCts.CancelAfter(TimeSpan.FromSeconds(2));

            var endpoint = BuildEndpoint(_config.HealthPath);
            using var response = await _http.GetAsync(endpoint, timeoutCts.Token);
            return response.IsSuccessStatusCode;
        }
        catch
        {
            return false;
        }
    }

    private async Task<bool> WaitForHealthyAsync(TimeSpan timeout, CancellationToken cancellationToken)
    {
        var effectiveTimeout = timeout <= TimeSpan.Zero ? TimeSpan.FromSeconds(1) : timeout;
        var deadline = DateTime.UtcNow.Add(effectiveTimeout);

        while (DateTime.UtcNow < deadline)
        {
            cancellationToken.ThrowIfCancellationRequested();

            if (await IsServiceHealthyAsync(cancellationToken))
                return true;

            await _delayAsync(TimeSpan.FromMilliseconds(500), cancellationToken);
        }

        return false;
    }

    private bool IsServicePortListening()
    {
        var (host, port) = ResolveHostAndPort(_config.BaseUrl);

        try
        {
            using var client = new TcpClient();
            var connectTask = client.ConnectAsync(host, port);
            var completed = connectTask.Wait(TimeSpan.FromMilliseconds(350));
            return completed && client.Connected;
        }
        catch
        {
            return false;
        }
    }

    private void StartServiceProcess()
    {
        var startupScript = ResolvePath(_config.StartupScriptPath);
        ProcessStartInfo startInfo;

        if (File.Exists(startupScript))
        {
            startInfo = CreateScriptStartInfo(startupScript);
            _logger.LogInformation("Starting XTTS service via script: {Script}", startupScript);
        }
        else
        {
            var scriptPath = ResolvePath(_config.ScriptPath);
            if (!File.Exists(scriptPath))
                throw new FileNotFoundException(
                    "XTTS startup script and fallback service script were not found.",
                    scriptPath);

            startInfo = new ProcessStartInfo
            {
                FileName = _config.PythonCommand,
                Arguments = BuildPythonArgs(scriptPath),
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };

            _logger.LogInformation(
                "Starting XTTS service via Python command '{PythonCommand}' and script '{ScriptPath}'",
                _config.PythonCommand,
                scriptPath);
        }

        _serviceProcess = _processStarter(startInfo)
            ?? throw new InvalidOperationException("Failed to start XTTS service process.");
    }

    private static ProcessStartInfo CreateScriptStartInfo(string startupScript)
    {
        var extension = Path.GetExtension(startupScript).ToLowerInvariant();
        if (extension == ".ps1")
        {
            return new ProcessStartInfo
            {
                FileName = "powershell",
                Arguments = $"-NoProfile -ExecutionPolicy Bypass -File \"{startupScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
        }

        if (extension is ".cmd" or ".bat")
        {
            return new ProcessStartInfo
            {
                FileName = "cmd",
                Arguments = $"/c \"{startupScript}\"",
                UseShellExecute = false,
                CreateNoWindow = true,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                WorkingDirectory = Directory.GetCurrentDirectory()
            };
        }

        return new ProcessStartInfo
        {
            FileName = startupScript,
            UseShellExecute = false,
            CreateNoWindow = true,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            WorkingDirectory = Directory.GetCurrentDirectory()
        };
    }

    private string BuildPythonArgs(string scriptPath)
    {
        var (host, port) = ResolveHostAndPort(_config.BaseUrl);
        var escaped = scriptPath.Replace("\"", "\\\"");
        return $"\"{escaped}\" --host {host} --port {port}";
    }

    private static (string Host, int Port) ResolveHostAndPort(string? baseUrl)
    {
        if (!Uri.TryCreate(baseUrl, UriKind.Absolute, out var uri))
            return ("localhost", 8020);

        var host = string.IsNullOrWhiteSpace(uri.Host) ? "localhost" : uri.Host;
        var port = uri.IsDefaultPort
            ? (uri.Scheme.Equals("https", StringComparison.OrdinalIgnoreCase) ? 443 : 80)
            : uri.Port;

        return (host, port);
    }

    private string BuildEndpoint(string path)
    {
        var baseUrl = (_config.BaseUrl ?? string.Empty).TrimEnd('/');
        var endpointPath = string.IsNullOrWhiteSpace(path) ? "/" : path.Trim();
        if (!endpointPath.StartsWith('/'))
            endpointPath = "/" + endpointPath;

        return baseUrl + endpointPath;
    }

    private static bool IsTransientStatus(HttpStatusCode statusCode) =>
        statusCode == HttpStatusCode.RequestTimeout ||
        statusCode == HttpStatusCode.TooManyRequests ||
        (int)statusCode >= 500;

    private static bool IsRetryable(Exception ex) =>
        ex is HttpRequestException ||
        ex is TaskCanceledException;

    private async Task BackoffAsync(int attempt, CancellationToken cancellationToken)
    {
        var baseDelayMs = Math.Max(50, _config.RetryBaseDelayMs);
        var delayMs = baseDelayMs * (int)Math.Pow(2, Math.Max(0, attempt - 1));
        await _delayAsync(TimeSpan.FromMilliseconds(delayMs), cancellationToken);
    }

    private static string JoinSegments(IReadOnlyList<NarrationSegment> segments)
    {
        var sb = new StringBuilder();

        foreach (var segment in segments)
        {
            if (segment is null)
                continue;

            var text = segment.Text?.Trim();
            if (string.IsNullOrWhiteSpace(text))
                continue;

            if (sb.Length > 0)
                sb.Append(' ');

            sb.Append(text);
        }

        return sb.ToString();
    }

    private static string ResolvePath(string path)
    {
        if (string.IsNullOrWhiteSpace(path))
            return path;

        if (Path.IsPathRooted(path))
            return path;

        var root = Directory.GetCurrentDirectory();
        return Path.GetFullPath(Path.Combine(root, path));
    }

    private sealed record XttsRequest(string Text, string VoicePath, string OutputPath);

    private sealed class XttsResponse
    {
        public bool Success { get; init; }
        public double DurationSeconds { get; init; }
        public string FilePath { get; init; } = string.Empty;
    }
}
