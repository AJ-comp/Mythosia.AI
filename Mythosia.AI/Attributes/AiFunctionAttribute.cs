using System;
using System.Collections.Generic;
using System.Text;

namespace Mythosia.AI.Attributes
{
    /// <summary>
    /// Marks a method as an AI-callable function
    /// </summary>
    [AttributeUsage(AttributeTargets.Method)]
    public class AiFunctionAttribute : Attribute
    {
        /// <summary>
        /// Optional custom name for the function. If null, method name will be used.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Description of what the function does
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Creates an AI function with auto-generated name
        /// </summary>
        public AiFunctionAttribute(string description)
        {
            Description = description;
        }

        /// <summary>
        /// Creates an AI function with custom name
        /// </summary>
        public AiFunctionAttribute(string name, string description)
        {
            Name = name;
            Description = description;
        }

        /// <summary>
        /// Creates an AI function with auto-generated name and description
        /// </summary>
        public AiFunctionAttribute()
        {
        }
    }
}
