using System.Text;

namespace Mythosia.AI.Loaders.Office.Compat
{
    internal static class OfficeCompatParserUtilities
    {
        public static string NormalizeWhitespace(string value)
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
