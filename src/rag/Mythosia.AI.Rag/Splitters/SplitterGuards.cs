using System;

namespace Mythosia.AI.Rag.Splitters
{
    internal static class SplitterGuards
    {
        internal static void ValidateSize(int value, string name)
        {
            if (value <= 0)
                throw new ArgumentOutOfRangeException(name, "Chunk size must be positive.");
        }

        internal static void ValidateOverlap(int value, string name)
        {
            if (value < 0)
                throw new ArgumentOutOfRangeException(name, "Chunk overlap cannot be negative.");
        }

        internal static void ValidateSeparator(string separator, string name)
        {
            for (int i = 0; i < separator.Length; i++)
            {
                if (char.IsHighSurrogate(separator[i]) && i + 1 < separator.Length
                    && char.IsLowSurrogate(separator[i + 1]))
                {
                    i++;
                    continue;
                }
                if (char.IsSurrogate(separator[i]))
                    throw new ArgumentException("Separators must contain complete Unicode scalar values.", name);
            }
        }

        // Lengths remain UTF-16 code units for compatibility. A scalar is never split;
        // the sole size exception is a two-unit scalar with a one-unit budget.
        internal static int SafeEnd(string text, int start, int maxLength)
        {
            int end = start + Math.Min(maxLength, text.Length - start);
            if (end > start && end < text.Length
                && char.IsHighSurrogate(text[end - 1]) && char.IsLowSurrogate(text[end]))
            {
                end--;
                if (end == start) end += 2;
            }
            return end;
        }

        internal static int SafeStart(string text, int index)
        {
            if (index > 0 && index < text.Length
                && char.IsHighSurrogate(text[index - 1]) && char.IsLowSurrogate(text[index]))
                return index + 1;
            return index;
        }
    }
}
