using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Encryption;
using Ably.PubSub.Http;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Presence
{
    /// <summary>
    /// Derived from uts/rest/unit/presence/rest_presence.md in ably/specification.
    ///
    /// Spec points: RSP1, RSP1a, RSP1b, RSP3, RSP3a, RSP3a1, RSP3a2, RSP3a3, RSP3b, RSP3c, RSP4,
    /// RSP4a, RSP4b1, RSP4b2, RSP4b3, RSP5, RSL3
    ///
    /// <para>
    /// The spec's <c>RestPresence</c> is <c>IPresence</c>, reached through <c>IHttpChannel.Presence</c>.
    /// <c>get(limit:, clientId:, connectionId:)</c> maps straight onto <c>GetAsync</c>; the history
    /// parameters go through a <c>PaginatedRequestParams</c> rather than named arguments.
    /// </para>
    ///
    /// <para>
    /// RSP4b2a and RSP4b3b are written as "the parameter is absent", which this SDK does not do - it
    /// always emits <c>direction</c> and <c>limit</c>. They assert the parameter is present with the
    /// default value, for the reason already set out in <c>Rest/Unit/Channel/HistoryTests.cs</c>
    /// against RSL2b1: an absence assertion would pass whatever the SDK sent, while a switch to omitting the
    /// parameter is an observable change worth catching. RSP3a1b's spec text allows both readings
    /// outright.
    /// </para>
    ///
    /// <para>
    /// <c>rest/unit/RSP5/decode-msgpack-binary-3</c> is not translated: msgpack is compiled out of
    /// this build. See M1 in Uts/deviations.md and the entry in Uts/coverage.md.
    /// </para>
    /// </summary>
    public class RestPresenceTests : UtsTestBase
    {
        private const string ChannelName = "test-presence";

        public RestPresenceTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSP1a/presence-channel-attribute-0
        [Fact]
        public void RSP1a_PresenceIsAChannelAttribute()
        {
            var client = RestClient(new MockHttpClient());

            var presence = client.Channels.Get(ChannelName).Presence;

            presence.Should().NotBeNull();
            presence.Should().BeAssignableTo<IPresence>();
        }

        // UTS: rest/unit/RSP1b/same-instance-returned-0
        [Fact]
        public void RSP1b_SameInstanceReturned()
        {
            var client = RestClient(new MockHttpClient());
            var channel = client.Channels.Get(ChannelName);

            channel.Presence.Should().BeSameAs(channel.Presence);
        }

        // UTS: rest/unit/RSP3a/get-request-endpoint-0
        [Fact]
        public async Task RSP3a_GetRequestEndpoint()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(
                capturedRequests,
                new { action = 1, clientId = "client1", data = "hello" },
                new { action = 1, clientId = "client2", data = "world" }));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            capturedRequests.Should().HaveCount(1);
            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be($"/channels/{ChannelName}/presence");

            result.Should().BeOfType<PaginatedResult<PresenceMessage>>();
            result.Items.Should().HaveCount(2);
        }

        // UTS: rest/unit/RSP3b/get-returns-presence-messages-0
        [Fact]
        public async Task RSP3b_GetReturnsPresenceMessages()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new
                {
                    action = 1,
                    clientId = "user123",
                    connectionId = "conn456",
                    data = "status data",
                    encoding = (string)null,
                    timestamp = 1234567890000L,
                }));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            result.Items.Should().HaveCount(1);
            result.Items[0].Should().BeOfType<PresenceMessage>();
            result.Items[0].Action.Should().Be(PresenceAction.Present);
            result.Items[0].ClientId.Should().Be("user123");
            result.Items[0].ConnectionId.Should().Be("conn456");
            result.Items[0].Data.Should().Be("status data");
            result.Items[0].Timestamp.Value.ToUnixTimeMilliseconds().Should().Be(1234567890000L);
        }

        // UTS: rest/unit/RSP3c/get-empty-members-0
        [Fact]
        public async Task RSP3c_GetEmptyMembers()
        {
            var client = RestClient(MembersMock(new List<PendingHttpRequest>()));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            result.Items.Should().NotBeNull();
            result.Items.Should().BeEmpty();
            result.HasNext.Should().BeFalse();
        }

        // UTS: rest/unit/RSP3a1/get-limit-parameter-0
        [Fact]
        public async Task RSP3a1_GetLimitParameter()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.GetAsync(limit: 50);

            capturedRequests[0].Url.QueryParams["limit"].Should().Be("50");
        }

        // UTS: rest/unit/RSP3a1/get-limit-default-100-1
        //
        // The spec allows either reading here: absent, or present as "100".
        [Fact]
        public async Task RSP3a1_GetLimitDefaults100()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.GetAsync();

            var queryParams = capturedRequests[0].Url.QueryParams;
            if (queryParams.ContainsKey("limit"))
            {
                queryParams["limit"].Should().Be("100");
            }
        }

        // UTS: rest/unit/RSP3a1/get-limit-max-1000-2
        [Fact]
        public async Task RSP3a1_GetLimitMax1000()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.GetAsync(limit: 1000);

            capturedRequests[0].Url.QueryParams["limit"].Should().Be("1000");
        }

        // UTS: rest/unit/RSP3a2/get-clientid-filter-0
        [Fact]
        public async Task RSP3a2_GetClientIdFilter()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.GetAsync(clientId: "specific-client");

            capturedRequests[0].Url.QueryParams["clientId"].Should().Be("specific-client");
        }

        // UTS: rest/unit/RSP3a3/get-connectionid-filter-0
        [Fact]
        public async Task RSP3a3_GetConnectionIdFilter()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.GetAsync(connectionId: "conn123");

            capturedRequests[0].Url.QueryParams["connectionId"].Should().Be("conn123");
        }

        // UTS: rest/unit/RSP3/get-multiple-filters-0
        [Fact]
        public async Task RSP3_GetMultipleFilters()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence
                .GetAsync(limit: 25, clientId: "user1", connectionId: "conn1");

            var queryParams = capturedRequests[0].Url.QueryParams;
            queryParams["limit"].Should().Be("25");
            queryParams["clientId"].Should().Be("user1");
            queryParams["connectionId"].Should().Be("conn1");
        }

        // UTS: rest/unit/RSP4a/history-request-endpoint-0
        [Fact]
        public async Task RSP4a_HistoryRequestEndpoint()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            var result = await client.Channels.Get(ChannelName).Presence.HistoryAsync();

            capturedRequests[0].Method.Should().Be("GET");
            capturedRequests[0].Url.Path.Should().Be($"/channels/{ChannelName}/presence/history");
            result.Should().BeOfType<PaginatedResult<PresenceMessage>>();
        }

        // UTS: rest/unit/RSP4a/history-returns-paginated-1
        [Fact]
        public async Task RSP4a_HistoryReturnsPaginated()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new { action = 2, clientId = "c1" },
                new { action = 3, clientId = "c2" },
                new { action = 4, clientId = "c3" }));

            var result = await client.Channels.Get(ChannelName).Presence.HistoryAsync();

            result.Should().BeOfType<PaginatedResult<PresenceMessage>>();
            result.Items.Should().HaveCount(3);
            result.Items[0].Action.Should().Be(PresenceAction.Enter);
            result.Items[1].Action.Should().Be(PresenceAction.Leave);
            result.Items[2].Action.Should().Be(PresenceAction.Update);
        }

        // UTS: rest/unit/RSP4b1/history-start-parameter-0
        [Fact]
        public async Task RSP4b1_HistoryStartParameter()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(
                new PaginatedRequestParams { Start = StartTime });

            capturedRequests[0].Url.QueryParams["start"].Should().Be("1609459200000");
        }

        // UTS: rest/unit/RSP4b1/history-end-parameter-1
        [Fact]
        public async Task RSP4b1_HistoryEndParameter()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(
                new PaginatedRequestParams { End = EndTime });

            capturedRequests[0].Url.QueryParams["end"].Should().Be("1609545600000");
        }

        // UTS: rest/unit/RSP4b1/history-start-end-params-2
        [Fact]
        public async Task RSP4b1_HistoryStartAndEndParameters()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(
                new PaginatedRequestParams { Start = StartTime, End = EndTime });

            capturedRequests[0].Url.QueryParams["start"].Should().Be("1609459200000");
            capturedRequests[0].Url.QueryParams["end"].Should().Be("1609545600000");
        }

        // UTS: rest/unit/RSP4b1/history-datetime-objects-3
        //
        // The spec's separate "accepts a DateTime as well as a millisecond number" case has no
        // counterpart here: PaginatedRequestParams.Start is a DateTimeOffset?, so a DateTime is the
        // only form it takes. The test keeps the spec's instant and asserts the same serialisation,
        // constructing it from date components rather than from a millisecond value.
        [Fact]
        public async Task RSP4b1_HistoryAcceptsDateTimeObjects()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            var startDateTime = new DateTimeOffset(2021, 1, 1, 0, 0, 0, TimeSpan.Zero);

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(
                new PaginatedRequestParams { Start = startDateTime });

            capturedRequests[0].Url.QueryParams["start"].Should().Be("1609459200000");
        }

        // UTS: rest/unit/RSP4b2/history-direction-backwards-default-0
        //
        // Asserted as present-with-the-default rather than absent - see the class note.
        [Fact]
        public async Task RSP4b2_HistoryDirectionBackwardsByDefault()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync();

            capturedRequests[0].Url.QueryParams["direction"].Should().Be("backwards");
        }

        // UTS: rest/unit/RSP4b2/history-direction-forwards-1
        [Fact]
        public async Task RSP4b2_HistoryDirectionForwards()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(
                new PaginatedRequestParams { Direction = QueryDirection.Forwards });

            capturedRequests[0].Url.QueryParams["direction"].Should().Be("forwards");
        }

        // UTS: rest/unit/RSP4b2/history-direction-backwards-explicit-2
        [Fact]
        public async Task RSP4b2_HistoryDirectionBackwardsExplicit()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(
                new PaginatedRequestParams { Direction = QueryDirection.Backwards });

            capturedRequests[0].Url.QueryParams["direction"].Should().Be("backwards");
        }

        // UTS: rest/unit/RSP4b3/history-limit-parameter-0
        [Fact]
        public async Task RSP4b3_HistoryLimitParameter()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(
                new PaginatedRequestParams { Limit = 50 });

            capturedRequests[0].Url.QueryParams["limit"].Should().Be("50");
        }

        // UTS: rest/unit/RSP4b3/history-limit-default-100-1
        //
        // Asserted as present-with-the-default rather than absent - see the class note.
        [Fact]
        public async Task RSP4b3_HistoryLimitDefaults100()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync();

            capturedRequests[0].Url.QueryParams["limit"].Should().Be("100");
        }

        // UTS: rest/unit/RSP4b3/history-limit-max-1000-2
        [Fact]
        public async Task RSP4b3_HistoryLimitMax1000()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(
                new PaginatedRequestParams { Limit = 1000 });

            capturedRequests[0].Url.QueryParams["limit"].Should().Be("1000");
        }

        // UTS: rest/unit/RSP4/history-all-parameters-0
        [Fact]
        public async Task RSP4_HistoryAllParameters()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync(new PaginatedRequestParams
            {
                Start = StartTime,
                End = EndTime,
                Direction = QueryDirection.Forwards,
                Limit = 50,
            });

            var queryParams = capturedRequests[0].Url.QueryParams;
            queryParams["start"].Should().Be("1609459200000");
            queryParams["end"].Should().Be("1609545600000");
            queryParams["direction"].Should().Be("forwards");
            queryParams["limit"].Should().Be("50");
        }

        // UTS: rest/unit/RSP5/decode-string-data-0
        [Fact]
        public async Task RSP5_DecodeStringData()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new { action = 1, clientId = "c1", data = "plain string data" }));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            result.Items[0].Data.Should().Be("plain string data");
            result.Items[0].Data.Should().BeOfType<string>();
        }

        // UTS: rest/unit/RSP5/decode-json-data-1
        [Fact]
        public async Task RSP5_DecodeJsonData()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new
                {
                    action = 1,
                    clientId = "c1",
                    data = "{\"status\":\"online\",\"count\":42}",
                    encoding = "json",
                }));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            var data = result.Items[0].Data.Should().BeOfType<JObject>().Subject;
            data["status"].Value<string>().Should().Be("online");
            data["count"].Value<int>().Should().Be(42);
            result.Items[0].Encoding.Should().BeNullOrEmpty("the encoding is consumed by decoding");
        }

        // UTS: rest/unit/RSP5/decode-base64-binary-2
        [Fact]
        public async Task RSP5_DecodeBase64Binary()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new
                {
                    action = 1,
                    clientId = "c1",
                    data = "SGVsbG8gV29ybGQ=",
                    encoding = "base64",
                }));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            var data = result.Items[0].Data.Should().BeOfType<byte[]>().Subject;
            data.Should().Equal(System.Text.Encoding.UTF8.GetBytes("Hello World"));
            result.Items[0].Encoding.Should().BeNullOrEmpty("the encoding is consumed by decoding");
        }

        // UTS: rest/unit/RSP5/decode-utf8-data-4
        [Fact]
        public async Task RSP5_DecodeUtf8Data()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new
                {
                    action = 1,
                    clientId = "c1",
                    data = "SGVsbG8gV29ybGQ=",
                    encoding = "utf-8/base64",
                }));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            result.Items[0].Data.Should().Be("Hello World");
            result.Items[0].Data.Should().BeOfType<string>();
        }

        // UTS: rest/unit/RSP5/decode-chained-encoding-5
        //
        // SPEC ERROR, already analysed under D8 in Uts/deviations.md. The chain the spec uses,
        // `json/base64`, is not a valid one: RSL4d1 defines base64 as the transform for *binary*
        // and RSL4d3 defines json as producing a *string*, so a json step handed raw bytes has
        // nothing it can legally do. The canonical chain is `json/utf-8/base64`, covered by the
        // passing RSP5_DecodeUtf8Data above.
        //
        // What this SDK does is the RSL6b-correct response to an undecodable step: it keeps the
        // payload as of the last successful decoding and leaves the residual transform in
        // `encoding`. Measured: `byte[]` holding `{"key":"value"}`, with encoding `json`. The test
        // asserts that outcome rather than the spec's, so it runs and pins the compliant behaviour.
        [Fact]
        public async Task RSP5_DecodeChainedEncoding()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new
                {
                    action = 1,
                    clientId = "c1",
                    data = "eyJrZXkiOiJ2YWx1ZSJ9",
                    encoding = "json/base64",
                }));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            var data = result.Items[0].Data.Should().BeOfType<byte[]>(
                "RSL6b - decoding stops at the step it cannot apply").Subject;
            System.Text.Encoding.UTF8.GetString(data).Should().Be("{\"key\":\"value\"}");
            result.Items[0].Encoding.Should().Be("json", "RSL6b - the residual transform is left behind");
        }

        // UTS: rest/unit/RSP5/decode-history-messages-6
        [Fact]
        public async Task RSP5_DecodeHistoryMessages()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new
                {
                    action = 2,
                    clientId = "c1",
                    data = "{\"event\":\"entered\"}",
                    encoding = "json",
                }));

            var result = await client.Channels.Get(ChannelName).Presence.HistoryAsync();

            var data = result.Items[0].Data.Should().BeOfType<JObject>().Subject;
            data["event"].Value<string>().Should().Be("entered");
        }

        // UTS: rest/unit/RSP5/decode-cipher-channel-7
        //
        // ADAPTED FIXTURE. The spec's ciphertext cannot be what it says it is. It decodes to 32
        // bytes, which under AES-CBC is a 16-byte IV plus a single block - at most 15 bytes of
        // plaintext once PKCS7 padding is accounted for - while the plaintext it claims,
        // {"secret":"data"}, is 17 bytes and needs 48. Measured against the SDK, decoding stopped
        // after base64 with `json/utf-8/cipher+aes-128-cbc` still in `encoding`, which is the
        // RSL6b-correct response to a payload it cannot decrypt.
        //
        // RSP5g's requirement is that presence data is decrypted using the channel's cipher
        // options, so the fixture is built here with the SDK's own cipher from the spec's key and
        // plaintext. That keeps the assertion the spec's - the full chain decodes to an object -
        // and tests the thing RSP5g is about. Recorded as a spec error in Uts/deviations.md.
        [Fact]
        public async Task RSP5_DecodeCipherChannel()
        {
            var cipherKey = Convert.FromBase64String("WUP6u0K7MXI5Zeo0VppPwg==");
            var cipherParams = new CipherParams(cipherKey);

            const string Plaintext = "{\"secret\":\"data\"}";
            var encrypted = Crypto.GetCipher(cipherParams)
                .Encrypt(System.Text.Encoding.UTF8.GetBytes(Plaintext));

            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new
                {
                    action = 1,
                    clientId = "c1",
                    data = Convert.ToBase64String(encrypted),
                    encoding = "json/utf-8/cipher+aes-128-cbc/base64",
                }));

            var channel = client.Channels.Get(ChannelName, new ChannelOptions(cipherParams));

            var result = await channel.Presence.GetAsync();

            var data = result.Items[0].Data.Should().BeOfType<JObject>(
                "the cipher link in the encoding chain is decrypted with the channel's key").Subject;
            data["secret"].Value<string>().Should().Be("data");
            result.Items[0].Encoding.Should().BeNullOrEmpty("the whole chain is consumed");
        }

        // UTS: rest/unit/RSP5/presence-action-mapping-8
        [Fact]
        public async Task RSP5_PresenceActionMapping()
        {
            var client = RestClient(MembersMock(
                new List<PendingHttpRequest>(),
                new { action = 0, clientId = "c1" },
                new { action = 1, clientId = "c2" },
                new { action = 2, clientId = "c3" },
                new { action = 3, clientId = "c4" },
                new { action = 4, clientId = "c5" }));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            result.Items[0].Action.Should().Be(PresenceAction.Absent);
            result.Items[1].Action.Should().Be(PresenceAction.Present);
            result.Items[2].Action.Should().Be(PresenceAction.Enter);
            result.Items[3].Action.Should().Be(PresenceAction.Leave);
            result.Items[4].Action.Should().Be(PresenceAction.Update);
        }

        // UTS: rest/unit/RSP3/get-pagination-link-header-1
        [Fact]
        public async Task RSP3_GetPaginationLinkHeader()
        {
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(
                    200,
                    new object[]
                    {
                        new { action = 1, clientId = "client1" },
                        new { action = 1, clientId = "client2" },
                    },
                    new Dictionary<string, string>
                    {
                        { "Link", $"</channels/{ChannelName}/presence?page=2>; rel=\"next\"" },
                    })));

            var result = await client.Channels.Get(ChannelName).Presence.GetAsync();

            result.Items.Should().HaveCount(2);
            result.HasNext.Should().BeTrue();
        }

        // UTS: rest/unit/RSP3/get-pagination-next-page-2
        [Fact]
        public async Task RSP3_GetPaginationNextPage()
        {
            var requestCount = 0;
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    if (requestCount == 1)
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { action = 1, clientId = "client1" } },
                            new Dictionary<string, string>
                            {
                                { "Link", $"</channels/{ChannelName}/presence?page=2>; rel=\"next\"" },
                            });
                    }
                    else
                    {
                        req.RespondWith(200, new object[] { new { action = 1, clientId = "client2" } });
                    }
                }));

            var page1 = await client.Channels.Get(ChannelName).Presence.GetAsync();
            var page2 = await page1.NextAsync();

            page1.Items[0].ClientId.Should().Be("client1");
            page2.Items[0].ClientId.Should().Be("client2");
            page2.HasNext.Should().BeFalse();
        }

        // UTS: rest/unit/RSP4/history-pagination-1
        [Fact]
        public async Task RSP4_HistoryPagination()
        {
            var requestCount = 0;
            var client = RestClient(new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    if (requestCount == 1)
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { action = 2, clientId = "client1" } },
                            new Dictionary<string, string>
                            {
                                {
                                    "Link",
                                    $"</channels/{ChannelName}/presence/history?page=2>; rel=\"next\""
                                },
                            });
                    }
                    else
                    {
                        req.RespondWith(200, new object[] { new { action = 3, clientId = "client1" } });
                    }
                }));

            var page1 = await client.Channels.Get(ChannelName).Presence.HistoryAsync();
            var page2 = await page1.NextAsync();

            page1.Items[0].Action.Should().Be(PresenceAction.Enter);
            page2.Items[0].Action.Should().Be(PresenceAction.Leave);
        }

        // UTS: rest/unit/RSP3/get-server-error-3
        [Fact]
        public async Task RSP3_GetServerError()
        {
            var client = RestClient(ErrorMock(500, 50000, "Internal server error"));

            Func<Task> act = () => client.Channels.Get(ChannelName).Presence.GetAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(50000);
            ((int)error.StatusCode.Value).Should().Be(500);
        }

        // UTS: rest/unit/RSP4/history-auth-error-2
        [Fact]
        public async Task RSP4_HistoryAuthError()
        {
            var client = RestClient(ErrorMock(401, 40101, "Invalid credentials"));

            Func<Task> act = () => client.Channels.Get(ChannelName).Presence.HistoryAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40101);
            ((int)error.StatusCode.Value).Should().Be(401);
        }

        // UTS: rest/unit/RSP3/get-channel-not-found-4
        [Fact]
        public async Task RSP3_GetChannelNotFound()
        {
            var client = RestClient(ErrorMock(404, 40400, "Channel not found"));

            Func<Task> act = () => client.Channels.Get(ChannelName).Presence.GetAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40400);
            ((int)error.StatusCode.Value).Should().Be(404);
        }

        // UTS: rest/unit/RSP3/get-standard-headers-5
        [Fact]
        public async Task RSP3_GetStandardHeaders()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.GetAsync();

            var headers = capturedRequests[0].Headers;
            headers.Should().ContainKey("X-Ably-Version");
            headers["Ably-Agent"].Should().Contain("ably-");
            headers.Should().ContainKey("Accept");
        }

        // UTS: rest/unit/RSP4/history-auth-header-3
        [Fact]
        public async Task RSP4_HistoryAuthHeader()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(MembersMock(capturedRequests));

            await client.Channels.Get(ChannelName).Presence.HistoryAsync();

            capturedRequests[0].Headers.Should().ContainKey("Authorization");
            capturedRequests[0].Headers["Authorization"].Should().StartWith("Basic ");
        }

        // UTS: rest/unit/RSP3/get-request-id-enabled-6
        //
        // DEVIATION. Same root cause as D4 in Uts/deviations.md, found first against
        // RestClientTests.RSC7c_RequestIdIncluded: with AddRequestIds on, this SDK sends the request
        // id as a `request_id` *header* rather than as the query parameter RSC7c and this spec test
        // both require.
        [DeviationFact]
        public async Task RSP3_GetRequestIdEnabled()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var client = RestClient(
                MembersMock(capturedRequests),
                configure: options => options.AddRequestIds = true);

            await client.Channels.Get(ChannelName).Presence.GetAsync();

            capturedRequests[0].Url.QueryParams.Should().ContainKey("request_id");
            capturedRequests[0].Url.QueryParams["request_id"].Should().NotBeNullOrEmpty();
        }

        private static DateTimeOffset StartTime
            => DateTimeOffset.FromUnixTimeMilliseconds(1609459200000L);

        private static DateTimeOffset EndTime
            => DateTimeOffset.FromUnixTimeMilliseconds(1609545600000L);

        private static MockHttpClient MembersMock(
            List<PendingHttpRequest> capturedRequests,
            params object[] members)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, members);
                });

        private static MockHttpClient ErrorMock(int statusCode, int code, string message)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(statusCode, new
                {
                    error = new { code, statusCode, message },
                }));
    }
}
