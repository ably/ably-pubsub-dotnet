// REST Channel Attributes and Methods (RSL7, RSL8, RSL8a, RSL9, CHD2, CHD2a, CHD2b, CHS2, CHS2a, CHS2b, CHO2, CHO2a, CHM2, CHM2a, CHM2b, CHM2c, CHM2d, CHM2e, CHM2f, CHM2g, CHM2h)
// Derived from uts/rest/unit/channel/rest_channel_attributes.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Http;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    [Trait("uts", "unit")]
    public class RestChannelAttributesTests : UtsUnitTestBase
    {
        public RestChannelAttributesTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL9/channel-name-attribute-0
        [Fact]
        [Trait("spec", "RSL9")]
        public void RSL9_ChannelNameAttribute()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            // Assertions
            var channel = client.Channels.Get("my-channel");
            channel.Name.Should().Be("my-channel");

            // Also works with special characters
            var channel2 = client.Channels.Get("namespace:channel-name");
            channel2.Name.Should().Be("namespace:channel-name");
        }

        // UTS: rest/unit/RSL7/setoptions-updates-options-0
        [Fact]
        [Trait("spec", "RSL7")]
        public void RSL7_SetoptionsUpdatesOptions()
        {
            // Setup
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            var channel = client.Channels.Get("test-RSL7");

            // Test Steps
            // NOTE: setOptions is the Options property setter of HttpChannel (not on IHttpChannel); ChannelOptions is the SDK's RestChannelOptions
            ((HttpChannel)channel).Options = new ChannelOptions();

            // Assertions
            // setOptions completes without error (indicates success)
            // No exception thrown
        }

        // UTS: rest/unit/RSL7/setoptions-stores-options-1
        [Fact]
        [Trait("spec", "RSL7")]
        public void RSL7_SetoptionsStoresOptions()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JArray());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            var channel = client.Channels.Get("test-RSL7-store");

            // Test Steps
            // Set options — the effect of channel options is primarily on encryption
            // (RSL5) which is not yet implemented. For now, verify the call succeeds
            // and options are stored by observing they can be set without error.
            // NOTE: setOptions is the Options property setter of HttpChannel (not on IHttpChannel); ChannelOptions is the SDK's RestChannelOptions
            ((HttpChannel)channel).Options = new ChannelOptions();

            // Assertions
            // setOptions completes without error
            // Implementation note: once encryption is supported (RSL5), this test
            // should verify that cipher params set via setOptions are applied to
            // subsequent publish/history operations.
        }

        // UTS: rest/unit/RSL8/status-get-correct-endpoint-0
        [UtsFact]
        [Trait("spec", "RSL8")]
        public async Task RSL8_StatusGetCorrectEndpoint()
        {
            // Setup
            PendingHttpRequest capturedRequest = null;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequest = req;
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test-RSL8",
                        ["status"] = new JObject
                        {
                            ["isActive"] = true,
                            ["occupancy"] = new JObject
                            {
                                ["metrics"] = new JObject
                                {
                                    ["connections"] = 0,
                                    ["publishers"] = 0,
                                    ["subscribers"] = 0,
                                    ["presenceConnections"] = 0,
                                    ["presenceMembers"] = 0,
                                    ["presenceSubscribers"] = 0,
                                },
                            },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            var channel = client.Channels.Get("test-RSL8");

            // Test Steps
            var result = await channel.StatusAsync();

            // Assertions
            // Correct HTTP method and path
            capturedRequest.Should().NotBeNull();
            capturedRequest.Method.Should().Be("GET");
            capturedRequest.Path.Should().EndWith("/channels/test-RSL8");
        }

        // UTS: rest/unit/RSL8/status-special-chars-encoded-1
        // DEVIATION: see deviations.md
        [DeviationFact("RSL8", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSL8")]
        public async Task RSL8_StatusSpecialCharsEncoded()
        {
            // Setup
            PendingHttpRequest capturedRequest = null;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequest = req;
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "namespace:my channel",
                        ["status"] = new JObject
                        {
                            ["isActive"] = true,
                            ["occupancy"] = new JObject
                            {
                                ["metrics"] = new JObject
                                {
                                    ["connections"] = 0,
                                    ["publishers"] = 0,
                                    ["subscribers"] = 0,
                                    ["presenceConnections"] = 0,
                                    ["presenceMembers"] = 0,
                                    ["presenceSubscribers"] = 0,
                                },
                            },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            var channel = client.Channels.Get("namespace:my channel");

            // Test Steps
            var result = await channel.StatusAsync();

            // Assertions
            capturedRequest.Should().NotBeNull();
            capturedRequest.Method.Should().Be("GET");

            // Channel name must be URI-encoded in the path
            capturedRequest.Path.Should().EndWith("/channels/" + Uri.EscapeDataString("namespace:my channel"));
        }

        // UTS: rest/unit/RSL8a/status-returns-channel-details-0
        [UtsFact]
        [Trait("spec", "RSL8a")]
        public async Task RSL8a_StatusReturnsChannelDetails()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test-RSL8a",
                        ["status"] = new JObject
                        {
                            ["isActive"] = true,
                            ["occupancy"] = new JObject
                            {
                                ["metrics"] = new JObject
                                {
                                    ["connections"] = 5,
                                    ["publishers"] = 2,
                                    ["subscribers"] = 3,
                                    ["presenceConnections"] = 1,
                                    ["presenceMembers"] = 1,
                                    ["presenceSubscribers"] = 0,
                                },
                            },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            var channel = client.Channels.Get("test-RSL8a");

            // Test Steps
            var result = await channel.StatusAsync();

            // Assertions
            // Result is a ChannelDetails object (CHD1)
            result.Should().BeAssignableTo<ChannelDetails>();

            // CHD2a: channelId attribute
            result.ChannelId.Should().Be("test-RSL8a");

            // CHD2b: status attribute is a ChannelStatus (CHS1)
            result.Status.Should().NotBeNull();
            result.Status.IsActive.Should().BeTrue();

            // CHS2b: occupancy metrics
            result.Status.Occupancy.Should().NotBeNull();
            result.Status.Occupancy.Metrics.Connections.Should().Be(5);
            result.Status.Occupancy.Metrics.Publishers.Should().Be(2);
            result.Status.Occupancy.Metrics.Subscribers.Should().Be(3);
        }

        // UTS: rest/unit/CHM2/parses-all-metrics-fields-0
        [UtsFact]
        [Trait("spec", "CHM2")]
        public async Task CHM2_ParsesAllMetricsFields()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test-CHM2-all-fields",
                        ["status"] = new JObject
                        {
                            ["isActive"] = true,
                            ["occupancy"] = new JObject
                            {
                                ["metrics"] = new JObject
                                {
                                    ["connections"] = 10,
                                    ["presenceConnections"] = 7,
                                    ["presenceMembers"] = 4,
                                    ["presenceSubscribers"] = 3,
                                    ["publishers"] = 6,
                                    ["subscribers"] = 8,
                                    ["objectPublishers"] = 2,
                                    ["objectSubscribers"] = 5,
                                },
                            },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            var channel = client.Channels.Get("test-CHM2-all-fields");

            // Test Steps
            var result = await channel.StatusAsync();

            // Assertions
            // CHD2a: channelId
            result.ChannelId.Should().Be("test-CHM2-all-fields");

            // CHD2b + CHS2a: status.isActive
            result.Status.Should().NotBeNull();
            result.Status.IsActive.Should().BeTrue();

            // CHS2b + CHO2a: occupancy.metrics
            result.Status.Occupancy.Should().NotBeNull();
            result.Status.Occupancy.Metrics.Should().NotBeNull();

            var metrics = result.Status.Occupancy.Metrics;

            // CHM2a: connections
            metrics.Connections.Should().Be(10);

            // CHM2b: presenceConnections
            metrics.PresenceConnections.Should().Be(7);

            // CHM2c: presenceMembers
            metrics.PresenceMembers.Should().Be(4);

            // CHM2d: presenceSubscribers
            metrics.PresenceSubscribers.Should().Be(3);

            // CHM2e: publishers
            metrics.Publishers.Should().Be(6);

            // CHM2f: subscribers
            metrics.Subscribers.Should().Be(8);

            // CHM2g: objectPublishers
            // Missing SDK public API: ChannelMetrics#objectPublishers (CHM2g); see deviations.md (Missing SDK Public API / Features)
            // ASSERT metrics.objectPublishers == 2

            // CHM2h: objectSubscribers
            // Missing SDK public API: ChannelMetrics#objectSubscribers (CHM2h); see deviations.md (Missing SDK Public API / Features)
            // ASSERT metrics.objectSubscribers == 5
        }

        // UTS: rest/unit/CHM2/zero-and-missing-metrics-1
        [UtsFact]
        [Trait("spec", "CHM2")]
        public async Task CHM2_ZeroAndMissingMetrics()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    // Response omits objectPublishers and objectSubscribers (CHM2g, CHM2h)
                    // to simulate an older server that does not include these fields.
                    // All other metrics are explicitly zero.
                    req.RespondWith(200, new JObject
                    {
                        ["channelId"] = "test-CHM2-defaults",
                        ["status"] = new JObject
                        {
                            ["isActive"] = false,
                            ["occupancy"] = new JObject
                            {
                                ["metrics"] = new JObject
                                {
                                    ["connections"] = 0,
                                    ["presenceConnections"] = 0,
                                    ["presenceMembers"] = 0,
                                    ["presenceSubscribers"] = 0,
                                    ["publishers"] = 0,
                                    ["subscribers"] = 0,
                                },
                            },
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
            });

            var channel = client.Channels.Get("test-CHM2-defaults");

            // Test Steps
            var result = await channel.StatusAsync();

            // Assertions
            // CHD2a: channelId
            result.ChannelId.Should().Be("test-CHM2-defaults");

            // CHS2a: isActive can be false
            result.Status.IsActive.Should().BeFalse();

            var metrics = result.Status.Occupancy.Metrics;

            // CHM2a-f: explicit zero values are parsed correctly
            metrics.Connections.Should().Be(0);
            metrics.PresenceConnections.Should().Be(0);
            metrics.PresenceMembers.Should().Be(0);
            metrics.PresenceSubscribers.Should().Be(0);
            metrics.Publishers.Should().Be(0);
            metrics.Subscribers.Should().Be(0);

            // CHM2g-h: missing fields default to 0
            // Missing SDK public API: ChannelMetrics#objectPublishers (CHM2g); see deviations.md (Missing SDK Public API / Features)
            // ASSERT metrics.objectPublishers == 0

            // Missing SDK public API: ChannelMetrics#objectSubscribers (CHM2h); see deviations.md (Missing SDK Public API / Features)
            // ASSERT metrics.objectSubscribers == 0
        }
    }
}
