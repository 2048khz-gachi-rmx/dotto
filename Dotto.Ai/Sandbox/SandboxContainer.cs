using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace Dotto.Ai.Sandbox;

/// <summary>
/// The lazily-started container segment of a sandbox session. Exposes the API
/// URL, container ID, and the allowed operations on the underlying container
/// (executing a command, health checks, and streaming file transfer).
/// </summary>
public sealed class SandboxContainer(Uri apiUrl, string containerId, string token) : ISandboxContainer
{
    private const int ExecuteTimeoutSeconds = 310; // slightly above the "long" (300s) timeout tier
    private const int FileTimeoutSeconds = 900;
    // The sandbox server caps tool output at 32k chars; JSON escaping can
    // inflate that, so allow up to 64k on the buffered (execute/ping) client.
    private const long MaxBufferedResponseBytes = 64 * 1024;

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNamingPolicy = JsonNamingPolicy.SnakeCaseLower
    };

    private readonly HttpClient _http = CreateHttpClient(ExecuteTimeoutSeconds, MaxBufferedResponseBytes);
    private readonly HttpClient _fileHttp = CreateHttpClient(FileTimeoutSeconds);

    private static HttpClient CreateHttpClient(int timeoutSeconds, long? maxBufferedResponseBytes = null)
    {
        var client = new HttpClient(
            new SocketsHttpHandler
            {
                // The sandbox must never be able to bounce the bot (and the token
                // header) somewhere else.
                AllowAutoRedirect = false,
                UseCookies = false,
            })
        {
            Timeout = TimeSpan.FromSeconds(timeoutSeconds)
        };

        if (maxBufferedResponseBytes is > 0)
            client.MaxResponseContentBufferSize = maxBufferedResponseBytes.Value;

        return client;
    }

    public string ContainerId { get; } = containerId;
    public Uri ApiUrl { get; } = apiUrl;

    /// <summary>
    /// Polls <c>GET /ping</c>. Returns <c>true</c> when it responds with HTTP 200.
    /// </summary>
    public async Task<bool> PingAsync(CancellationToken ct)
    {
        using var request = CreateRequest(HttpMethod.Get, "/ping");
        using var response = await _http.SendAsync(request, ct);
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
        var payload = new SandboxExecuteRequest { Command = command, Cwd = cwd, Timeout = timeout };

        // python stdlib HTTP server is being a cunt about Transfer-Encoding: chunked which is used by PostAsJson
        using var content = new StringContent(JsonSerializer.Serialize(payload, JsonOptions), Encoding.UTF8, "application/json");

        using var request = CreateRequest(HttpMethod.Post, "/execute", content);
        using var response = await _http.SendAsync(request, ct);

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

    /// <summary>
    /// Streams a file out of the sandbox. The HTTP response (and therefore the
    /// underlying connection) is kept alive until the returned stream is
    /// disposed, so the caller must finish reading before the container stops.
    /// </summary>
    public async Task<SandboxFile> GetFileAsync(string sandboxPath, CancellationToken ct)
    {
        var path = $"/file?path={Uri.EscapeDataString(sandboxPath)}";
        using var request = CreateRequest(HttpMethod.Get, path);

        var response = await _fileHttp.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, ct);

        if (!response.IsSuccessStatusCode)
        {
            var status = (int)response.StatusCode;
            var body = await response.Content.ReadAsStringAsync(ct);
            response.Dispose();
            throw new SandboxHttpException(status, body);
        }

        var length = response.Content.Headers.ContentLength
                     ?? throw new SandboxHttpException(500, "sandbox did not send a Content-Length");

        var stream = await response.Content.ReadAsStreamAsync(ct);
        return new SandboxFile(new ResponseStream(stream, response), length);
    }

    /// <summary>
    /// Streams a file into the sandbox. The server enforces an explicit
    /// Content-Length (its request reader is not chunked-capable) and owns path
    /// resolution within the sandbox namespace.
    /// </summary>
    public async Task<string> PutFileAsync(
        string sandboxPath,
        Stream content,
        long length,
        bool dedupe,
        CancellationToken ct)
    {
        var path = $"/file?path={Uri.EscapeDataString(sandboxPath)}{(dedupe ? "&dedupe=1" : "")}";
        using var request = CreateRequest(HttpMethod.Put, path);

        var streamContent = new StreamContent(content);
        // Content-Length must be explicit: the python server rejects chunked
        // bodies, and non-seekable downloader streams cannot compute it.
        streamContent.Headers.ContentLength = length;
        request.Content = streamContent;

        using var response = await _fileHttp.SendAsync(request, ct);

        if (!response.IsSuccessStatusCode)
        {
            var body = await response.Content.ReadAsStringAsync(ct);
            throw new SandboxHttpException((int)response.StatusCode, body);
        }

        var result = await response.Content.ReadFromJsonAsync<SandboxPutFileResponse>(JsonOptions, ct);
        return result?.Path ?? sandboxPath;
    }

    private HttpRequestMessage CreateRequest(HttpMethod method, string path, HttpContent? content = null)
    {
        var request = new HttpRequestMessage(method, new Uri(ApiUrl, path));
        request.Headers.Add("X-Sandbox-Token", token);
        request.Content = content;
        return request;
    }
}

/// <summary>
/// Wraps a response body stream so disposing it also disposes the owning HTTP
/// response, releasing the connection.
/// </summary>
internal sealed class ResponseStream(Stream inner, HttpResponseMessage response) : Stream
{
    public override bool CanRead => inner.CanRead;
    public override bool CanSeek => false;
    public override bool CanWrite => false;

    public override long Length => throw new NotSupportedException();

    public override long Position
    {
        get => throw new NotSupportedException();
        set => throw new NotSupportedException();
    }

    public override int Read(byte[] buffer, int offset, int count) => inner.Read(buffer, offset, count);
    public override ValueTask<int> ReadAsync(Memory<byte> buffer, CancellationToken cancellationToken = default)
        => inner.ReadAsync(buffer, cancellationToken);
    public override Task<int> ReadAsync(byte[] buffer, int offset, int count, CancellationToken cancellationToken)
        => inner.ReadAsync(buffer, offset, count, cancellationToken);
    public override void Flush() => inner.Flush();
    public override long Seek(long offset, SeekOrigin origin) => throw new NotSupportedException();
    public override void SetLength(long value) => throw new NotSupportedException();
    public override void Write(byte[] buffer, int offset, int count) => throw new NotSupportedException();

    protected override void Dispose(bool disposing)
    {
        if (disposing)
        {
            inner.Dispose();
            response.Dispose();
        }

        base.Dispose(disposing);
    }

    public override async ValueTask DisposeAsync()
    {
        await inner.DisposeAsync();
        response.Dispose();
    }
}

/// <summary>
/// The parsed result of a <c>POST /execute</c> call.
/// </summary>
public sealed record SandboxExecuteResult(int ExitCode, string Stdout, string Stderr);

/// <summary>
/// A file streamed out of the sandbox, with the length the server announced.
/// </summary>
public sealed record SandboxFile(Stream Stream, long Length);

/// <summary>
/// Thrown when the sandbox API returns a non-success HTTP status.
/// </summary>
public sealed class SandboxHttpException(int statusCode, string body)
    : Exception($"Sandbox error (HTTP {statusCode}): {body}")
{
    public int StatusCode { get; } = statusCode;
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

internal sealed record SandboxPutFileResponse
{
    [JsonPropertyName("path")]
    public string Path { get; init; } = string.Empty;
}
