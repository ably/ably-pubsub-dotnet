using System;

namespace IO.Ably
{
    /// <summary>
    /// Extension methods working with dates.
    /// </summary>
    public static class DateExtensions
    {
        /// <summary>
        /// Unix Epoch.
        /// </summary>
        public static readonly DateTimeOffset Epoch = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);

        /// <summary>
        /// Converts Unix time in milliseconds to a DateTimeOffset object.
        /// </summary>
        /// <param name="unixTime">The unix time.</param>
        /// <returns>returns corresponding DateTimeOffset object.</returns>
        public static DateTimeOffset FromUnixTimeInMilliseconds(this long unixTime)
        {
            return Epoch.AddMilliseconds(unixTime);
        }

        /// <summary>
        /// Converts a datetime offsite to a unix time in milliseconds.
        /// </summary>
        /// <param name="date">the date offset to be converted.</param>
        /// <returns>unix time in milliseconds.</returns>
        public static long ToUnixTimeInMilliseconds(this DateTimeOffset date)
        {
            return Convert.ToInt64((date - Epoch).TotalMilliseconds);
        }

        /// <summary>
        /// Converts a Timespan to a string in the following format {0}h:{1}m:{2}s.{3}ms
        /// e.g. 00:03:32.8289777.
        /// </summary>
        /// <param name="timeSpan">The timespan.</param>
        /// <returns>the timespan as a string.</returns>
        public static string TimeToString(this TimeSpan timeSpan)
        {
            return $"{timeSpan.Hours}h:{timeSpan.Minutes}m:{timeSpan.Seconds}s.{timeSpan.Milliseconds}ms";
        }
    }
}
