using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Mythosia.AI.Attributes;
using Mythosia.AI.Builders;
using Mythosia.AI.Models.Functions;
using Mythosia.AI.Services.Base;

namespace Mythosia.AI.Extensions
{
    /// <summary>
    /// Extension methods for function calling support
    /// </summary>
    public static class FunctionExtensions
    {
        #region Function Registration Methods

        /// <summary>
        /// Registers a single method as an AI function
        /// </summary>
        public static AIService WithFunction(this AIService service, Delegate function)
        {
            var methodInfo = function.Method;
            var functionDef = BuildFunctionDefinition(methodInfo, function.Target, function);
            service.ActivateChat.AddFunction(functionDef);
            return service;
        }

        /// <summary>
        /// Registers all AI functions from an object
        /// </summary>
        public static AIService WithFunctions<T>(this AIService service, T instance) where T : class
        {
            var methods = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Instance)
                .Where(m => m.GetCustomAttribute<AiFunctionAttribute>() != null);

            foreach (var method in methods)
            {
                var functionDef = BuildFunctionDefinition(method, instance, null);
                service.ActivateChat.AddFunction(functionDef);
            }

            return service;
        }

        /// <summary>
        /// Registers all static AI functions from a type
        /// </summary>
        public static AIService WithStaticFunctions<T>(this AIService service) where T : class
        {
            var methods = typeof(T).GetMethods(BindingFlags.Public | BindingFlags.Static)
                .Where(m => m.GetCustomAttribute<AiFunctionAttribute>() != null);

            foreach (var method in methods)
            {
                var functionDef = BuildFunctionDefinition(method, null, null);
                service.ActivateChat.AddFunction(functionDef);
            }

            return service;
        }

        /// <summary>
        /// Simple function registration with inline handler
        /// </summary>
        public static AIService WithFunction(
            this AIService service,
            string name,
            string description,
            Func<Dictionary<string, object>, Task<string>> handler)
        {
            // FunctionBuilder automatically handles the schema correctly
            var function = FunctionBuilder.Create(name)
                .WithDescription(description)
                .WithHandler(handler)
                .Build();

            service.ActivateChat.AddFunction(function);
            return service;
        }

        /// <summary>
        /// Parameterless Function
        /// </summary>
        public static AIService WithFunction(
            this AIService service,
            string name,
            string description,
            Func<string> handler)
        {
            var function = FunctionBuilder.Create(name)
                .WithDescription(description)
                .WithHandler(args => handler())  // args 무시
                .Build();

            service.ActivateChat.AddFunction(function);
            return service;
        }


        /// <summary>
        /// Register a pre-built function definition
        /// </summary>
        public static AIService WithFunction(this AIService service, FunctionDefinition function)
        {
            // Ensure the function has proper parameter structure
            if (function.Parameters == null)
            {
                function.Parameters = new FunctionParameters
                {
                    Type = "object",
                    Properties = new Dictionary<string, ParameterProperty>(),
                    Required = new List<string>()
                };
            }
            else if (string.IsNullOrEmpty(function.Parameters.Type))
            {
                function.Parameters.Type = "object";
            }

            service.ActivateChat.AddFunction(function);
            return service;
        }

        #endregion

        #region Function Control Methods

        /// <summary>
        /// Temporarily disable functions (like StatelessMode)
        /// </summary>
        public static AIService WithoutFunctions(this AIService service, bool disable = true)
        {
            service.FunctionsDisabled = disable;
            return service;
        }

        /// <summary>
        /// Enable/disable functions for the chat
        /// </summary>
        public static AIService WithFunctionsEnabled(this AIService service, bool enabled = true)
        {
            service.ActivateChat.EnableFunctions = enabled;
            return service;
        }

        /// <summary>
        /// Execute with functions temporarily disabled
        /// </summary>
        public static async Task<string> AskWithoutFunctionsAsync(this AIService service, string prompt)
        {
            var backup = service.FunctionsDisabled;
            service.FunctionsDisabled = true;
            try
            {
                return await service.GetCompletionAsync(prompt);
            }
            finally
            {
                service.FunctionsDisabled = backup;
            }
        }

        /// <summary>
        /// Force a specific function to be called
        /// </summary>
        public static async Task<string> CallFunctionAsync(
            this AIService service,
            string functionName,
            string prompt)
        {
            var backupMode = service.ActivateChat.FunctionCallMode;
            var backupForce = service.ActivateChat.ForceFunctionName;

            service.ActivateChat.FunctionCallMode = FunctionCallMode.Force;
            service.ActivateChat.ForceFunctionName = functionName;

            try
            {
                return await service.GetCompletionAsync(prompt);
            }
            finally
            {
                service.ActivateChat.FunctionCallMode = backupMode;
                service.ActivateChat.ForceFunctionName = backupForce;
            }
        }

        #endregion

        #region Private Helper Methods

        private static FunctionDefinition BuildFunctionDefinition(
            MethodInfo method,
            object instance,
            Delegate existingDelegate)
        {
            var attr = method.GetCustomAttribute<AiFunctionAttribute>();

            // Auto-generate function name if not specified
            var functionName = attr?.Name ?? ConvertToSnakeCase(method.Name);

            // Auto-generate description if not specified
            var description = attr?.Description ?? $"Executes {functionName}";

            var builder = FunctionBuilder.Create(functionName)
                .WithDescription(description);

            // Process parameters
            var parameters = method.GetParameters();
            foreach (var param in parameters)
            {
                var paramAttr = param.GetCustomAttribute<AiParameterAttribute>();

                var paramName = paramAttr?.Name ?? param.Name;
                var paramDesc = paramAttr?.Description ?? $"The {param.Name}";
                var required = paramAttr?.Required ?? !param.HasDefaultValue;

                builder.AddParameter(
                    paramName,
                    GetJsonType(param.ParameterType),
                    paramDesc,
                    required,
                    param.HasDefaultValue ? param.DefaultValue : null
                );
            }

            // Create handler
            builder.WithHandler(async (args) =>
            {
                try
                {
                    // Prepare parameter values
                    var paramValues = new object[parameters.Length];
                    for (int i = 0; i < parameters.Length; i++)
                    {
                        var param = parameters[i];
                        var paramName = param.GetCustomAttribute<AiParameterAttribute>()?.Name ?? param.Name;

                        if (args.ContainsKey(paramName))
                        {
                            var value = args[paramName];
                            paramValues[i] = ConvertValue(value, param.ParameterType);
                        }
                        else if (param.HasDefaultValue)
                        {
                            paramValues[i] = param.DefaultValue;
                        }
                        else
                        {
                            throw new ArgumentException($"Required parameter '{paramName}' not provided");
                        }
                    }

                    // Invoke the method
                    object result;
                    if (existingDelegate != null)
                    {
                        result = existingDelegate.DynamicInvoke(paramValues);
                    }
                    else if (method.IsStatic)
                    {
                        result = method.Invoke(null, paramValues);
                    }
                    else
                    {
                        result = method.Invoke(instance, paramValues);
                    }

                    // Handle async results
                    if (result is Task<string> taskString)
                    {
                        return await taskString;
                    }
                    else if (result is Task task)
                    {
                        await task;
                        return "Success";
                    }
                    else if (result is string stringResult)
                    {
                        return stringResult;
                    }
                    else if (result == null)
                    {
                        return "Done";
                    }
                    else
                    {
                        return JsonSerializer.Serialize(result);
                    }
                }
                catch (Exception ex)
                {
                    return $"Error: {ex.Message}";
                }
            });

            return builder.Build();
        }

        private static string ConvertToSnakeCase(string name)
        {
            // Convert PascalCase/camelCase to snake_case
            var result = Regex.Replace(name, "([a-z])([A-Z])", "$1_$2");
            result = Regex.Replace(result, "([A-Z])([A-Z][a-z])", "$1_$2");
            return result.ToLower();
        }

        private static string GetJsonType(Type type)
        {
            // Unwrap nullable types
            type = Nullable.GetUnderlyingType(type) ?? type;

            if (type == typeof(string))
                return "string";
            if (type == typeof(int) || type == typeof(long) || type == typeof(short) || type == typeof(byte))
                return "integer";
            if (type == typeof(float) || type == typeof(double) || type == typeof(decimal))
                return "number";
            if (type == typeof(bool))
                return "boolean";
            if (type.IsArray || typeof(System.Collections.IEnumerable).IsAssignableFrom(type))
                return "array";

            return "object";
        }

        private static object ConvertValue(object value, Type targetType)
        {
            if (value == null)
                return null;

            targetType = Nullable.GetUnderlyingType(targetType) ?? targetType;

            // Handle JSON elements
            if (value is JsonElement jsonElement)
            {
                if (targetType == typeof(string))
                    return jsonElement.GetString();
                if (targetType == typeof(int))
                    return jsonElement.GetInt32();
                if (targetType == typeof(long))
                    return jsonElement.GetInt64();
                if (targetType == typeof(float))
                    return (float)jsonElement.GetDouble();
                if (targetType == typeof(double))
                    return jsonElement.GetDouble();
                if (targetType == typeof(bool))
                    return jsonElement.GetBoolean();
                if (targetType == typeof(decimal))
                    return jsonElement.GetDecimal();

                // For complex types, deserialize
                var json = jsonElement.GetRawText();
                return JsonSerializer.Deserialize(json, targetType);
            }

            // Direct conversion
            if (targetType.IsAssignableFrom(value.GetType()))
                return value;

            // Try type conversion
            return Convert.ChangeType(value, targetType);
        }

        #endregion
    }
}