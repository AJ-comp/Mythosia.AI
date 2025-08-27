using System;
using System.Collections.Generic;
using System.Threading.Tasks;

namespace Mythosia.AI.Models.Functions
{
    /// <summary>
    /// Represents a function that can be called by AI
    /// </summary>
    public class FunctionDefinition
    {
        public string Name { get; set; }
        public string Description { get; set; }
        public FunctionParameters Parameters { get; set; }
        public Func<Dictionary<string, object>, Task<string>> Handler { get; set; }

        public FunctionDefinition()
        {
            Parameters = new FunctionParameters();
        }
    }

    /// <summary>
    /// Represents function parameters schema
    /// </summary>
    public class FunctionParameters
    {
        public string Type { get; set; } = "object";
        public Dictionary<string, ParameterProperty> Properties { get; set; }
        public List<string> Required { get; set; }

        public FunctionParameters()
        {
            Properties = new Dictionary<string, ParameterProperty>();
            Required = new List<string>();
        }
    }

    /// <summary>
    /// Represents a single parameter property
    /// </summary>
    public class ParameterProperty
    {
        public string Type { get; set; }
        public string Description { get; set; }
        public List<string> Enum { get; set; }
        public object Default { get; set; }
    }

    /// <summary>
    /// Represents a function call from AI
    /// </summary>
    public class FunctionCall
    {
        public string Name { get; set; }
        public Dictionary<string, object> Arguments { get; set; }
        public string CallId { get; set; }  // Optional, for new API compatibility

        public FunctionCall()
        {
            Arguments = new Dictionary<string, object>();
        }
    }

    /// <summary>
    /// Function call mode
    /// </summary>
    public enum FunctionCallMode
    {
        None,   // Don't call functions
        Auto,   // AI decides when to call
    }
}