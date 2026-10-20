// Idempotent Publishing Tests (RSL1k, RSL1k1, RSL1k2, RSL1k3, RSL1k4, RSL1k5)
// Derived from uts/rest/unit/channel/idempotency.md
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
    public class IdempotencyTests : UtsUnitTestBase
    {
        public IdempotencyTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL1k1/idempotent-default-true-0
        [Fact]
        [Trait("spec", "RSL1k1")]
        public void RSL1k1_IdempotentDefaultTrue()
        {
            // Test Cases
            // | ID | Library Version | Expected Default |
            // | 1 | >= 1.2 | true |
            // NOTE: single-row table (library version >= 1.2, expected default true): translated as one test

            // Test Steps
            var options = new ClientOptions { Key = "appId.keyId:keySecret" };
            var client = Rest(options);

            // Verify default value
            // NOTE: the client keeps the ClientOptions instance it is given, so the option is read from it (client.options in the spec)
            options.IdempotentRestPublishing.Should().BeTrue();
        }

        // UTS: rest/unit/RSL1k2/message-id-format-0
        [UtsFact]
        [Trait("spec", "RSL1k2")]
        public async Task RSL1k2_MessageIdFormat()
        {
            // Setup
            var channelName = "test-RSL1k2-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                IdempotentRestPublishing = true,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", "data");

            // Assertions
            var request = capturedRequests[0];
            var body = (JObject)JArray.Parse(request.BodyText)[0];

            body.Properties().Should().Contain(p => p.Name == "id");
            var messageId = (string)body["id"];

            // Format: <base64>:<serial>
            var parts = messageId.Split(':');
            parts.Should().HaveCount(2);

            // First part is base64-encoded (url-safe)
            // NOTE: "matches pattern" is read as a full-string match, so the pattern is anchored. RSL1k1/RSAN1c4 require "base64-encoding" (not url-safe); the SDK uses standard base64, so + / and = padding are allowed here - see deviations.md (UTS Spec Errors, RSL1k1)
            parts[0].Should().MatchRegex("^[A-Za-z0-9+/_-]+=*$");
            parts[0].Length.Should().BeGreaterThanOrEqualTo(12); // At least 9 bytes base64 encoded

            // Second part is a serial number (starting from 0)
            parts[1].Should().Be("0");
        }

        // UTS: rest/unit/RSL1k2/serial-increments-batch-1
        [UtsFact]
        [Trait("spec", "RSL1k2")]
        public async Task RSL1k2_SerialIncrementsBatch()
        {
            // Setup
            var channelName = "test-RSL1k2-batch-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1", "s2", "s3" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                IdempotentRestPublishing = true,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            var messages = new[]
            {
                new Message("event1", "data1"),
                new Message("event2", "data2"),
                new Message("event3", "data3"),
            };
            await channel.PublishAsync(messages);

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText);

            // All messages should share the same base but different serials
            var baseIds = new List<string>();
            var serials = new List<int>();

            for (var i = 0; i < body.Count; i++)
            {
                var parts = ((string)body[i]["id"]).Split(':');
                baseIds.Add(parts[0]);
                serials.Add(int.Parse(parts[1]));
            }

            // Same base for all messages in batch
            baseIds.Should().NotContain(b => b != baseIds[0]);

            // Sequential serials starting from 0
            serials.Should().Equal(0, 1, 2);
        }

        // UTS: rest/unit/RSL1k3/unique-base-ids-0
        [UtsFact]
        [Trait("spec", "RSL1k3")]
        public async Task RSL1k3_UniqueBaseIds()
        {
            // Setup
            var channelName = "test-RSL1k3-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                IdempotentRestPublishing = true,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event1", "data1");
            await channel.PublishAsync("event2", "data2");

            // Assertions
            var body1 = JArray.Parse(capturedRequests[0].BodyText)[0];
            var body2 = JArray.Parse(capturedRequests[1].BodyText)[0];

            var base1 = ((string)body1["id"]).Split(':')[0];
            var base2 = ((string)body2["id"]).Split(':')[0];

            // Different publish calls should have different base IDs
            base1.Should().NotBe(base2);
        }

        // UTS: rest/unit/RSL1k3/no-id-when-disabled-1
        [UtsFact]
        [Trait("spec", "RSL1k3")]
        public async Task RSL1k3_NoIdWhenDisabled()
        {
            // Setup
            var channelName = "test-RSL1k3-disabled-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                IdempotentRestPublishing = false,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", "data");

            // Assertions
            var request = capturedRequests[0];
            var body = (JObject)JArray.Parse(request.BodyText)[0];

            // No automatic ID should be added
            body.Properties().Should().NotContain(p => p.Name == "id");
        }

        // UTS: rest/unit/RSL1k/client-id-preserved-0
        [UtsFact]
        [Trait("spec", "RSL1k")]
        public async Task RSL1k_ClientIdPreserved()
        {
            // Setup
            var channelName = "test-RSL1k-preserved-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                IdempotentRestPublishing = true, // Even with this enabled
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync(new Message("event", "data") { Id = "my-custom-id" });

            // Assertions
            var request = capturedRequests[0];
            var body = JArray.Parse(request.BodyText)[0];

            // Client-supplied ID should be preserved exactly
            ((string)body["id"]).Should().Be("my-custom-id");
        }

        // UTS: rest/unit/RSL1k2/same-id-on-retry-2
        [UtsFact]
        [Trait("spec", "RSL1k2")]
        public async Task RSL1k2_SameIdOnRetry()
        {
            // Setup
            var channelName = "test-RSL1k2-retry-" + Guid.NewGuid().ToString("N");
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount = requestCount + 1;

                    // First request fails with retryable error
                    if (requestCount == 1)
                    {
                        req.RespondWith(500, new JObject { ["error"] = new JObject { ["code"] = 50000 } });
                    }
                    else
                    {
                        // Retry succeeds
                        req.RespondWith(201, new JObject { ["serials"] = new JArray { "s1" } });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                IdempotentRestPublishing = true,
            });
            var channel = client.Channels.Get(channelName);

            // Test Steps
            await channel.PublishAsync("event", "data");

            // Assertions
            requestCount.Should().Be(2);

            var body1 = JArray.Parse(capturedRequests[0].BodyText)[0];
            var body2 = JArray.Parse(capturedRequests[1].BodyText)[0];

            // Same ID should be used for retry
            ((string)body1["id"]).Should().Be((string)body2["id"]);
        }

        // UTS: rest/unit/RSL1k/mixed-ids-in-batch-1
        [UtsFact(Skip = "UTS spec error RSL1k3: the test expects a library-generated id for a message without one in a batch where others carry ids; see deviations.md (UTS Spec Errors)")]
        [Trait("spec", "RSL1k")]
        public Task RSL1k_MixedIdsInBatch()
        {
            // SPEC ERROR RSL1k3: RSL1k1 generates ids only when all messages have an empty id, and RSL1k3 says that if one or more messages in a batch carry an id then all message ids (present or absent) are preserved, so the message without an id is sent without one.
            // Fix the UTS spec first - see deviations.md (UTS Spec Errors).
            UtsAssert.Fail("UTS spec error RSL1k3 - fix the spec first; see deviations.md");
            return Task.CompletedTask;
        }
    }
}
