using System.Collections;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Linq;

namespace IO.Ably
{
    [SuppressMessage("ReSharper", "InconsistentlySynchronizedField", Justification = "Double-check locking pattern used.")]
    internal sealed class LockedList<T> : IEnumerable<T>
    {
        private readonly List<T> _items = new List<T>();
        private readonly object _lock = new object();

        public void Add(T item)
        {
            if (_items.Contains(item))
            {
                return;
            }

            lock (_lock)
            {
                if (_items.Contains(item) == false)
                {
                    _items.Add(item);
                }
            }
        }

        public void Remove(T item)
        {
            lock (_lock)
            {
                _items.Remove(item);
            }
        }

        public IEnumerator<T> GetEnumerator()
        {
            lock (_lock)
            {
                return _items.ToList().GetEnumerator();
            }
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return GetEnumerator();
        }
    }
}
