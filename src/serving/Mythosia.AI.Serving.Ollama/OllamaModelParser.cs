using System;
using System.Collections.Generic;
using System.Threading;
using Newtonsoft.Json.Linq;

namespace Mythosia.AI.Serving.Ollama
{
    internal static class OllamaModelParser
    {
        internal static IReadOnlyList<ServerModel> Merge(JObject registered, JObject running, CancellationToken cancellationToken)
        {
            var installedModels = ReadModels(registered, false, cancellationToken);
            var runningModels = ReadModels(running, true, cancellationToken);
            var result = new List<ServerModel>(installedModels.Count + runningModels.Count);
            foreach (var installed in installedModels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                runningModels.TryGetValue(installed.Key, out var loaded);
                result.Add(ToModel(installed.Key, installed.Value, loaded));
                runningModels.Remove(installed.Key);
            }
            // A model can disappear from the registered list between the two observations.
            // A running record is evidence of a runner, not evidence of an installed manifest.
            foreach (var loaded in runningModels)
            {
                cancellationToken.ThrowIfCancellationRequested();
                result.Add(ToModel(loaded.Key, null, loaded.Value));
            }
            return result.AsReadOnly();
        }

        private static Dictionary<string, JObject> ReadModels(JObject envelope, bool running, CancellationToken cancellationToken)
        {
            if (!(envelope["models"] is JArray models))
                throw Invalid("The model list must contain a models array.");
            var result = new Dictionary<string, JObject>(StringComparer.Ordinal);
            foreach (var item in models)
            {
                cancellationToken.ThrowIfCancellationRequested();
                if (!(item is JObject model))
                    throw Invalid("Each model entry must be an object.");
                var id = OptionalString(model, "model") ?? OptionalString(model, "name");
                if (string.IsNullOrWhiteSpace(id))
                    throw Invalid("Each model entry must have a non-empty model or name identifier.");
                if (result.ContainsKey(id!))
                    throw Invalid("The model list contains duplicate identifiers.");
                OptionalString(model, "name");
                OptionalLong(model, "size");
                if (running)
                {
                    OptionalLong(model, "size_vram");
                    OptionalContextLength(model);
                }
                else
                {
                    OptionalString(model, "remote_model");
                    OptionalString(model, "remote_host");
                }
                result.Add(id!, model);
            }
            return result;
        }

        private static ServerModel ToModel(string id, JObject? installed, JObject? running)
        {
            var remote = installed != null &&
                (!string.IsNullOrWhiteSpace(OptionalString(installed, "remote_model")) ||
                 !string.IsNullOrWhiteSpace(OptionalString(installed, "remote_host")));
            var loadState = running != null ? ModelLoadState.Loaded :
                remote ? ModelLoadState.Unknown : ModelLoadState.Unloaded;
            return new ServerModel(id,
                displayName: OptionalString(installed ?? running!, "name") ?? id,
                installationState: installed != null ? ModelInstallationState.Installed : ModelInstallationState.Unknown,
                loadState: loadState,
                sizeBytes: installed != null ? OptionalLong(installed, "size") : null,
                memoryBytes: running != null ? OptionalLong(running, "size_vram") : null,
                contextLength: running != null ? OptionalContextLength(running) : null,
                nativeState: running != null ? "running" : remote ? "registered-remote" : "registered",
                isRemote: remote ? true : (bool?)null);
        }

        internal static string? OptionalString(JObject value, string field)
        {
            var token = value[field];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.String)
                throw Invalid("A string field has an invalid value.");
            return token.Value<string>();
        }

        internal static long? OptionalLong(JObject value, string field)
        {
            var token = value[field];
            if (token == null || token.Type == JTokenType.Null) return null;
            if (token.Type != JTokenType.Integer ||
                !long.TryParse(token.ToString(), System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out var number) || number < 0)
                throw Invalid("A nonnegative integer field has an invalid value.");
            return number;
        }

        private static int? OptionalContextLength(JObject value)
        {
            var length = OptionalLong(value, "context_length");
            if (length > int.MaxValue)
                throw Invalid("The context length exceeds the supported integer range.");
            return length.HasValue ? (int?)length.Value : null;
        }

        internal static ServingException Invalid(string detail)
            => new ServingException("Ollama returned an invalid response. " + detail,
                failureKind: ServingFailureKind.InvalidResponse);
    }
}
