using System;

namespace Ably.PubSub
{
    internal static class UriExtensions
    {
        public static bool IsNotEmpty(this Uri uri)
        {
            return uri != null && uri.ToString().IsNotEmpty();
        }
    }
}
