using System;
using System.Threading.Tasks;
using FluentAssertions;
using Ably.PubSub.Device;
using Ably.PubSub.Server;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests;
using Ably.PubSub.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.PubSub
{
    /// <summary>
    /// Dual-construction-mode conformance: a client built through each door must actually work
    /// against the Ably sandbox, not just carry the right agent over the fake transport. One
    /// realtime publish/subscribe round-trip per realtime door, and one REST call for the HTTP
    /// door. Tagged type=integration so it runs in the existing Test.NetStandard.Integration legs.
    /// </summary>
    [Collection("SandBox Connection")]
    [Trait("type", "integration")]
    public class PubSubDoorSandboxSpecs : SandboxSpecs
    {
        public PubSubDoorSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        [Fact]
        [Trait("spec", "RSC7d")]
        public async Task ServerRealtimeDoor_ConnectsAndRoundTripsAMessage()
        {
            var client = PubSubServer.CreateRealtimeClient(await DoorOptions());

            await RoundTripAMessage(client);
        }

        [Fact]
        [Trait("spec", "RSC7d")]
        public async Task DeviceDoor_ConnectsAndRoundTripsAMessage()
        {
            var client = PubSubDevice.CreateClient(await DoorOptions());

            await RoundTripAMessage(client);
        }

        [Fact]
        [Trait("spec", "RSC7d")]
        public async Task ServerHttpDoor_CompletesARestCall()
        {
            var client = PubSubServer.CreateHttpClient(await DoorOptions());

            var serverTime = await client.TimeAsync();

            serverTime.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMinutes(15));
        }

        private static async Task<ClientOptions> DoorOptions()
        {
            var settings = await AblySandboxFixture.GetSettings();
            var options = settings.CreateDefaultOptions();
            options.TransportFactory = new TestTransportFactory();
            return options;
        }

        private async Task RoundTripAMessage(PubSubRealtimeClient client)
        {
            using (client)
            {
                await client.WaitForState(ConnectionState.Connected);

                var channel = client.Channels.Get("pubsub-door-smoke".AddRandomSuffix());
                Message received = null;
                channel.Subscribe(message => received = message);
                await channel.AttachAsync();

                var result = await channel.PublishAsync(new Message("greeting", "hello"));
                result.IsSuccess.Should().BeTrue();

                await new ConditionalAwaiter(() => received != null);
                received.Data.Should().Be("hello");
            }
        }
    }
}
