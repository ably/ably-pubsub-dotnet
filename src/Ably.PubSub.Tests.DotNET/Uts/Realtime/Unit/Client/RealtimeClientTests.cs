using System;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Http;
using Ably.PubSub.Push;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Realtime.Unit.Client
{
    /// <summary>
    /// Derived from uts/realtime/unit/client/realtime_client.md in ably/specification.
    ///
    /// Spec points: RTC1a, RTC1b, RTC1c, RTC1f, RTC1f1, RTC2, RTC3, RTC4, RTC12, RTC13, RTC15,
    /// RTC16, RTC17, RTN16k
    ///
    /// <para>
    /// The accessors the spec names map onto this SDK as follows: <c>client.connection</c> and
    /// <c>client.channels</c> keep their names, <c>client.auth</c> is an <c>IAblyAuth</c>,
    /// <c>client.push</c> is a <c>PushHttp</c> whose <c>Admin</c> is the <c>PushAdmin</c>, and
    /// <c>client.clientId</c> is <c>ClientId</c>. Idiomatic naming, not deviations.
    /// </para>
    ///
    /// <para>
    /// RTC12's two tests are pointers rather than tests - both sections say "the Realtime client has
    /// the same constructors / error handling as the REST client" and refer to the REST files. They
    /// are translated as the realtime half of what those files already cover, so that the behaviour
    /// is pinned for this client too.
    /// </para>
    /// </summary>
    [Collection(UtsTestBase.RealtimeUnitCollection)]
    public class RealtimeClientTests : UtsTestBase
    {
        public RealtimeClientTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC12/constructor-string-detection-0
        [Fact]
        public void RTC12_ConstructorStringDetection()
        {
            // A key, because it carries a colon.
            var withKey = Track(new PubSubRealtimeClient(new ClientOptions(UtsClients.ValidKey)
            {
                AutoConnect = false,
                TransportFactory = new MockWebSocket().TransportFactory,
            }));

            withKey.Options.Key.Should().Be(UtsClients.ValidKey);
            withKey.Options.Token.Should().BeNullOrEmpty();

            // No colon, so a token.
            var withToken = Track(new PubSubRealtimeClient(new ClientOptions("a-token-string")
            {
                AutoConnect = false,
                TransportFactory = new MockWebSocket().TransportFactory,
            }));

            withToken.Options.Token.Should().Be("a-token-string");
            withToken.Options.Key.Should().BeNullOrEmpty();
        }

        // UTS: realtime/unit/RTC12/invalid-arguments-error-1
        [Fact]
        public void RTC12_InvalidArgumentsError()
        {
            Action act = () => new PubSubRealtimeClient(new ClientOptions
            {
                AutoConnect = false,
                TransportFactory = new MockWebSocket().TransportFactory,
            });

            var error = act.Should().Throw<AblyException>().Which.ErrorInfo;
            error.Code.Should().Be(40106, "RSC1b - no credentials of any kind were supplied");
        }

        // UTS: realtime/unit/RTC2/connection-attribute-0
        [Fact]
        public void RTC2_ConnectionAttribute()
        {
            var client = RealtimeClient(new MockWebSocket());

            client.Connection.Should().NotBeNull();
            client.Connection.Should().BeOfType<Ably.PubSub.Realtime.Connection>();
            client.Connection.State.Should().Be(ConnectionState.Initialized);
        }

        // UTS: realtime/unit/RTC3/channels-attribute-0
        [Fact]
        public void RTC3_ChannelsAttribute()
        {
            const string ChannelName = "test-RTC3";

            var client = RealtimeClient(new MockWebSocket());

            client.Channels.Should().NotBeNull();
            client.Channels.Should().BeAssignableTo<IChannels<IRealtimeChannel>>();

            var channel = client.Channels.Get(ChannelName);
            channel.Should().BeAssignableTo<IRealtimeChannel>();
            channel.Name.Should().Be(ChannelName);
        }

        // UTS: realtime/unit/RTC4/auth-attribute-0
        [Fact]
        public void RTC4_AuthAttribute()
        {
            var client = RealtimeClient(new MockWebSocket());

            client.Auth.Should().NotBeNull();
            client.Auth.Should().BeAssignableTo<IAblyAuth>();
        }

        // UTS: realtime/unit/RTC13/push-attribute-0
        [Fact]
        public void RTC13_PushAttribute()
        {
            var client = RealtimeClient(new MockWebSocket());

            client.Push.Should().NotBeNull();
            client.Push.Should().BeOfType<PushRealtime>();
            client.Push.Admin.Should().BeOfType<PushAdmin>();
        }

        // UTS: realtime/unit/RTC17/client-id-attribute-0
        [Fact]
        public void RTC17_ClientIdAttribute()
        {
            var client = RealtimeClient(
                new MockWebSocket(),
                configure: options => options.ClientId = "explicit-client-id");

            client.ClientId.Should().Be("explicit-client-id");
            client.ClientId.Should().Be(client.Auth.ClientId);
        }

        // UTS: realtime/unit/RTC1a/echo-messages-option-0
        [Fact]
        public async Task RTC1a_EchoMessagesOption()
        {
            await AssertConnectQueryParam(
                options => options.EchoMessages = true,
                "echo",
                "true");

            await AssertConnectQueryParam(
                options => options.EchoMessages = false,
                "echo",
                "false");
        }

        // UTS: realtime/unit/RTC1b/auto-connect-option-0
        [Fact]
        public async Task RTC1b_AutoConnectOption()
        {
            // autoConnect: true connects without being asked.
            var autoMock = ConnectingMock();
            var autoClient = RealtimeClient(autoMock, configure: options => options.AutoConnect = true);

            await UtsClients.AwaitConnectionState(
                autoClient.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            autoMock.ConnectionAttempts.Should().NotBeEmpty();

            // autoConnect: false does not, and stays INITIALIZED until asked.
            var manualMock = ConnectingMock();
            var manualClient = RealtimeClient(manualMock);

            manualClient.Connection.State.Should().Be(ConnectionState.Initialized);
            manualMock.ConnectionAttempts.Should().BeEmpty();

            // The spec's "let pending async work settle instead of sleeping".
            await Task.Yield();

            manualClient.Connection.State.Should().Be(ConnectionState.Initialized);
            manualMock.ConnectionAttempts.Should().BeEmpty();

            manualClient.Connect();

            await UtsClients.AwaitConnectionState(
                manualClient.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            manualMock.Events.Count(e => e.Type == MockEventType.ConnectionAttempt).Should().Be(1);
        }

        // UTS: realtime/unit/RTC1c/recover-option-0
        [Fact]
        public async Task RTC1c_RecoverOption()
        {
            var recoveryKey = new JObject
            {
                ["connectionKey"] = "previous-connection-key",
                ["msgSerial"] = 5,
                ["channelSerials"] = new JObject { ["channel1"] = "serial1" },
            }.ToString(Newtonsoft.Json.Formatting.None);

            await AssertConnectQueryParam(
                options => options.Recover = recoveryKey,
                "recover",
                "previous-connection-key");

            // RTN16k - the recover option applies to the first attempt only.
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.Recover = new JObject
                {
                    ["connectionKey"] = "previous-connection-key",
                    ["msgSerial"] = 5,
                    ["channelSerials"] = new JObject(),
                }.ToString(Newtonsoft.Json.Formatting.None);
            });

            client.Connect();
            await UtsClients.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var reconnected = UtsClients.NextConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            mockWs.ActiveConnection.SimulateDisconnect();

            await reconnected;

            mockWs.ConnectionAttempts.Should().HaveCountGreaterOrEqualTo(2);
            mockWs.ConnectionAttempts[1].QueryParams.Should().NotContainKey(
                "recover",
                "RTN16k - recover is for the initial connection only");
        }

        // UTS: realtime/unit/RTC1f/transport-params-option-0
        [Fact]
        public async Task RTC1f_TransportParamsOption()
        {
            // RTC1f_1 - the params reach the connect URL.
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options =>
            {
                options.TransportParams["customParam"] = "customValue";
                options.TransportParams["anotherParam"] = "123";
            });

            client.Connect();
            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 1,
                "the connection attempt");

            var queryParams = mockWs.ConnectionAttempts[0].QueryParams;
            queryParams["customParam"].Should().Be("customValue");
            queryParams["anotherParam"].Should().Be("123");

            // RTC1f_2 - values of other types are stringified.
            var typedMock = ConnectingMock();
            var typedClient = RealtimeClient(typedMock, configure: options =>
            {
                options.TransportParams["stringParam"] = "hello";
                options.TransportParams["numberParam"] = 42;
                options.TransportParams["boolTrueParam"] = true;
                options.TransportParams["boolFalseParam"] = false;
            });

            typedClient.Connect();
            await UtsClients.PollUntil(
                () => typedMock.ConnectionAttempts.Count >= 1,
                "the connection attempt");

            var typedParams = typedMock.ConnectionAttempts[0].QueryParams;
            typedParams["stringParam"].Should().Be("hello");
            typedParams["numberParam"].Should().Be("42");
            typedParams["boolTrueParam"].Should().Be("true");
            typedParams["boolFalseParam"].Should().Be("false");

            // RTC1f1 - a user value overrides the library's own default for the same name.
            var overrideMock = ConnectingMock();
            var overrideClient = RealtimeClient(overrideMock, configure: options =>
            {
                options.TransportParams["v"] = "3";
                options.TransportParams["heartbeats"] = "false";
            });

            overrideClient.Connect();
            await UtsClients.PollUntil(
                () => overrideMock.ConnectionAttempts.Count >= 1,
                "the connection attempt");

            var overrideParams = overrideMock.ConnectionAttempts[0].QueryParams;
            overrideParams["v"].Should().Be("3");
            overrideParams["heartbeats"].Should().Be("false");
        }

        // UTS: realtime/unit/RTC15/connect-method-0
        [Fact]
        public async Task RTC15_ConnectMethod()
        {
            var client = RealtimeClient(ConnectingMock());

            client.Connection.State.Should().Be(ConnectionState.Initialized);

            client.Connect();

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        // UTS: realtime/unit/RTC16/close-method-0
        [Fact]
        public async Task RTC16_CloseMethod()
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: options => options.AutoConnect = true);

            await UtsClients.AwaitConnectionState(
                client.Connection,
                ConnectionState.Connected,
                TimeSpan.FromSeconds(5));

            var closed = UtsClients.NextConnectionState(client.Connection, ConnectionState.Closed);

            client.Close();

            await mockWs.AwaitProtocolMessages(ProtocolMessage.MessageAction.Close);
            mockWs.SendToClient(ProtocolMessages.ClosedMessage());

            await closed;

            client.Connection.State.Should().Be(ConnectionState.Closed);
        }

        private async Task AssertConnectQueryParam(
            Action<ClientOptions> configure,
            string name,
            string expected)
        {
            var mockWs = ConnectingMock();
            var client = RealtimeClient(mockWs, configure: configure);

            client.Connect();

            await UtsClients.PollUntil(
                () => mockWs.ConnectionAttempts.Count >= 1,
                $"the connection attempt carrying {name}");

            mockWs.ConnectionAttempts[0].QueryParams[name].Should().Be(expected);
        }

        private static MockWebSocket ConnectingMock()
            => new MockWebSocket(onConnectionAttempt: conn =>
            {
                conn.RespondWithSuccess();
                conn.SendToClient(ProtocolMessages.ConnectedMessageNoIdle());
            });
    }
}
