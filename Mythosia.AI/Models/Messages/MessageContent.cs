using System;
using System.Collections.Generic;
using Mythosia.AI.Models.Enums;

namespace Mythosia.AI.Models.Messages
{
    /// <summary>
    /// Base class for all message content types (text, image, audio, etc.)
    /// </summary>
    public abstract class MessageContent
    {
        public abstract string Type { get; }

        /// <summary>
        /// Converts the content to the appropriate format for the specified AI provider
        /// </summary>
        public abstract object ToRequestFormat(AIProvider provider);

        /// <summary>
        /// Gets a display-friendly description of the content
        /// </summary>
        public abstract string GetDescription();

        /// <summary>
        /// Estimates the token count for this content
        /// </summary>
        public abstract uint EstimateTokens();
    }

    /// <summary>
    /// Text content in a message
    /// </summary>
    public class TextContent : MessageContent
    {
        public override string Type => "text";
        public string Text { get; set; }

        public TextContent(string text)
        {
            Text = text ?? string.Empty;
        }

        public override object ToRequestFormat(AIProvider provider)
        {
            return provider switch
            {
                AIProvider.OpenAI => new { type = "text", text = Text },
                AIProvider.xAI => new { type = "text", text = Text },
                AIProvider.Anthropic => new { type = "text", text = Text },
                AIProvider.Google => new { text = Text },
                _ => Text
            };
        }

        public override string GetDescription() => Text.Length > 50
            ? Text.Substring(0, 47) + "..."
            : Text;

        public override uint EstimateTokens() => (uint)(Text.Length / 4); // Rough estimate
    }

    /// <summary>
    /// Image content in a message
    /// </summary>
    public class ImageContent : MessageContent
    {
        public override string Type => "image";
        public string? Url { get; set; }
        public byte[]? Data { get; set; }
        public string? MimeType { get; set; }
        public bool IsHighDetail { get; set; } = false;

        // Constructor for URL-based images
        public ImageContent(string url)
        {
            Url = url;
        }

        // Constructor for binary image data
        public ImageContent(byte[] data, string mimeType)
        {
            Data = data;
            MimeType = mimeType;
        }

        public string GetBase64Url()
        {
            if (Data != null && !string.IsNullOrEmpty(MimeType))
            {
                return $"data:{MimeType};base64,{Convert.ToBase64String(Data)}";
            }
            return Url ?? string.Empty;
        }

        public override object ToRequestFormat(AIProvider provider)
        {
            var imageUrl = GetBase64Url();

            return provider switch
            {
                AIProvider.OpenAI => new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = imageUrl,
                        detail = IsHighDetail ? "high" : "low"
                    }
                },
                AIProvider.xAI => new
                {
                    type = "image_url",
                    image_url = new
                    {
                        url = imageUrl,
                        detail = IsHighDetail ? "high" : "low"
                    }
                },
                AIProvider.Anthropic => new
                {
                    type = "image",
                    source = new
                    {
                        type = Data != null ? "base64" : "url",
                        media_type = MimeType ?? "image/jpeg",
                        data = Data != null ? Convert.ToBase64String(Data) : null,
                        url = Url
                    }
                },
                AIProvider.Google => new
                {
                    inline_data = new
                    {
                        mime_type = MimeType ?? "image/jpeg",
                        data = Data != null ? Convert.ToBase64String(Data) : null
                    }
                },
                _ => throw new NotSupportedException($"Image content not supported for {provider}")
            };
        }

        public override string GetDescription()
        {
            if (!string.IsNullOrEmpty(Url))
                return $"[Image URL: {Url}]";
            else if (Data != null)
                return $"[Image: {Data.Length} bytes, {MimeType}]";
            else
                return "[Empty Image]";
        }

        public override uint EstimateTokens() => IsHighDetail ? 765u : 170u; // OpenAI estimates
    }

    /// <summary>
    /// Audio content in a message (for future expansion)
    /// </summary>
    public class AudioContent : MessageContent
    {
        public override string Type => "audio";
        public byte[]? Data { get; set; }
        public string? MimeType { get; set; }
        public TimeSpan Duration { get; set; }

        public AudioContent(byte[] data, string mimeType)
        {
            Data = data;
            MimeType = mimeType;
        }

        public override object ToRequestFormat(AIProvider provider)
        {
            throw new NotSupportedException($"Audio content not yet supported for {provider}");
        }

        public override string GetDescription()
            => $"[Audio: {Duration.TotalSeconds:F1}s, {MimeType}]";

        public override uint EstimateTokens() => (uint)(Duration.TotalSeconds * 10); // Rough estimate
    }
}