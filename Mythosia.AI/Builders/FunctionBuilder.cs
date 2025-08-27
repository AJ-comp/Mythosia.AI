using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Mythosia.AI.Models.Functions;

namespace Mythosia.AI.Builders
{
    /// <summary>
    /// Fluent builder for creating function definitions
    /// </summary>
    public class FunctionBuilder
    {
        private readonly FunctionDefinition _function;
        private readonly Dictionary<string, ParameterProperty> _parameters;
        private readonly List<string> _required;

        private FunctionBuilder(string name)
        {
            _function = new FunctionDefinition { Name = name };
            _parameters = new Dictionary<string, ParameterProperty>();
            _required = new List<string>();
        }

        /// <summary>
        /// Creates a new FunctionBuilder instance
        /// </summary>
        public static FunctionBuilder Create(string name) => new FunctionBuilder(name);

        /// <summary>
        /// Sets the function description
        /// </summary>
        public FunctionBuilder WithDescription(string description)
        {
            _function.Description = description;
            return this;
        }

        /// <summary>
        /// Adds a parameter to the function
        /// </summary>
        public FunctionBuilder AddParameter(
            string name,
            string type,
            string description,
            bool required = false,
            object defaultValue = null)
        {
            _parameters[name] = new ParameterProperty
            {
                Type = type,
                Description = description,
                Default = required ? null : (defaultValue ?? GetDefaultValueForType(type))
            };

            // Always add to required for OpenAI API compatibility (both old and new)
            _required.Add(name);

            return this;
        }

        /// <summary>
        /// Adds an enum parameter to the function
        /// </summary>
        public FunctionBuilder AddEnumParameter(
            string name,
            string description,
            List<string> values,
            bool required = false,
            string defaultValue = null)
        {
            _parameters[name] = new ParameterProperty
            {
                Type = "string",
                Description = description,
                Enum = values,
                Default = required ? null : (defaultValue ?? values.FirstOrDefault())
            };

            // Always add to required for OpenAI API compatibility
            _required.Add(name);

            return this;
        }

        /// <summary>
        /// Sets the function handler
        /// </summary>
        public FunctionBuilder WithHandler(Func<Dictionary<string, object>, Task<string>> handler)
        {
            _function.Handler = handler;
            return this;
        }

        /// <summary>
        /// Sets a synchronous function handler
        /// </summary>
        public FunctionBuilder WithHandler(Func<Dictionary<string, object>, string> handler)
        {
            _function.Handler = args => Task.FromResult(handler(args));
            return this;
        }

        /// <summary>
        /// Builds the final FunctionDefinition
        /// </summary>
        public FunctionDefinition Build()
        {
            // OpenAI API requires all properties to be in required array
            // Optional parameters are indicated by having a default value
            var allPropertyNames = _parameters.Keys.ToList();

            _function.Parameters = new FunctionParameters
            {
                Type = "object",
                Properties = _parameters.Count > 0 ? _parameters : new Dictionary<string, ParameterProperty>(),
                Required = allPropertyNames  // All properties must be in required array
            };

            // If no handler was provided, add a default one
            if (_function.Handler == null)
            {
                _function.Handler = args => Task.FromResult("Function executed successfully");
            }

            return _function;
        }

        /// <summary>
        /// Gets a default value for a given type
        /// </summary>
        private object GetDefaultValueForType(string type)
        {
            return type?.ToLower() switch
            {
                "string" => "",
                "integer" => 0,
                "number" => 0.0,
                "boolean" => false,
                "array" => new List<object>(),
                "object" => new Dictionary<string, object>(),
                _ => ""
            };
        }
    }
}