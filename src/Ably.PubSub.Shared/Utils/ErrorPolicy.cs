using System;

namespace IO.Ably.Utils
{
    /// <summary>
    /// Utility type to defining various error policies.
    /// </summary>
    internal static class ErrorPolicy
    {
        /// <summary>
        /// Upon receiving an *unexpected* exception and local policy is unclear, defer to this method.
        /// </summary>
        /// <param name="e">The unexpected exception.</param>
        /// <param name="logger">Logger to report to.</param>
        public static void HandleUnexpected(Exception e, ILogger logger)
        {
            string type = e.GetType().Name;
            string message = $"Caught unexpected '{type}': '{e.Message}'";
            logger.Debug(message);
        }
    }
}
