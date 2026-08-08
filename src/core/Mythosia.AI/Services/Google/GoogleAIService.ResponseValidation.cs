using Mythosia.AI.Exceptions;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Streaming;
using System;
using System.Collections.Generic;
using System.Text.Json;

namespace Mythosia.AI.Services.Google
{
    public partial class GoogleAIService
    {
        private const string SuccessfulFinishReason = "STOP";

        private sealed class GeminiStreamState
        {
            public bool TerminalSeen { get; set; }
            public bool Failed { get; set; }
            public string? FinishReason { get; set; }
            public bool IsSuccessful => TerminalSeen && !Failed;
        }

        private static void ValidateCompletedGeminiResponse(JsonElement root)
        {
            if (TryGetPromptBlockReason(root, out var blockReason))
            {
                throw CreateGeminiResponseException(
                    "Gemini blocked the prompt before generating a response.",
                    blockReason,
                    root);
            }

            if (!TryGetFirstCandidate(root, out var candidate))
            {
                throw CreateGeminiResponseException(
                    "Gemini returned no response candidate.",
                    "missing_candidate",
                    root);
            }

            if (!TryGetFinishReason(candidate, out var finishReason))
            {
                throw CreateGeminiResponseException(
                    "Gemini returned an incomplete response without a terminal finish reason.",
                    "missing_finish_reason",
                    root);
            }

            if (!string.Equals(finishReason, SuccessfulFinishReason, StringComparison.Ordinal))
            {
                var message = string.Equals(finishReason, "MAX_TOKENS", StringComparison.Ordinal)
                    ? "Gemini reached the output-token limit; the partial response was not saved."
                    : $"Gemini stopped with finish reason '{finishReason}'; the response was not saved.";
                throw CreateGeminiResponseException(message, finishReason, root);
            }

        }

        private static StreamingContent? InspectGeminiStreamEnvelope(
            JsonElement root,
            GeminiStreamState state)
        {
            if (TryGetPromptBlockReason(root, out var blockReason))
            {
                state.Failed = true;
                return CreateGeminiStreamError(
                    "Gemini blocked the prompt before generating a response.",
                    blockReason,
                    root.GetRawText());
            }

            if (state.TerminalSeen && HasCandidateContentParts(root))
            {
                state.Failed = true;
                return CreateGeminiStreamError(
                    "Gemini emitted candidate content after the terminal finish reason; the partial stream was not saved.",
                    "content_after_terminal",
                    root.GetRawText());
            }

            if (!TryGetFirstCandidate(root, out var candidate) ||
                !TryGetFinishReason(candidate, out var finishReason))
            {
                return null;
            }

            state.FinishReason = finishReason;
            if (string.Equals(finishReason, SuccessfulFinishReason, StringComparison.Ordinal))
            {
                state.TerminalSeen = true;
                return null;
            }

            state.Failed = true;
            var message = string.Equals(finishReason, "MAX_TOKENS", StringComparison.Ordinal)
                ? "Gemini reached the output-token limit; the partial stream was not saved."
                : $"Gemini stream stopped with finish reason '{finishReason}'; the partial stream was not saved.";
            return CreateGeminiStreamError(message, finishReason, root.GetRawText());
        }

        private static bool HasCandidateContentParts(JsonElement root)
        {
            return TryGetFirstCandidate(root, out var candidate) &&
                   candidate.TryGetProperty("content", out var content) &&
                   content.ValueKind == JsonValueKind.Object &&
                   content.TryGetProperty("parts", out var parts) &&
                   parts.ValueKind == JsonValueKind.Array &&
                   parts.GetArrayLength() > 0;
        }

        private static bool TryGetFirstCandidate(JsonElement root, out JsonElement candidate)
        {
            candidate = default;
            if (!root.TryGetProperty("candidates", out var candidates) ||
                candidates.ValueKind != JsonValueKind.Array ||
                candidates.GetArrayLength() == 0)
            {
                return false;
            }

            candidate = candidates[0];
            return candidate.ValueKind == JsonValueKind.Object;
        }

        private static bool TryGetFinishReason(JsonElement candidate, out string finishReason)
        {
            finishReason = string.Empty;
            if (!candidate.TryGetProperty("finishReason", out var finishReasonElement) ||
                finishReasonElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            finishReason = finishReasonElement.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(finishReason);
        }

        private static bool TryGetPromptBlockReason(JsonElement root, out string blockReason)
        {
            blockReason = string.Empty;
            if (!root.TryGetProperty("promptFeedback", out var promptFeedback) ||
                !promptFeedback.TryGetProperty("blockReason", out var blockReasonElement) ||
                blockReasonElement.ValueKind != JsonValueKind.String)
            {
                return false;
            }

            blockReason = blockReasonElement.GetString() ?? string.Empty;
            return !string.IsNullOrWhiteSpace(blockReason) &&
                   !blockReason.EndsWith("UNSPECIFIED", StringComparison.Ordinal);
        }

        private static AIServiceException CreateGeminiResponseException(
            string message,
            string reason,
            JsonElement root)
        {
            return new AIServiceException(
                message,
                JsonSerializer.Serialize(new
                {
                    reason,
                    response = root.GetRawText()
                }),
                nameof(AIProvider.Google));
        }

        private static StreamingContent CreateGeminiStreamError(
            string message,
            string reason,
            string? response = null)
        {
            var metadata = new Dictionary<string, object>
            {
                ["provider"] = nameof(AIProvider.Google),
                ["reason"] = reason
            };
            if (!string.IsNullOrWhiteSpace(response))
                metadata["response"] = response;

            return new StreamingContent
            {
                Type = StreamingContentType.Error,
                Content = message,
                Metadata = metadata
            };
        }
    }
}
