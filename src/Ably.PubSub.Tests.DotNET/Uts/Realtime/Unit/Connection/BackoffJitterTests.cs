using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Shared.Utils;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Connection
{
    /// <summary>
    /// Derived from uts/realtime/unit/connection/backoff_jitter_test.md in ably/specification.
    ///
    /// Spec points: RTB1, RTB1a, RTB1b
    ///
    /// <para>
    /// The spec's first two tests call the backoff and jitter functions directly, which its own
    /// Implementation Notes sanction. Here they are <c>ReconnectionStrategy.GetBackoffCoefficient</c> and
    /// <c>ReconnectionStrategy.GetJitterCoefficient</c> - internal, and reachable from this assembly
    /// through <c>InternalsVisibleTo</c>, so neither has to be driven indirectly.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>enable_fake_timers()</c> / <c>ADVANCE_TIME</c> loop has no equivalent here. There is
    /// no timer seam: every connection state constructs its own <c>CountdownTimer</c> inline, with no
    /// injection point reachable from a client. The DISCONNECTED waits are therefore made to elapse by
    /// shortening <c>DisconnectedRetryTimeout</c> - the very value every assertion is a ratio of - so the
    /// arithmetic under test is unchanged and only the wall-clock cost of waiting it out shrinks.
    /// </para>
    ///
    /// <para>
    /// <c>realtime/unit/RTB1/suspended-channel-retry-delay-1</c> is NOT translated. It asserts on
    /// <c>ChannelStateChange.retryIn</c>, and <c>Ably.PubSub.Realtime.ChannelStateChange</c> has no such
    /// member: its surface is Previous, Current, Error, Resumed, Event and an internal ProtocolMessage.
    /// The SDK does apply RTB1 to a SUSPENDED channel - <c>RealtimeChannel.ReattachAfterTimeout</c>
    /// computes <c>ReconnectionStrategy.GetRetryTime(Options.ChannelRetryTimeout, retryCount)</c> - but it
    /// keeps the figure in a local and never publishes it, so there is no observable, internal or
    /// otherwise, for the test to read. Writing it would be a compile error, not a failing assertion.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class BackoffJitterTests : UtsTestBase
    {
        /// <summary>
        /// <c>RetryIn</c> is a <c>TimeSpan</c> built from a <c>double</c> number of milliseconds, so the
        /// reported figure can sit a fraction of a millisecond off a bound computed in double arithmetic.
        /// A millisecond of slack absorbs that; it is two orders of magnitude narrower than the jitter
        /// band being checked, so it cannot admit a wrong backoff coefficient.
        /// </summary>
        private const double QuantisationToleranceMs = 1.0;

        public BackoffJitterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTB1a/backoff-coefficient-sequence-0
        [Fact]
        public void RTB1a_BackoffCoefficientSequence()
        {
            var coefficients = new List<double>();
            for (var n = 1; n <= 10; n++)
            {
                coefficients.Add(ReconnectionStrategy.GetBackoffCoefficient(n));
            }

            coefficients[0].Should().Be(1.0, "n=1 gives (1+2)/3 = 1");
            coefficients[1].Should().Be(4.0 / 3.0, "n=2 gives (2+2)/3 = 4/3");
            coefficients[2].Should().Be(5.0 / 3.0, "n=3 gives (3+2)/3 = 5/3");
            coefficients[3].Should().Be(2.0, "n=4 gives (4+2)/3 = 2, which is also the cap");

            for (var i = 3; i < coefficients.Count; i++)
            {
                coefficients[i].Should().Be(2.0, $"retry {i + 1} is capped at 2");
            }
        }

        // UTS: realtime/unit/RTB1b/jitter-coefficient-range-0
        [Fact]
        public void RTB1b_JitterCoefficientRange()
        {
            const int sampleCount = 1000;

            var jitterValues = new List<double>();
            for (var i = 0; i < sampleCount; i++)
            {
                jitterValues.Add(ReconnectionStrategy.GetJitterCoefficient());
            }

            foreach (var jitter in jitterValues)
            {
                jitter.Should().BeGreaterOrEqualTo(0.8);
                jitter.Should().BeLessOrEqualTo(1.0);
            }

            // Approximate uniformity: the mean should sit near the 0.9 midpoint. Over 1000 samples of a
            // uniform [0.8, 1.0] the standard error of the mean is about 0.0018, so the spec's window is
            // roughly 27 standard errors wide and a correct generator cannot leave it in practice.
            var mean = jitterValues.Average();
            mean.Should().BeGreaterOrEqualTo(0.85);
            mean.Should().BeLessOrEqualTo(0.95);

            var minValue = jitterValues.Min();
            var maxValue = jitterValues.Max();
            (maxValue - minValue).Should().BeGreaterThan(
                0.05,
                "a generator that returns one value for every sample is not jitter");
        }

        // UTS: realtime/unit/RTB1/disconnected-retry-delay-0
        //
        // UTS SPEC ERROR — recorded under "UTS Spec Errors" in Uts/deviations.md, not as an SDK
        // deviation. Gated so the suite stays green while the question goes upstream.
        //
        // The spec drives the cycle with simulate_disconnect() from CONNECTED and then expects the
        // FIRST DISCONNECTED to advertise retryIn = disconnectedRetryTimeout * backoff(1) * jitter.
        // That ignores RTN15a/RTN15h3, which earn an unexpectedly dropped connection an *immediate*
        // reconnect: under RTN14d, retryIn is "the time in milliseconds until the next connection
        // attempt", so it is correctly 0 when no wait is taken, and the RTB1 coefficient sequence
        // begins on the DISCONNECTED after it.
        //
        // This SDK does exactly that, and asserts it deliberately in a pre-existing test tagged
        // against RTN14d: DisconnectedStateSpecs.WhenRetryingInstantly_ShouldReportNoWaitAndNotStart
        // TheTimer (Tests.Shared/.../DisconnectedStateSpecs.cs:135-146) pins
        // RetryIn == TimeSpan.Zero and no timer started.
        //
        // Measured: observed [0, 4/3, 5/3, 2, 2] against the spec's [1, 4/3, 5/3, 2, 2] — indices
        // 1 to 4 match exactly, and only index 0 diverges. The spec should either start its expected
        // sequence one retry later, or provoke the first DISCONNECTED from CONNECTING, where no
        // immediate retry is budgeted.
        [DeviationFact]
        public async Task RTB1_DisconnectedRetryDelay()
        {
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                connectionAttemptCount++;

                if (connectionAttemptCount == 1)
                {
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage(
                        connectionId: "connection-id",
                        connectionKey: "connection-key",
                        connectionStateTtl: 60000,
                        maxIdleInterval: 15000));
                }
                else
                {
                    conn.RespondWithRefused();
                }
            });

            // The spec's 2000ms, shortened because there is no timer seam to advance - see the class
            // summary. Every bound below is a ratio of this value, so the sequence being checked is the
            // same one; only the real time spent waiting it out differs from the spec's fake-timer run.
            var disconnectedRetryTimeout = TimeSpan.FromMilliseconds(300);

            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.DisconnectedRetryTimeout = disconnectedRetryTimeout;
                options.AutoConnect = false;

                // The spec's useBinaryProtocol: false. Already the only possibility here - msgpack is
                // compiled out of this build and the setter is a no-op - but set as the spec sets it.
                options.UseBinaryProtocol = false;
            });

            // The spec's connection.on listener that appends change.retryIn on every DISCONNECTED, kept as
            // the harness's recorder: registered before Connect() so the first transition cannot be
            // missed, and safe to read while the client is still cycling.
            var changes = UtsClients.RecordConnectionStateChanges(client.Connection);

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // The spec reads the connection off its CONNECTION_SUCCESS event; ActiveConnection is that
            // same object, and simulate_disconnect is the server dropping the socket.
            mockWs.ActiveConnection.SimulateDisconnect();

            await UtsClients.PollUntil(
                () => RetryDelays(changes).Count >= 5,
                "five DISCONNECTED state changes",
                TimeSpan.FromSeconds(20));

            var retryDelays = RetryDelays(changes);
            var observed = string.Join(
                ", ",
                retryDelays.Select(d => d.HasValue ? $"{d.Value.TotalMilliseconds}ms" : "null"));

            retryDelays.Count.Should().BeGreaterOrEqualTo(5);

            void AssertRetryInForBackoff(int index, double backoff)
            {
                var retryIn = retryDelays[index];
                var nominal = disconnectedRetryTimeout.TotalMilliseconds * backoff;

                retryIn.Should().HaveValue(
                    $"retry {index + 1} must report the delay until the next attempt; observed [{observed}]");

                retryIn.Value.TotalMilliseconds.Should().BeInRange(
                    (nominal * 0.8) - QuantisationToleranceMs,
                    nominal + QuantisationToleranceMs,
                    $"retry {index + 1} is disconnectedRetryTimeout * {backoff} * a jitter coefficient " +
                    $"in [0.8, 1.0]; observed [{observed}]");
            }

            // NOTE: index 0 is expected to fail against this SDK, and the spec's assertion is translated as
            // written rather than adapted to it. On an unexpected drop from CONNECTED the SDK takes
            // RTN15a/RTN15h3's immediate reconnect - RealtimeWorkflow's SetDisconnectedStateCommand
            // handler is given retryInstantly: Connection.ConnectionResumable - and
            // ConnectionDisconnectedState.StartTimer then reports RetryIn = TimeSpan.Zero for that
            // transition and starts no timer at all, deliberately: RTN14d asks for "the time until the
            // next connection attempt", and here there is no wait. Ably.PubSub.Tests.Shared's
            // ConnectionFailureSpecs asserts exactly that zero. One instant retry is budgeted per
            // domain (RTN17j), so with no fallback hosts the RTB1 coefficients begin on the DISCONNECTED
            // after it and the observed sequence is [0, 4/3, 5/3, 2, 2, ...] where the spec expects
            // [1, 4/3, 5/3, 2, 2] - the spec's scenario reaches the backoff one retry later than it
            // assumes. Which side is wrong is for the diagnosis to settle, not for this file.
            AssertRetryInForBackoff(0, 1.0);
            AssertRetryInForBackoff(1, 4.0 / 3.0);
            AssertRetryInForBackoff(2, 5.0 / 3.0);
            AssertRetryInForBackoff(3, 2.0);
            AssertRetryInForBackoff(4, 2.0);
        }

        /// <summary>
        /// The spec's <c>retry_delays</c>: the <c>retryIn</c> of every DISCONNECTED transition, in order.
        /// </summary>
        private static List<TimeSpan?> RetryDelays(List<ConnectionStateChange> changes) =>
            UtsClients.Snapshot(changes)
                .Where(change => change.Current == ConnectionState.Disconnected)
                .Select(change => change.RetryIn)
                .ToList();
    }
}
