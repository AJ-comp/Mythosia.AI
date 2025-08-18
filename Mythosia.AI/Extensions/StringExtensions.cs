using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Mythosia.AI.Extensions
{
    /// <summary>
    /// String extension methods
    /// </summary>
    public static class StringExtensions
    {
        /// <summary>
        /// Truncates a string to the specified length
        /// </summary>
        public static string Truncate(this string value, int maxLength)
        {
            if (string.IsNullOrEmpty(value)) return value;
            return value.Length <= maxLength ? value : value.Substring(0, maxLength) + "...";
        }

        /// <summary>
        /// Checks if a string contains any of the specified values
        /// </summary>
        public static bool ContainsAny(this string value, params string[] values)
        {
            return values.Any(v => value.Contains(v, StringComparison.OrdinalIgnoreCase));
        }
    }
}