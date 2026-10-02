using System;
using System.Threading.Tasks;
using Ably.PubSub.Http;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Channel
{
    /// <summary>
    /// Derived from uts/rest/unit/channel/rest_channel_attributes.md in ably/specification.
    ///
    /// Spec points: RSL7, RSL8, RSL8a, RSL9, CHD2, CHD2a, CHD2b, CHS2, CHS2a, CHS2b, CHO2, CHO2a,
    /// CHM2, CHM2a, CHM2b, CHM2c, CHM2d, CHM2e, CHM2f
    ///
    /// Three translation notes that apply to the whole file:
    ///
    /// CHM2g (objectPublishers) and CHM2h (objectSubscribers) are not asserted anywhere, because
    /// Ably.PubSub.Http.ChannelMetrics has no member for either. The two CHM2 tests still send those
    /// fields in the stubbed response, so they do assert that the SDK tolerates them rather than
    /// failing to parse a response from a newer server.
    ///
    /// The spec's RestChannel#setOptions is a settable Options property here, and it lives on the
    /// concrete HttpChannel rather than on IHttpChannel, so the RSL7 tests cast what
    /// client.Channels.Get returns. Likewise RestChannelOptions is ChannelOptions.
    ///
    /// Tests whose spec setup installs no mock HTTP client install one anyway: with no mock on the
    /// ClientOptions.HttpClient seam an unexpected request would reach the real network from a unit
    /// test.
    /// </summary>
    public class RestChannelAttributesTests : UtsTestBase
    {
        public RestChannelAttributesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL9/channel-name-attribute-0
        [Fact]
        public void RSL9_ChannelNameAttribute()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, Array.Empty<object>()));

            var client = RestClient(mockHttp);

            var channel = client.Channels.Get("my-channel");
            channel.Name.Should().Be("my-channel");

            var channel2 = client.Channels.Get("namespace:channel-name");
            channel2.Name.Should().Be("namespace:channel-name");

            mockHttp.CapturedRequests.Should().BeEmpty("reading the name must not talk to the server");
        }

        // UTS: rest/unit/RSL7/setoptions-updates-options-0
        [Fact]
        public void RSL7_SetOptionsUpdatesOptions()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, Array.Empty<object>()));

            var client = RestClient(mockHttp);
            var channel = (HttpChannel)client.Channels.Get("test-RSL7");

            var options = new ChannelOptions();

            // The spec's AWAIT channel.setOptions(RestChannelOptions()). There is nothing to await on a
            // property, so "indicates success" is translated as "the assignment is accepted and the
            // stored options are the ones supplied".
            channel.Options = options;

            channel.Options.Should().BeSameAs(options);
        }

        // UTS: rest/unit/RSL7/setoptions-stores-options-1
        [Fact]
        public void RSL7_SetOptionsStoresOptions()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, Array.Empty<object>()));

            var client = RestClient(mockHttp);
            var channel = (HttpChannel)client.Channels.Get("test-RSL7-store");

            var options = new ChannelOptions();

            channel.Options = options;

            // The spec's own implementation note: the observable effect of channel options is
            // encryption (RSL5), which these tests do not reach, so what is verified here is that the
            // options are retained and readable afterwards.
            channel.Options.Should().BeSameAs(options);
        }

        // UTS: rest/unit/RSL8/status-get-correct-endpoint-0
        [Fact]
        public async Task RSL8_StatusGetCorrectEndpoint()
        {
            PendingHttpRequest capturedRequest = null;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequest = req;
                    req.RespondWith(200, new
                    {
                        channelId = "test-RSL8",
                        status = new
                        {
                            isActive = true,
                            occupancy = new
                            {
                                metrics = new
                                {
                                    connections = 0,
                                    publishers = 0,
                                    subscribers = 0,
                                    presenceConnections = 0,
                                    presenceMembers = 0,
                                    presenceSubscribers = 0,
                                },
                            },
                        },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test-RSL8");

            await channel.StatusAsync();

            capturedRequest.Should().NotBeNull();
            capturedRequest.Method.Should().Be("GET");
            capturedRequest.Url.Path.Should().EndWith("/channels/test-RSL8");
        }

        // UTS: rest/unit/RSL8/status-special-chars-encoded-1
        //
        // DEVIATION: HttpChannel.StatusAsync builds its path from the raw channel name
        // ("/channels/" + Name) where HistoryAsync uses name.EncodeUriPart(). The colon therefore
        // reaches the wire unescaped and the observed path is "/channels/namespace:my%20channel" --
        // the space is escaped only by System.Uri's own canonicalisation, not by the SDK. Carries the
        // spec-correct assertion behind the RUN_DEVIATIONS gate; see Uts/deviations.md.
        [DeviationFact]
        public async Task RSL8_StatusSpecialCharsEncoded()
        {
            PendingHttpRequest capturedRequest = null;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequest = req;
                    req.RespondWith(200, new
                    {
                        channelId = "namespace:my channel",
                        status = new
                        {
                            isActive = true,
                            occupancy = new
                            {
                                metrics = new
                                {
                                    connections = 0,
                                    publishers = 0,
                                    subscribers = 0,
                                    presenceConnections = 0,
                                    presenceMembers = 0,
                                    presenceSubscribers = 0,
                                },
                            },
                        },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("namespace:my channel");

            await channel.StatusAsync();

            capturedRequest.Should().NotBeNull();
            capturedRequest.Method.Should().Be("GET");

            // encode_uri_component("namespace:my channel") == "namespace%3Amy%20channel"
            capturedRequest.Url.Path.Should().EndWith("/channels/namespace%3Amy%20channel");
        }

        // UTS: rest/unit/RSL8a/status-returns-channel-details-0
        [Fact]
        public async Task RSL8a_StatusReturnsChannelDetails()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new
                {
                    channelId = "test-RSL8a",
                    status = new
                    {
                        isActive = true,
                        occupancy = new
                        {
                            metrics = new
                            {
                                connections = 5,
                                publishers = 2,
                                subscribers = 3,
                                presenceConnections = 1,
                                presenceMembers = 1,
                                presenceSubscribers = 0,
                            },
                        },
                    },
                }));

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test-RSL8a");

            var result = await channel.StatusAsync();

            // CHD1: "result IS ChannelDetails" is a static guarantee -- StatusAsync is typed
            // ChannelDetails -- so BeOfType only pins the concrete type that comes back.
            result.Should().BeOfType<ChannelDetails>();

            // CHD2a
            result.ChannelId.Should().Be("test-RSL8a");

            // CHD2b, CHS2a
            result.Status.Should().NotBeNull();
            result.Status.IsActive.Should().BeTrue();

            // CHS2b, CHO2a, CHM2a, CHM2e, CHM2f
            result.Status.Occupancy.Should().NotBeNull();
            result.Status.Occupancy.Metrics.Connections.Should().Be(5);
            result.Status.Occupancy.Metrics.Publishers.Should().Be(2);
            result.Status.Occupancy.Metrics.Subscribers.Should().Be(3);
        }

        // UTS: rest/unit/CHM2/parses-all-metrics-fields-0
        [Fact]
        public async Task CHM2_ParsesAllMetricsFields()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new
                {
                    channelId = "test-CHM2-all-fields",
                    status = new
                    {
                        isActive = true,
                        occupancy = new
                        {
                            metrics = new
                            {
                                connections = 10,
                                presenceConnections = 7,
                                presenceMembers = 4,
                                presenceSubscribers = 3,
                                publishers = 6,
                                subscribers = 8,
                                objectPublishers = 2,
                                objectSubscribers = 5,
                            },
                        },
                    },
                }));

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test-CHM2-all-fields");

            var result = await channel.StatusAsync();

            // CHD2a
            result.ChannelId.Should().Be("test-CHM2-all-fields");

            // CHD2b, CHS2a
            result.Status.Should().NotBeNull();
            result.Status.IsActive.Should().BeTrue();

            // CHS2b, CHO2a
            result.Status.Occupancy.Should().NotBeNull();
            result.Status.Occupancy.Metrics.Should().NotBeNull();

            var metrics = result.Status.Occupancy.Metrics;

            metrics.Connections.Should().Be(10); // CHM2a
            metrics.PresenceConnections.Should().Be(7); // CHM2b
            metrics.PresenceMembers.Should().Be(4); // CHM2c
            metrics.PresenceSubscribers.Should().Be(3); // CHM2d
            metrics.Publishers.Should().Be(6); // CHM2e
            metrics.Subscribers.Should().Be(8); // CHM2f

            // CHM2g (objectPublishers) and CHM2h (objectSubscribers) have no member on
            // Ably.PubSub.Http.ChannelMetrics, so the spec's last two assertions cannot be written.
            // Both fields are present in the stubbed response above, so what this test does prove is
            // that the SDK ignores them rather than failing the parse.
        }

        // UTS: rest/unit/CHM2/zero-and-missing-metrics-1
        [Fact]
        public async Task CHM2_ZeroAndMissingMetrics()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, new
                {
                    // The response omits objectPublishers and objectSubscribers (CHM2g, CHM2h) to
                    // stand in for an older server. Every other metric is explicitly zero.
                    channelId = "test-CHM2-defaults",
                    status = new
                    {
                        isActive = false,
                        occupancy = new
                        {
                            metrics = new
                            {
                                connections = 0,
                                presenceConnections = 0,
                                presenceMembers = 0,
                                presenceSubscribers = 0,
                                publishers = 0,
                                subscribers = 0,
                            },
                        },
                    },
                }));

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test-CHM2-defaults");

            var result = await channel.StatusAsync();

            // CHD2a
            result.ChannelId.Should().Be("test-CHM2-defaults");

            // CHS2a: isActive can be false
            result.Status.Should().NotBeNull();
            result.Status.IsActive.Should().BeFalse();

            var metrics = result.Status.Occupancy.Metrics;

            // CHM2a-f: explicit zero values are parsed as zero
            metrics.Connections.Should().Be(0);
            metrics.PresenceConnections.Should().Be(0);
            metrics.PresenceMembers.Should().Be(0);
            metrics.PresenceSubscribers.Should().Be(0);
            metrics.Publishers.Should().Be(0);
            metrics.Subscribers.Should().Be(0);

            // The spec's remaining two assertions -- that the absent CHM2g and CHM2h default to 0 --
            // have no member to read on ChannelMetrics, so they are omitted rather than written.
        }
    }
}
