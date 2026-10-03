using Mythosia.AI.Models;
using Mythosia.AI.Models.Functions;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using System.Text.Json;
using System.Text.Json.Nodes;

namespace Mythosia.AI.Services.Base
{
    public abstract partial class AIService
    {
        private Action ApplyPreparationProfile(RequestOperation operation, AIRequestProfile profile)
        {
            var baseline = new Dictionary<string, object?>(StringComparer.Ordinal);
            var copies = new Dictionary<object, object>(PreparationReferenceComparer.Instance);
            foreach (var setting in _requestExecution.Value!.Settings)
                baseline[setting.Key] = CopyPreparationValue(setting.Value, copies);
            var writes = new Dictionary<string, object>(_requestExecution.Value.SettingWrites, StringComparer.Ordinal);
            var restore = ApplyRequestProfile(profile);
            try
            {
                var applied = new Dictionary<string, object?>(StringComparer.Ordinal);
                var appliedWrites = new Dictionary<string, object?>(StringComparer.Ordinal);
                copies.Clear();
                foreach (var setting in _requestExecution.Value!.Settings)
                {
                    writes.TryGetValue(setting.Key, out var beforeWrite);
                    _requestExecution.Value.SettingWrites.TryGetValue(setting.Key, out var afterWrite);
                    // Entering a scope copies owned functions and policy, but does not
                    // make them profile overrides. Also recognize in-place profile hooks.
                    if (ReferenceEquals(beforeWrite, afterWrite) && baseline.TryGetValue(setting.Key, out var before) &&
                        SamePreparationValue(before, setting.Value)) continue;
                    applied[setting.Key] = CopyPreparationValue(setting.Value, copies);
                    appliedWrites[setting.Key] = afterWrite;
                }
                operation.ProfileBaseline = baseline;
                operation.ProfileAppliedSettings = applied;
                operation.ProfileSettingWrites = appliedWrites;
                return restore;
            }
            catch { restore(); throw; }
        }

        // Snapshot supported mutable setting values without serializing functions,
        // delegates or arbitrary provider objects. Opaque objects and content beyond
        // this traversal bound remain owner-managed, as in captured request settings.
        // The reference map preserves legal cycles and shared collection elements.
        private const int PreparationSnapshotDepth = 64;
        private static object? CopyPreparationValue(object? value, Dictionary<object, object> copies, int depth = 0)
        {
            if (value is JsonElement element)
                return element.ValueKind == JsonValueKind.Undefined ? element : element.Clone();
            if (value == null || value is string || value is Delegate || value.GetType().IsValueType || depth >= PreparationSnapshotDepth)
                return value;
            if (copies.TryGetValue(value, out var existing)) return existing;
            if (value is JsonNode node)
            {
                var json = node.DeepClone();
                copies[value] = json;
                return json;
            }
            object? Copy(object? item) => CopyPreparationValue(item, copies, depth + 1);
            if (value.GetType() == typeof(FunctionDefinition) && value is FunctionDefinition function)
            {
                var clone = new FunctionDefinition { Name = function.Name, Description = function.Description,
                    AllowAsync = function.AllowAsync, HandlerWithCancellation = function.HandlerWithCancellation };
                copies[value] = clone;
                clone.Parameters = (FunctionParameters)Copy(function.Parameters)!;
                return clone;
            }
            if (value.GetType() == typeof(FunctionParameters) && value is FunctionParameters parameters)
            {
                var clone = new FunctionParameters { Type = parameters.Type };
                copies[value] = clone;
                clone.Properties = (Dictionary<string, ParameterProperty>)Copy(parameters.Properties)!;
                clone.Required = (List<string>)Copy(parameters.Required)!;
                return clone;
            }
            if (value.GetType() == typeof(ParameterProperty) && value is ParameterProperty property)
            {
                var clone = new ParameterProperty { Type = property.Type, Description = property.Description };
                copies[value] = clone;
                clone.Enum = (List<string>?)Copy(property.Enum);
                clone.Default = Copy(property.Default);
                clone.Items = (ParameterProperty?)Copy(property.Items);
                return clone;
            }
            if (value.GetType() == typeof(FunctionCallingPolicy) && value is FunctionCallingPolicy policy)
            {
                var clone = policy.Clone();
                copies[value] = clone;
                return clone;
            }
            if (value is IDictionary dictionary && CreatePreparationDictionary(dictionary) is IDictionary dictionaryClone)
            {
                copies[value] = dictionaryClone;
                // Keys and the original comparer define lookup identity. Cloning a
                // reference key would change a valid captured dictionary's contract.
                foreach (DictionaryEntry entry in dictionary) dictionaryClone.Add(entry.Key, Copy(entry.Value));
                return dictionaryClone;
            }
            if (value is IList list && CreatePreparationList(list) is IList listClone)
            {
                copies[value] = listClone;
                for (var i = 0; i < list.Count; i++)
                {
                    if (listClone.IsFixedSize) listClone[i] = Copy(list[i]);
                    else listClone.Add(Copy(list[i]));
                }
                return listClone;
            }
            return value;
        }

        // Only known BCL containers are constructed or inspected. Custom constructors,
        // getters and clone hooks must never run merely because a profile is forwarded.
        private static IDictionary? CreatePreparationDictionary(IDictionary source)
        {
            var type = source.GetType();
            if (type == typeof(Hashtable) || type == typeof(SortedList))
            {
                var clone = (IDictionary)((ICloneable)source).Clone();
                clone.Clear();
                return clone;
            }
            if (!type.IsGenericType) return null;
            var definition = type.GetGenericTypeDefinition();
            if (definition != typeof(Dictionary<,>) && definition != typeof(SortedDictionary<,>) && definition != typeof(SortedList<,>))
                return null;
            return (IDictionary)Activator.CreateInstance(type, type.GetProperty("Comparer")!.GetValue(source))!;
        }

        private static IList? CreatePreparationList(IList source)
        {
            if (source is Array array)
                return array.Rank == 1 && array.GetLowerBound(0) == 0 ? (IList)array.Clone() : null;
            var type = source.GetType();
            return type == typeof(ArrayList) || (type.IsGenericType && type.GetGenericTypeDefinition() == typeof(List<>))
                ? (IList)Activator.CreateInstance(type)! : null;
        }

        private static bool SamePreparationValue(object? left, object? right)
            => SamePreparationValue(left, right, new Dictionary<object, object>(PreparationReferenceComparer.Instance), 0);

        private static bool SamePreparationValue(object? left, object? right, Dictionary<object, object> pairs, int depth)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null || left.GetType() != right.GetType()) return false;
            if (left is JsonElement element && right is JsonElement otherElement)
                return element.ValueKind == JsonValueKind.Undefined || otherElement.ValueKind == JsonValueKind.Undefined
                    ? element.ValueKind == otherElement.ValueKind : JsonElement.DeepEquals(element, otherElement);
            if (left is JsonNode node && right is JsonNode otherNode) return JsonNode.DeepEquals(node, otherNode);
            if (left is string || left is Delegate || left.GetType().IsValueType || depth >= PreparationSnapshotDepth)
                return Equals(left, right);
            if (pairs.TryGetValue(left, out var paired)) return ReferenceEquals(paired, right);
            pairs[left] = right;
            bool Same(object? first, object? second) => SamePreparationValue(first, second, pairs, depth + 1);
            if (left.GetType() == typeof(FunctionDefinition) && left is FunctionDefinition function && right is FunctionDefinition otherFunction)
                return function.Name == otherFunction.Name && function.Description == otherFunction.Description &&
                    function.AllowAsync == otherFunction.AllowAsync && Equals(function.HandlerWithCancellation, otherFunction.HandlerWithCancellation) &&
                    Same(function.Parameters, otherFunction.Parameters);
            if (left.GetType() == typeof(FunctionParameters) && left is FunctionParameters parameters && right is FunctionParameters otherParameters)
                return parameters.Type == otherParameters.Type && Same(parameters.Properties, otherParameters.Properties) &&
                    Same(parameters.Required, otherParameters.Required);
            if (left.GetType() == typeof(ParameterProperty) && left is ParameterProperty property && right is ParameterProperty otherProperty)
                return property.Type == otherProperty.Type && property.Description == otherProperty.Description &&
                    Same(property.Enum, otherProperty.Enum) && Same(property.Default, otherProperty.Default) && Same(property.Items, otherProperty.Items);
            if (left.GetType() == typeof(FunctionCallingPolicy) && left is FunctionCallingPolicy policy && right is FunctionCallingPolicy otherPolicy)
                return policy.MaxRounds == otherPolicy.MaxRounds && policy.TimeoutSeconds == otherPolicy.TimeoutSeconds &&
                    policy.ExecutionMode == otherPolicy.ExecutionMode && policy.MaxConcurrency == otherPolicy.MaxConcurrency &&
                    policy.EnableLogging == otherPolicy.EnableLogging;
            if (left is IDictionary dictionary && right is IDictionary otherDictionary)
            {
                if (dictionary.Count != otherDictionary.Count) return false;
                foreach (DictionaryEntry entry in dictionary)
                    if (!otherDictionary.Contains(entry.Key) || !Same(entry.Value, otherDictionary[entry.Key])) return false;
                return true;
            }
            if (left is IList list && right is IList otherList)
            {
                if (list.Count != otherList.Count) return false;
                for (var i = 0; i < list.Count; i++) if (!Same(list[i], otherList[i])) return false;
                return true;
            }
            return Equals(left, right);
        }

        private sealed class PreparationReferenceComparer : IEqualityComparer<object>
        {
            internal static PreparationReferenceComparer Instance { get; } = new PreparationReferenceComparer();
            public new bool Equals(object? left, object? right) => ReferenceEquals(left, right);
            public int GetHashCode(object value) => RuntimeHelpers.GetHashCode(value);
        }
    }
}
