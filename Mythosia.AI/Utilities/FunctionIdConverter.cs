using System;
using System.Security.Cryptography;
using System.Text;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Enums;

namespace Mythosia.AI.Utilities
{
    /// <summary>
    /// Handles ID conversion between different AI providers
    /// </summary>
    public static class FunctionIdConverter
    {
        /// <summary>
        /// Converts a function ID to Claude-compatible format
        /// </summary>
        public static string ToClaudeId(string originalId, IdSource source)
        {
            if (source == IdSource.Claude)
                return originalId;

            // Deterministic conversion using SHA256
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{source}:{originalId}"));
                var shortHash = Convert.ToBase64String(hash)
                    .Replace("+", "")
                    .Replace("/", "")
                    .Replace("=", "")
                    .Substring(0, Math.Min(20, Convert.ToBase64String(hash).Length));
                return $"toolu_{shortHash}";
            }
        }

        /// <summary>
        /// Converts a function ID to OpenAI-compatible format
        /// </summary>
        public static string ToOpenAIId(string originalId, IdSource source)
        {
            if (source == IdSource.OpenAI)
                return originalId;

            // Deterministic conversion using SHA256
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{source}:{originalId}"));
                var shortHash = Convert.ToBase64String(hash)
                    .Replace("+", "")
                    .Replace("/", "")
                    .Replace("=", "")
                    .Substring(0, Math.Min(20, Convert.ToBase64String(hash).Length));
                return $"call_{shortHash}";
            }
        }

        /// <summary>
        /// Converts a function ID to Gemini-compatible format
        /// </summary>
        public static string ToGeminiId(string originalId, IdSource source)
        {
            if (source == IdSource.Gemini)
                return originalId;

            // Gemini doesn't have strict ID requirements, but we'll keep consistency
            using (var sha = SHA256.Create())
            {
                var hash = sha.ComputeHash(Encoding.UTF8.GetBytes($"{source}:{originalId}"));
                var shortHash = Convert.ToBase64String(hash)
                    .Replace("+", "")
                    .Replace("/", "")
                    .Replace("=", "")
                    .Substring(0, Math.Min(20, Convert.ToBase64String(hash).Length));
                return $"func_{shortHash}";
            }
        }

        /// <summary>
        /// Gets the appropriate ID for the target provider
        /// </summary>
        public static string ConvertId(string originalId, IdSource source, AIProvider targetProvider)
        {
            return targetProvider switch
            {
                AIProvider.OpenAI => ToOpenAIId(originalId, source),
                AIProvider.Anthropic => ToClaudeId(originalId, source),
                AIProvider.Google => ToGeminiId(originalId, source),
                _ => originalId  // For others, use as-is
            };
        }
    }
}