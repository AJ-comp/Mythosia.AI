using System;
using System.Collections.Generic;
using System.Linq;
using Mythosia.AI.Models.Enums;

namespace Mythosia.AI.Models.Messages
{
    /// <summary>
    /// Represents a message in a conversation, supporting both text-only and multimodal content
    /// </summary>
    public class Message
    {
        public string Id { get; } = Guid.NewGuid().ToString();
        public ActorRole Role { get; set; }
        public string Content { get; set; } = string.Empty; // For backward compatibility
        public List<MessageContent> Contents { get; set; } = new List<MessageContent>();
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
    }
}