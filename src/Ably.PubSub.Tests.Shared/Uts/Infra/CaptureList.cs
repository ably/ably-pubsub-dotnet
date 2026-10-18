using System.Collections;
using System.Collections.Generic;

namespace Ably.PubSub.Tests.Uts.Infra
{
    /// <summary>
    /// A list that SDK callbacks append to while the test body reads it: the specs'
    /// <c>captured = []</c> + <c>captured.append(x)</c>.
    ///
    /// SDK listeners run on the realtime workflow's thread (or a timer or HTTP continuation), never on
    /// the test's, so every access is locked and every read is a snapshot. Enumerating it, indexing it
    /// or reading <see cref="Count"/> never sees a half-written list.
    /// </summary>
    /// <typeparam name="T">The captured item type.</typeparam>
    public sealed class CaptureList<T> : IReadOnlyList<T>
    {
        private readonly object _lock = new object();
        private readonly List<T> _items = new List<T>();

        public int Count
        {
            get
            {
                lock (_lock)
                {
                    return _items.Count;
                }
            }
        }

        public T this[int index]
        {
            get
            {
                lock (_lock)
                {
                    return _items[index];
                }
            }
        }

        /// <summary>The specs' <c>append</c> / <c>push</c>.</summary>
        public void Add(T item)
        {
            lock (_lock)
            {
                _items.Add(item);
            }
        }

        public void Clear()
        {
            lock (_lock)
            {
                _items.Clear();
            }
        }

        /// <summary>A copy, safe to assert on while the SDK keeps appending.</summary>
        public List<T> Snapshot()
        {
            lock (_lock)
            {
                return new List<T>(_items);
            }
        }

        public IEnumerator<T> GetEnumerator() => Snapshot().GetEnumerator();

        IEnumerator IEnumerable.GetEnumerator() => GetEnumerator();
    }
}
