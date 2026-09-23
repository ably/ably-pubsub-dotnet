using FluentAssertions;
using IO.Ably.Push;
using Xunit;

namespace IO.Ably.Tests.Push
{
    public class ListSubscriptionsRequestTests
    {
        private const string Channel = "hikers";
        private const string ClientId = "zaphod";
        private const string DeviceId = "marvin";
        private const int Limit = 42;

        [Fact]
        public void PropertiesReflectDeviceIdFactory()
        {
            var o = ListSubscriptionsRequest.WithDeviceId(Channel, DeviceId, Limit);

            o.Channel.Should().Be(Channel);
            o.ClientId.Should().BeNull();
            o.DeviceId.Should().Be(DeviceId);
            o.Limit.Should().Be(Limit);
        }

        [Fact]
        public void PropertiesReflectClientIdFactory()
        {
            var o = ListSubscriptionsRequest.WithClientId(Channel, ClientId, Limit);

            o.Channel.Should().Be(Channel);
            o.ClientId.Should().Be(ClientId);
            o.DeviceId.Should().BeNull();
            o.Limit.Should().Be(Limit);
        }
    }
}
