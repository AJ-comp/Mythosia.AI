using Mythosia.AI.Loaders.Document;
using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using UglyToad.PdfPig;

namespace Mythosia.AI.Loaders.Pdf
{
    /// <summary>
    /// Parses PDF files using PdfPig into a structured DoclingDocument.
    /// </summary>
    public class PdfPigParser : IDocumentParser
    {
        private readonly PdfParserOptions _options;

        public PdfPigParser(PdfParserOptions? options = null)
        {
            _options = options ?? new PdfParserOptions();
        }

        public bool CanParse(string source)
            => string.Equals(Path.GetExtension(source), ".pdf", StringComparison.OrdinalIgnoreCase);

        public Task<DoclingDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            var docName = Path.GetFileNameWithoutExtension(source);
            var result = new DoclingDocument { Name = docName };

            using var document = OpenDocument(source);
            var pageCount = 0;

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                pageCount++;

                if (_options.IncludePageNumbers)
                    result.AddHeading($"Page {pageCount}", 2);

                var pageText = page.Text ?? string.Empty;
                if (_options.NormalizeWhitespace)
                    pageText = NormalizeWhitespace(pageText);

                if (!string.IsNullOrWhiteSpace(pageText))
                    result.AddParagraph(pageText);
            }

            return Task.FromResult(result);
        }

        private PdfDocument OpenDocument(string source)
        {
            if (string.IsNullOrWhiteSpace(_options.Password))
                return PdfDocument.Open(source);

            var parsingOptions = new ParsingOptions();
            TrySetProperty(parsingOptions, "Password", _options.Password);
            return PdfDocument.Open(source, parsingOptions);
        }

        private static void AddDocumentInfoMetadata(PdfDocument document, IDictionary<string, string> metadata)
        {
            var info = document.GetType().GetProperty("Information")?.GetValue(document);
            AddStringProperty(info, metadata, "Title", "title");
            AddStringProperty(info, metadata, "Author", "author");
            AddStringProperty(info, metadata, "Creator", "creator");
            AddStringProperty(info, metadata, "Subject", "subject");
            AddStringProperty(info, metadata, "Producer", "producer");
        }

        private static void AddStringProperty(object? source, IDictionary<string, string> metadata, string propertyName, string key)
        {
            if (source == null)
                return;

            var value = source.GetType().GetProperty(propertyName)?.GetValue(source) as string;
            if (!string.IsNullOrWhiteSpace(value) && !metadata.ContainsKey(key))
                metadata[key] = value;
        }

        private static void TrySetProperty(object target, string propertyName, object? value)
        {
            if (value == null)
                return;

            var property = target.GetType().GetProperty(propertyName);
            if (property != null && property.CanWrite)
                property.SetValue(target, value);
        }

        private static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            var inWhitespace = false;

            foreach (var ch in value)
            {
                if (char.IsWhiteSpace(ch))
                {
                    if (!inWhitespace)
                    {
                        sb.Append(' ');
                        inWhitespace = true;
                    }
                }
                else
                {
                    sb.Append(ch);
                    inWhitespace = false;
                }
            }

            return sb.ToString().Trim();
        }
    }
}
