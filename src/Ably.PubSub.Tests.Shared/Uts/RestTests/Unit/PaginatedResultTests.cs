// PaginatedResult Types Tests (TG1, TG2, TG3, TG4)
// Derived from uts/rest/unit/types/paginated_result.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
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
    public class PaginatedResultTests : UtsUnitTestBase
    {
        public PaginatedResultTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/TG1/paginated-result-items-0
        [UtsFact]
        [Trait("spec", "TG1")]
        public async Task TG1_PaginatedResultItems()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        new { id = "item1", name = "e1", data = "d1" },
                        new { id = "item2", name = "e2", data = "d2" },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var result = await channel.HistoryAsync();

            // Assertions
            result.Items.Should().BeAssignableTo<List<Message>>();
            result.Items.Should().HaveCount(2);
            result.Items[0].Id.Should().Be("item1");
            result.Items[1].Id.Should().Be("item2");
        }

        // UTS: rest/unit/TG2/has-next-is-last-0
        [UtsFact]
        [Trait("spec", "TG2")]
        public async Task TG2_HasNextIsLast()
        {
            // Test Case 1: Has more pages
            // Setup
            var capturedRequestsMorePages = new CaptureList<PendingHttpRequest>();

            var mockHttpMorePages = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequestsMorePages.Add(req);
                    req.RespondWith(
                        200,
                        body: new[] { new { id = "item1" } },
                        headers: new Dictionary<string, string>
                        {
                            ["Link"] = "</channels/test/messages?cursor=next123>; rel=\"next\"",
                        });
                });
            InstallMock(mockHttpMorePages);

            var clientMorePages = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channelMorePages = clientMorePages.Channels.Get("test");

            // Test Steps
            var resultMorePages = await channelMorePages.HistoryAsync();

            // Assertions
            resultMorePages.HasNext.Should().BeTrue();
            resultMorePages.IsLast.Should().BeFalse();

            // Test Case 2: No more pages
            // Setup
            var capturedRequestsNoMorePages = new CaptureList<PendingHttpRequest>();

            var mockHttpNoMorePages = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequestsNoMorePages.Add(req);

                    // No Link header for next
                    req.RespondWith(
                        200,
                        body: new[] { new { id = "item1" } },
                        headers: new Dictionary<string, string>());
                });
            InstallMock(mockHttpNoMorePages);

            var clientNoMorePages = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channelNoMorePages = clientNoMorePages.Channels.Get("test");

            // Test Steps
            var resultNoMorePages = await channelNoMorePages.HistoryAsync();

            // Assertions
            resultNoMorePages.HasNext.Should().BeFalse();
            resultNoMorePages.IsLast.Should().BeTrue();
        }

        // UTS: rest/unit/TG3/next-fetches-next-page-0
        [UtsFact]
        [Trait("spec", "TG3")]
        public async Task TG3_NextFetchesNextPage()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        // First page
                        req.RespondWith(
                            200,
                            body: new[] { new { id = "page1-item1" }, new { id = "page1-item2" } },
                            headers: new Dictionary<string, string>
                            {
                                ["Link"] = "</channels/test/messages?cursor=abc123>; rel=\"next\"",
                            });
                    }
                    else
                    {
                        // Second page
                        // Last page
                        req.RespondWith(
                            200,
                            body: new[] { new { id = "page2-item1" } },
                            headers: new Dictionary<string, string>());
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var page1 = await channel.HistoryAsync();
            var page2 = await page1.NextAsync();

            // Assertions
            // First page
            page1.Items.Should().HaveCount(2);
            page1.Items[0].Id.Should().Be("page1-item1");
            page1.HasNext.Should().BeTrue();

            // Second page
            page2.Items.Should().HaveCount(1);
            page2.Items[0].Id.Should().Be("page2-item1");
            page2.HasNext.Should().BeFalse();

            // Verify next request used cursor from Link header
            var nextRequest = capturedRequests[1];
            nextRequest.Url.QueryParams.Should().ContainKey("cursor");
            nextRequest.Url.QueryParams["cursor"].Should().Be("abc123");
        }

        // UTS: rest/unit/TG4/first-returns-first-page-0
        [UtsFact(Skip = "Missing SDK public API: PaginatedResult#first (TG4); see deviations.md (Missing SDK Public API / Features)")]
        [Trait("spec", "TG4")]
        public Task TG4_FirstReturnsFirstPage()
        {
            // NOTE: missing SDK API: PaginatedResult<T> exposes NextAsync, HasNext and IsLast but no FirstAsync (only the HttpPaginatedResponse returned by request() has one), so page2.first() can't be called and the whole test is a stub. Spec lines:
            // captured_requests = []
            // request_count = 0
            //
            // mock_http = MockHttpClient(
            //   onConnectionAttempt: (conn) => conn.respond_with_success(),
            //   onRequest: (req) => {
            //     captured_requests.push(req)
            //     request_count++
            //
            //     IF request_count == 1:
            //       # Initial request
            //       req.respond_with(200,
            //         body: [{ "id": "item1" }],
            //         headers: {
            //           "Link": "</channels/test/messages?cursor=next>; rel=\"next\", </channels/test/messages>; rel=\"first\""
            //         }
            //       )
            //     ELSE IF request_count == 2:
            //       # Next page
            //       req.respond_with(200,
            //         body: [{ "id": "item2" }],
            //         headers: {
            //           "Link": "</channels/test/messages>; rel=\"first\""
            //         }
            //       )
            //     ELSE:
            //       # First page again
            //       req.respond_with(200,
            //         body: [{ "id": "item1" }],
            //         headers: {
            //           "Link": "</channels/test/messages?cursor=next>; rel=\"next\""
            //         }
            //       )
            //   }
            // )
            // install_mock(mock_http)
            //
            // client = Rest(options: ClientOptions(key: "appId.keyId:keySecret"))
            // channel = client.channels.get("test")
            //
            // page1 = AWAIT channel.history()
            // page2 = AWAIT page1.next()
            // first_page = AWAIT page2.first()
            //
            // ASSERT first_page.items[0].id == "item1"
            return Task.CompletedTask;
        }

        // UTS: rest/unit/TG/empty-result-handling-0
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_EmptyResultHandling()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(
                        200,
                        body: new JArray(),
                        headers: new Dictionary<string, string>());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var result = await channel.HistoryAsync();

            // Assertions
            result.Items.Should().BeAssignableTo<List<Message>>();
            result.Items.Should().HaveCount(0);
            result.HasNext.Should().BeFalse();
            result.IsLast.Should().BeTrue();
        }

        // UTS: rest/unit/TG/link-header-parsing-1
        // DEVIATION: see deviations.md
        [DeviationFact("TG", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "TG")]
        public async Task TG_LinkHeaderParsing()
        {
            // Test Cases
            var testCases = new[]
            {
                new { LinkHeader = "</path?cursor=abc>; rel=\"next\"", ExpectedHasNext = true },
                new { LinkHeader = "</path?cursor=abc>; rel=\"next\", </path>; rel=\"first\"", ExpectedHasNext = true },
                new { LinkHeader = "</path>; rel=\"first\"", ExpectedHasNext = false },
                new { LinkHeader = string.Empty, ExpectedHasNext = false },
            };

            // Setup and Execution
            foreach (var testCase in testCases)
            {
                var capturedRequests = new CaptureList<PendingHttpRequest>();

                var mockHttp = new MockHttpClient(
                    onConnectionAttempt: conn => conn.RespondWithSuccess(),
                    onRequest: req =>
                    {
                        capturedRequests.Add(req);

                        if (!string.IsNullOrEmpty(testCase.LinkHeader))
                        {
                            req.RespondWith(
                                200,
                                body: new[] { new { id = "item" } },
                                headers: new Dictionary<string, string> { ["Link"] = testCase.LinkHeader });
                        }
                        else
                        {
                            req.RespondWith(
                                200,
                                body: new[] { new { id = "item" } },
                                headers: new Dictionary<string, string>());
                        }
                    });
                InstallMock(mockHttp);

                var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
                var result = await client.Channels.Get("test").HistoryAsync();

                result.HasNext.Should().Be(testCase.ExpectedHasNext);
            }
        }

        // UTS: rest/unit/TG/type-parameter-items-2
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_TypeParameterItems()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[]
                    {
                        new { id = "msg1", name = "event", data = "test" },
                    });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            // History returns PaginatedResult<Message>
            var historyResult = await channel.HistoryAsync();
            historyResult.Items[0].Should().BeAssignableTo<Message>();

            // If the language supports generics, verify:
            // PaginatedResult<Message> cannot be assigned to PaginatedResult<String>
            // (compile-time guarantee: HistoryAsync returns Task<PaginatedResult<Message>>)
        }

        // UTS: rest/unit/TG/next-on-last-page-3
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_NextOnLastPage()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    // No next link
                    req.RespondWith(
                        200,
                        body: new[] { new { id = "item" } },
                        headers: new Dictionary<string, string>());
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var result = await channel.HistoryAsync();
            result.IsLast.Should().BeTrue();

            var nextResult = await result.NextAsync();

            // Assertions
            // Implementation may either:
            // 1. Return null
            // 2. Return empty PaginatedResult
            // 3. Throw an exception
            (nextResult == null || nextResult.Items.Count == 0).Should().BeTrue();
        }

        // UTS: rest/unit/TG/pagination-preserves-auth-4
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_PaginationPreservesAuth()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(
                            200,
                            body: new[] { new { id = "item1" } },
                            headers: new Dictionary<string, string>
                            {
                                ["Link"] = "</channels/test/messages?cursor=next>; rel=\"next\"",
                            });
                    }
                    else
                    {
                        req.RespondWith(200, body: new[] { new { id = "item2" } });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var page1 = await channel.HistoryAsync();
            var page2 = await page1.NextAsync();

            // Assertions
            // Both requests should have Authorization header
            capturedRequests[0].Headers.Should().ContainKey("Authorization");
            capturedRequests[1].Headers.Should().ContainKey("Authorization");
            capturedRequests[0].Headers["Authorization"].Should().Be(capturedRequests[1].Headers["Authorization"]);
        }

        // UTS: rest/unit/TG/pagination-relative-urls-5
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_PaginationRelativeUrls()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(
                            200,
                            body: new[] { new { id = "item1" } },
                            headers: new Dictionary<string, string>
                            {
                                ["Link"] = "</channels/test/messages?page=2>; rel=\"next\"",
                            });
                    }
                    else
                    {
                        req.RespondWith(200, body: new[] { new { id = "item2" } });
                    }
                });
            InstallMock(mockHttp);

            // NOTE: restHost is a removed option (Endpoint replaces it); Endpoint = "<host>" gives the same primary domain (REC1b2)
            // restHost: "rest.ably.io"
            var client = Rest(new ClientOptions
            {
                Key = "appId.keyId:keySecret",
                Endpoint = "rest.ably.io",
            });
            var channel = client.Channels.Get("test");

            // Test Steps
            var page1 = await channel.HistoryAsync();
            var page2 = await page1.NextAsync();

            // Assertions
            // Second request should use full URL
            capturedRequests[1].Url.Host.Should().Be("rest.ably.io");
            capturedRequests[1].Url.Path.Should().Be("/channels/test/messages");
            capturedRequests[1].Url.QueryParams.Should().ContainKey("page");
        }

        // UTS: rest/unit/TG/multiple-link-relations-6
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_MultipleLinkRelations()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(
                        200,
                        body: new[] { new { id = "item1" } },
                        headers: new Dictionary<string, string>
                        {
                            ["Link"] = "</channels/test/messages?page=2>; rel=\"next\", </channels/test/messages?page=1>; rel=\"first\", </channels/test/messages?page=5>; rel=\"last\"",
                        });
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var result = await channel.HistoryAsync();

            // Assertions
            result.HasNext.Should().BeTrue();

            // Implementation should be able to navigate to next, first, or last pages
        }

        // UTS: rest/unit/TG/pagination-presence-results-7
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_PaginationPresenceResults()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(
                            200,
                            body: new[] { new { action = 1, clientId = "client1" } },
                            headers: new Dictionary<string, string>
                            {
                                ["Link"] = "</channels/test/presence?page=2>; rel=\"next\"",
                            });
                    }
                    else
                    {
                        req.RespondWith(200, body: new[] { new { action = 1, clientId = "client2" } });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var page1 = await channel.Presence.GetAsync();
            var page2 = await page1.NextAsync();

            // Assertions
            // ASSERT page1 IS PaginatedResult<PresenceMessage>: guaranteed at compile time (GetAsync returns PaginatedResult<PresenceMessage>)
            page1.Items[0].ClientId.Should().Be("client1");
            page2.Items[0].ClientId.Should().Be("client2");
        }

        // UTS: rest/unit/TG/pagination-includes-headers-8
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_PaginationIncludesHeaders()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(
                            200,
                            body: new[] { new { id = "item1" } },
                            headers: new Dictionary<string, string>
                            {
                                ["Link"] = "</channels/test/messages?cursor=next>; rel=\"next\"",
                            });
                    }
                    else
                    {
                        req.RespondWith(200, body: new[] { new { id = "item2" } });
                    }
                });
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var page1 = await channel.HistoryAsync();
            var page2 = await page1.NextAsync();

            // Assertions
            // Check headers on pagination request
            var nextRequest = capturedRequests[1];
            nextRequest.Headers.Should().ContainKey("X-Ably-Version");
            nextRequest.Headers.Should().ContainKey("Ably-Agent");
            nextRequest.Headers["Ably-Agent"].Should().Contain("ably-");
        }

        // UTS: rest/unit/TG/error-handling-on-next-9
        [UtsFact]
        [Trait("spec", "TG")]
        public async Task TG_ErrorHandlingOnNext()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);

                    if (Interlocked.Increment(ref requestCount) == 1)
                    {
                        req.RespondWith(
                            200,
                            body: new[] { new { id = "item1" } },
                            headers: new Dictionary<string, string>
                            {
                                ["Link"] = "</channels/test/messages?cursor=invalid>; rel=\"next\"",
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
            InstallMock(mockHttp);

            var client = Rest(new ClientOptions { Key = "appId.keyId:keySecret" });
            var channel = client.Channels.Get("test");

            // Test Steps
            var page1 = await channel.HistoryAsync();

            Func<Task> act = () => page1.NextAsync();
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;

            // Assertions
            ((int?)error.StatusCode).Should().Be(404);
            error.Code.Should().Be(40400);
        }
    }
}
