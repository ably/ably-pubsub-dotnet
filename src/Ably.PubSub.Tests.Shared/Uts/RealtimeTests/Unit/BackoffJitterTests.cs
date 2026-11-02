// Backoff and Jitter Tests (RTB1, RTB1a, RTB1b)
// Derived from uts/realtime/unit/connection/backoff_jitter_test.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: white-box: ReconnectionStrategy.GetBackoffCoefficient and GetJitterCoefficient (internal, via InternalsVisibleTo; realtime-mapping.md section 14); fake timers enabled before the client is built

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Shared.Utils;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class BackoffJitterTests : UtsUnitTestBase
    {
        public BackoffJitterTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTB1a/backoff-coefficient-sequence-0
        [Fact]
        [Trait("spec", "RTB1a")]
        public void RTB1a_BackoffCoefficientSequence()
        {
            // Setup
            // This test verifies the backoff coefficient calculation function directly.
            // The function under test takes a retry count (1-indexed) and returns the
            // backoff coefficient.

            // Expected values:
            // n=1: min((1+2)/3, 2) = min(1, 2) = 1.0
            // n=2: min((2+2)/3, 2) = min(4/3, 2) = 1.333...
            // n=3: min((3+2)/3, 2) = min(5/3, 2) = 1.666...
            // n=4: min((4+2)/3, 2) = min(2, 2) = 2.0
            // n=5: min((5+2)/3, 2) = min(7/3, 2) = 2.0
            // n=10: min((10+2)/3, 2) = min(4, 2) = 2.0

            // Test Steps
            // Calculate backoff coefficients for retries 1 through 10
            var coefficients = new List<double>();
            for (var n = 1; n <= 10; n++)
            {
                var coefficient = ReconnectionStrategy.GetBackoffCoefficient(n);
                coefficients.Add(coefficient);
            }

            // Assertions
            // Verify exact values for the first few retries
            coefficients[0].Should().Be(1.0); // n=1: (1+2)/3 = 1
            coefficients[1].Should().Be(4.0 / 3.0); // n=2: (2+2)/3 = 4/3
            coefficients[2].Should().Be(5.0 / 3.0); // n=3: (3+2)/3 = 5/3
            coefficients[3].Should().Be(2.0); // n=4: (4+2)/3 = 2, capped at 2

            // Verify all subsequent retries are capped at 2.0
            for (var i = 3; i <= 9; i++)
            {
                coefficients[i].Should().Be(2.0);
            }
        }

        // UTS: realtime/unit/RTB1b/jitter-coefficient-range-0
        [Fact]
        [Trait("spec", "RTB1b")]
        public void RTB1b_JitterCoefficientRange()
        {
            // Setup
            // This test verifies the jitter coefficient generator.
            // We sample it many times and verify range and approximate uniformity.

            // Test Steps
            var sampleCount = 1000;
            var jitterValues = new List<double>();

            for (var i = 1; i <= sampleCount; i++)
            {
                var jitter = ReconnectionStrategy.GetJitterCoefficient();
                jitterValues.Add(jitter);
            }

            // Assertions
            // All values must be within [0.8, 1.0]
            foreach (var jitter in jitterValues)
            {
                jitter.Should().BeGreaterThanOrEqualTo(0.8);
                jitter.Should().BeLessThanOrEqualTo(1.0);
            }

            // Verify approximate uniformity: the mean should be close to 0.9
            // (the midpoint of 0.8 and 1.0). Allow some tolerance for randomness.
            var mean = jitterValues.Sum() / sampleCount;
            mean.Should().BeGreaterThanOrEqualTo(0.85);
            mean.Should().BeLessThanOrEqualTo(0.95);

            // Verify spread: not all values are the same (degenerate case)
            var minValue = jitterValues.Min();
            var maxValue = jitterValues.Max();
            (maxValue - minValue).Should().BeGreaterThan(0.05);
        }

        // UTS: realtime/unit/RTB1/disconnected-retry-delay-0
        // DEVIATION: see deviations.md
        [DeviationFact("RTB1", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RTB1")]
        public async Task RTB1_DisconnectedRetryDelay()
        {
            // Setup
            var connectionAttemptCount = 0;
            var retryDelays = new CaptureList<TimeSpan?>();

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // Initial connection succeeds
                    conn.RespondWithSuccess(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", connectionStateTtl: 60000, maxIdleInterval: 15000));
                }
                else
                {
                    // All reconnection attempts fail
                    conn.RespondWithRefused();
                }
            });
            InstallMock(mockWs);

            var disconnectedRetryTimeout = 2000; // 2 seconds

            // NOTE: the spec enables fake timers after building the client; they are enabled first here (before Realtime(...)), which is equivalent because no timer has started yet
            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(disconnectedRetryTimeout),
                AutoConnect = false,
            });

            // Capture retryIn from DISCONNECTED state changes
            client.Connection.On(change =>
            {
                if (change.Current == ConnectionState.Disconnected)
                {
                    retryDelays.Add(change.RetryIn);
                }
            });

            // Test Steps
            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Simulate unexpected disconnect to trigger reconnection cycle
            var ws = mockWs.Connections[0];
            ws.SimulateDisconnect();

            // Advance time in increments to allow multiple retry cycles.
            // Each retry fails (respond_with_refused), producing another DISCONNECTED
            // state change with a retryIn value.
            // We want at least 5 DISCONNECTED events to verify the backoff sequence.
            for (var i = 0; i < 30; i++)
            {
                await AdvanceTime(5000);
                if (retryDelays.Count >= 5)
                {
                    break;
                }
            }

            // Assertions
            retryDelays.Count.Should().BeGreaterThanOrEqualTo(5);
            var delays = retryDelays.Snapshot();
            foreach (var delay in delays.Take(5))
            {
                delay.Should().HaveValue();
            }

            // For each retry, verify retryIn is within the expected range:
            // retryIn = disconnectedRetryTimeout * backoff(n) * jitter
            // where jitter is in [0.8, 1.0]

            // Retry 1: backoff = 1.0, range = [2000*0.8, 2000*1.0] = [1600, 2000]
            delays[0].Value.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(disconnectedRetryTimeout * 1.0 * 0.8);
            delays[0].Value.TotalMilliseconds.Should().BeLessThanOrEqualTo(disconnectedRetryTimeout * 1.0 * 1.0);

            // Retry 2: backoff = 4/3, range = [2000*4/3*0.8, 2000*4/3*1.0] = [2133, 2667]
            delays[1].Value.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(disconnectedRetryTimeout * (4.0 / 3.0) * 0.8);
            delays[1].Value.TotalMilliseconds.Should().BeLessThanOrEqualTo(disconnectedRetryTimeout * (4.0 / 3.0) * 1.0);

            // Retry 3: backoff = 5/3, range = [2000*5/3*0.8, 2000*5/3*1.0] = [2667, 3333]
            delays[2].Value.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(disconnectedRetryTimeout * (5.0 / 3.0) * 0.8);
            delays[2].Value.TotalMilliseconds.Should().BeLessThanOrEqualTo(disconnectedRetryTimeout * (5.0 / 3.0) * 1.0);

            // Retry 4+: backoff = 2.0 (capped), range = [2000*2*0.8, 2000*2*1.0] = [3200, 4000]
            delays[3].Value.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(disconnectedRetryTimeout * 2.0 * 0.8);
            delays[3].Value.TotalMilliseconds.Should().BeLessThanOrEqualTo(disconnectedRetryTimeout * 2.0 * 1.0);

            delays[4].Value.TotalMilliseconds.Should().BeGreaterThanOrEqualTo(disconnectedRetryTimeout * 2.0 * 0.8);
            delays[4].Value.TotalMilliseconds.Should().BeLessThanOrEqualTo(disconnectedRetryTimeout * 2.0 * 1.0);

            // Verify the delays are monotonically non-decreasing (on average),
            // accounting for jitter. The max of retry n should be <= max of retry n+1
            // when backoff is increasing.
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTB1/suspended-channel-retry-delay-1
        [UtsFact(Skip = "UTS spec error RTB1: the spec's ChannelStateChange has no retryIn (TH1-TH6, TA2 is connection-only); see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RTB1")]
        public async Task RTB1_SuspendedChannelRetryDelay()
        {
            // NOTE: the test reads change.retryIn from SUSPENDED ChannelStateChange events; ChannelStateChange (public API) exposes only Current, Previous, Error, Resumed and Event, so the delay cannot be observed
            // Setup
            // channel_name = "test-RTB1-channel-${random_id()}"
            // connection_attempt_count = 0
            // retry_delays = []
            // attach_count = 0
            //
            // mock_ws = MockWebSocket(
            //   onConnectionAttempt: (conn) => {
            //     connection_attempt_count++
            //     conn.respond_with_success(ProtocolMessage(action: CONNECTED, connectionId: "connection-id", connectionKey: "connection-key",
            //       connectionDetails: ConnectionDetails(connectionKey: "connection-key", maxIdleInterval: 15000, connectionStateTtl: 120000)))
            //   },
            //   onMessage: (msg) => {
            //     IF msg.action == ATTACH:
            //       attach_count++
            //       IF attach_count == 1:
            //         # First attach succeeds
            //         mock_ws.active_connection.send_to_client(ProtocolMessage(action: ATTACHED, channel: msg.channel, flags: 0))
            //       ELSE:
            //         # All subsequent re-attach attempts fail with DETACHED (per RTL13b, this triggers SUSPENDED state)
            //         mock_ws.active_connection.send_to_client(ProtocolMessage(action: DETACHED, channel: msg.channel,
            //           error: ErrorInfo(code: 90001, statusCode: 500, message: "Channel re-attach failed")))
            //   }
            // )
            // install_mock(mock_ws)
            // channel_retry_timeout = 3000  # 3 seconds
            // client = Realtime(options: ClientOptions(key: "appId.keyId:keySecret", channelRetryTimeout: channel_retry_timeout, autoConnect: false, useBinaryProtocol: false))
            //
            // Test Steps
            // enable_fake_timers()
            // client.connect()
            // AWAIT_STATE client.connection.state == ConnectionState.connected
            // channel = client.channels.get(channel_name)
            // # Capture retryIn from SUSPENDED state changes
            // channel.on((change) => { IF change.current == ChannelState.suspended: retry_delays.append(change.retryIn) })
            // # Initial attach succeeds
            // channel.attach()
            // AWAIT_STATE channel.state == ChannelState.attached
            // # Server sends ERROR on the channel, triggering re-attach (RTL13b).
            // mock_ws.active_connection.send_to_client(ProtocolMessage(action: ERROR, channel: channel_name, error: ErrorInfo(code: 90001, statusCode: 500, message: "Channel error")))
            // LOOP up to 30 times:
            //   ADVANCE_TIME(7000)
            //   IF retry_delays.length >= 4: BREAK
            //
            // Assertions
            // ASSERT retry_delays.length >= 4
            // # Retry 1: backoff = 1.0, range = [3000*0.8, 3000*1.0] = [2400, 3000]
            // ASSERT retry_delays[0] >= channel_retry_timeout * 1.0 * 0.8
            // ASSERT retry_delays[0] <= channel_retry_timeout * 1.0 * 1.0
            // # Retry 2: backoff = 4/3, range = [3200, 4000]
            // ASSERT retry_delays[1] >= channel_retry_timeout * (4.0/3.0) * 0.8
            // ASSERT retry_delays[1] <= channel_retry_timeout * (4.0/3.0) * 1.0
            // # Retry 3: backoff = 5/3, range = [4000, 5000]
            // ASSERT retry_delays[2] >= channel_retry_timeout * (5.0/3.0) * 0.8
            // ASSERT retry_delays[2] <= channel_retry_timeout * (5.0/3.0) * 1.0
            // # Retry 4: backoff = 2.0 (capped), range = [4800, 6000]
            // ASSERT retry_delays[3] >= channel_retry_timeout * 2.0 * 0.8
            // ASSERT retry_delays[3] <= channel_retry_timeout * 2.0 * 1.0
            // CLOSE_CLIENT(client)
            await Task.CompletedTask;
        }
    }
}
