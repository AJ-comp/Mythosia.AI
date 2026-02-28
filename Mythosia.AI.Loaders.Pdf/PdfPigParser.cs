using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Mythosia.AI.Loaders;
using UglyToad.PdfPig;
using UglyToad.PdfPig.Parser;

namespace Mythosia.AI.Loaders.Pdf
{
    /// <summary>
    /// Parses PDF files using PdfPig.
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

        public Task<ParsedDocument> ParseAsync(string source, CancellationToken ct = default)
        {
            ct.ThrowIfCancellationRequested();

            using var document = OpenDocument(source);
            var sb = new StringBuilder();
            var pageCount = 0;

            foreach (var page in document.GetPages())
            {
                ct.ThrowIfCancellationRequested();
                pageCount++;

                if (_options.IncludePageNumbers)
                    sb.AppendLine($"[page {pageCount}]");

                var pageText = page.Text ?? string.Empty;
                if (_options.NormalizeWhitespace)
                    pageText = NormalizeWhitespace(pageText);

                sb.AppendLine(pageText);
                sb.AppendLine();
            }

            var parsed = new ParsedDocument(sb.ToString().Trim());
            if (_options.IncludeMetadata)
            {
                parsed.Metadata["page_count"] = pageCount.ToString(CultureInfo.InvariantCulture);
                AddDocumentInfoMetadata(document, parsed.Metadata);
            }

            return Task.FromResult(parsed);
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
