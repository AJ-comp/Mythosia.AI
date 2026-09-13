using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Perplexity;
using System;
using System.Collections.Generic;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Runtime.CompilerServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Services.Perplexity
{
    /// <summary>A durable server job. CancelAsync cancels server work; cancelling observation only stops local waiting.</summary>
    /// <remarks>The caller owns the HttpClient and must keep it alive while using this handle.
    /// Observation and cancellation retry HTTP 429 up to three times, respecting Retry-After up to one minute.
    /// Creating a background job is never automatically retried.</remarks>
    public sealed class PerplexityBackgroundRun
    {
        private readonly HttpClient _http;
        private readonly string _key;
        private readonly List<string> _initialEvents = new List<string>();
        private string _id = string.Empty;
        public string Id => _id;
        public PerplexityAgentResponse? LastResponse { get; private set; }
        /// <summary>Last event delivered by StreamAsync. Submission only discovers the ID and does not advance this cursor.</summary>
        public long? LastSequenceNumber { get; private set; }

        internal PerplexityBackgroundRun(HttpClient http, string key, string? responseId = null)
        {
            _http = http;
            _key = key;
            if (responseId != null) { ValidateId(responseId, nameof(responseId)); _id = responseId; }
        }

        internal void SetInitial(PerplexityAgentResponse response)
        {
            ValidateId(response.Id, nameof(response.Id));
            _id = response.Id;
            LastResponse = response;
        }

        public async Task<PerplexityAgentResponse> GetResponseAsync(CancellationToken cancellationToken = default)
        {
            using var response = await SendObservationRequestAsync(HttpMethod.Get, "", cancellationToken).ConfigureAwait(false);
            var snapshot = await ReadSnapshotAsync(response, cancellationToken).ConfigureAwait(false);
            if (snapshot.Id != Id) throw new AIServiceException("The server returned a different response ID.");
            LastResponse = snapshot;
            return snapshot;
        }

        /// <summary>Waits for a terminal snapshot. Inspect Status before treating Text as a successful answer.</summary>
        public async Task<PerplexityAgentResponse> WaitForCompletionAsync(TimeSpan? pollInterval = null, CancellationToken cancellationToken = default)
        {
            var interval = pollInterval ?? TimeSpan.FromSeconds(2);
            if (interval < TimeSpan.FromMilliseconds(100) || interval > TimeSpan.FromMinutes(1))
                throw new ArgumentOutOfRangeException(nameof(pollInterval), "Polling interval must be between 100 milliseconds and one minute.");
            cancellationToken.ThrowIfCancellationRequested();
            var current = LastResponse;
            while (current == null || !current.IsTerminal)
            {
                current = await GetResponseAsync(cancellationToken).ConfigureAwait(false);
                if (!current.IsTerminal) await Task.Delay(interval, cancellationToken).ConfigureAwait(false);
            }
            return current;
        }

        /// <summary>Requests server cancellation and waits for its terminal state. A job may have already completed.</summary>
        public async Task<PerplexityAgentResponse> CancelAsync(CancellationToken cancellationToken = default)
        {
            var current = await GetResponseAsync(cancellationToken).ConfigureAwait(false);
            if (current.IsTerminal) return current;
            using var response = await SendObservationRequestAsync(HttpMethod.Post, "/cancel", cancellationToken).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            if (response.StatusCode == HttpStatusCode.BadRequest)
            {
                // Completion can win the race between the read and the cancellation request.
                current = await GetResponseAsync(cancellationToken).ConfigureAwait(false);
                if (current.IsTerminal) return current;
            }
            if (!response.IsSuccessStatusCode) throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, body);
            using (var document = JsonDocument.Parse(body))
            {
                var status = String(document.RootElement, "status");
                if (status != "cancelling" && status != "cancelled")
                    throw new AIServiceException("Cancellation returned an unexpected status.");
            }
            return await WaitForCompletionAsync(cancellationToken: cancellationToken).ConfigureAwait(false);
        }

        /// <summary>Observes a durable SSE stream. With no cursor, delivers buffered submission events before reconnecting after their last sequence.
        /// A reattached handle without submission events starts after sequence zero. Resume after a saved sequence number;
        /// an expired reconnect window fails with HTTP 400.</summary>
        public async IAsyncEnumerable<PerplexityAgentEvent> StreamAsync(long? startingAfter = null,
            [EnumeratorCancellation] CancellationToken cancellationToken = default)
        {
            if (startingAfter < 0) throw new ArgumentOutOfRangeException(nameof(startingAfter));
            cancellationToken.ThrowIfCancellationRequested();
            long? cursor = startingAfter;
            foreach (var json in _initialEvents)
            {
                cancellationToken.ThrowIfCancellationRequested();
                var item = ParseEvent(json);
                if (item.SequenceNumber.HasValue && cursor.HasValue && item.SequenceNumber <= cursor) continue;
                var initialTerminal = ObserveEvent(item);
                if (item.SequenceNumber.HasValue) cursor = item.SequenceNumber;
                yield return item;
                if (initialTerminal) yield break;
            }
            // The provider requires starting_after even on the first GET. Submission events are
            // retained above so choosing this cursor cannot discard initial text or lifecycle events.
            cancellationToken.ThrowIfCancellationRequested();
            var query = "?stream=true&starting_after=" + (cursor ?? 0).ToString(System.Globalization.CultureInfo.InvariantCulture);
            using var response = await SendObservationRequestAsync(HttpMethod.Get, query, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false));
            var terminal = false;
            await foreach (var json in ReadSseDataAsync(response, cancellationToken).ConfigureAwait(false))
            {
                var item = ParseEvent(json);
                if (item.SequenceNumber.HasValue && cursor.HasValue && item.SequenceNumber <= cursor) continue;
                terminal = ObserveEvent(item);
                if (item.SequenceNumber.HasValue) cursor = item.SequenceNumber;
                yield return item;
                if (terminal) yield break;
            }
            cancellationToken.ThrowIfCancellationRequested();
            if (!terminal) throw new AIServiceException("Agent stream ended before a terminal response. Resume using LastSequenceNumber or retrieve the final snapshot.");
        }

        public async Task<IReadOnlyList<PerplexityResponseFile>> ListFilesAsync(CancellationToken cancellationToken = default)
        {
            using var response = await SendObservationRequestAsync(HttpMethod.Get, "/files", cancellationToken).ConfigureAwait(false);
            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, body);
            using var document = JsonDocument.Parse(body);
            if (!document.RootElement.TryGetProperty("data", out var data) || data.ValueKind != JsonValueKind.Array)
                throw new AIServiceException("File listing is missing its data array.");
            var files = new List<PerplexityResponseFile>();
            foreach (var entry in data.EnumerateArray())
            {
                var id = String(entry, "id") ?? throw new AIServiceException("File ID is missing.");
                ValidateId(id, "fileId");
                files.Add(new PerplexityResponseFile { Id = id, FileName = String(entry, "filename") ?? string.Empty,
                    Bytes = entry.GetProperty("bytes").GetInt64(),
                    CreatedAt = entry.TryGetProperty("created_at", out var created) && created.TryGetInt64(out var timestamp) ? timestamp : (long?)null });
            }
            return files;
        }

        public async Task<byte[]> DownloadFileAsync(string fileId, CancellationToken cancellationToken = default)
        {
            ValidateId(fileId, nameof(fileId));
            using var response = await SendObservationRequestAsync(HttpMethod.Get, "/files/" + Uri.EscapeDataString(fileId) + "/content", cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
                throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false));
            return await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false);
        }

        internal async Task<PerplexityAgentResponse> SendInitialAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            // Never retry POST: a disconnected submission may already have created server work.
            using var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode || !string.Equals(response.Content.Headers.ContentType?.MediaType,
                "text/event-stream", StringComparison.OrdinalIgnoreCase))
                return await ReadSnapshotAsync(response, cancellationToken).ConfigureAwait(false);

            try
            {
                await foreach (var json in ReadSseDataAsync(response, cancellationToken).ConfigureAwait(false))
                {
                    var item = ParseEvent(json);
                    _initialEvents.Add(json);
                    if (item.Response == null) continue;
                    cancellationToken.ThrowIfCancellationRequested();
                    // Discovering the ID is not delivery. StreamAsync first yields these buffered
                    // events, then reconnects after the final buffered sequence without losing text.
                    return item.Response;
                }
            }
            catch (JsonException exception)
            {
                throw new AIServiceException("Invalid background submission event before receiving its response ID.", exception);
            }
            throw new AIServiceException("Background submission stream ended before a response snapshot containing its ID was received. The submission was not retried.");
        }

        private static PerplexityAgentEvent ParseEvent(string json)
        {
            using var document = JsonDocument.Parse(json);
            var root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object) throw new AIServiceException("Agent event must be an object.");
            var type = String(root, "type") ?? throw new AIServiceException("Agent event is missing its type.");
            if (type == "error") throw new AIServiceException("Agent stream failed: " + (String(root, "message") ?? json));
            if (!root.TryGetProperty("sequence_number", out var number) || number.ValueKind != JsonValueKind.Number ||
                !number.TryGetInt64(out var sequence) || sequence < 0)
                throw new AIServiceException("Agent stream event requires a nonnegative sequence number for lossless reconnection.");
            var item = new PerplexityAgentEvent { Type = type, Json = json, SequenceNumber = sequence };
            if (type == "response.output_text.delta") item.TextDelta = String(root, "delta");
            if (root.TryGetProperty("response", out var snapshot))
            {
                if (snapshot.ValueKind != JsonValueKind.Object) throw new AIServiceException("Agent event must contain a response object.");
                item.Response = ParseSnapshot(snapshot);
            }
            return item;
        }

        private bool ObserveEvent(PerplexityAgentEvent item)
        {
            if (item.Response != null)
            {
                if (item.Response.Id != Id) throw new AIServiceException("The stream returned a different response ID.");
                LastResponse = item.Response;
            }
            if (item.SequenceNumber.HasValue) LastSequenceNumber = item.SequenceNumber;
            return item.Response?.IsTerminal == true;
        }

        private static async IAsyncEnumerable<string> ReadSseDataAsync(HttpResponseMessage response,
            [EnumeratorCancellation] CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var cancellation = cancellationToken.Register(response.Dispose);
            Stream stream;
            try { stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false); }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
            using (stream)
            using (var reader = new StreamReader(stream))
            {
                var data = new StringBuilder();
                while (true)
                {
                    cancellationToken.ThrowIfCancellationRequested();
                    string? line;
                    try { line = await reader.ReadLineAsync().ConfigureAwait(false); }
                    catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
                    if (line == null)
                    {
                        if (data.Length == 0) break;
                        line = string.Empty;
                    }
                    if (line.StartsWith("data:", StringComparison.Ordinal))
                    {
                        if (data.Length > 0) data.Append('\n');
                        data.Append(line.Substring(5).TrimStart(' '));
                    }
                    else if (line.Length == 0 && data.Length > 0)
                    {
                        var json = data.ToString();
                        data.Clear();
                        if (json == "[DONE]") break;
                        yield return json;
                    }
                }
            }
            cancellationToken.ThrowIfCancellationRequested();
        }

        private static async Task<PerplexityAgentResponse> ReadSnapshotAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            var body = await ReadBodyAsync(response, cancellationToken).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode) throw AIHttpErrorFactory.FromHttp((int)response.StatusCode, response.ReasonPhrase, body);
            using var document = JsonDocument.Parse(body);
            return ParseSnapshot(document.RootElement);
        }

        private async Task<HttpResponseMessage> SendObservationRequestAsync(HttpMethod method, string suffix, CancellationToken cancellationToken)
        {
            for (var attempt = 0; ; attempt++)
            {
                using var request = Request(method, suffix);
                var response = await _http.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken).ConfigureAwait(false);
                if (response.StatusCode != HttpStatusCode.TooManyRequests || attempt == 3) return response;
                var retry = response.Headers.RetryAfter;
                var delay = retry?.Delta ?? (retry?.Date.HasValue == true
                    ? retry.Date.Value - DateTimeOffset.UtcNow : TimeSpan.FromSeconds(1 << attempt));
                // A longer provider cooldown is returned to the caller, never shortened.
                if (delay > TimeSpan.FromMinutes(1)) return response;
                response.Dispose();
                await Task.Delay(delay > TimeSpan.Zero ? delay : TimeSpan.Zero, cancellationToken).ConfigureAwait(false);
            }
        }

        private HttpRequestMessage Request(HttpMethod method, string suffix)
        {
            ValidateId(Id, nameof(Id));
            var request = new HttpRequestMessage(method, "https://api.perplexity.ai/v1/agent/" + Uri.EscapeDataString(Id) + suffix);
            request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", _key);
            return request;
        }

        private static void ValidateId(string value, string name)
        {
            if (string.IsNullOrWhiteSpace(value) || value.Length > 256 || !Regex.IsMatch(value, "^[a-zA-Z0-9_-]+$"))
                throw new ArgumentException("A valid provider ID is required.", name);
        }

        private static string? String(JsonElement element, string property)
            => element.TryGetProperty(property, out var value) && value.ValueKind == JsonValueKind.String ? value.GetString() : null;

        internal static PerplexityAgentResponse ParseSnapshot(JsonElement root)
        {
            var result = new PerplexityAgentResponse
            {
                Id = String(root, "id") ?? throw new AIServiceException("Agent response ID is missing."),
                Status = String(root, "status") ?? throw new AIServiceException("Agent response status is missing."),
                Model = String(root, "model")
            };
            if (!result.IsTerminal && result.Status != "queued" && result.Status != "in_progress" && result.Status != "cancelling")
                throw new AIServiceException("Unknown Agent response status: " + result.Status);
            if (root.TryGetProperty("error", out var error) && error.ValueKind != JsonValueKind.Null) result.Error = error.GetRawText();
            if (root.TryGetProperty("usage", out var usage) && usage.ValueKind == JsonValueKind.Object) result.Usage = PerplexityService.ParseAgentUsage(usage);
            var text = new StringBuilder();
            var citations = new List<AICitation>();
            if (result.Status == "completed" &&
                (!root.TryGetProperty("output", out var completedOutput) || completedOutput.ValueKind != JsonValueKind.Array))
                throw new AIServiceException("Completed Agent response is missing its output array.");
            if (root.TryGetProperty("output", out var output) && output.ValueKind == JsonValueKind.Array)
            {
                result.OutputJson = output.GetRawText();
                var outputIndex = 0;
                foreach (var item in output.EnumerateArray())
                {
                    if (String(item, "type") == "message" && item.TryGetProperty("content", out var content) && content.ValueKind == JsonValueKind.Array)
                    {
                        var contentIndex = 0;
                        foreach (var part in content.EnumerateArray())
                        {
                            if (String(part, "type") == "output_text" || String(part, "type") == "text") text.Append(String(part, "text"));
                            else if (String(part, "type") == "refusal") text.Append(String(part, "refusal"));
                            if (part.TryGetProperty("annotations", out var annotations) && annotations.ValueKind == JsonValueKind.Array)
                                foreach (var annotation in annotations.EnumerateArray())
                                {
                                    var citation = PerplexityService.ParseAgentCitation(annotation, result.Id, outputIndex, contentIndex);
                                    if (citation != null) citations.Add(citation);
                                }
                            contentIndex++;
                        }
                    }
                    foreach (var source in PerplexityService.GetAgentToolSources(item))
                    {
                        var citation = PerplexityService.ParseAgentCitation(source, result.Id, outputIndex, null);
                        if (citation != null) citations.Add(citation);
                    }
                    outputIndex++;
                }
            }
            result.Text = text.ToString();
            result.Citations = citations;
            return result;
        }

        private static async Task<byte[]> ReadBytesAsync(HttpResponseMessage response, CancellationToken cancellationToken)
        {
            cancellationToken.ThrowIfCancellationRequested();
            using var registration = cancellationToken.Register(response.Dispose);
            try
            {
                using var stream = await response.Content.ReadAsStreamAsync().ConfigureAwait(false);
                using var buffer = new MemoryStream();
                await stream.CopyToAsync(buffer, 81920, cancellationToken).ConfigureAwait(false);
                return buffer.ToArray();
            }
            catch (Exception) when (cancellationToken.IsCancellationRequested) { throw new OperationCanceledException(cancellationToken); }
        }

        private static async Task<string> ReadBodyAsync(HttpResponseMessage response, CancellationToken cancellationToken)
            => Encoding.UTF8.GetString(await ReadBytesAsync(response, cancellationToken).ConfigureAwait(false));
    }
}
