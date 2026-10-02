using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Types
{
    /// <summary>
    /// Derived from uts/rest/unit/types/paginated_result.md in ably/specification.
    ///
    /// Spec points: TG1, TG2, TG3, TG4
    ///
    /// Translation notes that apply to the whole file:
    ///
    /// The spec writes hasNext(), isLast(), next() and first() as methods. In .NET they are the
    /// PaginatedResult properties HasNext and IsLast, and the method NextAsync(). That is ordinary
    /// translation, not a deviation.
    ///
    /// PaginatedResult has no First()/FirstAsync() at all — only HttpPaginatedResponse does — so TG4
    /// is not translated. The rel="first" relation IS parsed (FirstQueryParams), there is simply no
    /// public way to execute it, which is why the first-page relation is asserted through
    /// FirstQueryParams in the multiple-link-relations test instead.
    ///
    /// PaginatedRequestParams.GetLinkQuery reads a relation's query string as url.Split('?')[1], so a
    /// Link relation whose URL carries no query string raises IndexOutOfRangeException while the
    /// first page is still being parsed. Two of the spec's link-header cases contain such a relation
    /// (the spec writes rel="first" against a bare path), so those two cases are carried separately
    /// as a DeviationTheory rather than being dropped. Run them with RUN_DEVIATIONS=1.
    /// </summary>
    public class PaginatedResultTests : UtsTestBase
    {
        public PaginatedResultTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TG1/paginated-result-items-0
        [Fact]
        public async Task TG1_PaginatedResultItems()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { id = "item1", name = "e1", data = "d1" },
                        new { id = "item2", name = "e2", data = "d2" },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var result = await channel.HistoryAsync();

            // The spec's "result.items IS List" is a static guarantee here: Items is declared
            // List<Message>, so a runtime type assertion cannot fail. Asserting it was populated at
            // all is what keeps this half of the spec's assertion meaningful.
            result.Items.Should().NotBeNull();
            result.Items.Should().HaveCount(2);
            result.Items[0].Id.Should().Be("item1");
            result.Items[1].Id.Should().Be("item2");
        }

        // UTS: rest/unit/TG2/has-next-is-last-0
        [Fact]
        public async Task TG2_HasNextIsLastHasMorePages()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(
                        200,
                        new object[] { new { id = "item1" } },
                        new Dictionary<string, string>
                        {
                            { "Link", "</channels/test/messages?cursor=next123>; rel=\"next\"" },
                        });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var result = await channel.HistoryAsync();

            result.HasNext.Should().BeTrue();
            result.IsLast.Should().BeFalse();
        }

        // UTS: rest/unit/TG2/has-next-is-last-0
        [Fact]
        public async Task TG2_HasNextIsLastNoMorePages()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    // No Link header for next.
                    req.RespondWith(
                        200,
                        new object[] { new { id = "item1" } },
                        new Dictionary<string, string>());
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var result = await channel.HistoryAsync();

            result.HasNext.Should().BeFalse();
            result.IsLast.Should().BeTrue();
        }

        // UTS: rest/unit/TG3/next-fetches-next-page-0
        [Fact]
        public async Task TG3_NextFetchesNextPage()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;

                    if (requestCount == 1)
                    {
                        // First page.
                        req.RespondWith(
                            200,
                            new object[]
                            {
                                new { id = "page1-item1" },
                                new { id = "page1-item2" },
                            },
                            new Dictionary<string, string>
                            {
                                { "Link", "</channels/test/messages?cursor=abc123>; rel=\"next\"" },
                            });
                    }
                    else
                    {
                        // Second page, which is the last one.
                        req.RespondWith(
                            200,
                            new object[] { new { id = "page2-item1" } },
                            new Dictionary<string, string>());
                    }
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var page1 = await channel.HistoryAsync();
            var page2 = await page1.NextAsync();

            // First page.
            page1.Items.Should().HaveCount(2);
            page1.Items[0].Id.Should().Be("page1-item1");
            page1.HasNext.Should().BeTrue();

            // Second page.
            page2.Items.Should().HaveCount(1);
            page2.Items[0].Id.Should().Be("page2-item1");
            page2.HasNext.Should().BeFalse();

            // Verify next request used cursor from Link header.
            capturedRequests.Should().HaveCount(2);
            var nextRequest = capturedRequests[1];
            nextRequest.Url.QueryParams.Should().ContainKey("cursor");
            nextRequest.Url.QueryParams["cursor"].Should().Be("abc123");
        }

        // UTS: rest/unit/TG/empty-result-handling-0
        [Fact]
        public async Task TG_EmptyResultHandling()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>(), new Dictionary<string, string>());
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var result = await channel.HistoryAsync();

            // "result.items IS List" is static here — see TG1_PaginatedResultItems.
            result.Items.Should().NotBeNull();
            result.Items.Should().BeEmpty();
            result.HasNext.Should().BeFalse();
            result.IsLast.Should().BeTrue();
        }

        // UTS: rest/unit/TG/link-header-parsing-1
        // Cases 1 and 4 of the spec's table. Cases 2 and 3 are in the DeviationTheory below: both
        // carry a rel="first" relation whose URL has no query string, which the SDK cannot parse.
        [Theory]
        [InlineData("</path?cursor=abc>; rel=\"next\"", true)]
        [InlineData(null, false)]
        public async Task TG_LinkHeaderParsing(string linkHeader, bool expectedHasNext)
        {
            await AssertLinkHeaderGivesHasNext(linkHeader, expectedHasNext);
        }

        // UTS: rest/unit/TG/link-header-parsing-1
        // Cases 2 and 3 of the spec's table. Both fail: PaginatedRequestParams.GetLinkQuery reads a
        // relation's query string as url.Split('?')[1], so the rel="first" relation against the bare
        // path "/path" raises IndexOutOfRangeException while the *first* page is being parsed — the
        // error surfaces out of history(), before hasNext() is ever read.
        [DeviationTheory]
        [InlineData("</path?cursor=abc>; rel=\"next\", </path>; rel=\"first\"", true)]
        [InlineData("</path>; rel=\"first\"", false)]
        public async Task TG_LinkHeaderParsingRelationWithNoQueryString(string linkHeader, bool expectedHasNext)
        {
            await AssertLinkHeaderGivesHasNext(linkHeader, expectedHasNext);
        }

        // UTS: rest/unit/TG/type-parameter-items-2
        [Fact]
        public async Task TG_TypeParameterItems()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new object[]
                    {
                        new { id = "msg1", name = "event", data = "test" },
                    });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            // History returns PaginatedResult<Message>.
            var historyResult = await channel.HistoryAsync();

            // The spec notes this is primarily a compile-time check, and in C# it is: HistoryAsync()
            // is typed Task<PaginatedResult<Message>>, so the spec's second clause — that a
            // PaginatedResult<Message> cannot be assigned to a PaginatedResult<string> — is enforced
            // by the compiler and cannot be expressed as a runtime assertion. What is left to assert
            // is that the items really did deserialise into Message instances.
            historyResult.Items.Should().HaveCount(1);
            historyResult.Items[0].Should().BeOfType<Message>();
            historyResult.Items[0].Id.Should().Be("msg1");
            historyResult.Items[0].Name.Should().Be("event");
        }

        // UTS: rest/unit/TG/next-on-last-page-3
        [Fact]
        public async Task TG_NextOnLastPage()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    // No next link.
                    req.RespondWith(
                        200,
                        new object[] { new { id = "item" } },
                        new Dictionary<string, string>());
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var result = await channel.HistoryAsync();
            result.IsLast.Should().BeTrue();

            var nextResult = await result.NextAsync();

            // The spec allows any of: return null, return an empty PaginatedResult, or throw. This
            // SDK takes the middle option — NextAsync() short-circuits to an empty page — so the
            // "IS null" arm of the spec's disjunction is the one that does not apply here.
            nextResult.Should().NotBeNull();
            nextResult.Items.Should().BeEmpty();
            capturedRequests.Should().HaveCount(
                1,
                "short-circuiting on the last page means no second request is made");
        }

        // UTS: rest/unit/TG/pagination-preserves-auth-4
        [Fact]
        public async Task TG_PaginationPreservesAuth()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;

                    if (requestCount == 1)
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { id = "item1" } },
                            new Dictionary<string, string>
                            {
                                { "Link", "</channels/test/messages?cursor=next>; rel=\"next\"" },
                            });
                    }
                    else
                    {
                        req.RespondWith(200, new object[] { new { id = "item2" } });
                    }
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var page1 = await channel.HistoryAsync();
            await page1.NextAsync();

            // Both requests should have an Authorization header, and the same one.
            capturedRequests.Should().HaveCount(2);
            capturedRequests[0].Headers.Should().ContainKey("Authorization");
            capturedRequests[1].Headers.Should().ContainKey("Authorization");
            capturedRequests[1].Headers["Authorization"]
                .Should().Be(capturedRequests[0].Headers["Authorization"]);
        }

        // UTS: rest/unit/TG/pagination-relative-urls-5
        [Fact]
        public async Task TG_PaginationRelativeUrls()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;

                    if (requestCount == 1)
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { id = "item1" } },
                            new Dictionary<string, string>
                            {
                                { "Link", "</channels/test/messages?page=2>; rel=\"next\"" },
                            });
                    }
                    else
                    {
                        req.RespondWith(200, new object[] { new { id = "item2" } });
                    }
                });

            var client = RestClient(mockHttp, configure: options => options.RestHost = "rest.ably.io");
            var channel = client.Channels.Get("test");

            var page1 = await channel.HistoryAsync();
            await page1.NextAsync();

            // NOTE: this SDK does not resolve the relative link as a URL at all — it keeps only the
            // link's query string and re-issues it against the same endpoint the page came from. The
            // spec's assertions still hold, because that endpoint is the link's own path; what the
            // test cannot distinguish is a Link header pointing at a different path.
            capturedRequests.Should().HaveCount(2);
            capturedRequests[1].Url.Host.Should().Be("rest.ably.io");
            capturedRequests[1].Url.Path.Should().Be("/channels/test/messages");
            capturedRequests[1].Url.QueryParams.Should().ContainKey("page");
        }

        // UTS: rest/unit/TG/multiple-link-relations-6
        [Fact]
        public async Task TG_MultipleLinkRelations()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(
                        200,
                        new object[] { new { id = "item1" } },
                        new Dictionary<string, string>
                        {
                            {
                                "Link",
                                "</channels/test/messages?page=2>; rel=\"next\", " +
                                "</channels/test/messages?page=1>; rel=\"first\", " +
                                "</channels/test/messages?page=5>; rel=\"last\""
                            },
                        });
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var result = await channel.HistoryAsync();

            result.HasNext.Should().BeTrue();

            // The spec's follow-up ("should be able to navigate to next, first, or last") is a
            // comment rather than an assertion, and only part of it is observable here: the SDK
            // parses the current, next and first relations and has no accessor for rel="last" at
            // all. Asserting the two it does parse is as close as the public surface allows.
            result.NextQueryParams.IsEmpty.Should().BeFalse();
            result.FirstQueryParams.IsEmpty.Should().BeFalse();
        }

        // UTS: rest/unit/TG/pagination-presence-results-7
        [Fact]
        public async Task TG_PaginationPresenceResults()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;

                    if (requestCount == 1)
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { action = 1, clientId = "client1" } },
                            new Dictionary<string, string>
                            {
                                { "Link", "</channels/test/presence?page=2>; rel=\"next\"" },
                            });
                    }
                    else
                    {
                        req.RespondWith(200, new object[] { new { action = 1, clientId = "client2" } });
                    }
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var page1 = await channel.Presence.GetAsync();
            var page2 = await page1.NextAsync();

            // "page1 IS PaginatedResult<PresenceMessage>" is static here: Presence.GetAsync() is
            // declared Task<PaginatedResult<PresenceMessage>>.
            page1.Items.Should().HaveCount(1);
            page1.Items[0].ClientId.Should().Be("client1");
            page2.Items.Should().HaveCount(1);
            page2.Items[0].ClientId.Should().Be("client2");
        }

        // UTS: rest/unit/TG/pagination-includes-headers-8
        [Fact]
        public async Task TG_PaginationIncludesHeaders()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;

                    if (requestCount == 1)
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { id = "item1" } },
                            new Dictionary<string, string>
                            {
                                { "Link", "</channels/test/messages?cursor=next>; rel=\"next\"" },
                            });
                    }
                    else
                    {
                        req.RespondWith(200, new object[] { new { id = "item2" } });
                    }
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var page1 = await channel.HistoryAsync();
            await page1.NextAsync();

            // Check headers on pagination request.
            capturedRequests.Should().HaveCount(2);
            var nextRequest = capturedRequests[1];
            nextRequest.Headers.Should().ContainKey("X-Ably-Version");
            nextRequest.Headers.Should().ContainKey("Ably-Agent");
            nextRequest.Headers["Ably-Agent"].Should().Contain("ably-");
        }

        // UTS: rest/unit/TG/error-handling-on-next-9
        [Fact]
        public async Task TG_ErrorHandlingOnNext()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount++;

                    if (requestCount == 1)
                    {
                        req.RespondWith(
                            200,
                            new object[] { new { id = "item1" } },
                            new Dictionary<string, string>
                            {
                                { "Link", "</channels/test/messages?cursor=invalid>; rel=\"next\"" },
                            });
                    }
                    else
                    {
                        req.RespondWith(404, new
                        {
                            error = new
                            {
                                code = 40400,
                                statusCode = 404,
                                message = "Not found",
                            },
                        });
                    }
                });

            var client = RestClient(mockHttp);
            var channel = client.Channels.Get("test");

            var page1 = await channel.HistoryAsync();

            Func<Task> act = () => page1.NextAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int)error.StatusCode.Value).Should().Be(404);
            error.Code.Should().Be(40400);
        }

        /// <summary>
        /// The body of the spec's link-header-parsing loop, shared by the two theories above so that
        /// splitting the table by whether the SDK can parse the case does not duplicate the setup.
        /// </summary>
        private async Task AssertLinkHeaderGivesHasNext(string linkHeader, bool expectedHasNext)
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    var headers = new Dictionary<string, string>();
                    if (string.IsNullOrEmpty(linkHeader) == false)
                    {
                        headers.Add("Link", linkHeader);
                    }

                    req.RespondWith(200, new object[] { new { id = "item" } }, headers);
                });

            var client = RestClient(mockHttp);

            var result = await client.Channels.Get("test").HistoryAsync();

            // The spec's table also lists an expected cursor per case, but its own assertion step
            // reads hasNext() only; the cursor is covered by TG3_NextFetchesNextPage.
            result.HasNext.Should().Be(expectedHasNext);
        }
    }
}
