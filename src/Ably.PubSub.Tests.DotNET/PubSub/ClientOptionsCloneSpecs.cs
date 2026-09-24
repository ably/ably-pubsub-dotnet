using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using FluentAssertions;
using Ably.PubSub.Transport;
using Ably.PubSub.Tests.Realtime;
using Xunit;

namespace Ably.PubSub.Tests.PubSub
{
    /// <summary>
    /// A rot-guard for the hand-maintained public <see cref="ClientOptions.Clone"/>. Clone() is a
    /// public API of the core (the door factories copy-on-entry through it), and it enumerates every
    /// property by hand, so the day someone adds a property and forgets to copy it, a door consumer
    /// silently loses that setting. This spec stamps a distinct, non-default value on every public
    /// settable property (including the inherited AuthOptions members), clones, and asserts the clone
    /// matches the original - so a missed property fails here instead of in the field.
    ///
    /// The value factory THROWS on a property type it does not know how to stamp, so a newly-added
    /// property of an unhandled shape also fails here rather than being skipped.
    /// </summary>
    public class ClientOptionsCloneSpecs
    {
        // Public setter, no getter: the loop stamps them via the setter and asserts through the
        // private backing field (Clone copies the field by reference).
        private static readonly IReadOnlyDictionary<string, string> WriteOnlyBackingField =
            new Dictionary<string, string>
            {
                ["RestHost"] = "_restHost",
                ["RealtimeHost"] = "_realtimeHost",
                ["FallbackHosts"] = "_fallbackHosts",
            };

        // Clone rebuilds these as a fresh dictionary with equal contents (not a shared reference).
        private static readonly HashSet<string> DeepCopiedDictionaries =
            new HashSet<string> { "Agents", "AuthHeaders", "AuthParams", "TransportParams" };

        [Fact]
        public void Clone_CopiesEveryPublicSettableProperty()
        {
            var original = new ClientOptions();
            var defaults = new ClientOptions();

            // Public setter, including inherited AuthOptions members. ChannelDefaults has only an
            // internal setter, so GetSetMethod(nonPublic: false) excludes it here; it is asserted
            // separately below.
            var props = typeof(ClientOptions)
                .GetProperties(BindingFlags.Public | BindingFlags.Instance)
                .Where(p => p.GetSetMethod(nonPublic: false) != null)
                .ToArray();

            // 1. Stamp a distinct, non-default value on every public settable property.
            foreach (var p in props)
            {
                p.SetValue(original, DistinctNonDefaultFor(p, defaults));
            }

            var clone = original.Clone();

            // 2. Assert per property, comparing the CLONE against the ORIGINAL's getter (not the
            //    stamped value): a setter that deliberately no-ops - e.g. UseBinaryProtocol discards
            //    its value in non-MSGPACK builds - then compares default-to-default and passes, while
            //    a property Clone() forgot still fails (the original carries the stamp, the clone
            //    carries the default).
            foreach (var p in props)
            {
                if (WriteOnlyBackingField.TryGetValue(p.Name, out var fieldName))
                {
                    var field = typeof(ClientOptions)
                        .GetField(fieldName, BindingFlags.NonPublic | BindingFlags.Instance);
                    field.GetValue(clone).Should()
                        .Be(field.GetValue(original), $"{p.Name} (write-only, via {fieldName})");
                }
                else if (DeepCopiedDictionaries.Contains(p.Name))
                {
                    var cloneVal = (IEnumerable)p.GetValue(clone);
                    var origVal = (IEnumerable)p.GetValue(original);
                    cloneVal.Should().NotBeNull(p.Name);
                    cloneVal.Should().NotBeSameAs(origVal, $"{p.Name} must be a fresh dictionary, not a shared reference");
                    cloneVal.Should().BeEquivalentTo(origVal, $"{p.Name} contents must be copied");
                }
                else
                {
                    p.GetValue(clone).Should().Be(p.GetValue(original), p.Name);
                }
            }

            // 3. ChannelDefaults (internal setter, excluded from the public loop) is copied by
            //    reference.
            clone.ChannelDefaults.Should().BeSameAs(original.ChannelDefaults);

            // 4. Internal state the core reads, not visible to the public loop (bound via
            //    InternalsVisibleTo). Logger/NowFunc are copied by reference; SkipInternetCheck by value.
            var distinctLogger = InternalLogger.Create();
            Func<DateTimeOffset> distinctNow = () => DateTimeOffset.UnixEpoch;
            original.Logger = distinctLogger;
            original.NowFunc = distinctNow;
            original.SkipInternetCheck = true;

            var cloneWithInternals = original.Clone();
            cloneWithInternals.Logger.Should().BeSameAs(distinctLogger);
            cloneWithInternals.NowFunc.Should().BeSameAs(distinctNow);
            cloneWithInternals.SkipInternetCheck.Should().BeTrue();
        }

        /// <summary>
        /// Produces a value distinct from the property's default so that a Clone() that forgets to
        /// copy the property (leaving the clone at its default) fails the equality assertion. Throws
        /// on an unhandled type so a newly-added property of a new shape cannot pass silently.
        /// </summary>
        private static object DistinctNonDefaultFor(PropertyInfo p, ClientOptions defaults)
        {
            var t = p.PropertyType;

            // Write-only host properties have no getter; do not attempt to read a default.
            object def = p.CanRead ? p.GetValue(defaults) : null;

            if (t == typeof(string))
            {
                // ClientId rejects the "*" wildcard in its setter; any other value is fine.
                return "stamp-" + p.Name;
            }

            if (t == typeof(bool))
            {
                return !(bool)def;
            }

            if (t == typeof(bool?))
            {
                return def == null ? (object)true : !(bool)def;
            }

            if (t == typeof(int))
            {
                // Adding a positive keeps constrained ints valid (HeartbeatMonitorDelay must be >= 1).
                return (def is int i ? i : 0) + 7;
            }

            if (t == typeof(TimeSpan))
            {
                // A small positive offset keeps constrained spans valid (RealtimeRequestTimeout must
                // be >= 1ms and within Int32.MaxValue ms).
                return (def is TimeSpan ts ? ts : TimeSpan.Zero) + TimeSpan.FromMilliseconds(1234);
            }

            if (t.IsEnum)
            {
                return Enum.GetValues(t).Cast<object>().First(v => !Equals(v, def));
            }

            if (t == typeof(Uri))
            {
                return new Uri("https://stamp.example.com/" + p.Name);
            }

            if (t == typeof(HttpMethod))
            {
                return HttpMethod.Put;
            }

            if (t == typeof(string[]))
            {
                return new[] { "stamp-host-" + p.Name };
            }

            if (t == typeof(Dictionary<string, string>))
            {
                return new Dictionary<string, string> { ["k-" + p.Name] = "v-" + p.Name };
            }

            if (t == typeof(Dictionary<string, object>))
            {
                return new Dictionary<string, object> { ["k-" + p.Name] = "v-" + p.Name };
            }

            if (t == typeof(SynchronizationContext))
            {
                return new SynchronizationContext();
            }

            if (t == typeof(HttpClient))
            {
                return new HttpClient();
            }

            if (t == typeof(TokenParams))
            {
                return new TokenParams { Ttl = TimeSpan.FromMinutes(7) };
            }

            if (t == typeof(TokenDetails))
            {
                return new TokenDetails("stamp-" + p.Name);
            }

            if (t == typeof(ILoggerSink))
            {
                return new StubLoggerSink();
            }

            if (t == typeof(ITransportFactory))
            {
                return new FakeTransportFactory();
            }

            if (t == typeof(Func<TokenParams, Task<object>>))
            {
                return new Func<TokenParams, Task<object>>(_ => Task.FromResult<object>(null));
            }

            throw new NotSupportedException(
                $"{nameof(ClientOptionsCloneSpecs)}.{nameof(DistinctNonDefaultFor)} has no stamp for " +
                $"property '{p.Name}' of type '{t}'. Add a case here, and confirm ClientOptions.Clone() " +
                "copies the new property.");
        }

        private sealed class StubLoggerSink : ILoggerSink
        {
            public void LogEvent(LogLevel level, string message)
            {
            }
        }
    }
}
