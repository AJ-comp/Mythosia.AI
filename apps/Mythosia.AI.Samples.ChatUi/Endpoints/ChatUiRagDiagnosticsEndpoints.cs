using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Diagnostics;
using static Mythosia.AI.Samples.ChatUi.ChatUiUtilityHelpers;

namespace Mythosia.AI.Samples.ChatUi
{
    internal static class ChatUiRagDiagnosticsEndpoints
    {
        public static void MapChatUiRagDiagnosticsEndpoints(this WebApplication app, RagReferenceState ragState, HttpClient embeddingHttpClient)
        {
            app.MapGet("/api/rag/code-snippet", () =>
            {
                if (!ragState.TryGetSnapshot(out _, out var config))
                    return Results.BadRequest(new { error = "Run Reference first to generate the code snippet." });

                var code = GenerateRagReferenceCodeSnippet(config!);
                return Results.Ok(new { code });
            });

            app.MapGet("/api/rag/reference-history", () =>
            {
                var history = ragState.GetHistory()
                    .Select(entry => new
                    {
                        id = entry.Id,
                        createdAt = entry.CreatedAt,
                        sources = entry.Sources,
                        summary = entry.Summary,
                        config = entry.Config
                    })
                    .ToList();
                return Results.Ok(new { history });
            });

            app.MapGet("/api/rag/reference-history/{id:guid}/trace", (Guid id) =>
            {
                var trace = ragState.GetHistoryTrace(id);
                if (trace == null)
                    return Results.NotFound(new { error = "History entry not found." });
                return Results.Ok(trace);
            });

            app.MapGet("/api/rag/diagnose/health-check", async (CancellationToken ct) =>
            {
                if (ragState.RequiresReindex) return ChatUiRagCoreEndpoints.ReindexRequired();
                if (ragState.Store == null)
                    return Results.BadRequest(new { error = "No RAG index. Run Document Reference first." });

                try
                {
                    var session = ragState.Store.Diagnose();
                    var result = await session.HealthCheckAsync(cancellationToken: ct);
                    return Results.Ok(new
                    {
                        totalChunks = result.TotalChunks,
                        hasWarnings = result.HasWarnings,
                        items = result.Items.Select(i => new
                        {
                            status = i.Status.ToString().ToLowerInvariant(),
                            category = i.Category,
                            message = i.Message
                        }),
                        report = result.ToReport()
                    });
                }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            app.MapPost("/api/rag/diagnose/why-missing", async (WhyMissingRequest req, CancellationToken ct) =>
            {
                if (ragState.Store == null)
                    return Results.BadRequest(new { error = "No RAG index. Run Document Reference first." });

                if (string.IsNullOrWhiteSpace(req.Query) || string.IsNullOrWhiteSpace(req.ExpectedText))
                    return Results.BadRequest(new { error = "query and expectedText are required." });

                try
                {
                    await ApplyRequestedSettingsAsync(ragState, embeddingHttpClient, req.RagSettings, req.VectorStore, ct);
                    var session = ragState.Store.Diagnose();
                    var result = await session.WhyMissingAsync(req.Query, req.ExpectedText, cancellationToken: ct);
                    return Results.Ok(new
                    {
                        query = result.Query,
                        expectedText = result.ExpectedText,
                        hasIssues = result.HasIssues,
                        steps = result.Steps.Select(s => new
                        {
                            status = s.Status.ToString().ToLowerInvariant(),
                            stepName = s.StepName,
                            message = s.Message,
                            suggestion = s.Suggestion
                        }),
                        suggestions = result.Suggestions,
                        report = result.ToReport()
                    });
                }
                catch (RagReindexRequiredException) { return ChatUiRagCoreEndpoints.ReindexRequired(); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });

            app.MapPost("/api/rag/diagnose/query-scores", async (QueryScoresRequest req, CancellationToken ct) =>
            {
                if (ragState.Store == null)
                    return Results.BadRequest(new { error = "No RAG index. Run Document Reference first." });

                if (string.IsNullOrWhiteSpace(req.Query))
                    return Results.BadRequest(new { error = "query is required." });

                try
                {
                    await ApplyRequestedSettingsAsync(ragState, embeddingHttpClient, req.RagSettings, req.VectorStore, ct);
                    var diag = new RagDiagnostics(ragState.Store);
                    var result = await diag.DiagnoseQueryAsync(req.Query, req.ExpectedText, cancellationToken: ct);
                    return Results.Ok(new
                    {
                        query = req.Query,
                        expectedText = req.ExpectedText,
                        totalScored = result.AllScoredResults.Count,
                        topK = result.TopK,
                        minScore = result.MinScore,
                        targetChunk = result.TargetChunkInfo != null ? new
                        {
                            rank = result.TargetChunkInfo.Rank,
                            score = result.TargetChunkInfo.Score,
                            isInTopK = result.TargetChunkInfo.IsInTopK,
                            passesMinScore = result.TargetChunkInfo.PassesMinScore,
                            preview = result.TargetChunkInfo.Preview,
                            contentLength = result.TargetChunkInfo.Record.Content.Length
                        } : (object?)null,
                        results = result.AllScoredResults.Select(r => new
                        {
                            rank = r.Rank,
                            score = r.Score,
                            containsText = r.ContainsTarget,
                            preview = r.Preview,
                            content = r.Record.Content,
                            contentLength = r.Record.Content.Length,
                            id = r.Record.Id
                        })
                    });
                }
                catch (RagReindexRequiredException) { return ChatUiRagCoreEndpoints.ReindexRequired(); }
                catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });
        }

        private static async Task ApplyRequestedSettingsAsync(RagReferenceState state, HttpClient httpClient,
            RagPipelineSettingsRequest? request, VectorStoreConfigRequest? vectorStore, CancellationToken cancellationToken)
        {
            if (request == null)
            {
                if (state.RequiresReindex) throw new RagReindexRequiredException();
                return;
            }
            var current = state.GetSettings();
            var settings = current with
            {
                EmbeddingProvider = request.EmbeddingProvider?.Trim().ToLowerInvariant() ?? current.EmbeddingProvider,
                EmbeddingModel = request.EmbeddingModel?.Trim() ?? current.EmbeddingModel,
                EmbeddingDimensions = request.EmbeddingDimensions ?? current.EmbeddingDimensions,
                EmbeddingBaseUrl = request.EmbeddingBaseUrl?.Trim() ?? current.EmbeddingBaseUrl,
                EmbeddingTimeoutSeconds = request.EmbeddingTimeoutSeconds ?? current.EmbeddingTimeoutSeconds,
                EmbeddingMaxConcurrency = request.EmbeddingMaxConcurrency ?? current.EmbeddingMaxConcurrency,
                HybridSearchEnabled = request.HybridSearchEnabled ?? current.HybridSearchEnabled,
                HybridSearchVectorWeight = request.HybridSearchVectorWeight ?? current.HybridSearchVectorWeight,
                FinalFilter = request.FinalFilter ?? current.FinalFilter,
                RetrievalDerivation = request.RetrievalDerivation ?? current.RetrievalDerivation
            };
            var warning = await ChatUiRagCoreEndpoints.EnsureExternalStoreMatchesSettingsAsync(state, httpClient, settings, vectorStore, cancellationToken);
            if (warning != null) throw new InvalidOperationException(warning);
            state.UpdateSettings(settings);
            state.TryApplyQuerySettings(settings);
        }
    }
}
