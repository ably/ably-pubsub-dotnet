using System;
using System.Collections.Generic;
using System.Linq;

namespace IO.Ably
{
    /// <summary>
    /// Enumerable extension methods.
    /// </summary>
    public static class EnumerableExtensions
    {
        /// <summary>
        /// Shuffles the values of a collection.
        /// </summary>
        /// <typeparam name="T">type.</typeparam>
        /// <param name="source">source collection.</param>
        /// <returns>returns a shuffled collection.</returns>
        public static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source)
        {
            return source.Shuffle(new Random());
        }

        /// <summary>
        /// Shuffles the values of a collection.
        /// </summary>
        /// <typeparam name="T">type.</typeparam>
        /// <param name="source">source collection.</param>
        /// <param name="rng">random seed.</param>
        /// <returns>returns a shuffled collection.</returns>
        private static IEnumerable<T> Shuffle<T>(this IEnumerable<T> source, Random rng)
        {
            if (source == null)
            {
                throw new ArgumentNullException(nameof(source));
            }

            if (rng == null)
            {
                throw new ArgumentNullException(nameof(rng));
            }

            return source.ShuffleIterator(rng);
        }

        private static IEnumerable<T> ShuffleIterator<T>(
            this IEnumerable<T> source, Random rng)
        {
            var buffer = source.ToList();
            for (int i = 0; i < buffer.Count; i++)
            {
                int j = rng.Next(i, buffer.Count);
                yield return buffer[j];

                buffer[j] = buffer[i];
            }
        }
    }
}
