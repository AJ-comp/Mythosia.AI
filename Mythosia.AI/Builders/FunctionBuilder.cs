using System;
using System.Collections.Generic;
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
                Default = defaultValue
            };

            if (required)
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
                Default = defaultValue
            };

            if (required)
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
            _function.Parameters = new FunctionParameters
            {
                Properties = _parameters,
                Required = _required
            };
            return _function;
        }
    }
}