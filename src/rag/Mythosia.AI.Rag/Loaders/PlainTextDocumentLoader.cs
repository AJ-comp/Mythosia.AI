using Mythosia.Documents;
using Mythosia.Documents.Elements;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Loaders
{
    /// <summary>
    /// Loads plain text files (.txt, .md, .csv, .json, .xml, .html, etc.) as DoclingDocuments.
    /// Source is the normalized absolute file path, used as the automatic RAG document ID.
    /// </summary>
    public class PlainTextDocumentLoader : IDocumentLoader
    {
        public async Task<IReadOnlyList<DoclingDocument>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException($"Document file not found: {source}", source);

            var fullPath = Path.GetFullPath(source);
            var content = await File.ReadAllTextAsync(fullPath, cancellationToken);
            var fileName = Path.GetFileName(fullPath);

            var doc = new DoclingDocument
            {
                Name = Path.GetFileNameWithoutExtension(fullPath),
                Source = fullPath,
                RawContent = content,
            };
            doc.Metadata["filename"] = fileName;
            doc.Metadata["extension"] = Path.GetExtension(fullPath).ToLowerInvariant();

            return new[] { doc };
        }
    }

    /// <summary>
    /// Loads all supported text files from a directory recursively.
    /// Source is the normalized absolute file path; relative_path metadata retains the display path.
    /// </summary>
    public class DirectoryDocumentLoader : IDocumentLoader
    {
        private static readonly HashSet<string> DefaultExtensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            ".txt", ".md", ".csv", ".json", ".xml", ".html", ".htm", ".log", ".yaml", ".yml", ".ini", ".cfg", ".conf"
        };

        private readonly HashSet<string> _extensions;

        /// <summary>
        /// Creates a directory loader.
        /// </summary>
        /// <param name="extensions">
        /// Allowed file extensions. If null, uses a default set of text file extensions.
        /// </param>
        public DirectoryDocumentLoader(IEnumerable<string>? extensions = null)
        {
            _extensions = extensions != null
                ? new HashSet<string>(extensions, StringComparer.OrdinalIgnoreCase)
                : DefaultExtensions;
        }

        public async Task<IReadOnlyList<DoclingDocument>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"Document directory not found: {source}");

            var directoryPath = Path.GetFullPath(source);
            var files = Directory.GetFiles(directoryPath, "*", SearchOption.AllDirectories);
            var docs = new List<DoclingDocument>();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(file);
                if (!_extensions.Contains(ext))
                    continue;

                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var relativePath = Path.GetRelativePath(directoryPath, file);

                var doc = new DoclingDocument
                {
                    Name = Path.GetFileNameWithoutExtension(file),
                    Source = Path.GetFullPath(file),
                    RawContent = content,
                };
                doc.Metadata["filename"] = Path.GetFileName(file);
                doc.Metadata["extension"] = ext.ToLowerInvariant();
                doc.Metadata["relative_path"] = relativePath;

                docs.Add(doc);
            }

            return docs;
        }
    }
}
