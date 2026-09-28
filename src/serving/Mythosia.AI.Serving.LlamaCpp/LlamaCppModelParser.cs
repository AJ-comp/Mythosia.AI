using System;
using System.Collections.Generic;
using Newtonsoft.Json.Linq;

namespace Mythosia.AI.Serving.LlamaCpp
{
    internal static class LlamaCppModelParser
    {
        internal static ServingException InvalidResponse() =>
            new ServingException("The llama.cpp server returned an invalid management response.", failureKind: ServingFailureKind.InvalidResponse);

        internal static string RequiredString(JToken? value)
        {
            if (value?.Type != JTokenType.String || string.IsNullOrWhiteSpace((string?)value))
                throw InvalidResponse();
            return (string)value!;
        }

        internal static string? OptionalString(JToken? value)
        {
            if (value == null || value.Type == JTokenType.Null) return null;
            return RequiredString(value);
        }

        internal static long? OptionalCount(JToken? value)
        {
            if (value == null || value.Type == JTokenType.Null) return null;
            if (value.Type != JTokenType.Integer) throw InvalidResponse();
            try
            {
                var number = value.Value<long>();
                if (number < 0) throw InvalidResponse();
                return number;
            }
            catch (Exception ex) when (ex is OverflowException || ex is FormatException || ex is InvalidCastException)
            {
                throw InvalidResponse();
            }
        }

        internal static bool? OptionalBool(JToken? value)
        {
            if (value == null || value.Type == JTokenType.Null) return null;
            if (value.Type != JTokenType.Boolean) throw InvalidResponse();
            return value.Value<bool>();
        }

        internal static ServerMode GetMode(JObject props)
        {
            if (HasError(props)) throw InvalidResponse();
            var role = OptionalString(props["role"]);
            if (role == "router") return ServerMode.Router;
            if (role != null) return ServerMode.Unknown;
            // /models exists in both modes. Only native properties identify a single-model server.
            if (props["total_slots"] != null && props["default_generation_settings"] is JObject)
            {
                OptionalCount(props["total_slots"]);
                return ServerMode.SingleModel;
            }
            return ServerMode.Unknown;
        }

        internal static IReadOnlyList<ServerModel> ParseModels(JObject envelope, JObject? props)
        {
            if (HasError(envelope) || !(envelope["data"] is JArray array) || array.Count > 100000)
                throw InvalidResponse();
            var result = new List<ServerModel>(array.Count);
            var ids = new HashSet<string>(StringComparer.Ordinal);
            var mode = props == null ? ServerMode.Unknown : GetMode(props);
            foreach (var token in array)
            {
                if (!(token is JObject model)) throw InvalidResponse();
                var id = RequiredString(model["id"]);
                if (!ids.Add(id)) throw InvalidResponse();
                JObject? status = null;
                if (model["status"] != null && model["status"]!.Type != JTokenType.Null)
                    status = model["status"] as JObject ?? throw InvalidResponse();
                var nativeState = status == null ? null : RequiredString(status["value"]);
                var state = ParseLoadState(nativeState);
                if (status != null && OptionalBool(status["failed"]) == true) state = ModelLoadState.Failed;
                if (nativeState == null && mode == ServerMode.SingleModel)
                {
                    var sleeping = OptionalBool(props!["is_sleeping"]);
                    if (sleeping.HasValue)
                    {
                        state = sleeping.Value ? ModelLoadState.Sleeping : ModelLoadState.Loaded;
                        nativeState = sleeping.Value ? "sleeping" : "loaded";
                    }
                }

                var installation = ModelInstallationState.Unknown;
                var source = OptionalString(model["source"]);
                if (state == ModelLoadState.Downloading)
                    installation = ModelInstallationState.NotInstalled;
                else if (state == ModelLoadState.Loaded || state == ModelLoadState.Sleeping ||
                         mode == ServerMode.SingleModel ||
                         (state == ModelLoadState.Unloaded && (source == "cache" || source == "models_dir")))
                    installation = ModelInstallationState.Installed;

                JObject? meta = null;
                if (model["meta"] != null && model["meta"]!.Type != JTokenType.Null)
                    meta = model["meta"] as JObject ?? throw InvalidResponse();
                var size = OptionalCount(meta?["size"]);
                var context = OptionalCount(meta?["n_ctx"]);
                if (context > int.MaxValue) throw InvalidResponse();
                result.Add(new ServerModel(id, installationState: installation, loadState: state,
                    sizeBytes: size, contextLength: (int?)context, nativeState: nativeState));
            }
            return result.AsReadOnly();
        }

        internal static bool HasError(JObject value) => value["error"] != null && value["error"]!.Type != JTokenType.Null;

        private static ModelLoadState ParseLoadState(string? value)
        {
            switch (value)
            {
                case "unloaded": return ModelLoadState.Unloaded;
                case "loading": return ModelLoadState.Loading;
                case "loaded": return ModelLoadState.Loaded;
                case "unloading": return ModelLoadState.Unloading;
                case "sleeping": return ModelLoadState.Sleeping;
                case "downloading": return ModelLoadState.Downloading;
                case "failed": return ModelLoadState.Failed;
                default: return ModelLoadState.Unknown;
            }
        }
    }
}
