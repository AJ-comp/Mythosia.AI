using Mythosia.AI.Rag;
using Mythosia.AI.Rag.Diagnostics;
using static Mythosia.AI.Samples.ChatUi.ChatUiUtilityHelpers;

namespace Mythosia.AI.Samples.ChatUi
{
    internal static class ChatUiRagDiagnosticsEndpoints
    {
        public static void MapChatUiRagDiagnosticsEndpoints(this WebApplication app, RagReferenceState ragState)
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

            app.MapGet("/api/rag/diagnose/health-check", async (CancellationToken ct) =>
            {
                if (ragState.Store == null)
                    return Results.BadRequest(new { error = "No RAG index. Run Document Reference first." });

                try
                {
                    var session = ragState.Store.Diagnose();
                    var result = await session.HealthCheckAsync(cancellationToken: ct);
                    return Results.Ok(new
                    {
                        @namespace = result.Namespace,
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
                catch (Exception ex)
                {
                    return Results.BadRequest(new { error = ex.Message });
                }
            });
        }
    }
}
