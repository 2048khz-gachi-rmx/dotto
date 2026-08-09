using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dotto.Ai.Abstractions;

/// <summary>
/// The lazily-started container segment of a sandbox session. Exposes the API
/// URL, container ID, and the allowed operations on the underlying container
/// (executing a command and checking health).
/// </summary>
public sealed class SandboxContainer
{
    private const int ExecuteTimeoutSeconds = 310; // slightly above the "long" (300s) timeout tier

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http;

    public string ContainerId { get; }
    public Uri ApiUrl { get; }

    public SandboxContainer(Uri apiUrl, string containerId)
    {
        ApiUrl = apiUrl;
        ContainerId = containerId;
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(ExecuteTimeoutSeconds) };
    }

    /// <summary>
    /// Polls <c>GET /ping</c>. Returns <c>true</c> when it responds with HTTP 200.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        using var response = await _http.GetAsync(new Uri(ApiUrl, "/ping"), ct);
        return response.IsSuccessStatusCode;
    }

    /// <summary>
    /// Executes a shell command in the sandbox via <c>POST /execute</c>.
    /// </summary>
    public async Task<SandboxExecuteResult> ExecuteAsync(
        string command,
        string cwd = "",
        string timeout = "",
        CancellationToken ct = default)
    {
        var request = new SandboxExecuteRequest { Command = command, Cwd = cwd, Timeout = timeout };

        // Serialize manually so Content-Length is set — the Python stdlib HTTP
        // server does not handle Transfer-Encoding: chunked.
        var json = JsonSerializer.Serialize(request, JsonOptions);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");

        using var response = await _http.PostAsync(new Uri(ApiUrl, "/execute"), content, ct);

        if (!response.IsSuccessStatusCode)
        {
            var errorBody = await response.Content.ReadAsStringAsync(ct);
            throw new SandboxHttpException((int)response.StatusCode, errorBody);
        }

        var result = await response.Content.ReadFromJsonAsync<SandboxExecuteResponse>(JsonOptions, ct);
        return new SandboxExecuteResult(
            result?.ExitCode ?? -1,
            result?.Stdout ?? string.Empty,
            result?.Stderr ?? string.Empty);
    }
}

/// <summary>
/// The parsed result of a <c>POST /execute</c> call.
/// </summary>
public sealed record SandboxExecuteResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// Thrown when the sandbox API returns a non-success HTTP status.
/// </summary>
public sealed class SandboxHttpException : Exception
{
    public int StatusCode { get; }

    public SandboxHttpException(int statusCode, string body)
        : base($"Sandbox error (HTTP {statusCode}): {body}")
    {
        StatusCode = statusCode;
    }
}

internal sealed record SandboxExecuteRequest
{
    [JsonPropertyName("command")]
    public string Command { get; init; } = string.Empty;

    [JsonPropertyName("cwd")]
    public string Cwd { get; init; } = string.Empty;

    [JsonPropertyName("timeout")]
    public string Timeout { get; init; } = string.Empty;
}

internal sealed record SandboxExecuteResponse
{
    [JsonPropertyName("exitCode")]
    public int ExitCode { get; init; }

    [JsonPropertyName("stdout")]
    public string Stdout { get; init; } = string.Empty;

    [JsonPropertyName("stderr")]
    public string Stderr { get; init; } = string.Empty;
}
