using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Channel
{
    /// <summary>
    /// Derived from uts/rest/unit/channel/publish.md in ably/specification.
    ///
    /// Spec points: RSL1a, RSL1b, RSL1c, RSL1e, RSL1h, RSL1j, RSL1m1, RSL1m2, RSL1m3
    ///
    /// Two of the file's tests are not translated, because the API each needs is absent rather than
    /// differently spelled, and in C# that is a compile error rather than a failing assertion:
    ///
    /// <list type="bullet">
    ///   <item>
    ///     rest/unit/RSL1i/message-size-limit-0 — there is no <c>ClientOptions.MaxMessageSize</c>.
    ///     The only <c>MaxMessageSize</c> in the SDK is <c>ConnectionDetails.MaxMessageSize</c>, which
    ///     the server sends in CONNECTED and which no REST publish consults, so there is no
    ///     client-side 40009 rejection to provoke.
    ///   </item>
    ///   <item>
    ///     rest/unit/RSL1l/params-as-querystring-0 — no publish overload takes params.
    ///     <c>IHttpChannel</c> offers only <c>PublishAsync(name, data, clientId)</c>,
    ///     <c>PublishAsync(Message)</c> and <c>PublishAsync(IEnumerable&lt;Message&gt;)</c>; none of
    ///     them can carry a query string onto the POST.
    ///   </item>
    /// </list>
    ///
    /// Every test below leaves <c>IdempotentRestPublishing</c> at its default, as the spec's setup
    /// blocks do. That default is <c>true</c> (RSL1k1), so the library adds an <c>id</c> to each
    /// transmitted message; where a spec asserts on the whole body, the note at the site says how that
    /// generated field was accounted for.
    /// </summary>
    public class PublishTests : UtsTestBase
    {
        public PublishTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSL1a/publish-name-and-data-0
        [Fact]
        public async Task RSL1a_PublishNameAndData()
        {
            var channelName = "test-RSL1a-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "serial1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            await channel.PublishAsync("greeting", "hello");

            capturedRequests.Should().HaveCount(1);
            var request = capturedRequests[0];

            // RSL1b - single message published
            request.Method.Should().Be("POST");
            request.Path.Should().Be("/channels/" + Uri.EscapeDataString(channelName) + "/messages");

            var body = MessagesFrom(request);
            body.Count.Should().Be(1);
            ((string)body[0]["name"]).Should().Be("greeting");
            ((string)body[0]["data"]).Should().Be("hello");
        }

        // UTS: rest/unit/RSL1a/publish-message-array-1
        [Fact]
        public async Task RSL1a_PublishMessageArray()
        {
            var channelName = "test-RSL1c-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;
                    req.RespondWith(201, new { serials = new[] { "s1", "s2", "s3" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var messages = new List<Message>
            {
                new Message("event1", "data1"),
                new Message("event2", JObject.Parse("{\"key\":\"value\"}")),
                new Message("event3", new byte[] { 0x01, 0x02, 0x03 }),
            };

            await channel.PublishAsync(messages);

            // RSL1c - single request for array
            requestCount.Should().Be(1);

            var body = MessagesFrom(capturedRequests[0]);
            body.Count.Should().Be(3);

            ((string)body[0]["name"]).Should().Be("event1");
            ((string)body[0]["data"]).Should().Be("data1");
            ((string)body[1]["name"]).Should().Be("event2");

            // The spec asserts body[1]["data"] == { "key": "value" }, i.e. a nested JSON object. This
            // SDK follows RSL4 instead: a payload that is neither a string nor binary is run through
            // JsonEncoder, which stringifies it and records encoding "json", so the wire carries the
            // object as a JSON *string*. Asserted as the SDK sends it, with the object recovered by
            // re-parsing so the spec's intent is still what is checked.
            ((string)body[1]["encoding"]).Should().Be("json");
            var nestedData = JObject.Parse((string)body[1]["data"]);
            ((string)nestedData["key"]).Should().Be("value");

            // Note: binary data encoding tested separately in encoding tests.
        }

        // UTS: rest/unit/RSL1e/null-name-and-data-0
        [Fact]
        public async Task RSL1e_NullNameAndData()
        {
            var channelName = "test-RSL1e-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var testCases = new[]
            {
                new { Name = (string)null, Data = (object)"hello", Expected = "{\"data\":\"hello\"}" },
                new { Name = "event", Data = (object)null, Expected = "{\"name\":\"event\"}" },
                new { Name = (string)null, Data = (object)null, Expected = "{}" },
            };

            foreach (var testCase in testCases)
            {
                capturedRequests.Clear();

                await channel.PublishAsync(testCase.Name, testCase.Data);

                var body = MessagesFrom(capturedRequests[0]);
                body.Count.Should().Be(1);
                var message = (JObject)body[0];

                if (testCase.Name == null)
                {
                    message.ContainsKey("name").Should().BeFalse(
                        "a null name must be omitted, not sent as JSON null");
                }
                else
                {
                    ((string)message["name"]).Should().Be(testCase.Name);
                }

                if (testCase.Data == null)
                {
                    message.ContainsKey("data").Should().BeFalse(
                        "null data must be omitted, not sent as JSON null");
                }
                else
                {
                    ((string)message["data"]).Should().Be((string)testCase.Data);
                }

                // The spec's `body == [expected_body]` is written as though nothing else were present,
                // but its own RSL1k1 makes idempotentRestPublishing default to true, so the library
                // adds an "id". Dropping that one generated field is what makes the whole-body
                // equality the spec asks for checkable; every other field must still match exactly.
                message.ContainsKey("id").Should().BeTrue();
                message.Remove("id");
                message.ToString(Newtonsoft.Json.Formatting.None).Should().Be(testCase.Expected);
            }
        }

        // UTS: rest/unit/RSL1h/publish-signature-0
        [Fact]
        public async Task RSL1h_PublishSignature()
        {
            var channelName = "test-RSL1h-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            // The spec's "verify that extra positional args are rejected at compile time" half is
            // satisfied statically in C#: IHttpChannel declares PublishAsync(string, object, string)
            // and nothing wider, so a fourth argument would not compile. What remains to assert at
            // runtime is that the two-argument form publishes the one message it was given.
            await channel.PublishAsync("event", "payload");

            requestCount.Should().Be(1);
            var body = MessagesFrom(capturedRequests[0]);
            ((string)body[0]["name"]).Should().Be("event");
            ((string)body[0]["data"]).Should().Be("payload");
        }

        // UTS: rest/unit/RSL1j/all-attributes-transmitted-0
        [Fact]
        public async Task RSL1j_AllAttributesTransmitted()
        {
            var channelName = "test-RSL1j-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get(channelName);

            var extras = new MessageExtras(
                JObject.Parse("{\"push\":{\"notification\":{\"title\":\"Test\"}}}"));

            // RSL1m tests cover whether the clientId should be sent; here it is only one more
            // attribute that has to survive to the wire.
            var message = new Message("test-event", "test-data", "explicit-client-id", extras)
            {
                Id = "custom-message-id",
            };

            await channel.PublishAsync(message);

            var body = FirstMessageFrom(capturedRequests[0]);

            ((string)body["name"]).Should().Be("test-event");
            ((string)body["data"]).Should().Be("test-data");
            ((string)body["id"]).Should().Be("custom-message-id");
            ((string)body["extras"]["push"]["notification"]["title"]).Should().Be("Test");
        }

        // UTS: rest/unit/RSL1m/clientid-not-injected-0
        [Fact]
        public async Task RSL1m1_ClientIdNotInjected()
        {
            // NOTE: the spec file's Setup block configures clientId "library-client-id" while its Test
            // Steps block configures "lib-client". The Test Steps block is the operative one, so that
            // is the value used here and in RSL1m2.
            var channelName = "test-RSL1m1-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var clientWithId = RestClient(mockHttp, configure: options => options.ClientId = "lib-client");

            await clientWithId.Channels.Get(channelName).PublishAsync("e", "d");

            var body = FirstMessageFrom(capturedRequests[0]);
            body.ContainsKey("clientId").Should().BeFalse(
                "the library must not inject its own clientId");
        }

        // UTS: rest/unit/RSL1m/clientid-not-injected-0
        [Fact]
        public async Task RSL1m2_ClientIdNotInjected_ExplicitMatchesLibrary()
        {
            var channelName = "test-RSL1m2-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var clientWithId = RestClient(mockHttp, configure: options => options.ClientId = "lib-client");

            await clientWithId.Channels.Get(channelName)
                .PublishAsync(new Message("e", "d", "lib-client"));

            var body = FirstMessageFrom(capturedRequests[0]);
            ((string)body["clientId"]).Should().Be("lib-client", "an explicit clientId is preserved");
        }

        // UTS: rest/unit/RSL1m/clientid-not-injected-0
        [Fact]
        public async Task RSL1m3_ClientIdNotInjected_UnidentifiedClient()
        {
            var channelName = "test-RSL1m3-" + UtsSandbox.RandomId();
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(201, new { serials = new[] { "s1" } });
                });

            var clientNoId = RestClient(mockHttp);

            await clientNoId.Channels.Get(channelName)
                .PublishAsync(new Message("e", "d", "msg-client"));

            var body = FirstMessageFrom(capturedRequests[0]);
            ((string)body["clientId"]).Should().Be("msg-client");
        }

        /// <summary>
        /// The specs' <c>parse_json(request.body)</c> for a publish, plus its
        /// <c>ASSERT body IS List</c>: RSL1b requires the messages always to be a JSON array, even
        /// when there is only one of them.
        /// </summary>
        private static JArray MessagesFrom(PendingHttpRequest request)
        {
            var parsed = JToken.Parse(request.BodyText);

            // Not asserted through FluentAssertions' `because` with the body interpolated into it: the
            // body is JSON, and braces in a `because` string are placeholders there.
            (parsed is JArray).Should().BeTrue("RSL1b - the publish body must be a JSON array");
            return (JArray)parsed;
        }

        private static JObject FirstMessageFrom(PendingHttpRequest request)
            => (JObject)MessagesFrom(request)[0];
    }
}
