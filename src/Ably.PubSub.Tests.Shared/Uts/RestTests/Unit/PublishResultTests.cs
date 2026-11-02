// REST Channel Publish Result Tests (RSL1n, RSL1n1, PBR1, PBR2a)
// Derived from uts/rest/unit/channel/publish_result.md
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
    public class PublishResultTests : UtsUnitTestBase
    {
        public PublishResultTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL1n/publish-result-single-message-0
        [UtsFact]
        [Trait("spec", "RSL1n")]
        public async Task RSL1n_PublishResultSingleMessage()
        {
            // Setup
            var channelName = "test-RSL1n-single-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "serial-abc" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var result = await channel.PublishAsync("event", "hello");

            // Assertions
            result.Should().BeAssignableTo<PublishResult>();
            result.Serials.Should().BeAssignableTo<IReadOnlyList<string>>();
            result.Serials.Should().HaveCount(1);
            result.Serials[0].Should().Be("serial-abc");
        }

        // UTS: rest/unit/RSL1n/publish-result-batch-serials-1
        [UtsFact]
        [Trait("spec", "RSL1n")]
        public async Task RSL1n_PublishResultBatchSerials()
        {
            // Setup
            var channelName = "test-RSL1n-batch-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1", "s2", "s3" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var messages = new[]
            {
                new Message("event1", "data1"),
                new Message("event2", "data2"),
                new Message("event3", "data3"),
            };
            var result = await channel.PublishAsync(messages);

            // Assertions
            result.Should().BeAssignableTo<PublishResult>();
            result.Serials.Should().HaveCount(3);
            result.Serials[0].Should().Be("s1");
            result.Serials[1].Should().Be("s2");
            result.Serials[2].Should().Be("s3");
        }

        // UTS: rest/unit/RSL1n/publish-result-null-serial-2
        [UtsFact]
        [Trait("spec", "RSL1n")]
        public async Task RSL1n_PublishResultNullSerial()
        {
            // Setup
            var channelName = "test-RSL1n-null-" + Guid.NewGuid().ToString("N");

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { JValue.CreateNull(), "s2" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var messages = new[]
            {
                new Message("event1", "data1"),
                new Message("event2", "data2"),
            };
            var result = await channel.PublishAsync(messages);

            // Assertions
            result.Serials.Should().HaveCount(2);
            result.Serials[0].Should().BeNull();
            result.Serials[1].Should().Be("s2");
        }
    }
}
