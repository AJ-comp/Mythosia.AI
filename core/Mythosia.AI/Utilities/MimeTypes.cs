using System;
using System.Collections.Generic;
using System.IO;

namespace Mythosia.AI.Utilities
{
    /// <summary>
    /// Utility class for handling MIME types
    /// </summary>
    public static class MimeTypes
    {
        private static readonly Dictionary<string, string> MimeTypeMap = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            // Images
            { ".jpg", "image/jpeg" },
            { ".jpeg", "image/jpeg" },
            { ".png", "image/png" },
            { ".gif", "image/gif" },
            { ".bmp", "image/bmp" },
            { ".webp", "image/webp" },
            { ".svg", "image/svg+xml" },
            { ".ico", "image/x-icon" },
            { ".tiff", "image/tiff" },
            { ".tif", "image/tiff" },
            
            // Audio
            { ".mp3", "audio/mpeg" },
            { ".wav", "audio/wav" },
            { ".ogg", "audio/ogg" },
            { ".m4a", "audio/mp4" },
            { ".flac", "audio/flac" },
            { ".aac", "audio/aac" },
            { ".wma", "audio/x-ms-wma" },
            { ".opus", "audio/opus" },
            
            // Video
            { ".mp4", "video/mp4" },
            { ".avi", "video/x-msvideo" },
            { ".mov", "video/quicktime" },
            { ".wmv", "video/x-ms-wmv" },
            { ".flv", "video/x-flv" },
            { ".webm", "video/webm" },
            { ".mkv", "video/x-matroska" },
            { ".m4v", "video/x-m4v" },
            
            // Documents
            { ".pdf", "application/pdf" },
            { ".doc", "application/msword" },
            { ".docx", "application/vnd.openxmlformats-officedocument.wordprocessingml.document" },
            { ".xls", "application/vnd.ms-excel" },
            { ".xlsx", "application/vnd.openxmlformats-officedocument.spreadsheetml.sheet" },
            { ".ppt", "application/vnd.ms-powerpoint" },
            { ".pptx", "application/vnd.openxmlformats-officedocument.presentationml.presentation" },
            { ".txt", "text/plain" },
            { ".csv", "text/csv" },
            { ".json", "application/json" },
            { ".xml", "application/xml" },
            { ".html", "text/html" },
            { ".htm", "text/html" },
            
            // Archives
            { ".zip", "application/zip" },
            { ".rar", "application/x-rar-compressed" },
            { ".7z", "application/x-7z-compressed" },
            { ".tar", "application/x-tar" },
            { ".gz", "application/gzip" },
            
            // Other
            { ".bin", "application/octet-stream" },
            { ".exe", "application/x-msdownload" },
            { ".dll", "application/x-msdownload" }
        };

        /// <summary>
        /// Gets the MIME type from a file path
        /// </summary>
        public static string GetFromPath(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
                return "application/octet-stream";

            var extension = Path.GetExtension(filePath);
            if (string.IsNullOrEmpty(extension))
                return "application/octet-stream";

            return MimeTypeMap.TryGetValue(extension, out var mimeType)
                ? mimeType
                : "application/octet-stream";
        }

        /// <summary>
        /// Gets the MIME type from a file extension
        /// </summary>
        public static string GetFromExtension(string extension)
        {
            if (string.IsNullOrEmpty(extension))
                return "application/octet-stream";

            if (!extension.StartsWith("."))
                extension = "." + extension;

            return MimeTypeMap.TryGetValue(extension, out var mimeType)
                ? mimeType
                : "application/octet-stream";
        }

        /// <summary>
        /// Checks if a MIME type represents an image
        /// </summary>
        public static bool IsImage(string mimeType)
        {
            return !string.IsNullOrEmpty(mimeType) && mimeType.StartsWith("image/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a MIME type represents audio
        /// </summary>
        public static bool IsAudio(string mimeType)
        {
            return !string.IsNullOrEmpty(mimeType) && mimeType.StartsWith("audio/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a MIME type represents video
        /// </summary>
        public static bool IsVideo(string mimeType)
        {
            return !string.IsNullOrEmpty(mimeType) && mimeType.StartsWith("video/", StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Checks if a file path represents an image based on its extension
        /// </summary>
        public static bool IsImageFile(string filePath)
        {
            var mimeType = GetFromPath(filePath);
            return IsImage(mimeType);
        }

        /// <summary>
        /// Gets the file extension from a MIME type
        /// </summary>
        public static string GetExtension(string mimeType)
        {
            foreach (var kvp in MimeTypeMap)
            {
                if (kvp.Value.Equals(mimeType, StringComparison.OrdinalIgnoreCase))
                {
                    return kvp.Key;
                }
            }
            return ".bin"; // Default extension
        }

        /// <summary>
        /// Validates if a MIME type is supported for multimodal content
        /// </summary>
        public static bool IsSupportedMultimodal(string mimeType)
        {
            // Currently supporting images only for multimodal
            // Can be extended for audio/video when providers support them
            return IsImage(mimeType);
        }

        /// <summary>
        /// Gets a human-readable description of the MIME type
        /// </summary>
        public static string GetDescription(string mimeType)
        {
            return mimeType switch
            {
                "image/jpeg" => "JPEG Image",
                "image/png" => "PNG Image",
                "image/gif" => "GIF Image",
                "image/webp" => "WebP Image",
                "audio/mpeg" => "MP3 Audio",
                "audio/wav" => "WAV Audio",
                "video/mp4" => "MP4 Video",
                "application/pdf" => "PDF Document",
                "text/plain" => "Plain Text",
                _ => mimeType
            };
        }
    }
}