using System;
using System.Text;

namespace IO.Ably
{
    /// <summary>
    /// String utility functions.
    /// </summary>
    public static class StringUtils
    {
        /// <summary>
        /// Returns UTF8 bytes from a given string.
        /// </summary>
        /// <param name="text">input string.</param>
        /// <returns>UTF8 byte[].</returns>
        public static byte[] GetBytes(this string text)
        {
            return Encoding.UTF8.GetBytes(text);
        }

        internal static string GetText(this byte[] bytes)
        {
            return Encoding.UTF8.GetString(bytes, 0, bytes.Length);
        }

        internal static string ToBase64(this byte[] bytes)
        {
            return Convert.ToBase64String(bytes);
        }

        internal static string ToBase64(this string text)
        {
            if (text.IsEmpty())
            {
                return string.Empty;
            }

            return text.GetBytes().ToBase64();
        }

        // https://brockallen.com/2014/10/17/base64url-encoding/
        internal static byte[] FromBase64(this string base64String)
        {
            if (base64String.IsEmpty())
            {
                return Array.Empty<byte>();
            }

            string s = base64String;
            s = s.Replace('-', '+'); // 62nd char of encoding
            s = s.Replace('_', '/'); // 63rd char of encoding

            switch (s.Length % 4)
            {
                // Pad with trailing '='s
                case 0: break; // No pad chars in this case
                case 2: s += "=="; break; // Two pad chars
                case 3: s += "="; break; // One pad char
                default: throw new Exception("Illegal base64url string!");
            }

            return Convert.FromBase64String(s);
        }

        internal static string EncodeUriPart(this string text)
        {
            return Uri.EscapeDataString(text);
        }
    }
}
