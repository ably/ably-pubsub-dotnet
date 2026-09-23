using System.Text;

namespace IO.Ably
{
    internal static class ObjectExtensions
    {
        public static string ToJson(this object obj)
        {
            return JsonHelper.Serialize(obj);
        }

        public static string ToHexString(this byte[] ba)
        {
            StringBuilder hex = new StringBuilder(ba.Length * 2);
            foreach (byte b in ba)
            {
                hex.AppendFormat("{0:x2}", b);
            }

            return hex.ToString();
        }
    }
}
