using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Types;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.Realtime
{
    [Trait("type", "integration")]
    public class RealtimeSandboxSpecs : SandboxSpecs
    {
        public RealtimeSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [Theory]
        [ProtocolData]
        public async Task WhenDisposed_ShouldSendClosingMessage(Protocol protocol)
        {
            var sentMessages = new List<ProtocolMessage>();
            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.TransportFactory = new TestTransportFactory
                {
                    OnMessageSent = sentMessages.Add
                };
            });

            await client.WaitForState(ConnectionState.Connected);

            client.Dispose();

            await client.ProcessCommands();
            sentMessages.Count.Should().Be(1);
            sentMessages.First().Action.Should().Be(ProtocolMessage.MessageAction.Close);
            client.Disposed.Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTC8")]
        public async Task WithNotConnectedClient_AuthorizeObtainsNewTokenAndConnects(Protocol protocol)
        {
            // TODO: test against all non CONNECTED states
            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.AutoConnect = false;
                opts.ClientId = "RTC8";
            });

            // show that the connection is not connected
            client.Connection.State.Should().Be(ConnectionState.Initialized);

            var tokenDetails = await client.Auth.AuthorizeAsync();
            tokenDetails.ClientId.Should().Be("RTC8");

            await client.WaitForState(ConnectionState.Connecting);
            await client.WaitForState(ConnectionState.Connected);

            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RTC8")]
        [Trait("spec", "RTC8a")]
        [Trait("spec", "RTC8a1")]
        [Trait("spec", "RTC8a2")]
        [Trait("spec", "RTC8a3")]
        public async Task WithConnectedClient_AuthorizeObtainsNewTokenAndUpgradesConnection_AndShouldEmitUpdate(Protocol protocol)
        {
            const string validClientId1 = "RTC8";
            const string invalidClientId = "RTC8-incompatible-clientId";

            // For a realtime client, Auth#authorize instructs the library to obtain
            // a token using the provided tokenParams and authOptions and upgrade
            // the current connection to use that token
            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.ClientId = validClientId1;
                opts.UseTokenAuth = true;
            });

            var awaiter = new TaskCompletionAwaiter();
            client.Connection.On(ConnectionEvent.Update, args => { awaiter.SetCompleted(); });
            await client.WaitForState(ConnectionState.Connected);

            var tokenDetails = await client.Auth.AuthorizeAsync(new TokenParams { ClientId = validClientId1 });
            tokenDetails.ClientId.Should().Be(validClientId1);
            client.Connection.State.Should().Be(ConnectionState.Connected);
            client.RestClient.AblyAuth.CurrentToken.Should().Be(tokenDetails);
            var didUpdate = await awaiter.Task;

            client.Connection.State.Should().Be(ConnectionState.Connected);
            didUpdate.Should().BeTrue(
                "the AUTH message should trigger a CONNECTED response from the server that causes an UPDATE to be emitted.");

            client.Connection.On(args =>
            {
                if (args.Current != ConnectionState.Failed)
                {
                    Assert.True(false, $"unexpected state '{args.Current}'");
                }
            });

            // AuthorizeAsync will not return until either a CONNECTED or ERROR response
            // (or timeout) is seen from Ably, so we do not need to use WaitForState() here
            await Assert.ThrowsAsync<AblyException>(() => client.Auth.AuthorizeAsync(new TokenParams { ClientId = invalidClientId }));

            client.Connection.State.Should().Be(ConnectionState.Failed);
        }

        // TODO: Figure out which one is the right test
        [Theory]
        [ProtocolData]
        [Trait("spec", "RTC8")]
        [Trait("spec", "RTC8a")]
        [Trait("spec", "RTC8a1")]
        [Trait("spec", "RTC8a2")]
        [Trait("spec", "RTC8a3")]
        public async Task WithNotConnectedClient_WhenAuthorizeCalled_ShouldConnect(Protocol protocol)
        {
            const string validClientId1 = "RTC8";

            var client2 = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.AutoConnect = false;
                opts.ClientId = validClientId1;
            });

            await client2.Auth.AuthorizeAsync();
            await client2.WaitForState(ConnectionState.Connected);
            client2.Connection.State.Should().Be(ConnectionState.Connected);
            client2.Close();

            await client2.WaitForState(ConnectionState.Closed);
        }

        [Theory]
        [ProtocolData]
        public async Task WithConnectedClient_OnAuthUpdated_ShouldTimeOutIfNoResponseFromTheServer(Protocol protocol)
        {
            const string validClientId1 = "RTC8";

            var client = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.AutoConnect = true;
                opts.ClientId = validClientId1;
            });

            await client.WaitForState(ConnectionState.Connected);

            // internally AblyAuth.AuthorizeCompleted is used to indicate when an Authorize call is finished
            // AuthorizeCompleted should timeout if no valid response (CONNECTED or ERROR) is received from Ably
            var auth = client.RestClient.AblyAuth;
            try
            {
                client.BlockActionFromSending(ProtocolMessage.MessageAction.Auth);
                await auth.OnAuthUpdated(new TokenDetails("test"), true);
                throw new Exception("AuthorizeCompleted did not raise an exception.");
            }
            catch (AblyException e)
            {
                e.Should().BeOfType<AblyException>();
                e.ErrorInfo.Code.Should().Be(ErrorCodes.TokenError);
            }
        }

        [Theory(Skip = "Keeps failing")]
        [ProtocolData]
        [Trait("spec", "RTC8a1")]
        public async Task WithConnectedClient_WhenUpgradingCapabilities_ConnectionShouldNotBeImpaired(Protocol protocol)
        {
            var clientId = "RTC8a1".AddRandomSuffix();
            var capability = new Capability();
            capability.AddResource("foo").AllowPublish();

            var restClient = await GetRestClient(protocol, options => { options.UseTokenAuth = true; });
            var tokenDetails = await restClient.Auth.RequestTokenAsync(new TokenParams
            {
                ClientId = clientId,
                Capability = capability,
            });

            var realtime = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.Token = tokenDetails.Token;
            });
            await realtime.WaitForState();

            // upgrade of capabilities without any loss of continuity or connectivity
            realtime.Connection.Once(ConnectionEvent.Disconnected, change => throw new Exception("should not disconnect"));

            var fooChannel = realtime.Channels.Get("foo");
            var barChannel = realtime.Channels.Get("bar");

            var fooSuccessAWaiter = new TaskCompletionAwaiter(5000);
            fooChannel.Publish("test", "should-not-fail", (b, info) =>
            {
                // foo should succeed
                b.Should().BeTrue();
                info.Should().BeNull();
                fooSuccessAWaiter.SetCompleted();
            });
            fooChannel.Attach();
            Assert.True(await fooSuccessAWaiter.Task);

            var barFailAwaiter = new TaskCompletionAwaiter(5000);
            barChannel.Publish("test", "should-fail", (b, info) =>
            {
                // bar should fail
                b.Should().BeFalse();
                info.Code.Should().Be(ErrorCodes.OperationNotPermittedWithCapability);
                barFailAwaiter.SetCompleted();
            });
            barChannel.Attach();
            Assert.True(await barFailAwaiter.Task);

            // upgrade bar
            capability = new Capability();
            capability.AddResource("bar").AllowPublish();
            await realtime.Auth.AuthorizeAsync(new TokenParams
            {
                Capability = capability,
                ClientId = clientId,
            });

            realtime.Connection.State.Should().Be(ConnectionState.Connected);
            var barSuccessAwaiter = new TaskCompletionAwaiter(5000);
            barChannel.Attach((b2, info2) =>
            {
                b2.Should().BeTrue();
                barChannel.Publish("test", "should-succeed", (b, info) =>
                {
                    b.Should().BeTrue();
                    info.Should().BeNull();
                    barSuccessAwaiter.SetCompleted();
                });
            });

            Assert.True(await barSuccessAwaiter.Task);
        }

        [Theory(Skip = "Intermittently fails")]
        [ProtocolData]
        [Trait("spec", "RTC8a1")]
        public async Task WithConnectedClient_WhenDowngradingCapabilities_ChannelShouldBecomeFailed(Protocol protocol)
        {
            var clientId = "RTC8a1-downgrade".AddRandomSuffix();
            var channelName = "RTC8a1-downgrade-channel".AddRandomSuffix();
            var wrongChannelName = "wrong".AddRandomSuffix();

            var (realtime, channel) = await SetupRealtimeClient();
            channel.On(statechange => Output.WriteLine($"Changed state: {statechange.Previous} to {statechange.Current}. Error: {statechange.Error}"));
            realtime.Connection.Once(ConnectionEvent.Disconnected, change => throw new Exception("Should not require a disconnect"));

            var result = await channel.PublishAsync("test", "should-not-fail");
            result.IsSuccess.Should().BeTrue();

            ChannelStateChange stateChange = null;

            var failedAwaiter = new TaskCompletionAwaiter(2000);
            channel.Once(ChannelEvent.Failed, state =>
            {
                stateChange = state;
                failedAwaiter.SetCompleted();
            });
            await DowngradeCapability(realtime);

            await channel.WaitForState(ChannelState.Failed, TimeSpan.FromSeconds(6));
            await failedAwaiter.Task;

            stateChange.Should().NotBeNull("channel should have failed");
            stateChange.Error.Code.Should().Be(ErrorCodes.OperationNotPermittedWithCapability);
            stateChange.Error.Message.Should().Contain("Channel denied access");

            async Task DowngradeCapability(AblyRealtime rt)
            {
                var capability = new Capability();
                capability.AddResource(wrongChannelName).AllowSubscribe();

                var newToken = await rt.Auth.AuthorizeAsync(new TokenParams
                {
                    Capability = capability,
                    ClientId = clientId,
                });

                newToken.Should().NotBeNull();
            }

            async Task<(AblyRealtime, IRealtimeChannel)> SetupRealtimeClient()
            {
                var capability = new Capability();
                capability.AddResource(channelName).AllowAll();

                var restClient = await GetRestClient(protocol);
                var tokenDetails = await restClient.Auth.RequestTokenAsync(new TokenParams
                {
                    ClientId = clientId,
                    Capability = capability
                });

                var rt = await GetRealtimeClient(protocol, (opts, _) => { opts.Token = tokenDetails.Token; });

                await rt.WaitForState(ConnectionState.Connected);
                var ch = rt.Channels.Get(channelName);
                await ch.AttachAsync();

                return (rt, ch);
            }
        }
    }
}
