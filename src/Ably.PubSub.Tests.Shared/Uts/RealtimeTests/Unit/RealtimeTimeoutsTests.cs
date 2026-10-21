// Realtime Client Configured Timeouts (RTC7)
// Derived from uts/realtime/unit/client/realtime_timeouts.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: enable_fake_timers() is called before the client is built (the clock is injected at construction); client.options is read from the test's own ClientOptions (the IDL has no public options attribute); SDK operations are awaited through UtsWait.WithTimeout (10 s wall clock), so an SDK that never completes fails fast instead of hanging (infra-driving)

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
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
    public class RealtimeTimeoutsTests : UtsUnitTestBase
    {
        public RealtimeTimeoutsTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC7/attach-request-timeout-0
        [UtsFact]
        [Trait("spec", "RTC7")]
        public async Task RTC7_AttachRequestTimeout()
        {
            // Setup
            var channelName = "test-RTC7-attach-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", maxIdleInterval: 15000, connectionStateTtl: 120000));
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        // Do NOT respond - simulate timeout
                        // PASS
                    }
                });
            InstallMock(mockWs);

            // Test Steps
            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(500),
            });

            var channel = client.Channels.Get(channelName);

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            // Start attach - will not get a response
            var attachTask = channel.AttachAsync();

            // Advance past the custom timeout
            await AdvanceTime(600);

            // Attach should fail
            var attachResult = await UtsWait.WithTimeout(attachTask, TimeSpan.FromSeconds(10), "attachTask");
            attachResult.IsFailure.Should().BeTrue();
            var error = attachResult.Error;

            // Assertions
            // The timeout used the custom value (500ms), not the default (10000ms)
            error.Should().NotBeNull();

            // Channel should be in SUSPENDED state (RTL4f: attach timeout -> SUSPENDED)
            channel.State.Should().Be(ChannelState.Suspended);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC7/detach-request-timeout-1
        [UtsFact]
        [Trait("spec", "RTC7")]
        public async Task RTC7_DetachRequestTimeout()
        {
            // Setup
            var channelName = "test-RTC7-detach-" + Guid.NewGuid().ToString("N");
            var ignoreDetach = false;

            MockWebSocket mockWs = null;
            mockWs = new MockWebSocket(
                onConnectionAttempt: conn =>
                {
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", maxIdleInterval: 15000, connectionStateTtl: 120000));
                },
                onMessageFromClient: msg =>
                {
                    if ((int)msg.Action == ProtocolMessages.Attach)
                    {
                        mockWs.SendToClient(ProtocolMessages.AttachedMessage(channelName, new Dictionary<string, JToken> { ["flags"] = 0 }));
                    }

                    if ((int)msg.Action == ProtocolMessages.Detach && Volatile.Read(ref ignoreDetach))
                    {
                        // Do NOT respond - simulate timeout
                        // PASS
                    }
                });
            InstallMock(mockWs);

            // Test Steps
            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                RealtimeRequestTimeout = TimeSpan.FromMilliseconds(500),
            });

            var channel = client.Channels.Get(channelName);

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var attachResult = await UtsWait.WithTimeout(channel.AttachAsync(), TimeSpan.FromSeconds(10), "AttachAsync");
            attachResult.IsSuccess.Should().BeTrue("AWAIT channel.attach() succeeds ({0})", attachResult.Error?.Message);

            // Now ignore DETACH messages
            Volatile.Write(ref ignoreDetach, true);

            // Start detach - will not get a response
            var detachTask = channel.DetachAsync();

            // Advance past the custom timeout
            await AdvanceTime(600);

            // Detach should fail
            var detachResult = await UtsWait.WithTimeout(detachTask, TimeSpan.FromSeconds(10), "detachTask");
            detachResult.IsFailure.Should().BeTrue();
            var error = detachResult.Error;

            // Assertions
            // The timeout used the custom value (500ms), not the default (10000ms)
            error.Should().NotBeNull();

            // Channel should still be in ATTACHED state (RTL5f: detach timeout -> back to ATTACHED)
            channel.State.Should().Be(ChannelState.Attached);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC7/disconnected-retry-timeout-2
        [UtsFact]
        [Trait("spec", "RTC7")]
        public async Task RTC7_DisconnectedRetryTimeout()
        {
            // Setup
            var connectionAttemptCount = 0;

            var mockWs = new MockWebSocket(onConnectionAttempt: conn =>
            {
                if (Interlocked.Increment(ref connectionAttemptCount) == 1)
                {
                    // Initial connection succeeds
                    conn.RespondWithSuccess();
                    conn.SendToClient(ProtocolMessages.ConnectedMessage("connection-id", "connection-key", maxIdleInterval: 0, connectionStateTtl: 120000));
                }
                else
                {
                    // All subsequent attempts fail
                    conn.RespondWithRefused();
                }
            });
            InstallMock(mockWs);

            var mockHttp = new MockHttpClient(onRequest: req => req.RespondWith(200, "yes"));
            InstallMock(mockHttp);

            // Test Steps
            EnableFakeTimers();

            var client = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                AutoConnect = false,
                DisconnectedRetryTimeout = TimeSpan.FromMilliseconds(2000),
                FallbackHosts = new string[0],
            });

            client.Connect();
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            Volatile.Read(ref connectionAttemptCount).Should().Be(1);

            // Force disconnection - triggers RTN15a immediate retry (which fails),
            // then schedules timer-based retry using disconnectedRetryTimeout
            // NOTE: states are recorded before the close: DISCONNECTED may be left and re-entered at once, so the record is polled instead of AWAIT_STATE
            var states = UtsRecord.ConnectionStates(client.Connection);
            mockWs.ActiveConnection.CloseFromServer();

            // Wait for the immediate retry to fail and state to return to DISCONNECTED
            await UtsWait.PollUntil(
                () => states.Snapshot().Count(s => s == ConnectionState.Disconnected) >= 1 && Volatile.Read(ref connectionAttemptCount) >= 2 && client.Connection.State == ConnectionState.Disconnected,
                description: "DISCONNECTED again after the immediate retry failed");

            // Record attempts after the immediate retry cycle
            var countAfterImmediate = Volatile.Read(ref connectionAttemptCount);

            // Advance time by less than the custom timeout - no new retry yet
            await AdvanceTime(1500);
            Volatile.Read(ref connectionAttemptCount).Should().Be(countAfterImmediate);

            // Advance past the custom timeout (2000ms + jitter margin)
            await AdvanceTime(1500);

            // Assertions
            // A new reconnection attempt was made after the custom delay
            Volatile.Read(ref connectionAttemptCount).Should().BeGreaterThan(countAfterImmediate);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC7/default-timeouts-applied-3
        [UtsFact]
        [Trait("spec", "RTC7")]
        public async Task RTC7_DefaultTimeoutsApplied()
        {
            // Setup
            // NOTE: client.options is read from the test's own ClientOptions instance: the client keeps the instance it was given
            var options = new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false };
            var client = Realtime(options);

            // Assertions
            // Default values per spec (TO3l*)
            options.RealtimeRequestTimeout.Should().Be(TimeSpan.FromMilliseconds(10000));
            options.DisconnectedRetryTimeout.Should().Be(TimeSpan.FromMilliseconds(15000));
            options.SuspendedRetryTimeout.Should().Be(TimeSpan.FromMilliseconds(30000));
            options.HttpOpenTimeout.Should().Be(TimeSpan.FromMilliseconds(4000));
            options.HttpRequestTimeout.Should().Be(TimeSpan.FromMilliseconds(10000));
            await CloseClient(client);
        }
    }
}
