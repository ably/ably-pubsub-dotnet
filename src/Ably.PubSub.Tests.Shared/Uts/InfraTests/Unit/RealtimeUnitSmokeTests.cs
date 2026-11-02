using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.InfraTests.Unit
{
    /// <summary>
    /// The realtime unit tier end to end: a factory-built client wired to <see cref="MockWebSocket"/> and the
    /// fake clock through the real SDK hooks (TransportFactory, TimerScheduler, NowFunc).
    /// </summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "unit")]
    public class RealtimeUnitSmokeTests : UtsUnitTestBase
    {
        public RealtimeUnitSmokeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task FactoryBuiltClientReachesConnectedAndTheConnectedTemplateReachesIt()
        {
            var mock = new MockWebSocket(attempt => attempt.RespondWithSuccess(ProtocolMessages.ConnectedMessage()));
            InstallMock(mock);

            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Connection.Id.Should().Be(ProtocolMessages.TestConnectionId);
            client.Connection.Key.Should().Be(ProtocolMessages.TestConnectionKey);
            client.Connection.ConnectionStateTtl.Should().Be(TimeSpan.FromMilliseconds(120000));
            mock.ConnectionAttempts.Should().HaveCount(1);
        }

        [Fact]
        public async Task ConnectionAttemptExposesHostPortTlsAndQueryParams()
        {
            var mock = new MockWebSocket();
            InstallMock(mock);
            Realtime(new ClientOptions { Key = ValidKey, EchoMessages = false });

            var attempt = await mock.AwaitConnectionAttempt();

            attempt.Url.Scheme.Should().Be("wss");
            attempt.Url.Host.Should().Be("main.realtime.ably.net");
            attempt.Url.Port.Should().Be(443);
            attempt.Url.Tls.Should().BeTrue();
            attempt.Protocol.Should().Be("application/json");
            attempt.QueryParams["key"].Should().Be(ValidKey);
            attempt.QueryParams["v"].Should().Be(Defaults.ProtocolVersion);
            attempt.QueryParams["format"].Should().Be("json");
            attempt.QueryParams["echo"].Should().Be("false");
            attempt.QueryParams["heartbeats"].Should().Be("true");
            mock.LastConnectUrl.Uri.Should().Be(attempt.Url.Uri);
            attempt.RespondWithConnected();
        }

        [Fact]
        public async Task ConnectionAttemptReflectsANonTlsEndpointAndPort()
        {
            var mock = new MockWebSocket();
            InstallMock(mock);
            Realtime(new ClientOptions { Key = ValidKey, Endpoint = "localhost", Tls = false, Port = 8080 });

            var attempt = await mock.AwaitConnectionAttempt();

            attempt.Url.Scheme.Should().Be("ws");
            attempt.Url.Host.Should().Be("localhost");
            attempt.Url.Port.Should().Be(8080);
            attempt.Url.Tls.Should().BeFalse();
        }

        [Fact]
        public async Task ClientAttachWithANonAsciiChannelNameIsCapturedAndDecoded()
        {
            const string name = "café-频道-\U0001F600";
            var rawFrames = new CaptureList<string>();
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected(), onTextDataFrame: rawFrames.Add);
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            client.Channels.Get(name).Attach();
            var attach = await mock.AwaitClientMessage(ProtocolMessages.Attach);

            attach.Channel.Should().Be(name);
            ((int)attach.Action).Should().Be(ProtocolMessages.Attach);
            var raw = rawFrames.Select(JObject.Parse).Single(frame => (int)frame["action"] == ProtocolMessages.Attach);
            ((string)raw["channel"]).Should().Be(name, "the raw text frame carries the name intact too");
        }

        [Fact]
        public async Task ServerAttachedChangesTheChannelState()
        {
            const string name = "smoke-attached";
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var channel = client.Channels.Get(name);
            channel.Attach();
            await mock.AwaitClientMessage(ProtocolMessages.Attach);
            channel.State.Should().Be(ChannelState.Attaching);

            mock.SendToClient(ProtocolMessages.AttachedMessage(name));

            await UtsWait.AwaitChannelState(channel, ChannelState.Attached);
        }

        [Fact]
        public async Task SimulateDisconnectYieldsDisconnectedObservedByRecordAndVerify()
        {
            var mock = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            var changes = UtsRecord.ConnectionStateChanges(client.Connection);

            mock.SimulateDisconnect();
            await UtsWait.PollUntil(() => changes.Any(c => c.Current == ConnectionState.Connected), description: "the reconnection");

            UtsAssert.ContainsInOrder(
                changes.Select(c => c.Current),
                new[] { ConnectionState.Disconnected, ConnectionState.Connecting, ConnectionState.Connected });
            changes.First(c => c.Current == ConnectionState.Disconnected).Reason.Should().NotBeNull();
            mock.EventsOfType(MockEventType.ServerDisconnect).Should().HaveCount(1);
            mock.ConnectionAttempts.Should().HaveCount(2);
        }

        [UtsFact]
        public async Task FactoryBuiltClientIsOffTheProcessWideOsNetworkHookButANativeClientStaysOnIt()
        {
            // F5: Connection.NotifyOperatingSystemNetworkState (static, public) reaches every connection in
            // Connection.OsEventSubscribers; a UTS client must not be one of them. Not exercised by calling the
            // static, which would reach other tests' clients too.
            var uts = Realtime(new ClientOptions { Key = ValidKey, AutoConnect = false });
            var native = Track(new PubSubRealtimeClient(
                new ClientOptions
                {
                    Key = ValidKey,
                    AutoConnect = false,
                    AutomaticNetworkStateMonitoring = false,
                    TransportFactory = new MockWebSocket(),
                    HttpClient = MockHttpClient.Strict().AsHttpClient(),
                },
                new Ably.PubSub.Tests.Push.FakeMobileDevice()));

            IsOsNetworkSubscriber(uts.Connection).Should().BeFalse("the unit factory detached it");
            IsOsNetworkSubscriber(native.Connection).Should().BeTrue("the SDK's own behaviour is unchanged");

            native.Dispose();
            await UtsWait.PollUntil(() => !IsOsNetworkSubscriber(native.Connection), description: "dispose to unsubscribe the native client");
        }

        [Fact]
        public async Task TimerDrivenRetryWaitsForAdvanceTime()
        {
            EnableFakeTimers();
            var attempts = 0;
            var mock = new MockWebSocket(attempt =>
            {
                // The SDK retries a failed first attempt at once (one instant retry per domain, RTN17j);
                // the retry after that waits for the DISCONNECTED timer (RTN14d, RTB1).
                if (++attempts <= 2)
                {
                    attempt.RespondWithRefused();
                }
                else
                {
                    attempt.RespondWithConnected();
                }
            });
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey, FallbackHosts = new string[0] });
            var changes = UtsRecord.ConnectionStateChanges(client.Connection);
            await UtsWait.PollUntil(() => attempts == 2 && client.Connection.State == ConnectionState.Disconnected);
            await ProcessPendingEvents();
            var retryIn = changes.Last(c => c.Current == ConnectionState.Disconnected).RetryIn;
            retryIn.Should().BeGreaterThan(TimeSpan.FromSeconds(1), "the DISCONNECTED change reports the RTB1 delay it waits");

            await AdvanceTime(retryIn.Value - TimeSpan.FromMilliseconds(100));
            await ProcessPendingEvents();
            mock.ConnectionAttempts.Should().HaveCount(2, "the DISCONNECTED retry timer hasn't expired on the fake clock");
            client.Connection.State.Should().Be(ConnectionState.Disconnected);

            await AdvanceTime(200);
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            mock.ConnectionAttempts.Should().HaveCount(3, "the retry ran once the fake clock passed the timer");
            (mock.ConnectionAttempts[2].Timestamp - mock.ConnectionAttempts[1].Timestamp).Should().Be(retryIn.Value, "the retry fired at its due time in fake time");
        }

        [Fact]
        public async Task AwaitStyleDrivesRefusedAttemptsThroughToSuspended()
        {
            EnableFakeTimers();
            var mock = new MockWebSocket();
            InstallMock(mock);
            var client = Realtime(new ClientOptions { Key = ValidKey, FallbackHosts = new string[0] });
            var changes = UtsRecord.ConnectionStateChanges(client.Connection);

            var refused = 0;
            while (client.Connection.State != ConnectionState.Suspended && refused < 40)
            {
                var attempt = await mock.AwaitConnectionAttempt();
                attempt.RespondWithRefused();
                refused++;
                await ProcessPendingEvents();

                // An instant retry leaves the client CONNECTING with the next attempt parked; otherwise wait
                // out the DISCONNECTED timer on the fake clock.
                if (client.Connection.State == ConnectionState.Disconnected)
                {
                    await AdvanceTime(changes.Last(c => c.Current == ConnectionState.Disconnected).RetryIn.Value);
                }
            }

            client.Connection.State.Should().Be(ConnectionState.Suspended);
            UtsAssert.ContainsInOrder(
                changes.Select(c => c.Current),
                new[]
                {
                    ConnectionState.Connecting,
                    ConnectionState.Disconnected,
                    ConnectionState.Connecting,
                    ConnectionState.Disconnected,
                    ConnectionState.Suspended,
                });
            refused.Should().BeGreaterThan(2);
            mock.EventsOfType(MockEventType.ConnectionFailure).Should().HaveCount(refused);
            (Clock.Now - mock.ConnectionAttempts.First().Timestamp).Should().BeGreaterOrEqualTo(
                TimeSpan.FromMilliseconds(120000),
                "SUSPENDED comes once connectionStateTtl has passed on the fake clock");
        }

        private static bool IsOsNetworkSubscriber(Connection connection)
        {
            const BindingFlags flags = BindingFlags.NonPublic | BindingFlags.Static | BindingFlags.Instance;
            var subscribers = (IDictionary)typeof(Connection).GetField("OsEventSubscribers", flags).GetValue(null);
            var id = typeof(Connection).GetField("_objectId", flags).GetValue(connection);
            return subscribers.Contains(id);
        }
    }
}
