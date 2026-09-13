using Mythosia.AI.Models.Functions;
using Mythosia.AI.Models.Messages;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;

namespace Mythosia.AI.Models
{
    // An owned, immutable request description. Only detached copies enter execution.
    // Handler targets and custom message-content implementations remain caller-owned.
    internal sealed class AIRequest
    {
        internal Message Input { get; }
        internal IReadOnlyDictionary<string, object?> Settings { get; }
        internal AIRequestFeatures Features { get; }
        internal AIRequestContext? Context { get; }
        internal AIRequestProfile? Profile { get; }
        internal object? ProviderOptions { get; }

        internal AIRequest(Message input, IDictionary<string, object?> settings,
            AIRequestFeatures features, object? providerOptions = null,
            AIRequestContext? context = null, AIRequestProfile? profile = null)
        {
            Input = input;
            Settings = new System.Collections.ObjectModel.ReadOnlyDictionary<string, object?>(
                new Dictionary<string, object?>(settings, StringComparer.Ordinal));
            Features = features;
            ProviderOptions = providerOptions;
            Context = context;
            Profile = profile;
        }

        internal AIRequest WithSetting(string name, object? value)
        {
            var settings = Settings.ToDictionary(pair => pair.Key, pair => pair.Value, StringComparer.Ordinal);
            settings[name] = value;
            return new AIRequest(Input, settings, Features, ProviderOptions, Context, Profile);
        }

        internal AIRequest WithFeatures(AIRequestFeatures features)
            => new AIRequest(Input, Settings.ToDictionary(p => p.Key, p => p.Value), features, ProviderOptions, Context, Profile);

        internal AIRequest WithContext(AIRequestContext context)
            => new AIRequest(Input, Settings.ToDictionary(p => p.Key, p => p.Value), Features, ProviderOptions, context, Profile);

        internal AIRequest WithProfile(AIRequestProfile profile)
            => new AIRequest(Input, Settings.ToDictionary(p => p.Key, p => p.Value), Features, ProviderOptions, Context, profile);

        internal AIRequest WithInput(Message input)
            => new AIRequest(input, Settings.ToDictionary(p => p.Key, p => p.Value), Features, ProviderOptions, Context, Profile);

        internal static FunctionDefinition CopyFunction(FunctionDefinition function)
        {
            if (function == null) throw new ArgumentException("Functions cannot contain null.", nameof(function));
            return new FunctionDefinition
            {
                Name = function.Name, Description = function.Description,
                HandlerWithCancellation = function.HandlerWithCancellation,
                AllowAsync = function.AllowAsync,
                Parameters = new FunctionParameters
                {
                    Type = function.Parameters.Type,
                    Required = function.Parameters.Required.ToList(),
                    Properties = function.Parameters.Properties.ToDictionary(p => p.Key, p => CopyParameter(p.Value))
                }
            };
        }

        private static ParameterProperty CopyParameter(ParameterProperty property)
            => CopyParameter(property, new HashSet<ParameterProperty>(ParameterReferenceComparer.Instance), 0);

        private static ParameterProperty CopyParameter(ParameterProperty property,
            HashSet<ParameterProperty> path, int depth)
        {
            // Malformed caller schemas must fail normally instead of exhausting the process stack.
            // The transport's JSON serializer also imposes a bounded nesting depth.
            if (property == null || depth >= 64 || !path.Add(property))
                throw new ArgumentException("Function parameter schemas must be non-null, acyclic and no more than 64 levels deep.", nameof(property));
            try
            {
                return new ParameterProperty
                {
                    Type = property.Type, Description = property.Description, Enum = property.Enum?.ToList(),
                    Default = CopyDefault(property.Default),
                    Items = property.Items == null ? null : CopyParameter(property.Items, path, depth + 1)
                };
            }
            finally { path.Remove(property); }
        }

        private sealed class ParameterReferenceComparer : IEqualityComparer<ParameterProperty>
        {
            internal static ParameterReferenceComparer Instance { get; } = new ParameterReferenceComparer();
            public bool Equals(ParameterProperty? x, ParameterProperty? y) => ReferenceEquals(x, y);
            public int GetHashCode(ParameterProperty value) => RuntimeHelpers.GetHashCode(value);
        }

        private static object? CopyDefault(object? value)
        {
            if (value == null) return null;
            // JSON nodes and document-owned JsonElements must also be detached from their owner.
            using var document = System.Text.Json.JsonDocument.Parse(System.Text.Json.JsonSerializer.Serialize(value));
            return document.RootElement.Clone();
        }
    }
}
