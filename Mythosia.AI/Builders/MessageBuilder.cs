using System;
using System.Collections.Generic;
using System.IO;
using System.Threading.Tasks;
using Mythosia.AI.Models;
using Mythosia.AI.Models.Enums;
using Mythosia.AI.Models.Messages;
using Mythosia.AI.Utilities;

namespace Mythosia.AI.Builders
{
    /// <summary>
    /// Fluent builder for creating messages with multiple content types
    /// </summary>
    public class MessageBuilder
    {
        private readonly List<MessageContent> _contents = new List<MessageContent>();
        private ActorRole _role = ActorRole.User;

        /// <summary>
        /// Creates a new MessageBuilder instance
        /// </summary>
        public static MessageBuilder Create() => new MessageBuilder();

        /// <summary>
        /// Sets the role for the message
        /// </summary>
        public MessageBuilder WithRole(ActorRole role)
        {
            _role = role;
            return this;
        }

        /// <summary>
        /// Adds text content to the message
        /// </summary>
        public MessageBuilder AddText(string text)
        {
            if (!string.IsNullOrEmpty(text))
            {
                _contents.Add(new TextContent(text));
            }
            return this;
        }

        /// <summary>
        /// Adds image content from byte array
        /// </summary>
        public MessageBuilder AddImage(byte[] imageData, string mimeType)
        {
            if (imageData != null && imageData.Length > 0)
            {
                _contents.Add(new ImageContent(imageData, mimeType));
            }
            return this;
        }

        /// <summary>
        /// Adds image content from file path
        /// </summary>
        public MessageBuilder AddImage(string imagePath)
        {
            if (File.Exists(imagePath))
            {
                var imageData = File.ReadAllBytes(imagePath);
                var mimeType = MimeTypes.GetFromPath(imagePath);
                return AddImage(imageData, mimeType);
            }
            throw new FileNotFoundException($"Image file not found: {imagePath}");
        }

        /// <summary>
        /// Adds image content from file path asynchronously
        /// </summary>
        public async Task<MessageBuilder> AddImageAsync(string imagePath)
        {
            if (File.Exists(imagePath))
            {
                var imageData = await File.ReadAllBytesAsync(imagePath);
                var mimeType = MimeTypes.GetFromPath(imagePath);
                return AddImage(imageData, mimeType);
            }
            throw new FileNotFoundException($"Image file not found: {imagePath}");
        }

        /// <summary>
        /// Adds image content from URL
        /// </summary>
        public MessageBuilder AddImageUrl(string url)
        {
            if (!string.IsNullOrEmpty(url))
            {
                _contents.Add(new ImageContent(url));
            }
            return this;
        }

        /// <summary>
        /// Adds multiple images from file paths
        /// </summary>
        public MessageBuilder AddImages(params string[] imagePaths)
        {
            foreach (var path in imagePaths)
            {
                AddImage(path);
            }
            return this;
        }

        /// <summary>
        /// Adds multiple images from URLs
        /// </summary>
        public MessageBuilder AddImageUrls(params string[] urls)
        {
            foreach (var url in urls)
            {
                AddImageUrl(url);
            }
            return this;
        }

        /// <summary>
        /// Sets high detail mode for the last added image
        /// </summary>
        public MessageBuilder WithHighDetail()
        {
            if (_contents.Count > 0 && _contents[_contents.Count - 1] is ImageContent imageContent)
            {
                imageContent.IsHighDetail = true;
            }
            return this;
        }

        /// <summary>
        /// Adds audio content (for future use)
        /// </summary>
        public MessageBuilder AddAudio(byte[] audioData, string mimeType)
        {
            if (audioData != null && audioData.Length > 0)
            {
                _contents.Add(new AudioContent(audioData, mimeType));
            }
            return this;
        }

        /// <summary>
        /// Clears all content
        /// </summary>
        public MessageBuilder Clear()
        {
            _contents.Clear();
            return this;
        }

        /// <summary>
        /// Builds the final Message object
        /// </summary>
        public Message Build()
        {
            if (_contents.Count == 0)
            {
                throw new InvalidOperationException("Cannot build message with no content");
            }

            // If only text content, use the simple constructor
            if (_contents.Count == 1 && _contents[0] is TextContent textContent)
            {
                return new Message(_role, textContent.Text);
            }

            // Otherwise use the multimodal constructor
            return new Message(_role, new List<MessageContent>(_contents));
        }

        /// <summary>
        /// Builds and returns the message, then clears the builder
        /// </summary>
        public Message BuildAndClear()
        {
            var message = Build();
            Clear();
            _role = ActorRole.User; // Reset to default
            return message;
        }

        /// <summary>
        /// Checks if the builder has any content
        /// </summary>
        public bool HasContent => _contents.Count > 0;

        /// <summary>
        /// Gets the current content count
        /// </summary>
        public int ContentCount => _contents.Count;

        /// <summary>
        /// Creates a message with text and image in one call
        /// </summary>
        public static Message QuickTextImage(string text, string imagePath, ActorRole role = ActorRole.User)
        {
            return Create()
                .WithRole(role)
                .AddText(text)
                .AddImage(imagePath)
                .Build();
        }

        /// <summary>
        /// Creates a message with text and image URL in one call
        /// </summary>
        public static Message QuickTextImageUrl(string text, string imageUrl, ActorRole role = ActorRole.User)
        {
            return Create()
                .WithRole(role)
                .AddText(text)
                .AddImageUrl(imageUrl)
                .Build();
        }
    }
}