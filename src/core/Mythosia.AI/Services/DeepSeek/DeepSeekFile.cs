using System.Collections.Generic;

namespace Mythosia.AI.Services.DeepSeek
{
    /// <summary>Metadata for an image stored by DeepSeek's Files API.</summary>
    public sealed class DeepSeekFile
    {
        public string Id { get; }
        public string Object => "file";
        public long Bytes { get; }
        /// <summary>Creation time as a Unix timestamp in seconds.</summary>
        public long CreatedAt { get; }
        public string Filename { get; }
        public string Purpose => "user_data";
        /// <summary>Expiration as a Unix timestamp in seconds, or null for a permanent file.</summary>
        public long? ExpiresAt { get; }

        internal DeepSeekFile(string id, long bytes, long createdAt, string filename, long? expiresAt)
        {
            Id = id;
            Bytes = bytes;
            CreatedAt = createdAt;
            Filename = filename;
            ExpiresAt = expiresAt;
        }
    }

    /// <summary>A single page of image file metadata. Pass LastId as the next request's After cursor when HasMore is true.</summary>
    public sealed class DeepSeekFileList
    {
        public string Object => "list";
        public IReadOnlyList<DeepSeekFile> Data { get; }
        public string? FirstId { get; }
        public string? LastId { get; }
        public bool HasMore { get; }

        internal DeepSeekFileList(IReadOnlyList<DeepSeekFile> data, string? firstId, string? lastId, bool hasMore)
        {
            Data = data;
            FirstId = firstId;
            LastId = lastId;
            HasMore = hasMore;
        }
    }

    /// <summary>The server's acknowledgement of a file deletion.</summary>
    public sealed class DeepSeekFileDeletion
    {
        public string Id { get; }
        public string Object => "file";
        public bool Deleted { get; }

        internal DeepSeekFileDeletion(string id, bool deleted)
        {
            Id = id;
            Deleted = deleted;
        }
    }

    public enum DeepSeekFileOrder { Ascending, Descending }

    /// <summary>Cursor and ordering for a single Files API list request.</summary>
    public sealed class DeepSeekFileListOptions
    {
        public string? After { get; set; }
        /// <summary>Page size from 1 to 1000; null uses the provider default of 1000.</summary>
        public int? Limit { get; set; }
        public DeepSeekFileOrder Order { get; set; } = DeepSeekFileOrder.Ascending;
    }
}
