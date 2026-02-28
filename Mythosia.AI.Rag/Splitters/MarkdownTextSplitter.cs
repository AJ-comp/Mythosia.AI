using Mythosia.AI.Loaders;
using System;
using System.Collections.Generic;
using System.Text;

namespace Mythosia.AI.Rag.Splitters
{
    /// <summary>
    /// Splits markdown documents by H2 headings (##).
    /// </summary>
    public class MarkdownTextSplitter : ITextSplitter
    {
        /// <summary>
        /// Heading prefix used to start new chunks. Default is "##".
        /// </summary>
        public string HeadingPrefix { get; set; } = "##";

        public IReadOnlyList<RagChunk> Split(RagDocument document)
        {
            if (string.IsNullOrEmpty(document.Content))
                return Array.Empty<RagChunk>();

            var sections = SplitByHeading(document.Content);
            var chunks = new List<RagChunk>();
            int index = 0;

            foreach (var section in sections)
            {
                var content = section.Trim();
                if (content.Length == 0)
                    continue;

                var chunk = new RagChunk
                {
                    Id = $"{document.Id}_chunk_{index}",
                    DocumentId = document.Id,
                    Content = content,
                    Index = index,
                    Metadata = new Dictionary<string, string>(document.Metadata)
                };
                chunk.Metadata["source"] = document.Source;
                chunk.Metadata["chunk_index"] = index.ToString();

                chunks.Add(chunk);
                index++;
            }

            return chunks;
        }

        private List<string> SplitByHeading(string content)
        {
            var sections = new List<string>();
            var sb = new StringBuilder();
            var lines = content.Split(new[] { "\r\n", "\n" }, StringSplitOptions.None);

            foreach (var line in lines)
            {
                if (IsHeading(line) && sb.Length > 0)
                {
                    sections.Add(sb.ToString());
                    sb.Clear();
                }

                sb.AppendLine(line);
            }

            if (sb.Length > 0)
                sections.Add(sb.ToString());

            return sections;
        }

        private bool IsHeading(string line)
        {
            if (!line.StartsWith(HeadingPrefix, StringComparison.Ordinal))
                return false;

            if (line.Length == HeadingPrefix.Length)
                return true;

            return char.IsWhiteSpace(line[HeadingPrefix.Length]);
        }
    }
}
