using System.Collections.Generic;
using System.Text;
using DocumentFormat.OpenXml.Packaging;

namespace Mythosia.AI.Loaders.Office
{
    internal static class OfficeParserUtilities
    {
        public static string NormalizeWhitespace(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return string.Empty;

            var sb = new StringBuilder(value.Length);
            var inWhitespace = false;
            var consecutiveNewlines = 0;

            foreach (var ch in value)
            {
                if (ch == '\r')
                    continue;

                if (ch == '\n')
                {
                    consecutiveNewlines++;
                    if (consecutiveNewlines <= 2)
                        sb.Append('\n');
                    inWhitespace = false;
                }
                else if (char.IsWhiteSpace(ch))
                {
                    consecutiveNewlines = 0;
                    if (!inWhitespace)
                    {
                        sb.Append(' ');
                        inWhitespace = true;
                    }
                }
                else
                {
                    consecutiveNewlines = 0;
                    sb.Append(ch);
                    inWhitespace = false;
                }
            }

            return sb.ToString().Trim();
        }

        public static void AddPackageMetadata(OpenXmlPackage package, IDictionary<string, string> metadata)
        {
            if (package == null || metadata == null)
                return;

            var props = package.PackageProperties;
            TryAdd(metadata, "title", props?.Title);
            TryAdd(metadata, "author", props?.Creator);
            TryAdd(metadata, "subject", props?.Subject);
            TryAdd(metadata, "last_modified_by", props?.LastModifiedBy);
        }

        private static void TryAdd(IDictionary<string, string> metadata, string key, string? value)
        {
            if (string.IsNullOrWhiteSpace(value))
                return;
            if (metadata.ContainsKey(key))
                return;

            metadata[key] = value;
        }
    }
}
