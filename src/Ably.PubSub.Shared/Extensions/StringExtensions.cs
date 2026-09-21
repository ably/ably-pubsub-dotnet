using System;
using System.Collections.Generic;
using System.Linq;

namespace IO.Ably
{
    internal static class StringExtensions
    {
        public static bool IsNotEmpty(this string text)
        {
            return string.IsNullOrEmpty(text) == false;
        }

        public static bool IsEmpty(this string text)
        {
            return string.IsNullOrEmpty(text);
        }

        public static bool IsJson(this string input)
        {
            if (IsEmpty(input))
            {
                return false;
            }

            input = input.Trim();
            return (input.StartsWith("{") && input.EndsWith("}"))
                   || (input.StartsWith("[") && input.EndsWith("]"));
        }

        public static string SafeTrim(this string input)
        {
            return input.IsEmpty() ? input : input.Trim();
        }

        public static string JoinStrings(this IEnumerable<string> input, string delimiter = ", ")
        {
            return input == null ? string.Empty : string.Join(delimiter, input.Where(IsNotEmpty));
        }

        public static bool EqualsTo(this string input, string other, bool caseSensitive = false)
        {
            return string.Equals(input, other, caseSensitive ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase);
        }

        public static byte[] ToByteArray(this string hex)
        {
            int numberChars = hex.Length;
            var bytes = new byte[numberChars / 2];
            for (int i = 0; i < numberChars; i += 2)
            {
                bytes[i / 2] = Convert.ToByte(hex.Substring(i, 2), 16);
            }

            return bytes;
        }
    }
}
