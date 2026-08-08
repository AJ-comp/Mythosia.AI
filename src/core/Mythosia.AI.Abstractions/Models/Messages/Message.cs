using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.CompilerServices;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;

namespace Mythosia.AI.Models.Messages
{
    /// <summary>
    /// Represents a message in a conversation, supporting both text-only and multimodal content
    /// </summary>
    [DebuggerDisplay("{GetDebuggerDisplay(),nq}")]
    public class Message
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public ActorRole Role { get; set; }
        public string Content { get; set; } = string.Empty; // For backward compatibility
        public List<MessageContent> Contents { get; set; } = new List<MessageContent>();

        /// <summary>
        /// Optional metadata for the message (e.g., function call info)
        /// </summary>
        public Dictionary<string, object>? Metadata { get; set; }

        /// <summary>
        /// Function calls requested in this assistant turn, if any.
        /// </summary>
        public FunctionCallBatch? FunctionCallBatch { get; set; }

        /// <summary>
        /// Function results returned in this tool turn, if any.
        /// </summary>
        public FunctionCallResultBatch? FunctionCallResultBatch { get; set; }

        public DateTime Timestamp { get; set; } = DateTime.UtcNow;

        /// <summary>
        /// Indicates whether this message contains multimodal content
        /// </summary>
        public bool HasMultimodalContent => Contents.Any();

        /// <summary>
        /// Text-only constructor (backward compatible)
        /// </summary>
        public Message(ActorRole role, string content)
        {
            Role = role;
            Content = content ?? string.Empty;
        }

        /// <summary>
        /// Multimodal constructor
        /// </summary>
        public Message(ActorRole role, List<MessageContent> contents)
        {
            Role = role;
            Contents = contents ?? new List<MessageContent>();

            // Extract text content for backward compatibility
            Content = string.Join(" ", contents
                .OfType<TextContent>()
                .Select(c => c.Text));
        }

        /// <summary>
        /// Single content constructor
        /// </summary>
        public Message(ActorRole role, MessageContent content)
            : this(role, new List<MessageContent> { content })
        {
        }

        /// <summary>
        /// Gets a display-friendly representation of the message
        /// </summary>
        public string GetDisplayText()
        {
            if (FunctionCallBatch != null)
            {
                var calls = string.Join(", ", FunctionCallBatch.Calls.Select(call => call.Name));
                return string.IsNullOrEmpty(Content)
                    ? $"[함수 호출: {calls}]"
                    : $"{Content} [함수 호출: {calls}]";
            }

            if (FunctionCallResultBatch != null)
            {
                var results = string.Join("; ", FunctionCallResultBatch.Results.Select(result =>
                    $"{result.Call.Name}: {result.Content}"));
                return $"[함수 결과: {results}]";
            }

            if (HasMultimodalContent)
            {
                var parts = Contents.Select(c =>
                {
                    if (c is TextContent text)
                        return text.Text;
                    else if (c is ImageContent)
                        return "[이미지]";
                    else if (c is AudioContent)
                        return "[오디오]";
                    else
                        return "[미디어]";
                });
                return string.Join(" ", parts);
            }
            return Content;
        }

        /// <summary>
        /// Converts the message to the appropriate format for the specified AI provider
        /// </summary>
        public object ToRequestFormat(string provider)
        {
            var role = Role.ToDescription();

            // Text-only message
            if (!HasMultimodalContent)
            {
                return new { role, content = Content };
            }

            // Multimodal message
            switch (provider)
            {
                case nameof(AIProvider.OpenAI):
                    return new
                    {
                        role,
                        content = Contents.Select(c => c.ToRequestFormat(provider)).ToList()
                    };

                case nameof(AIProvider.Anthropic):
                    return new
                    {
                        role,
                        content = Contents.Select(c => c.ToRequestFormat(provider)).ToList()
                    };

                case nameof(AIProvider.Google):
                    var parts = Contents.Select(c => c.ToRequestFormat(provider)).ToList();
                    return new
                    {
                        role = role == "assistant" ? "model" : role,
                        parts
                    };

                default:
                    // Fallback to text-only for unsupported providers
                    return new { role, content = GetDisplayText() };
            }
        }

        /// <summary>
        /// Estimates the total token count for this message
        /// </summary>
        public uint EstimateTokens()
        {
            if (FunctionCallBatch != null)
            {
                long estimatedLength = Content?.Length ?? 0;
                foreach (var call in FunctionCallBatch.Calls)
                {
                    estimatedLength += call.Id?.Length ?? 0;
                    estimatedLength += call.Name?.Length ?? 0;
                    estimatedLength += EstimateSerializedLength(call.Arguments);
                    estimatedLength += 16; // Tool-call fields and JSON punctuation.
                }

                return ToTokenEstimate(estimatedLength);
            }

            if (FunctionCallResultBatch != null)
            {
                long estimatedLength = 0;
                foreach (var result in FunctionCallResultBatch.Results)
                {
                    estimatedLength += result.Call.Id?.Length ?? 0;
                    estimatedLength += result.Call.Name?.Length ?? 0;
                    estimatedLength += result.Content?.Length ?? 0;
                    estimatedLength += 12; // Result fields and JSON punctuation.
                }

                return ToTokenEstimate(estimatedLength);
            }

            if (HasMultimodalContent)
            {
                return (uint)Contents.Sum(c => c.EstimateTokens());
            }
            return (uint)(Content.Length / 4); // Rough estimate for text
        }

        private static int EstimateSerializedLength(object? value)
        {
            return (int)Math.Min(
                int.MaxValue,
                EstimateSerializedLengthCore(
                    value,
                    new HashSet<object>(ReferenceIdentityComparer.Instance),
                    0));
        }

        private static long EstimateSerializedLengthCore(
            object? value,
            HashSet<object> traversalPath,
            int depth)
        {
            if (value == null)
                return 4;

            if (value is string text)
                return text.Length + 2;

            if (depth >= 64)
                return 16;

            var tracksReference = !value.GetType().IsValueType;
            if (tracksReference && !traversalPath.Add(value))
                return 4;

            try
            {
                if (value is IDictionary dictionary)
                {
                    long length = 2;
                    foreach (DictionaryEntry item in dictionary)
                    {
                        length += SafeStringLength(item.Key);
                        length += EstimateSerializedLengthCore(
                            item.Value,
                            traversalPath,
                            depth + 1) + 4;
                    }

                    return length;
                }

                if (value is IEnumerable sequence)
                {
                    long length = 2;
                    var count = 0;
                    foreach (var item in sequence)
                    {
                        if (count++ >= 10_000)
                        {
                            length += 16;
                            break;
                        }

                        length += EstimateSerializedLengthCore(
                            item,
                            traversalPath,
                            depth + 1) + 1;
                    }
                    return length;
                }

                return SafeStringLength(value);
            }
            finally
            {
                if (tracksReference)
                    traversalPath.Remove(value);
            }
        }

        private static int SafeStringLength(object? value)
        {
            try
            {
                return value?.ToString()?.Length ?? 0;
            }
            catch
            {
                return 16;
            }
        }

        private static uint ToTokenEstimate(long estimatedLength)
        {
            if (estimatedLength <= 0)
                return 0;

            return (uint)Math.Min(uint.MaxValue, estimatedLength / 4);
        }

        /// <summary>
        /// Creates a deep copy of the message
        /// </summary>
        public Message Clone()
        {
            Message cloned;

            if (HasMultimodalContent)
            {
                cloned = new Message(Role, new List<MessageContent>(Contents))
                {
                    Timestamp = Timestamp
                };
            }
            else
            {
                cloned = new Message(Role, Content)
                {
                    Timestamp = Timestamp
                };
            }

            if (Metadata != null)
            {
                cloned.Metadata = ObjectGraphSnapshot.CloneDictionary(Metadata);
            }

            cloned.FunctionCallBatch = FunctionCallBatch?.Clone();
            cloned.FunctionCallResultBatch = FunctionCallResultBatch?.Clone();

            return cloned;
        }

        private sealed class ReferenceIdentityComparer : IEqualityComparer<object>
        {
            public static ReferenceIdentityComparer Instance { get; } = new ReferenceIdentityComparer();

            public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);

            public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
        }


        /// <summary>
        /// Gets a debug-friendly display string
        /// </summary>
        private string GetDebuggerDisplay()
        {
            // 텍스트 내용 가져오기
            string text = !string.IsNullOrEmpty(Content)
                ? Content
                : string.Join(" ", Contents.OfType<TextContent>().Select(c => c.Text));

            // 50자로 제한
            if (text.Length > 50)
                text = text.Substring(0, 47) + "...";

            // 멀티모달 정보
            string extras = "";
            if (HasMultimodalContent)
            {
                var imageCount = Contents.OfType<ImageContent>().Count();
                if (imageCount > 0)
                    extras += $" [🖼️×{imageCount}]";
            }

            // 메타데이터 정보 (function 등)
            if (Metadata?.ContainsKey("function_name") == true)
                extras += $" [fn:{Metadata["function_name"]}]";

            return $"{Role}: {text}{extras}";
        }
    }
}
