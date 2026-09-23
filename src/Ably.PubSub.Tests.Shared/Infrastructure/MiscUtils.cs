using System;
using System.Threading.Tasks;

namespace IO.Ably.Tests
{
    internal static class MiscUtils
    {
        public static string AddRandomSuffix(this string str)
        {
            if (str.IsEmpty())
            {
                return str;
            }

            return str + "_" + Guid.NewGuid().ToString("D").Substring(0, 8);
        }

        public static Task<AblyResponse> ToAblyResponse(this string txt)
        {
            return Task.FromResult(new AblyResponse { TextResponse = txt });
        }

        public static Task<AblyResponse> ToAblyJsonResponse(this string txt)
        {
            return Task.FromResult(new AblyResponse { TextResponse = txt, Type = ResponseType.Json });
        }

        public static Task<AblyResponse> ToTask(this AblyResponse r)
        {
            return Task.FromResult(r);
        }
    }
}
