using System.Collections.Generic;

using IO.Ably.Push;

using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests.Push
{
    public class ListDeviceDetailsRequestTests
    {
        private const string ClientId = "Zaphod";
        private const string DeviceId = "Marvin";

        private const string ClientIdKey = "clientId";
        private const string DeviceIdKey = "deviceId";
        private const string LimitKey = "limit";

        private const int Limit = 42;

        [Fact]
        public void EmptyFactoryCorrectlyInitialisesState()
        {
            var o = ListDeviceDetailsRequest.Empty(Limit);

            o.ClientId.Should().BeNull();
            o.DeviceId.Should().BeNull();
            o.Limit.Should().Be(Limit);
        }

        [Fact]
        public void ClientIdFactoryCorrectlyInitialisesState()
        {
            var o = ListDeviceDetailsRequest.WithClientId(ClientId, Limit);

            o.ClientId.Should().Be(ClientId);
            o.DeviceId.Should().BeNull();
            o.Limit.Should().Be(Limit);
        }

        [Fact]
        public void DeviceIdFactoryCorrectlyInitialisesState()
        {
            var o = ListDeviceDetailsRequest.WithDeviceId(DeviceId, Limit);

            o.ClientId.Should().BeNull();
            o.DeviceId.Should().Be(DeviceId);
            o.Limit.Should().Be(Limit);
        }

        [Fact]
        public void QueryParamsCorrectForEmpty()
        {
            var o = ListDeviceDetailsRequest.Empty(null);

            Dictionary<string, string> p = o.ToQueryParams();

            p.Count.Should().Be(0);
        }

        [Fact]
        public void QueryParamsCorrectForClientId()
        {
            var o = ListDeviceDetailsRequest.WithClientId(ClientId, Limit);

            Dictionary<string, string> p = o.ToQueryParams();

            p.Count.Should().Be(2);
            p[ClientIdKey].Should().Be(ClientId);
            p[LimitKey].Should().Be(Limit.ToString());
        }

        [Fact]
        public void QueryParamsCorrectForDeviceId()
        {
            var o = ListDeviceDetailsRequest.WithDeviceId(DeviceId, Limit);

            Dictionary<string, string> p = o.ToQueryParams();

            p.Count.Should().Be(2);
            p[DeviceIdKey].Should().Be(DeviceId);
            p[LimitKey].Should().Be(Limit.ToString());
        }
    }
}
