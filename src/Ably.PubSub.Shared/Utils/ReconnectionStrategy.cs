using System;

namespace IO.Ably.Shared.Utils
{
    // RTB1
    internal class ReconnectionStrategy
    {
        private static readonly Random Random = new Random();

        public static double GetBackoffCoefficient(int count)
        {
            return Math.Min((count + 2) / 3d, 2d);
        }

        public static double GetJitterCoefficient()
        {
            return 1 - (Random.NextDouble() * 0.2);
        }

        // Generates retryTimeout value for given timeout and retryAttempt.
        // If x is the value generated then
        // Upper bound = min((retryAttempt + 2) / 3, 2) * initialTimeout
        // Lower bound = 0.8 * Upper bound
        // Lower bound < x < Upper bound
        public static double GetRetryTime(double initialTimeout, int retryAttempt)
        {
            return initialTimeout * GetBackoffCoefficient(retryAttempt) * GetJitterCoefficient();
        }
    }
}
