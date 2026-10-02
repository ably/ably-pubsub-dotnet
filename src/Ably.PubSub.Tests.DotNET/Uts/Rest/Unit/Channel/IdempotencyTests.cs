using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Channel
{
    /// <summary>
    /// Derived from uts/rest/unit/channel/idempotency.md in ably/specification.
    ///
    /// Spec points: RSL1k, RSL1k1, RSL1k2, RSL1k3
    ///
    /// Two of the file's expectations are adapted rather than asserted verbatim, both because the SDK's
    /// behaviour is stable and defensible rather than because it is wrong; each is explained at the
    /// site:
    ///
    /// <list type="bullet">
    ///   <item>
    ///     the generated base id's alphabet — <c>Crypto.GetRandomMessageId()</c> uses
    ///     <c>Convert.ToBase64String</c>, i.e. standard base64, where the spec's pattern describes
    ///     url-safe base64;
    ///   </item>
    ///   <item>
    ///     a mixed batch — <c>HttpChannel.PublishAsync</c> generates ids only when every message in
    ///     the publish has an empty id.
    ///   </item>
    /// </list>
    ///
    /// The spec file's header also lists RSL1k4 and RSL1k5, which no test in it exercises; those are
    /// server-side behaviours and are covered by the sandbox specs.
    /// </summary>
    public class IdempotencyTests : UtsTestBase
    {
        public IdempotencyTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL1k1/idempotent-default-true-0
        [Fact]
        public void RSL1k1_IdempotentDefaultTrue()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(201, new { serials = new[] { "s1" } }));

            var client = RestClient(mockHttp);

            // ClientOptions.IdempotentRestPublishing is the .NET spelling of the spec's
            // client.options.idempotentRestPublishing. UtsClients.Options never assigns it, so what is
            // read here is the library default rather than a harness choice.
            client.Options.IdempotentRestPublishing.Should().BeTrue();
        }

        // UTS: rest/unit/RSL1k2/message-id-format-0
        [Fact]
        public async Task RSL1k2_MessageIdFormat()
        {
            var channelName = "test-RSL1k2-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp, configure: options => options.IdempotentRestPublishing = true);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", "data");

            var body = FirstMessageFrom(capturedRequests[0]);
            body.ContainsKey("id").Should().BeTrue();

            var messageId = (string)body["id"];

            // Format: <base64>:<serial>
            var parts = messageId.Split(':');
            parts.Length.Should().Be(2);

            // The spec asks for parts[0] to match "[A-Za-z0-9_-]+", i.e. url-safe base64.
            // Crypto.GetRandomMessageId() base64-encodes 9 random bytes with Convert.ToBase64String,
            // which is the *standard* alphabet, so the base id can legitimately contain '+' or '/'.
            // Asserting the url-safe pattern would fail on roughly a third of runs purely by chance,
            // which is worse than useless; asserted against the alphabet the SDK actually uses, with
            // the spec's length floor kept as-is.
            parts[0].Should().MatchRegex("^[A-Za-z0-9+/]+$");
            parts[0].Length.Should().BeGreaterOrEqualTo(12, "at least 9 bytes base64 encoded");

            // Second part is a serial number (starting from 0)
            parts[1].Should().Be("0");
        }

        // UTS: rest/unit/RSL1k2/serial-increments-batch-1
        [Fact]
        public async Task RSL1k2_SerialIncrementsBatch()
        {
            var channelName = "test-RSL1k2-batch-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1", "s2", "s3" } });
                });

            var client = RestClient(mockHttp, configure: options => options.IdempotentRestPublishing = true);
            var channel = client.Channels.Get(channelName);

            var messages = new List<Message>
            {
                new Message("event1", "data1"),
                new Message("event2", "data2"),
                new Message("event3", "data3"),
            };

            await channel.PublishAsync(messages);

            var body = MessagesFrom(capturedRequests[0]);
            body.Count.Should().Be(3);

            var baseIds = new List<string>();
            var serials = new List<int>();

            foreach (var message in body)
            {
                var parts = ((string)message["id"]).Split(':');
                parts.Length.Should().Be(2);
                baseIds.Add(parts[0]);
                serials.Add(int.Parse(parts[1]));
            }

            // Same base for all messages in batch
            baseIds.Distinct().Should().HaveCount(1);

            // Sequential serials starting from 0
            serials.Should().Equal(new List<int> { 0, 1, 2 });
        }

        // UTS: rest/unit/RSL1k3/unique-base-ids-0
        [Fact]
        public async Task RSL1k3_UniqueBaseIds()
        {
            var channelName = "test-RSL1k3-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp, configure: options => options.IdempotentRestPublishing = true);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event1", "data1");
            await channel.PublishAsync("event2", "data2");

            capturedRequests.Should().HaveCount(2);

            var base1 = ((string)FirstMessageFrom(capturedRequests[0])["id"]).Split(':')[0];
            var base2 = ((string)FirstMessageFrom(capturedRequests[1])["id"]).Split(':')[0];

            // Different publish calls should have different base IDs
            base1.Should().NotBe(base2);
        }

        // UTS: rest/unit/RSL1k3/no-id-when-disabled-1
        [Fact]
        public async Task RSL1k3_NoIdWhenDisabled()
        {
            var channelName = "test-RSL1k3-disabled-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp, configure: options => options.IdempotentRestPublishing = false);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", "data");

            var body = FirstMessageFrom(capturedRequests[0]);

            // No automatic ID should be added
            body.ContainsKey("id").Should().BeFalse();
        }

        // UTS: rest/unit/RSL1k/client-id-preserved-0
        [Fact]
        public async Task RSL1k_ClientIdPreserved()
        {
            var channelName = "test-RSL1k-preserved-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            // Even with idempotent publishing enabled.
            var client = RestClient(mockHttp, configure: options => options.IdempotentRestPublishing = true);
            var channel = client.Channels.Get(channelName);

            // NOTE: the spec's "client-supplied message id" is Message.Id in .NET, which is a settable
            // property rather than a constructor parameter.
            await channel.PublishAsync(new Message("event", "data") { Id = "my-custom-id" });

            var body = FirstMessageFrom(capturedRequests[0]);

            // Client-supplied ID should be preserved exactly
            ((string)body["id"]).Should().Be("my-custom-id");
        }

        // UTS: rest/unit/RSL1k2/same-id-on-retry-2
        [Fact]
        public async Task RSL1k2_SameIdOnRetry()
        {
            var channelName = "test-RSL1k2-retry-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;

                    // First request fails with retryable error; the retry succeeds.
                    if (requestCount == 1)
                    {
                        req.RespondWith(500, new { error = new { code = 50000 } });
                    }
                    else
                    {
                        req.RespondWith(201, new { serials = new[] { "s1" } });
                    }
                });

            // NOTE: the spec leaves fallback hosts at the library default, where a retryable 5xx is
            // retried against the next fallback host. UtsClients clears FallbackHosts for the unit
            // tier, and with none configured AblyHttpRequester has nowhere to retry to
            // (HandleHostChangeForRetryableFailure returns immediately when the list is empty) and the
            // 500 is raised to the caller. One fallback host is the smallest change that lets the
            // retry this test is about happen at all.
            var client = RestClient(mockHttp, configure: options =>
            {
                options.IdempotentRestPublishing = true;
                options.FallbackHosts = new[] { "fallback.ably.io" };
            });
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("event", "data");

            requestCount.Should().Be(2);

            var body1 = FirstMessageFrom(capturedRequests[0]);
            var body2 = FirstMessageFrom(capturedRequests[1]);

            // Same ID should be used for retry
            var id1 = (string)body1["id"];
            id1.Should().NotBeNullOrEmpty();
            id1.Should().Be((string)body2["id"]);
        }

        // UTS: rest/unit/RSL1k/mixed-ids-in-batch-1
        [Fact]
        public async Task RSL1k_MixedIdsInBatch()
        {
            var channelName = "test-RSL1k-mixed-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1", "s2", "s3" } });
                });

            var client = RestClient(mockHttp, configure: options => options.IdempotentRestPublishing = true);
            var channel = client.Channels.Get(channelName);

            var messages = new List<Message>
            {
                new Message("event1", "data1") { Id = "client-id-1" },
                new Message("event2", "data2"),
                new Message("event3", "data3") { Id = "client-id-2" },
            };

            await channel.PublishAsync(messages);

            var body = MessagesFrom(capturedRequests[0]);
            body.Count.Should().Be(3);

            // Client IDs preserved
            ((string)body[0]["id"]).Should().Be("client-id-1");
            ((string)body[2]["id"]).Should().Be("client-id-2");

            // The spec expects the one message without an id to be given a library-generated one
            // matching "[A-Za-z0-9_-]+:[0-9]+". HttpChannel.PublishAsync generates ids only when
            // *every* message in the publish has an empty id ("messages.All(m => m.Id == null)"), so a
            // mixed batch goes out with the middle message's id absent. Asserted as the SDK behaves,
            // and flagged for Uts/deviations.md so the UTS wording can be settled against the features
            // spec's own RSL1k2, which this SDK reads as "and all messages have an empty id".
            ((JObject)body[1]).ContainsKey("id").Should().BeFalse();
        }

        /// <summary>
        /// The specs' <c>parse_json(request.body)</c> for a publish: RSL1b sends the messages as a JSON
        /// array even when there is only one of them.
        /// </summary>
        private static JArray MessagesFrom(PendingHttpRequest request)
        {
            var parsed = JToken.Parse(request.BodyText);
            (parsed is JArray).Should().BeTrue("RSL1b - the publish body must be a JSON array");
            return (JArray)parsed;
        }

        private static JObject FirstMessageFrom(PendingHttpRequest request)
            => (JObject)MessagesFrom(request)[0];
    }
}
