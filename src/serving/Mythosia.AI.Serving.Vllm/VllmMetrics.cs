using System.Collections.Generic;

namespace Mythosia.AI.Serving.Vllm
{
    /// <summary>One Prometheus sample line: metric name, its labels (preserved), and the value.</summary>
    public class VllmMetricSample
    {
        /// <summary>Metric name as it appears on the sample line (e.g. <c>vllm:num_requests_running</c>).</summary>
        public string Name { get; }

        /// <summary>Labels of the sample (e.g. <c>model_name</c>, <c>engine</c>) — preserved so multi-engine/multi-model setups stay distinguishable.</summary>
        public IReadOnlyDictionary<string, string> Labels { get; }

        /// <summary>Sample value (<c>+Inf</c>/<c>-Inf</c>/<c>NaN</c> map to the corresponding <see cref="double"/> specials).</summary>
        public double Value { get; }

        public VllmMetricSample(string name, IReadOnlyDictionary<string, string> labels, double value)
        {
            Name = name;
            Labels = labels;
            Value = value;
        }
    }

    /// <summary>
    /// Parsed snapshot of vLLM's <c>GET /metrics</c> (Prometheus text exposition).
    /// <para>
    /// <see cref="Families"/> (all samples grouped by metric name, labels preserved) and
    /// <see cref="RawText"/> are the durable contract. The typed getters are convenience sugar
    /// bound to today's stable v1 metric names — vLLM has renamed metrics across versions before
    /// (e.g. <c>gpu_cache_usage_perc</c> → <c>kv_cache_usage_perc</c>), in which case a getter
    /// returns <c>null</c> while the same data remains reachable through <see cref="Families"/>.
    /// </para>
    /// </summary>
    public class VllmMetrics
    {
        /// <summary>All parsed samples grouped by metric name, labels preserved.</summary>
        public IReadOnlyDictionary<string, IReadOnlyList<VllmMetricSample>> Families { get; }

        /// <summary>The full, unmodified exposition text — safety valve for everything not typed.</summary>
        public string RawText { get; }

        public VllmMetrics(IReadOnlyDictionary<string, IReadOnlyList<VllmMetricSample>> families, string rawText)
        {
            Families = families;
            RawText = rawText;
        }

        /// <summary>Requests currently being processed (<c>vllm:num_requests_running</c>; summed across engines).</summary>
        public double? RunningRequests => Sum("vllm:num_requests_running");

        /// <summary>Requests waiting in the queue (<c>vllm:num_requests_waiting</c>; summed across engines).</summary>
        public double? WaitingRequests => Sum("vllm:num_requests_waiting");

        /// <summary>KV-cache usage as 0..1 (<c>vllm:kv_cache_usage_perc</c>; averaged when multiple engines report).</summary>
        public double? KvCacheUsage => Average("vllm:kv_cache_usage_perc");

        /// <summary>Total prompt tokens processed (<c>vllm:prompt_tokens_total</c>; summed).</summary>
        public double? PromptTokensTotal => Sum("vllm:prompt_tokens_total");

        /// <summary>Total tokens generated (<c>vllm:generation_tokens_total</c>; summed).</summary>
        public double? GenerationTokensTotal => Sum("vllm:generation_tokens_total");

        /// <summary>Total successfully finished requests (<c>vllm:request_success_total</c>; summed over all finish reasons).</summary>
        public double? RequestSuccessTotal => Sum("vllm:request_success_total");

        private double? Sum(string family)
        {
            if (!Families.TryGetValue(family, out var samples) || samples.Count == 0)
                return null;

            double total = 0;
            foreach (var sample in samples)
                total += sample.Value;
            return total;
        }

        private double? Average(string family)
        {
            var total = Sum(family);
            if (total == null)
                return null;
            return total.Value / Families[family].Count;
        }
    }
}
