using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using Mythosia.AI.Models.Enums;

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
        public Dictionary<string, object> Metadata { get; set; }

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
        public object ToRequestFormat(AIProvider provider)
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
                case AIProvider.OpenAI:
                    return new
                    {
                        role,
                        content = Contents.Select(c => c.ToRequestFormat(provider)).ToList()
                    };

                case AIProvider.Anthropic:
                    return new
                    {
                        role,
                        content = Contents.Select(c => c.ToRequestFormat(provider)).ToList()
                    };

                case AIProvider.Google:
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
            if (HasMultimodalContent)
            {
                return (uint)Contents.Sum(c => c.EstimateTokens());
            }
            return (uint)(Content.Length / 4); // Rough estimate for text
        }

        /// <summary>
        /// Creates a deep copy of the message
        /// </summary>
        public Message Clone()
        {
            if (HasMultimodalContent)
            {
                return new Message(Role, new List<MessageContent>(Contents))
                {
                    Timestamp = Timestamp
                };
            }
            return new Message(Role, Content)
            {
                Timestamp = Timestamp
            };
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