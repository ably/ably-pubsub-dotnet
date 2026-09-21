using System;
using System.Collections.Generic;
using System.Linq;

using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class StatsParsingTests
    {
        private readonly Stats _stats;

        public StatsParsingTests()
        {
            _stats = JsonHelper.Deserialize<List<Stats>>(ResourceHelper.GetResource(@"StatsInterval.json")).First();
        }

        [Fact]
        public void AllSectionHasCorrectValues()
        {
            _stats.All.All.Count.Should().Be(20);
            _stats.All.All.Data.Should().Be(180);
            _stats.All.Messages.Count.Should().Be(20);
            _stats.All.Messages.Data.Should().Be(180);
            _stats.All.Presence.Count.Should().Be(0);
            _stats.All.Presence.Data.Should().Be(0);
        }

        [Fact]
        public void InboundSectionHasCorrectValues()
        {
            _stats.Inbound.All.All.Count.Should().Be(20);
            _stats.Inbound.All.All.Data.Should().Be(180);
            _stats.Inbound.All.Messages.Count.Should().Be(20);
            _stats.Inbound.All.Messages.Data.Should().Be(180);
            _stats.Inbound.All.Presence.Count.Should().Be(0);
            _stats.Inbound.All.Presence.Data.Should().Be(0);

            // Realtime
            _stats.Inbound.Realtime.All.Count.Should().Be(0);
            _stats.Inbound.Realtime.All.Data.Should().Be(0);
            _stats.Inbound.Realtime.Messages.Count.Should().Be(0);
            _stats.Inbound.Realtime.Messages.Data.Should().Be(0);
            _stats.Inbound.Realtime.Presence.Count.Should().Be(0);
            _stats.Inbound.Realtime.Presence.Data.Should().Be(0);

            // Rest
            _stats.Inbound.Rest.All.Count.Should().Be(20);
            _stats.Inbound.Rest.All.Data.Should().Be(180);
            _stats.Inbound.Rest.Messages.Count.Should().Be(20);
            _stats.Inbound.Rest.Messages.Data.Should().Be(180);
            _stats.Inbound.Rest.Presence.Count.Should().Be(0);
            _stats.Inbound.Rest.Presence.Data.Should().Be(0);
        }

        [Fact]
        public void OutboundSectionHasCorrectValues()
        {
            _stats.Outbound.All.All.Count.Should().Be(0);
            _stats.Outbound.All.All.Data.Should().Be(0);
            _stats.Outbound.All.Messages.Count.Should().Be(0);
            _stats.Outbound.All.Messages.Data.Should().Be(0);
            _stats.Outbound.All.Presence.Count.Should().Be(0);
            _stats.Outbound.All.Presence.Data.Should().Be(0);

            // Realtime
            _stats.Outbound.Realtime.All.Count.Should().Be(0);
            _stats.Outbound.Realtime.All.Data.Should().Be(0);
            _stats.Outbound.Realtime.Messages.Count.Should().Be(0);
            _stats.Outbound.Realtime.Messages.Data.Should().Be(0);
            _stats.Outbound.Realtime.Presence.Count.Should().Be(0);
            _stats.Outbound.Realtime.Presence.Data.Should().Be(0);

            // Rest
            _stats.Outbound.Rest.All.Count.Should().Be(0);
            _stats.Outbound.Rest.All.Data.Should().Be(0);
            _stats.Outbound.Rest.Messages.Count.Should().Be(0);
            _stats.Outbound.Rest.Messages.Data.Should().Be(0);
            _stats.Outbound.Rest.Presence.Count.Should().Be(0);
            _stats.Outbound.Rest.Presence.Data.Should().Be(0);

            // HttpStream
            _stats.Outbound.Webhook.All.Count.Should().Be(0);
            _stats.Outbound.Webhook.All.Data.Should().Be(0);
            _stats.Outbound.Webhook.Messages.Count.Should().Be(0);
            _stats.Outbound.Webhook.Messages.Data.Should().Be(0);
            _stats.Outbound.Webhook.Presence.Count.Should().Be(0);
            _stats.Outbound.Webhook.Presence.Data.Should().Be(0);
        }

        [Fact]
        public void PersistedHasCorrectValues()
        {
            _stats.Persisted.All.Count.Should().Be(0);
            _stats.Persisted.All.Data.Should().Be(0);
            _stats.Persisted.Messages.Count.Should().Be(0);
            _stats.Persisted.Messages.Data.Should().Be(0);
            _stats.Persisted.Presence.Count.Should().Be(0);
            _stats.Persisted.Presence.Data.Should().Be(0);
        }

        [Fact]
        public void ConnectionsHasCorrectValues()
        {
            _stats.Connections.All.Opened.Should().Be(0);
            _stats.Connections.All.Peak.Should().Be(0);
            _stats.Connections.All.Mean.Should().Be(0);
            _stats.Connections.All.Min.Should().Be(0);
            _stats.Connections.All.Refused.Should().Be(0);

            _stats.Connections.Plain.Opened.Should().Be(0);
            _stats.Connections.Plain.Peak.Should().Be(0);
            _stats.Connections.Plain.Mean.Should().Be(0);
            _stats.Connections.Plain.Min.Should().Be(0);
            _stats.Connections.Plain.Refused.Should().Be(0);

            _stats.Connections.Tls.Opened.Should().Be(0);
            _stats.Connections.Tls.Peak.Should().Be(0);
            _stats.Connections.Tls.Mean.Should().Be(0);
            _stats.Connections.Tls.Min.Should().Be(0);
            _stats.Connections.Tls.Refused.Should().Be(0);
        }

        [Fact]
        public void ChannelsHasCorrectValues()
        {
            _stats.Channels.Opened.Should().Be(1);
            _stats.Channels.Peak.Should().Be(0);
            _stats.Channels.Mean.Should().Be(0);
            _stats.Channels.Min.Should().Be(0);
            _stats.Channels.Refused.Should().Be(0);
        }

        [Fact]
        public void ApiRequestsHasCorrectValues()
        {
            _stats.ApiRequests.Succeeded.Should().Be(20);
            _stats.ApiRequests.Failed.Should().Be(0);
            _stats.ApiRequests.Refused.Should().Be(0);
        }

        [Fact]
        public void TokenRequestsHasCorrectValues()
        {
            _stats.TokenRequests.Succeeded.Should().Be(0);
            _stats.TokenRequests.Failed.Should().Be(0);
            _stats.TokenRequests.Refused.Should().Be(0);
        }

        [Fact]
        public void IntervalIdHasCorrectValue()
        {
            _stats.Interval.Should().Be(new DateTime(2014, 01, 01, 16, 24, 0));
        }
    }
}
