using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Infra;
using FluentAssertions;
using Xunit;

namespace Ably.PubSub.Tests.Uts.InfraTests
{
    /// <summary>Self-tests of <see cref="CaptureList{T}"/>: appends from many threads while the test reads.</summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "common")]
    public class CaptureListTests
    {
        [Fact]
        public async Task CaptureListKeepsEveryItemUnderConcurrentAppends()
        {
            const int writers = 8;
            const int perWriter = 2000;
            var list = new CaptureList<int>();
            var start = new ManualResetEventSlim(false);
            var done = 0;

            var tasks = Enumerable.Range(0, writers).Select(w => Task.Run(() =>
            {
                start.Wait();
                for (var i = 0; i < perWriter; i++)
                {
                    list.Add((w * perWriter) + i);
                }

                Interlocked.Increment(ref done);
            })).ToList();

            // A reader enumerates and indexes while the writers append: every read is a consistent snapshot.
            var reader = Task.Run(() =>
            {
                start.Wait();
                var reads = 0;
                while (Volatile.Read(ref done) < writers)
                {
                    var snapshot = list.Snapshot();
                    snapshot.Count.Should().BeLessOrEqualTo(writers * perWriter);
                    foreach (var item in list)
                    {
                        item.Should().BeInRange(0, (writers * perWriter) - 1);
                    }

                    if (list.Count > 0)
                    {
                        list[0].Should().BeInRange(0, (writers * perWriter) - 1);
                    }

                    reads++;
                }

                return reads;
            });

            start.Set();
            await Task.WhenAll(tasks);
            await reader;

            list.Count.Should().Be(writers * perWriter);
            new HashSet<int>(list).Count.Should().Be(writers * perWriter, "no append was lost or duplicated");
        }

        [Fact]
        public void CaptureListSnapshotIsACopy()
        {
            var list = new CaptureList<string> { "a" };
            var snapshot = list.Snapshot();

            list.Add("b");

            snapshot.Should().Equal("a");
            list.Should().Equal("a", "b");
        }
    }
}
