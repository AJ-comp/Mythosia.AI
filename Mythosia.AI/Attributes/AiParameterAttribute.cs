using System;
using System.Collections.Generic;
using System.Text;

namespace Mythosia.AI.Attributes
{
    /// <summary>
    /// Marks a parameter with additional metadata
    /// </summary>
    [AttributeUsage(AttributeTargets.Parameter)]
    public class AiParameterAttribute : Attribute
    {
        /// <summary>
        /// Description of the parameter
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Whether this parameter is required
        /// </summary>
        public bool Required { get; set; }

        /// <summary>
        /// Optional custom name for the parameter
        /// </summary>
        public string Name { get; set; }

        public AiParameterAttribute(string description, bool required = true)
        {
            Description = description;
            Required = required;
        }

        public AiParameterAttribute()
        {
        }
    }
}
