using Mythosia.AI.Loaders;
using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;

namespace Mythosia.AI.Rag.Loaders
{
    /// <summary>
    /// Loads plain text files (.txt, .md, .csv, .json, .xml, .html, etc.) as RagDocuments.
    /// </summary>
    public class PlainTextDocumentLoader : IDocumentLoader
    {
        public async Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            if (!File.Exists(source))
                throw new FileNotFoundException($"Document file not found: {source}", source);

            var content = await File.ReadAllTextAsync(source, cancellationToken);
            var fileName = Path.GetFileName(source);

            var doc = new RagDocument
            {
                Id = fileName,
                Content = content,
                Source = source,
                Metadata =
                {
                    ["filename"] = fileName,
                    ["extension"] = Path.GetExtension(source).ToLowerInvariant()
                }
            };

            return new[] { doc };
        }
    }

    /// <summary>
    /// Loads all supported text files from a directory recursively.
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

        public async Task<IReadOnlyList<RagDocument>> LoadAsync(string source, CancellationToken cancellationToken = default)
        {
            if (!Directory.Exists(source))
                throw new DirectoryNotFoundException($"Document directory not found: {source}");

            var files = Directory.GetFiles(source, "*", SearchOption.AllDirectories);
            var docs = new List<RagDocument>();

            foreach (var file in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var ext = Path.GetExtension(file);
                if (!_extensions.Contains(ext))
                    continue;

                var content = await File.ReadAllTextAsync(file, cancellationToken);
                var relativePath = Path.GetRelativePath(source, file);

                docs.Add(new RagDocument
                {
                    Id = relativePath,
                    Content = content,
                    Source = file,
                    Metadata =
                    {
                        ["filename"] = Path.GetFileName(file),
                        ["extension"] = ext.ToLowerInvariant(),
                        ["relative_path"] = relativePath
                    }
                });
            }

            return docs;
        }
    }
}
