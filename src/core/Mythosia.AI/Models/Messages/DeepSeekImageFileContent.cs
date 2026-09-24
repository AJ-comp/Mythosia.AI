using System;

namespace Mythosia.AI.Models.Messages
{
    /// <summary>An immutable reference to an image uploaded with DeepSeek's Files API.</summary>
    /// <remarks>The file belongs to the API key used to upload it. This is an image reference, not document input.</remarks>
    public sealed class DeepSeekImageFileContent : MessageContent
    {
        public override string Type => "file";
        public string FileId { get; }

        public DeepSeekImageFileContent(string fileId)
        {
            ValidateFileId(fileId, nameof(fileId));
            FileId = fileId;
        }

        public override object ToRequestFormat(string provider)
        {
            if (!string.Equals(provider, nameof(AIProvider.DeepSeek), StringComparison.Ordinal))
                throw new NotSupportedException("DeepSeek uploaded image references can only be used with DeepSeek.");
            return new { type = "file", file_id = FileId };
        }

        public override string GetDescription() => $"[DeepSeek image file: {FileId}]";

        /// <summary>Conservatively reserves the provider's maximum 1024 image tokens because remote dimensions are unknown.</summary>
        public override uint EstimateTokens() => 1024;

        internal static void ValidateFileId(string fileId, string parameterName)
        {
            if (string.IsNullOrWhiteSpace(fileId) || !fileId.StartsWith("file-api-", StringComparison.Ordinal) || fileId.Length <= 9)
                throw new ArgumentException("A DeepSeek file ID must start with 'file-api-' and contain an identifier.", parameterName);
            foreach (var character in fileId)
                if (!(character >= 'a' && character <= 'z') && !(character >= 'A' && character <= 'Z') &&
                    !(character >= '0' && character <= '9') && character != '-' && character != '_')
                    throw new ArgumentException("A DeepSeek file ID may contain only ASCII letters, digits, hyphens and underscores.", parameterName);
        }
    }
}
