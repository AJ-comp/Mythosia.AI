using Mythosia.AI.Loaders;
using System;
using System.Collections.Generic;

namespace Mythosia.AI.Rag
{
    /// <summary>
    /// Builder for configuring per-source document routing (extension, loader, splitter).
    /// </summary>
    public sealed class DocumentSourceBuilder
    {
        private readonly HashSet<string> _extensions = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        private IDocumentLoader? _loader;
        private ITextSplitter? _textSplitter;

        /// <summary>
        /// Filters documents by a single file extension (e.g., ".pdf").
        /// </summary>
        public DocumentSourceBuilder WithExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                throw new ArgumentException("Extension cannot be null or whitespace.", nameof(extension));

            _extensions.Add(NormalizeExtension(extension));
            return this;
        }

        /// <summary>
        /// Filters documents by multiple file extensions.
        /// </summary>
        public DocumentSourceBuilder WithExtensions(params string[] extensions)
        {
            if (extensions == null)
                throw new ArgumentNullException(nameof(extensions));

            foreach (var extension in extensions)
            {
                WithExtension(extension);
            }

            return this;
        }

        /// <summary>
        /// Sets the loader to use for matching documents.
        /// </summary>
        public DocumentSourceBuilder WithLoader(IDocumentLoader loader)
        {
            _loader = loader ?? throw new ArgumentNullException(nameof(loader));
            return this;
        }

        /// <summary>
        /// Sets a per-source text splitter for matching documents.
        /// </summary>
        public DocumentSourceBuilder WithTextSplitter(ITextSplitter textSplitter)
        {
            _textSplitter = textSplitter ?? throw new ArgumentNullException(nameof(textSplitter));
            return this;
        }

        internal DocumentSourceOptions Build()
        {
            return new DocumentSourceOptions(_extensions, _loader, _textSplitter);
        }

        internal static string NormalizeExtension(string extension)
        {
            if (string.IsNullOrWhiteSpace(extension))
                return string.Empty;

            var normalized = extension.Trim();
            if (!normalized.StartsWith(".", StringComparison.Ordinal))
                normalized = "." + normalized;

            return normalized.ToLowerInvariant();
        }

        internal sealed class DocumentSourceOptions
        {
            private readonly HashSet<string> _extensions;

            public IReadOnlyCollection<string> Extensions => _extensions;
            public IDocumentLoader? Loader { get; }
            public ITextSplitter? TextSplitter { get; }

            public DocumentSourceOptions(
                IEnumerable<string> extensions,
                IDocumentLoader? loader,
                ITextSplitter? textSplitter)
            {
                _extensions = new HashSet<string>(extensions ?? Array.Empty<string>(), StringComparer.OrdinalIgnoreCase);
                Loader = loader;
                TextSplitter = textSplitter;
            }

            public bool MatchesExtension(string extension)
            {
                if (_extensions.Count == 0)
                    return true;

                var normalized = NormalizeExtension(extension);
                return _extensions.Contains(normalized);
            }
        }
    }
}
