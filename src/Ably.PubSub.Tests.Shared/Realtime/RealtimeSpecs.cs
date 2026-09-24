using System.Threading.Tasks;

using Ably.PubSub.Realtime;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests
{
    public class RealtimeSpecs : AblySpecs
    {
        [Fact]
        [Trait("spec", "RTC1")]
        public void UsesSameClientOptionsAsRestClient()
        {
            var options = new ClientOptions(ValidKey);

            var client = new PubSubRealtimeClient(options);

            client.Options.Should().BeSameAs(client.HttpClient.Options);
        }

        public class RealtimePropertiesSpec : MockHttpRealtimeSpecs
        {
            private readonly PubSubRealtimeClient _client;

            [Fact]
            [Trait("spec", "RTC2")]
            public void ShouldAllowAccessToConnectionObject()
            {
                _client.Connection.Should().NotBeNull();
                _client.Connection.Should().BeOfType<Connection>();
            }

            [Fact]
            [Trait("spec", "RTC3")]
            public void ShouldAllowAccessToChannelsObject()
            {
                _client.Channels.Should().NotBeNull();
                _client.Channels.Should().BeAssignableTo<IChannels<IRealtimeChannel>>();
            }

            [Fact]
            [Trait("spec", "RTC4")]
            public void ShouldHaveAccessToRestAuth()
            {
                _client.Auth.Should().BeSameAs(_client.HttpClient.Auth);
            }

            [Fact]
            [Trait("spec", "RTC5a")]
            public void ShouldProxyRestClientStats()
            {
                _client.StatsAsync();
                LastRequest.Url.Should().Contain("stats");
            }

            [Fact]
            [Trait("spec", "RTC5b")]
            public void ShouldImplementTheSameStatsInterfaceAsTheRestClient()
            {
                _client.Should().BeAssignableTo<IStatsCommands>();
            }

            [Fact]
            [Trait("spec", "RTC6a")]
            public async Task ShouldImplementTheTimeFunction()
            {
                try
                {
                    await _client.TimeAsync();
                }
                catch
                {
                    // ignore processing errors and only care about the request
                }

                LastRequest.Url.Should().Contain("time");
            }

            public RealtimePropertiesSpec(ITestOutputHelper output)
                : base(output)
            {
                _client = GetRealtimeClient();
            }
        }

        [Fact]
        public void Connection_AllowAccessToConnectionObject()
        {
            var client = new PubSubRealtimeClient(ValidKey);
            client.Connection.Should().NotBeNull();
        }

        [Fact]
        public void When_HostNotSetInOptions_UseBinaryProtocol_TrueByDefault()
        {
            // Arrange
            ClientOptions options = new ClientOptions();

            // Act
            if (Defaults.MsgPackEnabled)
#pragma warning disable 162
            {
                options.UseBinaryProtocol.Should().BeTrue();
            }
#pragma warning restore 162
        }

        [Fact]
        public void New_Realtime_HasConnection()
        {
            PubSubRealtimeClient realtime = new PubSubRealtimeClient(ValidKey);
            realtime.Connection.Should().NotBeNull();
        }

        [Fact]
        public void New_Realtime_HasChannels()
        {
            PubSubRealtimeClient realtime = new PubSubRealtimeClient(ValidKey);
            realtime.Channels.Should().NotBeNull();
        }

        [Fact]
        public void New_Realtime_HasAuth()
        {
            PubSubRealtimeClient realtime = new PubSubRealtimeClient(ValidKey);
            realtime.Auth.Should().NotBeNull();
        }

        [Theory]
        [InlineData(false)]
        [InlineData(true)]
        [Trait("issue", "380")]
        public void AutomaticNetworkDetectionCanBeDisabledByClientOption(bool enabled)
        {
            // Because this test depends on static state in the 'Platform' type we need
            // to reset the static 'Platform' state before each test run.

            Platform.HookedUpToNetworkEvents = false;

            _ = new PubSubRealtimeClient(new ClientOptions(ValidKey)
            {
                AutomaticNetworkStateMonitoring = enabled,
            });

            Platform.HookedUpToNetworkEvents.Should().Be(enabled);
        }

        [Fact]
        [Trait("issue", "380")]
        public void AutomaticNetworkStateMonitoring_ShouldBeEnabledByDefault()
        {
            var clientOptions = new ClientOptions(ValidKey);
            clientOptions.AutomaticNetworkStateMonitoring.Should().Be(true);
        }

        public RealtimeSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
