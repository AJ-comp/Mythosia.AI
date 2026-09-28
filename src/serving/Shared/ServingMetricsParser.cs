using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text;

namespace Mythosia.AI.Serving.Internal
{
    internal static class ServingMetricsParser
    {
        internal static ServerMetrics Parse(string text)
        {
            var samples = new List<ServerMetric>();
            var hasMetadata = false;
            foreach (var raw in text.Split('\n'))
            {
                var line = raw.Trim();
                if (line.Length == 0) continue;
                if (line[0] == '#')
                {
                    hasMetadata |= IsMetadata(line);
                    continue;
                }
                var index = 0;
                var name = ReadName(line, ref index, metric: true);
                var labels = new Dictionary<string, string>(StringComparer.Ordinal);
                if (index < line.Length && line[index] == '{')
                {
                    index++;
                    SkipSpace(line, ref index);
                    while (index < line.Length && line[index] != '}')
                    {
                        var label = ReadName(line, ref index, metric: false);
                        SkipSpace(line, ref index);
                        Expect(line, ref index, '=');
                        SkipSpace(line, ref index);
                        Expect(line, ref index, '"');
                        var value = new StringBuilder();
                        var closed = false;
                        while (index < line.Length)
                        {
                            var c = line[index++];
                            if (c == '"') { closed = true; break; }
                            if (c == '\\')
                            {
                                if (index >= line.Length) throw Invalid();
                                c = line[index++];
                                if (c == 'n') c = '\n';
                                else if (c != '\\' && c != '"') throw Invalid();
                            }
                            value.Append(c);
                        }
                        if (!closed || labels.ContainsKey(label)) throw Invalid();
                        labels.Add(label, value.ToString());
                        SkipSpace(line, ref index);
                        if (index < line.Length && line[index] == ',')
                        {
                            index++;
                            SkipSpace(line, ref index);
                        }
                        else if (index >= line.Length || line[index] != '}') throw Invalid();
                    }
                    Expect(line, ref index, '}');
                }
                if (index >= line.Length || !char.IsWhiteSpace(line[index])) throw Invalid();
                SkipSpace(line, ref index);
                var numeric = ReadToken(line, ref index);
                var valueNumber = ParseNumber(numeric);
                SkipSpace(line, ref index);
                if (index < line.Length && line[index] != '#')
                {
                    // An optional sample timestamp is preserved in RawText, not converted into a metric label.
                    ParseNumber(ReadToken(line, ref index));
                    SkipSpace(line, ref index);
                }
                if (index < line.Length && line[index] != '#') throw Invalid();
                samples.Add(new ServerMetric(name, valueNumber, labels));
            }
            // Empty text or arbitrary comments are not evidence of a metrics endpoint.
            // Legitimate metadata-only exposition can occur before any labelled series exists.
            if (samples.Count == 0 && !hasMetadata) throw Invalid();
            return new ServerMetrics(samples, text);
        }

        private static bool IsMetadata(string line)
        {
            var help = line.StartsWith("# HELP ", StringComparison.Ordinal);
            if (!help && !line.StartsWith("# TYPE ", StringComparison.Ordinal)) return false;
            var index = 7;
            SkipSpace(line, ref index);
            ReadName(line, ref index, metric: true);
            if (index >= line.Length || !char.IsWhiteSpace(line[index])) return false;
            SkipSpace(line, ref index);
            if (help) return true;
            var type = line.Substring(index);
            return type == "counter" || type == "gauge" || type == "histogram" || type == "summary" ||
                type == "untyped" || type == "info" || type == "stateset" || type == "gaugehistogram";
        }

        private static double ParseNumber(string text)
        {
            if (text == "+Inf" || text == "Inf") return double.PositiveInfinity;
            if (text == "-Inf") return double.NegativeInfinity;
            if (text == "NaN") return double.NaN;
            if (!double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var value)) throw Invalid();
            return value;
        }

        private static string ReadName(string line, ref int index, bool metric)
        {
            var start = index;
            while (index < line.Length)
            {
                var c = line[index];
                if ((c >= 'a' && c <= 'z') || (c >= 'A' && c <= 'Z') || c == '_' ||
                    (metric && c == ':') || (index > start && c >= '0' && c <= '9')) index++;
                else break;
            }
            if (start == index) throw Invalid();
            return line.Substring(start, index - start);
        }

        private static string ReadToken(string line, ref int index)
        {
            var start = index;
            while (index < line.Length && !char.IsWhiteSpace(line[index])) index++;
            if (start == index) throw Invalid();
            return line.Substring(start, index - start);
        }

        private static void SkipSpace(string line, ref int index)
        {
            while (index < line.Length && char.IsWhiteSpace(line[index])) index++;
        }

        private static void Expect(string line, ref int index, char value)
        {
            if (index >= line.Length || line[index++] != value) throw Invalid();
        }

        private static ServingException Invalid() =>
            new ServingException("The server returned invalid Prometheus metrics.", failureKind: ServingFailureKind.InvalidResponse);
    }
}
