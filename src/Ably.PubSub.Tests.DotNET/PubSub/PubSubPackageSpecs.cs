using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.PubSub.Device;
using IO.Ably.PubSub.Server;
using IO.Ably.Tests;
using IO.Ably.Tests.Infrastructure;
using IO.Ably.Tests.Realtime;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.PubSub
{
    /// <summary>
    /// The agent contract of the two door packages. These assertions are what Ably's billing
    /// reads: the realtime system grants the MAU server exemption on API-key auth by matching an
    /// agent entry ending in `-server`, so a rename, a stray version suffix or a lost stamp must
    /// fail here loudly rather than surface as a billing discrepancy.
    ///
    /// The identifiers are written out as literals on purpose - asserting against
    /// `Side.ServerAgentIdentifier` would make a rename of the constant pass silently.
    ///
    /// Every spec either disables AutoConnect or supplies the fake transport factory, so no unit
    /// test here opens a network connection. The one gap that follows from that is noted on
    /// StringOverloadSpecs below.
    /// </summary>
    [Trait("spec", "RSC7d")]
    public class PubSubPackageSpecs : AblySpecs
    {
        private const string DeviceFlag = "ably-pubsub-device";
        private const string ServerFlag = "ably-pubsub-server";
        private const string FamilyPrefix = "ably-pubsub-dotnet/";

        public PubSubPackageSpecs(ITestOutputHelper output)
            : base(output)
        {
        }

        private static string FamilyIdentifier => FamilyPrefix + Defaults.LibraryVersion;

        [Fact]
        public async Task ServerHttpClient_SendsBareServerFlagAndFamilyIdentifier()
        {
            var agentValues = await CaptureHttpAgentTokens(
                handler => PubSubServer.CreateHttpClient(options => UseFakeHttp(options, handler)));

            AssertServerSide(agentValues);
        }

        [Fact]
        public async Task ServerRealtimeClient_SendsBareServerFlagAndFamilyIdentifier()
        {
            var agentValues = await CaptureRealtimeAgentTokens(
                factory => PubSubServer.CreateRealtimeClient(o => UseFakeTransport(o, factory)));

            AssertServerSide(agentValues);
        }

        [Fact]
        public async Task DeviceClient_SendsBareDeviceFlagOverHttp()
        {
            var agentValues = await CaptureHttpAgentTokens(
                handler => PubSubDevice.CreateClient(options => UseFakeHttp(options, handler)).RestClient);

            AssertDeviceSide(agentValues);
        }

        [Fact]
        public async Task DeviceClient_SendsBareDeviceFlagOnTheRealtimeConnection()
        {
            var agentValues = await CaptureRealtimeAgentTokens(
                factory => PubSubDevice.CreateClient(o => UseFakeTransport(o, factory)));

            AssertDeviceSide(agentValues);
        }

        [Fact]
        public async Task CallerSuppliedAgents_ArePreservedAlongsideTheFlag()
        {
            var agentValues = await CaptureHttpAgentTokens(handler =>
                PubSubServer.CreateHttpClient(options =>
                {
                    UseFakeHttp(options, handler);
                    options.Agents = new Dictionary<string, string> { { "chat-dotnet", "1.0.0" } };
                }));

            agentValues.Should().Contain("chat-dotnet/1.0.0");
            AssertServerSide(agentValues);
        }

        [Fact]
        public async Task CallerEntryUnderTheSidesOwnKey_IsOverriddenWithTheBareFlag()
        {
            // Which side the package declares is the package's to state, not the caller's to
            // redefine, so the stamp is applied last and wins.
            var agentValues = await CaptureHttpAgentTokens(handler =>
                PubSubServer.CreateHttpClient(options =>
                {
                    UseFakeHttp(options, handler);
                    options.Agents = new Dictionary<string, string> { { ServerFlag, "9.9.9" } };
                }));

            AssertServerSide(agentValues);
        }

        [Fact]
        public void CallersOwnAgentsDictionary_IsNotMutated()
        {
            var callerAgents = new Dictionary<string, string> { { "chat-dotnet", "1.0.0" } };
            var options = new ClientOptions(ValidKey) { AutoConnect = false, Agents = callerAgents };

            using (PubSubServer.CreateRealtimeClient(options))
            {
                callerAgents.Should().HaveCount(1);
                callerAgents.Should().NotContainKey(ServerFlag);
                callerAgents.Should().ContainKey("chat-dotnet");

                // The options object itself is the one the client consumes, so its Agents
                // property is the new dictionary carrying the stamp.
                options.Agents.Should().NotBeSameAs(callerAgents);
                options.Agents.Should().ContainKey(ServerFlag);
            }
        }

        [Fact]
        public void EveryDoor_RejectsNullOptions()
        {
            Assert.Throws<ArgumentNullException>(() => PubSubServer.CreateRealtimeClient((ClientOptions)null));
            Assert.Throws<ArgumentNullException>(() => PubSubServer.CreateHttpClient((ClientOptions)null));
            Assert.Throws<ArgumentNullException>(() => PubSubDevice.CreateClient((ClientOptions)null));
        }

        [Fact]
        public void EveryDoor_RejectsANullConfigureAction()
        {
            Assert.Throws<ArgumentNullException>(() => PubSubServer.CreateRealtimeClient((Action<ClientOptions>)null));
            Assert.Throws<ArgumentNullException>(() => PubSubServer.CreateHttpClient((Action<ClientOptions>)null));
            Assert.Throws<ArgumentNullException>(() => PubSubDevice.CreateClient((Action<ClientOptions>)null));
        }

        [Fact]
        public void OptionsOverloads_ReturnTheConcreteCoreTypes()
        {
            using (var realtime = PubSubServer.CreateRealtimeClient(NoConnectOptions()))
            {
                realtime.Should().BeOfType<AblyRealtime>();
            }

            PubSubServer.CreateHttpClient(NoConnectOptions()).Should().BeOfType<AblyRest>();

            using (var device = PubSubDevice.CreateClient(NoConnectOptions()))
            {
                device.Should().BeOfType<AblyRealtime>();
            }
        }

        [Fact]
        public void ActionOverloads_ReturnTheConcreteCoreTypes()
        {
            using (var realtime = PubSubServer.CreateRealtimeClient(o => NoConnect(o)))
            {
                realtime.Should().BeOfType<AblyRealtime>();
            }

            PubSubServer.CreateHttpClient(o => NoConnect(o)).Should().BeOfType<AblyRest>();

            using (var device = PubSubDevice.CreateClient(o => NoConnect(o)))
            {
                device.Should().BeOfType<AblyRealtime>();
            }
        }

        /// <summary>
        /// The string overload of every door hands the string to `new ClientOptions(keyOrToken)`,
        /// which applies the core's own colon rule (AuthOptions: an Ably API key is
        /// `APP_ID.KEY_ID:KEY_SECRET` and always contains a colon; a token never does), and then
        /// takes the same stamping path as the options overload.
        ///
        /// Only the HTTP door is exercised here. `CreateRealtimeClient(string)` and
        /// `CreateClient(string)` cannot set AutoConnect, so constructing one in a unit test
        /// would open a real websocket - the flakiness noted on the superseded PR #1330. They
        /// share this exact code path, and the realtime stamping itself is covered above through
        /// the action overload with a fake transport.
        /// </summary>
        public class StringOverloadSpecs
        {
            [Fact]
            public void AKeyLikeString_IsTreatedAsAnApiKeyAndStillStamped()
            {
                var client = PubSubServer.CreateHttpClient("appId.keyId:secret");

                client.Options.Key.Should().Be("appId.keyId:secret");
                client.Options.Token.Should().BeNullOrEmpty();
                client.Options.Agents.Should().ContainKey(ServerFlag);
                client.Options.Agents[ServerFlag].Should().BeNullOrEmpty();
            }

            [Fact]
            public void ATokenLikeString_IsTreatedAsATokenAndStillStamped()
            {
                var client = PubSubServer.CreateHttpClient("a_token_with_no_colon");

                client.Options.Token.Should().Be("a_token_with_no_colon");
                client.Options.Key.Should().BeNullOrEmpty();
                client.Options.Agents.Should().ContainKey(ServerFlag);
                client.Options.Agents[ServerFlag].Should().BeNullOrEmpty();
            }
        }

        private static ClientOptions NoConnectOptions()
        {
            var options = new ClientOptions(ValidKey);
            NoConnect(options);
            return options;
        }

        private static void NoConnect(ClientOptions options)
        {
            options.Key = ValidKey;
            options.AutoConnect = false;
            options.SkipInternetCheck = true;
        }

        private static void UseFakeHttp(ClientOptions options, FakeHttpMessageHandler handler)
        {
            NoConnect(options);
            options.UseBinaryProtocol = false;
            options.HttpClient = new HttpClient(handler);
        }

        private static void UseFakeTransport(ClientOptions options, FakeTransportFactory factory)
        {
            options.Key = ValidKey;
            options.SkipInternetCheck = true;
            options.TransportFactory = factory;
        }

        /// <summary>
        /// Drives a real request through the door-created REST client and returns the
        /// `Ably-Agent` header the core actually put on the wire, split into tokens.
        /// </summary>
        private static async Task<string[]> CaptureHttpAgentTokens(Func<FakeHttpMessageHandler, AblyRest> createClient)
        {
            var response = new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new StringContent("[1500000000000]"),
            };
            response.Content.Headers.ContentType = new System.Net.Http.Headers.MediaTypeHeaderValue("application/json");

            var handler = new FakeHttpMessageHandler(response);
            var client = createClient(handler);

            await client.TimeAsync();

            handler.LastRequest.Should().NotBeNull();
            var values = handler.LastRequest.Headers.GetValues(Agent.AblyAgentHeader).ToArray();
            values.Should().HaveCount(1);

            return values[0].Split(' ');
        }

        /// <summary>
        /// Constructs a door-created realtime client over the fake transport and returns the
        /// agent tokens from the connection query parameters.
        ///
        /// Note the query key: `TransportParams.GetParams()` sends the agent under
        /// `Ably-Agent`, while spec RTN2g names the parameter `agent`. Whether that is a latent
        /// bug is a separate question (plan step 14); this spec asserts on whatever key the core
        /// currently uses so it does not pre-empt the answer.
        /// </summary>
        private static async Task<string[]> CaptureRealtimeAgentTokens(Func<FakeTransportFactory, AblyRealtime> createClient)
        {
            var factory = new FakeTransportFactory();
            using (createClient(factory))
            {
                // The connection is established off the calling thread, so wait for the factory
                // to be asked for a transport rather than assuming it already has been.
                using (var awaiter = new ConditionalAwaiter(
                    () => factory.LastCreatedTransport != null,
                    () => "the fake transport factory was never asked for a transport"))
                {
                    await awaiter;
                }

                var transport = factory.LastCreatedTransport;
                transport.Should().NotBeNull("the fake transport factory should have been used instead of a real websocket");

                var parameters = transport.Parameters.GetParams();
                parameters.Should().ContainKey(Agent.AblyAgentHeader);

                return parameters[Agent.AblyAgentHeader].Split(' ');
            }
        }

        private static void AssertServicedByThisSdk(string[] agentValues)
        {
            agentValues.Should().Contain(
                FamilyIdentifier,
                $"the family identifier must be the versioned '{FamilyPrefix}<version>' token registered in ably-common");
        }

        private static void AssertServerSide(string[] agentValues)
        {
            AssertServicedByThisSdk(agentValues);

            agentValues.Should().Contain(
                ServerFlag,
                "the server flag must be a bare token - the '-server' suffix is what earns the MAU exemption on API-key auth");
            agentValues.Should().NotContain(
                t => t.StartsWith(ServerFlag + "/", StringComparison.Ordinal),
                "the side flag is deliberately versionless (ably-common models it with versioned: false)");
            agentValues.Should().NotContain(
                t => t.StartsWith(DeviceFlag, StringComparison.Ordinal),
                "a server client must never declare the device side");
        }

        private static void AssertDeviceSide(string[] agentValues)
        {
            AssertServicedByThisSdk(agentValues);

            agentValues.Should().Contain(DeviceFlag, "the device flag must be a bare token");
            agentValues.Should().NotContain(
                t => t.StartsWith(DeviceFlag + "/", StringComparison.Ordinal),
                "the side flag is deliberately versionless (ably-common models it with versioned: false)");
            agentValues.Should().NotContain(
                t => t.StartsWith(ServerFlag, StringComparison.Ordinal),
                "a device client must never declare the server side, which would earn it the MAU exemption");
        }
    }
}
