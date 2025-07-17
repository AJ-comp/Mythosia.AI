using System;
using System.ComponentModel;
using System.Linq;
using System.Reflection;

namespace Mythosia.AI.Extensions
{
    /// <summary>
    /// Extension methods for enum types
    /// </summary>
    public static class EnumExtensions
    {
        /// <summary>
        /// Gets the Description attribute value from an enum
        /// </summary>
        public static string ToDescription(this Enum value)
        {
            var field = value.GetType().GetField(value.ToString());
            if (field == null) return value.ToString();

            var attribute = field.GetCustomAttribute<DescriptionAttribute>();
            return attribute?.Description ?? value.ToString();
        }

        /// <summary>
        /// Converts a description string back to enum value
        /// </summary>
        public static T FromDescription<T>(string description) where T : Enum
        {
            var type = typeof(T);
            foreach (var field in type.GetFields())
            {
                var attribute = field.GetCustomAttribute<DescriptionAttribute>();
                if (attribute?.Description == description)
                {
                    return (T)field.GetValue(null);
                }

                if (field.Name == description)
                {
                    return (T)field.GetValue(null);
                }
            }

            throw new ArgumentException($"No {type.Name} with description '{description}' found.");
        }

        /// <summary>
        /// Tries to convert a description string to enum value
        /// </summary>
        public static bool TryFromDescription<T>(string description, out T result) where T : Enum
        {
            try
            {
                result = FromDescription<T>(description);
                return true;
            }
            catch
            {
                result = default(T);
                return false;
            }
        }

        /// <summary>
        /// Gets all values of an enum type
        /// </summary>
        public static T[] GetValues<T>() where T : Enum
        {
            return (T[])Enum.GetValues(typeof(T));
        }

        /// <summary>
        /// Gets all descriptions for an enum type
        /// </summary>
        public static string[] GetDescriptions<T>() where T : Enum
        {
            return GetValues<T>().Select(v => v.ToDescription()).ToArray();
        }
    }

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