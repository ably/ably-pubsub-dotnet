// Realtime Client Tests (RTC1, RTC1a, RTC1b, RTC1c, RTC1f, RTC2, RTC3, RTC4, RTC12, RTC13, RTC15, RTC16, RTC17)
// Derived from uts/realtime/unit/client/realtime_client.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

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
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RealtimeTests.Unit
{
    [Trait("uts", "unit")]
    public class RealtimeClientTests : UtsUnitTestBase
    {
        public RealtimeClientTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: realtime/unit/RTC12/constructor-string-detection-0
        [UtsFact]
        [Trait("spec", "RTC12")]
        public async Task RTC12_ConstructorStringDetection()
        {
            // The Realtime client has the same constructors as the REST client.
            // See: uts/test/realtime/unit/client/client_options.md - RSC1, RSC1a, RSC1c
            // The same test cases apply:
            // - API key string ("appId.keyId:keySecret") -> Basic auth
            // - Token string (no ":" delimiter) -> Token auth
            // - Empty string -> Error
            // NOTE: the C# string constructor is ClientOptions(string): the string is detected as a key (contains ':') or a token, and the Realtime client is built from those options

            // API key string -> Basic auth
            var keyOptions = new ClientOptions("appId.keyId:keySecret") { AutoConnect = false };
            keyOptions.Key.Should().Be("appId.keyId:keySecret");
            keyOptions.Token.Should().BeNull();
            var keyClient = Realtime(keyOptions);
            keyClient.Connection.State.Should().Be(ConnectionState.Initialized);

            // Token string (no ':' delimiter) -> Token auth
            var tokenOptions = new ClientOptions("some-token-string") { AutoConnect = false };
            tokenOptions.Token.Should().Be("some-token-string");
            tokenOptions.Key.Should().BeNull();
            var tokenClient = Realtime(tokenOptions);
            tokenClient.Connection.State.Should().Be(ConnectionState.Initialized);

            // Empty string -> Error
            Func<ClientOptions> createEmpty = () => new ClientOptions(string.Empty);
            var error = createEmpty.Should().Throw<AblyException>().Which.ErrorInfo;
            error.Code.Should().Be(40106);
            await CloseClient(keyClient);
            await CloseClient(tokenClient);
        }

        // UTS: realtime/unit/RTC12/invalid-arguments-error-1
        [UtsFact]
        [Trait("spec", "RTC12")]
        public async Task RTC12_InvalidArgumentsError()
        {
            // The Realtime client has the same error handling as the REST client for invalid credentials.
            // See: uts/rest/unit/auth/auth_scheme.md - RSC1b
            // Error code 40106 should be raised when no valid credentials are provided.
            // NOTE: the spec gives no setup; a client with no key, token or auth callback is created and creation must raise 40106
            Action create = () => Realtime(new ClientOptions { AutoConnect = false });
            var error = create.Should().Throw<AblyException>().Which.ErrorInfo;
            error.Code.Should().Be(40106);
            await Task.CompletedTask;
        }

        // UTS: realtime/unit/RTC2/connection-attribute-0
        [UtsFact]
        [Trait("spec", "RTC2")]
        public async Task RTC2_ConnectionAttribute()
        {
            // Setup
            var mockWs = new MockWebSocket();
            InstallMock(mockWs);

            // Create client with autoConnect: false to avoid immediate connection
            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Assertions
            client.Connection.Should().NotBeNull();

            // ASSERT client.connection IS Connection: compile-time guarantee (Connection is a sealed type)
            client.Connection.State.Should().Be(ConnectionState.Initialized);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC3/channels-attribute-0
        [UtsFact]
        [Trait("spec", "RTC3")]
        public async Task RTC3_ChannelsAttribute()
        {
            // Setup
            var channelName = "test-RTC3-" + Guid.NewGuid().ToString("N");

            var mockWs = new MockWebSocket();
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Assertions
            client.Channels.Should().NotBeNull();

            // ASSERT client.channels IS Channels: compile-time guarantee (declared as RealtimeChannels)
            // Should be able to get/create channels
            var channel = client.Channels.Get(channelName);

            // ASSERT channel IS RealtimeChannel: the declared type IRealtimeChannel guarantees it
            channel.Should().NotBeNull();
            channel.Name.Should().Be(channelName);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC4/auth-attribute-0
        [UtsFact]
        [Trait("spec", "RTC4")]
        public async Task RTC4_AuthAttribute()
        {
            // Setup
            var mockWs = new MockWebSocket();
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Assertions
            client.Auth.Should().NotBeNull();

            // ASSERT client.auth IS Auth: compile-time guarantee (declared as IAblyAuth)
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC13/push-attribute-0
        [UtsFact]
        [Trait("spec", "RTC13")]
        public async Task RTC13_PushAttribute()
        {
            // Setup
            var mockWs = new MockWebSocket();
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Assertions
            client.Push.Should().NotBeNull();

            // ASSERT client.push IS Push: compile-time guarantee (declared as PushRealtime)
            client.Push.Admin.Should().NotBeNull();

            // ASSERT client.push.admin IS PushAdmin: compile-time guarantee (declared as PushAdmin)
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC17/client-id-attribute-0
        [UtsFact]
        [Trait("spec", "RTC17")]
        public async Task RTC17_ClientIdAttribute()
        {
            // RTC17a - Returns auth clientId
            var mockWs = new MockWebSocket();
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", ClientId = "explicit-client-id", AutoConnect = false });

            client.ClientId.Should().Be("explicit-client-id");
            client.ClientId.Should().Be(client.Auth.ClientId);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC1a/echo-messages-option-0
        [UtsFact]
        [Trait("spec", "RTC1a")]
        public async Task RTC1a_EchoMessagesOption()
        {
            // RTC1a_1 - echoMessages defaults to true
            var mockWs1 = new MockWebSocket();
            InstallMock(mockWs1);

            var client1 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Wait for connection attempt
            var pending1 = await mockWs1.AwaitConnectionAttempt();

            // Check the connection URL query parameters
            pending1.QueryParams["echo"].Should().Be("true");
            await CloseClient(client1);

            // RTC1a_2 - echoMessages set to false
            var mockWs2 = new MockWebSocket();
            InstallMock(mockWs2);

            var client2 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", EchoMessages = false });

            // Wait for connection attempt
            var pending2 = await mockWs2.AwaitConnectionAttempt();

            // Check the connection URL query parameters
            pending2.QueryParams["echo"].Should().Be("false");
            await CloseClient(client2);
        }

        // UTS: realtime/unit/RTC1b/auto-connect-option-0
        [UtsFact]
        [Trait("spec", "RTC1b")]
        public async Task RTC1b_AutoConnectOption()
        {
            // RTC1b_1 - autoConnect defaults to true
            var mockWs1 = new MockWebSocket();
            mockWs1.OnConnect(ProtocolMessages.ConnectedMessage());
            InstallMock(mockWs1);

            var client1 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Should immediately attempt connection (state may be connecting or already connected)
            // Wait for connection
            // NOTE: AWAIT_STATE connecting OR connected and the following once(connected) are covered by one wait for CONNECTED (it completes at once if already connected, and a wait for connecting could miss the transient state)
            await UtsWait.AwaitConnectionState(client1.Connection, ConnectionState.Connected);

            mockWs1.ConnectionAttempts.Count.Should().BeGreaterThanOrEqualTo(1);
            await CloseClient(client1);

            // RTC1b_2 - autoConnect set to false
            var mockWs2 = new MockWebSocket();
            InstallMock(mockWs2);

            var client2 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            // Should NOT attempt connection
            client2.Connection.State.Should().Be(ConnectionState.Initialized);
            mockWs2.ConnectionAttempts.Count.Should().Be(0);

            // Should remain in initialized state until explicit connect: let pending async
            // work settle instead of sleeping (see process_pending_events in uts/README.md)
            await ProcessPendingEvents();
            client2.Connection.State.Should().Be(ConnectionState.Initialized);
            mockWs2.ConnectionAttempts.Count.Should().Be(0);
            await CloseClient(client2);

            // RTC1b_3 - Explicit connect after autoConnect false
            var mockWs3 = new MockWebSocket();
            mockWs3.OnConnect(ProtocolMessages.ConnectedMessage());
            InstallMock(mockWs3);

            var client3 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            client3.Connection.State.Should().Be(ConnectionState.Initialized);

            // Explicit connect
            // NOTE: states are recorded before connect(): CONNECTING may be left at once, so AWAIT_STATE connecting could miss it; verified in order below
            var states3 = UtsRecord.ConnectionStates(client3.Connection);
            client3.Connection.Connect();

            // Wait for connection
            await UtsWait.AwaitConnectionState(client3.Connection, ConnectionState.Connected);
            UtsAssert.ContainsInOrder(states3.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Connected });

            mockWs3.EventsOfType(MockEventType.ConnectionAttempt).Count.Should().Be(1);
            client3.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client3);
        }

        // UTS: realtime/unit/RTC1c/recover-option-0
        [UtsFact]
        [Trait("spec", "RTC1c")]
        public async Task RTC1c_RecoverOption()
        {
            // RTC1c_1 - recover string sent in connection request
            var mockWs1 = new MockWebSocket();
            InstallMock(mockWs1);

            var recoveryKey1 = new JObject
            {
                ["connectionKey"] = "previous-connection-key",
                ["msgSerial"] = 5,
                ["channelSerials"] = new JObject { ["channel1"] = "serial1" },
            }.ToString(Formatting.None);

            var client1 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", Recover = recoveryKey1 });

            // Wait for connection attempt
            var pending1 = await mockWs1.AwaitConnectionAttempt();

            // Check the connection URL query parameters
            pending1.QueryParams["recover"].Should().Be("previous-connection-key");
            await CloseClient(client1);

            // RTC1c_2 - recover option cleared after connection attempt (RTN16k)
            var mockWs2 = new MockWebSocket();
            mockWs2.OnConnect(ProtocolMessages.ConnectedMessage());
            InstallMock(mockWs2);

            var recoveryKey2 = new JObject
            {
                ["connectionKey"] = "previous-connection-key",
                ["msgSerial"] = 5,
                ["channelSerials"] = new JObject(),
            }.ToString(Formatting.None);

            var client2 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", Recover = recoveryKey2 });

            // Wait for connection
            // NOTE: states are recorded from before the first connect completes and polled, because DISCONNECTED may be left again at once (an immediate reconnect)
            var states2 = UtsRecord.ConnectionStates(client2.Connection);
            await UtsWait.AwaitConnectionState(client2.Connection, ConnectionState.Connected);

            // Simulate disconnect and reconnect
            mockWs2.SimulateDisconnect();
            await UtsWait.PollUntil(
                () => states2.Snapshot().SkipWhile(s => s != ConnectionState.Disconnected).Any(s => s == ConnectionState.Connected),
                description: "connection disconnected and connected again");

            // Second connection should NOT include recover parameter
            // (RTN16k - recover is used only for initial connection)
            var secondConnectUrl = mockWs2.ConnectionAttempts[1].Url;
            secondConnectUrl.QueryParams.Should().NotContainKey("recover");
            await CloseClient(client2);

            // RTC1c_3 - Invalid recovery key handled gracefully
            var mockWs3 = new MockWebSocket();
            InstallMock(mockWs3);

            var client3 = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", Recover = "invalid-not-a-valid-recovery-key" });

            // Wait for connection attempt (recovery key decoding failure is logged, not fatal)
            var pending3 = await mockWs3.AwaitConnectionAttempt();

            // Connection should proceed without recover parameter
            pending3.QueryParams.Should().NotContainKey("recover");
            await CloseClient(client3);
        }

        // UTS: realtime/unit/RTC1f/transport-params-option-0
        [UtsFact]
        [Trait("spec", "RTC1f")]
        public async Task RTC1f_TransportParamsOption()
        {
            // RTC1f_1 - transportParams included in connection URL
            var mockWs1 = new MockWebSocket();
            InstallMock(mockWs1);

            var client1 = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                TransportParams = new Dictionary<string, object>
                {
                    ["customParam"] = "customValue",
                    ["anotherParam"] = "123",
                },
            });

            // Wait for connection attempt
            var pending1 = await mockWs1.AwaitConnectionAttempt();

            // Check the connection URL query parameters
            pending1.QueryParams["customParam"].Should().Be("customValue");
            pending1.QueryParams["anotherParam"].Should().Be("123");
            await CloseClient(client1);

            // RTC1f_2 - transportParams with different value types (Stringifiable)
            var mockWs2 = new MockWebSocket();
            InstallMock(mockWs2);

            var client2 = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                TransportParams = new Dictionary<string, object>
                {
                    ["stringParam"] = "hello",
                    ["numberParam"] = 42,
                    ["boolTrueParam"] = true,
                    ["boolFalseParam"] = false,
                },
            });

            // Wait for connection attempt
            var pending2 = await mockWs2.AwaitConnectionAttempt();

            // Check stringification of values (RTC1f)
            pending2.QueryParams["stringParam"].Should().Be("hello");
            pending2.QueryParams["numberParam"].Should().Be("42");
            pending2.QueryParams["boolTrueParam"].Should().Be("true");
            pending2.QueryParams["boolFalseParam"].Should().Be("false");
            await CloseClient(client2);

            // RTC1f1 - transportParams override library defaults
            var mockWs3 = new MockWebSocket();
            InstallMock(mockWs3);

            var client3 = Realtime(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                TransportParams = new Dictionary<string, object>
                {
                    ["v"] = "3", // Override protocol version
                    ["heartbeats"] = "false", // Override heartbeats
                },
            });

            // Wait for connection attempt
            var pending3 = await mockWs3.AwaitConnectionAttempt();

            // User-specified values should override defaults
            pending3.QueryParams["v"].Should().Be("3");
            pending3.QueryParams["heartbeats"].Should().Be("false");
            await CloseClient(client3);
        }

        // UTS: realtime/unit/RTC15/connect-method-0
        [UtsFact]
        [Trait("spec", "RTC15")]
        public async Task RTC15_ConnectMethod()
        {
            // RTC15a - connect() calls Connection#connect
            var mockWs = new MockWebSocket();
            mockWs.OnConnect(ProtocolMessages.ConnectedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", AutoConnect = false });

            client.Connection.State.Should().Be(ConnectionState.Initialized);

            // Call connect on client (should proxy to connection)
            // NOTE: states are recorded before connect(): CONNECTING may be left at once, so AWAIT_STATE connecting could miss it; verified in order below
            var states = UtsRecord.ConnectionStates(client.Connection);
            client.Connect();

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            UtsAssert.ContainsInOrder(states.Snapshot(), new[] { ConnectionState.Connecting, ConnectionState.Connected });
            client.Connection.State.Should().Be(ConnectionState.Connected);
            await CloseClient(client);
        }

        // UTS: realtime/unit/RTC16/close-method-0
        [UtsFact]
        [Trait("spec", "RTC16")]
        public async Task RTC16_CloseMethod()
        {
            // RTC16a - close() calls Connection#close
            var mockWs = new MockWebSocket();
            mockWs.OnConnect(ProtocolMessages.ConnectedMessage());
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            // Wait for connection
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);
            client.Connection.State.Should().Be(ConnectionState.Connected);

            // Configure mock to respond to CLOSE with CLOSED
            // Disclosures: the mock answers CLOSE with CLOSED (infra-driving)
            mockWs.OnMessage(ProtocolMessages.Close, ProtocolMessages.ClosedMessage());

            // Call close on client (should proxy to connection)
            // NOTE: states are recorded before close(): CLOSING may be left at once, so AWAIT_STATE closing OR closed is verified on the record
            var states = UtsRecord.ConnectionStates(client.Connection);
            client.Close();

            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Closed);
            states.Snapshot().Should().Contain(ConnectionState.Closed);
            client.Connection.State.Should().Be(ConnectionState.Closed);
            await CloseClient(client);
        }

        // NOTE: no Test ID in the spec: TLS Setting (RSC18) in Realtime
        [UtsFact]
        [Trait("spec", "RSC18")]
        public async Task NoId_TlsSettingRsc18InRealtime()
        {
            var mockWs = new MockWebSocket();
            mockWs.OnConnect(ProtocolMessages.ConnectedMessage());
            InstallMock(mockWs);

            foreach (var tlsSetting in new[] { true, false })
            {
                mockWs.Reset();

                // Note: Basic auth requires TLS, so use token auth for tls: false
                PubSubRealtimeClient client;
                if (tlsSetting)
                {
                    client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", Tls = true });
                }
                else
                {
                    client = Realtime(new ClientOptions { Token = "test-token", Tls = false });
                }

                await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

                var connectUrl = mockWs.LastConnectUrl;
                if (tlsSetting)
                {
                    connectUrl.Scheme.Should().Be("wss");
                }
                else
                {
                    connectUrl.Scheme.Should().Be("ws");
                }

                // client.close(): un-awaited in the spec, done by teardown
            }
        }

        // NOTE: no Test ID in the spec: useBinaryProtocol in Realtime
        // NOTE: the use_binary = true iteration (format == "msgpack") is not applicable: msgpack is compiled out of this SDK build (UseBinaryProtocol is a no-op); the JSON iteration is kept
        [UtsFact]
        [Trait("spec", "TO3f")]
        public async Task NoId_UseBinaryProtocolInRealtime()
        {
            var mockWs = new MockWebSocket();
            InstallMock(mockWs);

            mockWs.Reset();

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret", UseBinaryProtocol = false });

            var pending = await mockWs.AwaitConnectionAttempt();

            pending.QueryParams["format"].Should().Be("json");

            // client.close(): un-awaited in the spec, done by teardown
            await Task.CompletedTask;
        }

        // NOTE: no Test ID in the spec: Standard Query Parameters
        [UtsFact]
        [Trait("spec", "RTC1")]
        public async Task NoId_StandardQueryParameters()
        {
            var mockWs = new MockWebSocket();
            InstallMock(mockWs);

            var client = Realtime(new ClientOptions { Key = "appId.keyId:keySecret" });

            var pending = await mockWs.AwaitConnectionAttempt();

            // Required parameters
            pending.QueryParams.Should().ContainKey("v"); // Protocol version
            pending.QueryParams.Should().ContainKey("format"); // msgpack or json
            pending.QueryParams.Should().ContainKey("heartbeats"); // RTN23b
            pending.QueryParams.Should().ContainKey("echo");

            // Auth parameters (one of these depending on auth method)
            (pending.QueryParams.ContainsKey("key") || pending.QueryParams.ContainsKey("accessToken")).Should().BeTrue();
            await CloseClient(client);
        }
    }
}
