using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mythosia.AI.Serving.Vllm
{
    /// <summary>
    /// Minimal, tolerant parser for the Prometheus text exposition format — just enough for
    /// <c>GET /metrics</c> snapshots: sample lines with optional labels (escape-aware) and
    /// optional trailing timestamps; comment/malformed lines are skipped rather than failing
    /// the whole snapshot.
    /// </summary>
    internal static class PrometheusTextParser
    {
        private static readonly IReadOnlyDictionary<string, string> EmptyLabels =
            new Dictionary<string, string>();

        public static IReadOnlyDictionary<string, IReadOnlyList<VllmMetricSample>> Parse(string? expositionText)
        {
            var families = new Dictionary<string, IReadOnlyList<VllmMetricSample>>(StringComparer.Ordinal);
            if (string.IsNullOrEmpty(expositionText))
                return families;

            var building = new Dictionary<string, List<VllmMetricSample>>(StringComparer.Ordinal);
            foreach (var rawLine in expositionText!.Split('\n'))
            {
                var line = rawLine.Trim();
                if (line.Length == 0 || line[0] == '#')
                    continue;

                var sample = ParseSampleLine(line);
                if (sample == null)
                    continue;

                if (!building.TryGetValue(sample.Name, out var list))
                {
                    list = new List<VllmMetricSample>();
                    building[sample.Name] = list;
                }
                list.Add(sample);
            }

            foreach (var pair in building)
                families[pair.Key] = pair.Value;
            return families;
        }

        private static VllmMetricSample? ParseSampleLine(string line)
        {
            var i = 0;

            // metric name — letters, digits, '_', ':' (vLLM uses colon-prefixed names).
            while (i < line.Length && IsNameChar(line[i])) i++;
            if (i == 0) return null;
            var name = line.Substring(0, i);

            var labels = EmptyLabels;
            if (i < line.Length && line[i] == '{')
            {
                var parsed = ParseLabels(line, ref i);
                if (parsed == null) return null; // malformed label block — skip the line
                labels = parsed;
            }

            while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
            var valueStart = i;
            while (i < line.Length && !char.IsWhiteSpace(line[i])) i++;
            if (valueStart == i) return null;

            var valueToken = line.Substring(valueStart, i - valueStart);
            if (!TryParseValue(valueToken, out var value)) return null;

            // Anything after the value (an optional timestamp) is deliberately ignored.
            return new VllmMetricSample(name, labels, value);
        }

        private static IReadOnlyDictionary<string, string>? ParseLabels(string line, ref int i)
        {
            i++; // consume '{'
            var labels = new Dictionary<string, string>(StringComparer.Ordinal);

            while (true)
            {
                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i >= line.Length) return null; // unterminated block
                if (line[i] == '}') { i++; return labels; }

                var nameStart = i;
                while (i < line.Length && IsNameChar(line[i])) i++;
                if (nameStart == i) return null;
                var labelName = line.Substring(nameStart, i - nameStart);

                if (i >= line.Length || line[i] != '=') return null;
                i++;
                if (i >= line.Length || line[i] != '"') return null;
                i++;

                var value = new StringBuilder();
                while (i < line.Length && line[i] != '"')
                {
                    var c = line[i];
                    if (c == '\\' && i + 1 < line.Length)
                    {
                        i++;
                        var escaped = line[i];
                        if (escaped == 'n') value.Append('\n');
                        else if (escaped == '\\') value.Append('\\');
                        else if (escaped == '"') value.Append('"');
                        else value.Append(escaped);
                    }
                    else
                    {
                        value.Append(c);
                    }
                    i++;
                }
                if (i >= line.Length) return null; // unterminated string
                i++; // consume closing '"'

                labels[labelName] = value.ToString();

                while (i < line.Length && char.IsWhiteSpace(line[i])) i++;
                if (i < line.Length && line[i] == ',') { i++; continue; }
                // otherwise the loop expects '}' on the next pass
            }
        }

        private static bool TryParseValue(string token, out double value)
        {
            switch (token)
            {
                case "+Inf":
                case "Inf":
                    value = double.PositiveInfinity;
                    return true;
                case "-Inf":
                    value = double.NegativeInfinity;
                    return true;
                case "NaN":
                    value = double.NaN;
                    return true;
                default:
                    return double.TryParse(token, NumberStyles.Float, CultureInfo.InvariantCulture, out value);
            }
        }

        private static bool IsNameChar(char c)
            => (c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || (c >= '0' && c <= '9') || c == '_' || c == ':';
    }
}
