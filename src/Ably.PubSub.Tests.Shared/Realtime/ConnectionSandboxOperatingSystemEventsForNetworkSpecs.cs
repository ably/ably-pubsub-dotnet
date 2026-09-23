using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Realtime.Workflow;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Collection("SandBox Connection")]
    [Trait("type", "integration")]
    public class ConnectionSandboxOperatingSystemEventsForNetworkSpecs : SandboxSpecs
    {
        [Theory]
#if MSGPACK
        [InlineData(Protocol.MsgPack, ConnectionState.Connected)]
        [InlineData(Protocol.MsgPack, ConnectionState.Connecting)]
#endif
        [InlineData(Protocol.Json, ConnectionState.Connecting)]
        [InlineData(Protocol.Json, ConnectionState.Connected)]
        [Trait("spec", "RTN20a")]
        public async Task
            WhenOperatingSystemNetworkIsNotAvailable_ShouldTransitionToDisconnectedAndRetry(
                Protocol protocol,
                ConnectionState initialState)
        {
            var client = await GetRealtimeClient(protocol, (options, _) => options.AutoConnect = false);

            client.Connect();

            await client.WaitForState(initialState);

            List<ConnectionState> states = new List<ConnectionState>();
            client.Connection.On(stateChange => states.Add(stateChange.Current));

            Connection.NotifyOperatingSystemNetworkState(NetworkState.Offline, Logger);

            await new ConditionalAwaiter(() => states.Count >= 2);

            states.Should().Contain(ConnectionState.Disconnected);
            states.Should().Contain(ConnectionState.Connecting);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN20b")]
        public async Task
            WhenOperatingSystemNetworkBecomesAvailableAndStateIsDisconnected_ShouldTransitionTryToConnectImmediately(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, _) => options.AutoConnect = false);

            client.Connect();

            await client.WaitForState();

            client.Workflow.QueueCommand(SetDisconnectedStateCommand.Create(null, retryInstantly: false));
            await client.WaitForState(ConnectionState.Disconnected);

            List<ConnectionState> states = new List<ConnectionState>();
            client.Connection.On(stateChange => states.Add(stateChange.Current));

            Connection.NotifyOperatingSystemNetworkState(NetworkState.Online, Logger);

            await new ConditionalAwaiter(() => states.Count >= 2);

            states.Should().Contain(ConnectionState.Connecting);
            states.Should().Contain(ConnectionState.Connected);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN20b")]
        public async Task
            WhenOperatingSystemNetworkBecomesAvailableAndStateIsSuspended_ShouldTransitionTryToConnectImmediately(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, _) => options.AutoConnect = false);

            client.Connection.On(stateChange => Output.WriteLine("State Changed: " + stateChange.Current + " From: " + stateChange.Previous));
            client.Connect();

            await WaitToBecomeConnected(client);

            client.Workflow.QueueCommand(SetSuspendedStateCommand.Create(ErrorInfo.ReasonSuspended));

            await client.WaitForState(ConnectionState.Suspended);

            Connection.NotifyOperatingSystemNetworkState(NetworkState.Online, Logger);

            await WaitForState(client, ConnectionState.Connecting);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN20c")]
        public async Task
            WhenOperatingSystemNetworkBecomesAvailableAndStateIsConnecting_ShouldTransitionToConnectingAndRenewsTransport(Protocol protocol)
        {
            var transportFactory = new TestTransportFactory(transport => { transport.KeepInConnectingState = true; });

            var client = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.AutoConnect = false;
                options.TransportFactory = transportFactory;
            });

            client.Connection.On(stateChange => Output.WriteLine("State Changed: " + stateChange.Current + " From: " + stateChange.Previous));
            client.Connect();

            await WaitForState(client, ConnectionState.Connecting);
            await client.ProcessCommands(); // waits for workflow command to finish so transport can be created for connecting state

            var transportId = client.ConnectionManager.Transport.Id;

            Connection.NotifyOperatingSystemNetworkState(NetworkState.Online, Logger);
            await client.ProcessCommands();
            var newTransportId = client.ConnectionManager.Transport.Id;

            newTransportId.Should().NotBe(transportId);

            await client.WaitForState(ConnectionState.Connecting);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN22")]
        public async Task WhenAuthMessageReceived_ShouldAttemptTokenRenewal(Protocol protocol)
        {
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.UseTokenAuth = true;
            });

            await client.WaitForState(ConnectionState.Connected);

            var initialToken = client.RestClient.AblyAuth.CurrentToken;
            var initialClientId = client.ClientId;

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Auth));

            await client.ProcessCommands();

            client.RestClient.AblyAuth.CurrentToken.Should().NotBe(initialToken);
            client.ClientId.Should().Be(initialClientId);
            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN22a")]
        public async Task WhenFakeDisconnectedMessageContainsTokenError_ForcesClientToReauthenticate(Protocol protocol)
        {
            var reconnectAwaiter = new TaskCompletionAwaiter();
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.UseTokenAuth = true;
            });

            await client.WaitForState(ConnectionState.Connected);

            var initialToken = client.RestClient.AblyAuth.CurrentToken;

            client.Connection.Once(ConnectionEvent.Disconnected, state2 =>
            {
                client.Connection.Once(ConnectionEvent.Connected, state3 =>
                {
                    reconnectAwaiter.SetCompleted();
                });
            });

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected) { Error = new ErrorInfo("testing RTN22a", ErrorCodes.TokenError) });
            var didReconnect = await reconnectAwaiter.Task;
            didReconnect.Should().BeTrue();
            client.RestClient.AblyAuth.CurrentToken.Should().NotBe(initialToken);
            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN22a")]
        public async Task WhenDisconnectedMessageContainsTokenError_ForcesClientToReauthenticate(Protocol protocol)
        {
            var authClient = await GetRestClient(protocol);

            var reconnectAwaiter = new TaskCompletionAwaiter(60000);
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.AuthCallback = tokenParams =>
                {
                    var results = authClient.AblyAuth.RequestToken(new TokenParams { ClientId = "RTN22a", Ttl = TimeSpan.FromSeconds(35) });
                    return Task.FromResult<object>(results);
                };
                options.ClientId = "RTN22a";
            });

            await client.WaitForState(ConnectionState.Connected);

            var initialToken = client.RestClient.AblyAuth.CurrentToken;

            client.Connection.Once(ConnectionEvent.Disconnected, state2 =>
            {
                client.Connection.Once(ConnectionEvent.Connected, state3 =>
                {
                    reconnectAwaiter.SetCompleted();
                });
            });

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Disconnected) { Error = new ErrorInfo("testing RTN22a", ErrorCodes.TokenError) });
            var didReconnect = await reconnectAwaiter.Task;
            didReconnect.Should().BeTrue();
            client.RestClient.AblyAuth.CurrentToken.Should().NotBe(initialToken);
            client.Close();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTN24")]
        [Trait("spec", "RTN21")]
        [Trait("spec", "RTN4h")]
        [Trait("spec", "RTC8a1")]
        public async Task WhenConnectedMessageReceived_ShouldEmitUpdate(Protocol protocol)
        {
            var updateAwaiter = new TaskCompletionAwaiter(5000);
            var client = await GetRealtimeClient(protocol, (options, settings) =>
            {
                options.UseTokenAuth = true;
                options.AutoConnect = true;
            });

            await client.WaitForState(ConnectionState.Connected);

            client.Connection.ConnectionStateTtl.Should().NotBe(TimeSpan.MaxValue);

            var key = client.Connection.Key;

            client.Connection.Once(state =>
            {
                // RTN4h - can emit UPDATE event
                if (state.Event == ConnectionEvent.Update)
                {
                    // should have both previous and current attributes set to CONNECTED
                    state.Current.Should().Be(ConnectionState.Connected);
                    state.Previous.Should().Be(ConnectionState.Connected);
                    state.Reason.Message = "fake-error";
                    updateAwaiter.SetCompleted();
                }
                else
                {
                    throw new Exception($"'{state.Event}' was handled. Only an 'Update' event should have occured");
                }
            });

            client.FakeProtocolMessageReceived(new ProtocolMessage(ProtocolMessage.MessageAction.Connected)
            {
                ConnectionDetails = new ConnectionDetails
                {
                    ConnectionKey = "key",
                    ClientId = "RTN21",
                    ConnectionStateTtl = TimeSpan.MaxValue
                },
                Error = new ErrorInfo("fake-error"),
            });

            var didUpdate = await updateAwaiter.Task;
            didUpdate.Should().BeTrue();

            // RTN21 - new connection details over write old values
            client.Connection.Key.Should().NotBe(key);
            client.ClientId.Should().Be("RTN21");
            client.Connection.ConnectionStateTtl.Should().Be(TimeSpan.MaxValue);
        }

        public ConnectionSandboxOperatingSystemEventsForNetworkSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output) { }
    }
}
