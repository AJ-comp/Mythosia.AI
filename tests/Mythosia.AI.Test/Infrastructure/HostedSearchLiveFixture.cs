using Mythosia.AI.Models;
using System.Net;
using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Mythosia.AI.Tests;

/// <summary>
/// Owns only the synthetic document and hosted store created by one live test.
/// Creation failures and disposal both attempt cleanup of every acquired resource.
/// </summary>
internal sealed class HostedSearchLiveFixture : IAsyncDisposable
{
    private readonly HttpClient _client = new() { Timeout = TimeSpan.FromSeconds(60) };
    private readonly string _provider;
    private string? _storeId;
    private string? _fileId;
    private bool _disposed;

    public string DocumentTitle { get; } = "mythosia-live-" + Guid.NewGuid().ToString("N") + ".txt";
    public string ProjectName { get; } = "Project " + Guid.NewGuid().ToString("N");
    public string VerificationToken { get; } = "CLEARANCE_" + Guid.NewGuid().ToString("N");
    public string? FileId => _fileId;
    public FileSearchStore Store => new(_provider,
        _storeId ?? throw new InvalidOperationException("The fixture store has not been created."));
    public string Query => $"Search the uploaded document for {ProjectName}. " +
        "What is its exact clearance code? Copy the code verbatim and cite the source document. " +
        "The code is available only in the uploaded document; do not guess.";

    private HostedSearchLiveFixture(string provider, string apiKey)
    {
        _provider = provider;
        if (provider == "OpenAI")
        {
            _client.BaseAddress = new Uri("https://api.openai.com/v1/");
            _client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", apiKey);
        }
        else if (provider == "Google")
        {
            _client.BaseAddress = new Uri("https://generativelanguage.googleapis.com/");
            _client.DefaultRequestHeaders.Add("x-goog-api-key", apiKey);
        }
        else
        {
            _client.Dispose();
            throw new NotSupportedException("No hosted-search fixture is implemented for " + provider);
        }
    }

    public static async Task<HostedSearchLiveFixture> CreateAsync(
        string provider, string apiKey, CancellationToken cancellationToken)
    {
        var fixture = new HostedSearchLiveFixture(provider, apiKey);
        try
        {
            byte[] content = Encoding.UTF8.GetBytes(
                $"Synthetic integration-test document: {fixture.DocumentTitle}\n" +
                $"Project: {fixture.ProjectName}\n" +
                $"The exact clearance code for {fixture.ProjectName} is {fixture.VerificationToken}.\n" +
                "This document contains fabricated test data and no customer information.\n");
            if (provider == "OpenAI")
                await fixture.CreateOpenAIAsync(content, cancellationToken);
            else
                await fixture.CreateGoogleAsync(content, cancellationToken);
            Console.WriteLine($"LIVE_SEARCH_FIXTURE_READY provider={provider} store={fixture._storeId}");
            return fixture;
        }
        catch (Exception creationError)
        {
            try { await fixture.DisposeAsync(); }
            catch (Exception cleanupError)
            {
                throw new AggregateException("Fixture creation and cleanup both failed.", creationError, cleanupError);
            }
            throw;
        }
    }

    private async Task CreateOpenAIAsync(byte[] content, CancellationToken cancellationToken)
    {
        // https://developers.openai.com/api/docs/guides/tools-file-search
        using var upload = new MultipartFormDataContent();
        upload.Add(new StringContent("assistants"), "purpose");
        var file = new ByteArrayContent(content);
        file.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        upload.Add(file, "file", DocumentTitle);
        using (var uploaded = await SendJsonAsync(HttpMethod.Post, "files", upload, cancellationToken))
            _fileId = RequiredString(uploaded.RootElement, "id");

        using (var store = await SendJsonAsync(HttpMethod.Post, "vector_stores", Json(new
        {
            name = DocumentTitle,
            expires_after = new { anchor = "last_active_at", days = 1 }
        }), cancellationToken))
            _storeId = RequiredString(store.RootElement, "id");

        string associationPath = $"vector_stores/{Segment(_storeId)}/files/{Segment(_fileId)}";
        using (await SendJsonAsync(HttpMethod.Post, $"vector_stores/{Segment(_storeId)}/files",
            Json(new { file_id = _fileId }), cancellationToken)) { }

        while (true)
        {
            using var status = await SendJsonAsync(HttpMethod.Get, associationPath, null, cancellationToken);
            string state = RequiredString(status.RootElement, "status");
            if (state == "completed") return;
            if (state is "failed" or "cancelled")
                throw new InvalidOperationException("OpenAI fixture indexing ended with status " + state);
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
    }

    private async Task CreateGoogleAsync(byte[] content, CancellationToken cancellationToken)
    {
        // https://ai.google.dev/gemini-api/docs/file-search
        // Use the text embedding model supported by the existing generateContent model catalogue.
        using (var store = await SendJsonAsync(HttpMethod.Post, "v1beta/fileSearchStores",
            Json(new { displayName = DocumentTitle, embeddingModel = "models/gemini-embedding-001" }), cancellationToken))
            _storeId = RequiredString(store.RootElement, "name");

        string storePath = GoogleResource(_storeId, "fileSearchStores/");
        using var start = new HttpRequestMessage(HttpMethod.Post,
            "upload/v1beta/" + storePath + ":uploadToFileSearchStore")
        {
            Content = Json(new { displayName = DocumentTitle })
        };
        start.Headers.Add("X-Goog-Upload-Protocol", "resumable");
        start.Headers.Add("X-Goog-Upload-Command", "start");
        start.Headers.Add("X-Goog-Upload-Header-Content-Length", content.Length.ToString(System.Globalization.CultureInfo.InvariantCulture));
        start.Headers.Add("X-Goog-Upload-Header-Content-Type", "text/plain");
        using var started = await _client.SendAsync(start, cancellationToken);
        EnsureSuccess(started, "start synthetic upload");
        if (!started.Headers.TryGetValues("X-Goog-Upload-URL", out var locations) ||
            !Uri.TryCreate(locations.Single(), UriKind.Absolute, out var location) ||
            location.Scheme != Uri.UriSchemeHttps ||
            !string.Equals(location.Host, "generativelanguage.googleapis.com", StringComparison.OrdinalIgnoreCase))
            throw new InvalidOperationException("Google returned an unexpected upload destination.");

        using var finalize = new HttpRequestMessage(HttpMethod.Post, location)
        {
            Content = new ByteArrayContent(content)
        };
        finalize.Content.Headers.ContentType = new MediaTypeHeaderValue("text/plain");
        finalize.Headers.Add("X-Goog-Upload-Offset", "0");
        finalize.Headers.Add("X-Goog-Upload-Command", "upload, finalize");
        using var finalized = await _client.SendAsync(finalize, cancellationToken);
        EnsureSuccess(finalized, "finalize synthetic upload");
        using var initial = JsonDocument.Parse(await finalized.Content.ReadAsStringAsync(cancellationToken));
        JsonElement operation = initial.RootElement.Clone();
        while (true)
        {
            if (operation.TryGetProperty("error", out _))
                throw new InvalidOperationException("Google fixture indexing reported an operation error.");
            if (operation.TryGetProperty("done", out var done) && done.GetBoolean()) break;
            string operationPath = GoogleResource(RequiredString(operation, "name"), storePath + "/");
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            using var current = await SendJsonAsync(HttpMethod.Get, "v1beta/" + operationPath, null, cancellationToken);
            operation = current.RootElement.Clone();
        }

        // The upload operation's response shape can vary; list only this newly created store.
        using var documents = await SendJsonAsync(HttpMethod.Get, "v1beta/" + storePath + "/documents", null, cancellationToken);
        if (!documents.RootElement.TryGetProperty("documents", out var entries) || entries.GetArrayLength() != 1)
            throw new InvalidOperationException("The Google fixture must contain exactly one synthetic document.");
        _fileId = RequiredString(entries[0], "name");
    }

    public async ValueTask DisposeAsync()
    {
        if (_disposed) return;
        _disposed = true;
        var failures = new List<Exception>();
        using var cleanup = new CancellationTokenSource(TimeSpan.FromMinutes(2));
        try
        {
            if (_storeId != null)
            {
                string path = _provider == "OpenAI"
                    ? "vector_stores/" + Segment(_storeId)
                    : "v1beta/" + GoogleResource(_storeId, "fileSearchStores/") + "?force=true";
                try { await DeleteOwnedAsync(path, cleanup.Token); }
                catch (Exception exception) { failures.Add(exception); }
            }
            if (_provider == "OpenAI" && _fileId != null)
            {
                // Deleting a vector store does not delete its source File.
                try { await DeleteOwnedAsync("files/" + Segment(_fileId), cleanup.Token); }
                catch (Exception exception) { failures.Add(exception); }
            }
        }
        finally { _client.Dispose(); }
        if (failures.Count > 0)
            throw new AggregateException($"Cleanup failed for synthetic {_provider} store {_storeId} and file {_fileId}.", failures);
        Console.WriteLine($"LIVE_SEARCH_FIXTURE_DELETED provider={_provider} store={_storeId} file={_fileId}");
    }

    private async Task DeleteOwnedAsync(string path, CancellationToken cancellationToken)
    {
        for (int attempt = 0; ; attempt++)
        {
            using var response = await _client.DeleteAsync(path, cancellationToken);
            if (response.IsSuccessStatusCode || response.StatusCode == HttpStatusCode.NotFound) return;
            if (attempt >= 2 || (response.StatusCode != HttpStatusCode.TooManyRequests && (int)response.StatusCode < 500))
                EnsureSuccess(response, "delete owned fixture resource");
            await Task.Delay(TimeSpan.FromSeconds(attempt + 1), cancellationToken);
        }
    }

    private async Task<JsonDocument> SendJsonAsync(
        HttpMethod method, string path, HttpContent? content, CancellationToken cancellationToken)
    {
        using var request = new HttpRequestMessage(method, path) { Content = content };
        using var response = await _client.SendAsync(request, cancellationToken);
        EnsureSuccess(response, method.Method + " fixture resource");
        return JsonDocument.Parse(await response.Content.ReadAsStringAsync(cancellationToken));
    }

    private static void EnsureSuccess(HttpResponseMessage response, string operation)
    {
        // Never include request URLs, authorization headers, or unfiltered provider bodies in test output.
        if (!response.IsSuccessStatusCode)
            throw new HttpRequestException($"Could not {operation}: HTTP {(int)response.StatusCode}.", null, response.StatusCode);
    }

    private static StringContent Json(object value) => new(JsonSerializer.Serialize(value), Encoding.UTF8, "application/json");
    private static string RequiredString(JsonElement element, string name) =>
        element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String &&
        !string.IsNullOrWhiteSpace(value.GetString()) ? value.GetString()! :
        throw new InvalidOperationException("Missing fixture response field: " + name);
    private static string Segment(string value) => Uri.EscapeDataString(value);
    private static string GoogleResource(string value, string requiredPrefix)
    {
        if (!value.StartsWith(requiredPrefix, StringComparison.Ordinal) ||
            value.Contains("..", StringComparison.Ordinal) || value.Any(c => !(char.IsAsciiLetterOrDigit(c) || c is '/' or '-' or '_')))
            throw new InvalidOperationException("Unexpected Google fixture resource name.");
        return value;
    }
}
