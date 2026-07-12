using Newtonsoft.Json;

namespace Mythosia.AI.Serving.Vllm
{
    /// <summary>
    /// One entry of vLLM's <c>GET /v1/models</c> response.
    /// <para>
    /// vLLM emits one card per <c>--served-model-name</c> alias (all sharing an identical
    /// <see cref="Root"/>) plus one card per loaded LoRA adapter. <c>data[0].Id</c> is the
    /// canonical served name the server echoes in chat responses.
    /// </para>
    /// </summary>
    public class VllmModelCard
    {
        /// <summary>Served model name (alias) — the value chat requests put in their <c>model</c> field.</summary>
        [JsonProperty("id")]
        public string Id { get; set; } = string.Empty;

        /// <summary>
        /// The <b>actually loaded</b> model: the raw <c>--model</c> CLI value, verbatim — a
        /// HuggingFace repo id or a local filesystem path (vLLM does not resolve local paths, so
        /// this can expose host filesystem layout; consider masking path-like values in end-user UI).
        /// On LoRA adapter cards this is the adapter path instead.
        /// <para>
        /// Nullable by contract: <c>root</c> is a stable-since-2023 but <b>undocumented</b> vLLM
        /// extension (absent from vLLM's docs and its own OpenAPI schema, mirroring fields OpenAI
        /// removed in 2023) — display <see cref="DisplayModel"/> rather than assuming it is present.
        /// </para>
        /// </summary>
        [JsonProperty("root")]
        public string? Root { get; set; }

        /// <summary>
        /// <c>null</c> for base models; on LoRA adapter cards, the base model's served name.
        /// (Same stability caveat as <see cref="Root"/>.)
        /// </summary>
        [JsonProperty("parent")]
        public string? Parent { get; set; }

        /// <summary>Engine-effective context window (vLLM extension); <c>null</c> on LoRA adapter cards.</summary>
        [JsonProperty("max_model_len")]
        public int? MaxModelLen { get; set; }

        /// <summary>
        /// Unix seconds — <b>regenerated on every request</b>, so it is NOT the model load time;
        /// do not render it as a timestamp of anything meaningful.
        /// </summary>
        [JsonProperty("created")]
        public long Created { get; set; }

        /// <summary>Constant <c>"vllm"</c>.</summary>
        [JsonProperty("owned_by")]
        public string OwnedBy { get; set; } = string.Empty;

        /// <summary>Whether this card is a loaded LoRA adapter rather than a base model.</summary>
        [JsonIgnore]
        public bool IsLoraAdapter => Parent != null;

        /// <summary>
        /// The name to display as "the model actually running": <see cref="Root"/> when the server
        /// exposes it, otherwise the served alias <see cref="Id"/> — the built-in fallback for the
        /// undocumented-field caveat on <see cref="Root"/>.
        /// </summary>
        [JsonIgnore]
        public string DisplayModel => string.IsNullOrEmpty(Root) ? Id : Root!;
    }
}
