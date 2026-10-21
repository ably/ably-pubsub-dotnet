using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Infra.Integration;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;

namespace Ably.PubSub.Tests.Uts.InfraTests.Integration
{
    /// <summary>
    /// Self-tests of <see cref="SandboxApp"/> (guide 2.7 "Direct sandbox": "SandboxApp creates and deletes an app,
    /// and never retries POST /apps"). The no-retry, status-before-parse and Basic-auth halves run offline through
    /// the handler seam; one test creates and deletes a real app.
    ///
    /// Not in the "UTS Sandbox" collection: none of these needs the collection's app, and the offline ones must
    /// not fail because the collection's provisioning did.
    /// </summary>
    [Trait("uts", "integration")]
    [Trait("uts-infra", "integration")]
    [Trait("type", "integration")]
    public class SandboxAppSelfTests
    {
        private const string Endpoint = "nonprod:sandbox";

        private const string AppBody = "{\"appId\":\"appX\",\"keys\":[" +
            "{\"keyName\":\"k1\",\"keySecret\":\"s1\",\"keyStr\":\"appX.k1:s1\",\"capability\":\"{\\\"*\\\":[\\\"*\\\"]}\"}," +
            "{\"keyName\":\"k2\",\"keySecret\":\"s2\",\"keyStr\":\"appX.k2:s2\",\"capability\":\"{\\\"*\\\":[\\\"subscribe\\\"]}\"}]}";

        [Fact]
        public async Task PostAppsIsSentOnceAndNotRetriedWhenTheResponseIsLost()
        {
            var handler = new RecordingHandler(_ => throw new HttpRequestException("connection reset after the request was sent"));

            Func<Task> create = () => SandboxApp.CreateAsync(Endpoint, handler);

            await create.Should().ThrowAsync<HttpRequestException>();
            handler.Requests.Should().HaveCount(1);
            handler.Requests[0].Method.Should().Be(HttpMethod.Post);
            handler.Requests[0].Uri.Should().Be(new Uri("https://sandbox.realtime.ably-nonprod.net/apps"));
        }

        [Fact]
        public async Task PostAppsIsNotRetriedOnAServerErrorAndTheStatusIsCheckedBeforeParsing()
        {
            var handler = new RecordingHandler(_ => new HttpResponseMessage(HttpStatusCode.ServiceUnavailable)
            {
                Content = new StringContent("<html>upstream unavailable</html>", Encoding.UTF8, "text/html"),
            });

            Func<Task> create = () => SandboxApp.CreateAsync(Endpoint, handler);

            // An InvalidOperationException naming the status, not a JSON parse error of the HTML body.
            (await create.Should().ThrowAsync<InvalidOperationException>()).WithMessage("*503*");
            handler.Requests.Should().HaveCount(1);
        }

        [Fact]
        public async Task CreateSendsTheAppSetupAndParsesTheKeysAndDeleteUsesBasicAuthWithTheFirstKey()
        {
            var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(AppBody, Encoding.UTF8, "application/json") }
                : new HttpResponseMessage(HttpStatusCode.NoContent));

            var app = await SandboxApp.CreateAsync(Endpoint, handler);

            var sent = JObject.Parse(handler.Requests[0].Body);
            ((JArray)sent["keys"]).Count.Should().Be(((JArray)SandboxApp.AppSetupBody()["keys"]).Count);
            ((JArray)sent["namespaces"]).Any(ns => (string)ns["id"] == SandboxApp.MutableMessagesNamespace).Should().BeTrue();

            app.AppId.Should().Be("appX");
            app.Keys.Select(k => k.KeyName).Should().Equal("appX.k1", "appX.k2");
            app.Keys.Select(k => k.KeySecret).Should().Equal("s1", "s2");
            app.KeyStr.Should().Be("appX.k1:s1");
            app.Keys[1].Capability.Should().Be("{\"*\":[\"subscribe\"]}");

            (await app.DeleteAsync()).Should().BeTrue();

            handler.Requests.Should().HaveCount(2);
            var delete = handler.Requests[1];
            delete.Method.Should().Be(HttpMethod.Delete);
            delete.Uri.Should().Be(new Uri("https://sandbox.realtime.ably-nonprod.net/apps/appX"));
            delete.Authorization.Should().Be("Basic " + Convert.ToBase64String(Encoding.UTF8.GetBytes("appX.k1:s1")));
            app.Deleted.Should().BeTrue();
        }

        [Fact]
        public async Task DeleteIsBestEffortAndNeverThrows()
        {
            var handler = new RecordingHandler(request => request.Method == HttpMethod.Post
                ? new HttpResponseMessage(HttpStatusCode.Created) { Content = new StringContent(AppBody, Encoding.UTF8, "application/json") }
                : throw new HttpRequestException("network down"));
            var app = await SandboxApp.CreateAsync(Endpoint, handler);

            var deleted = await app.DeleteAsync();

            deleted.Should().BeFalse();
            app.Deleted.Should().BeFalse();
            app.LastDeleteError.Should().BeOfType<HttpRequestException>();

            // POST once; DELETE, being idempotent, may be retried.
            handler.Requests.Count(r => r.Method == HttpMethod.Post).Should().Be(1);
            handler.Requests.Count(r => r.Method == HttpMethod.Delete).Should().BeGreaterThanOrEqualTo(1);
        }

        [Fact(Timeout = 90000)]
        public async Task CreatesAndDeletesARealSandboxApp()
        {
            // Network: the default endpoint (ABLY_ENDPOINT or nonprod:sandbox).
            var app = await SandboxApp.CreateAsync();
            try
            {
                app.AppId.Should().NotBeNullOrEmpty();
                app.Keys.Should().NotBeEmpty();
                app.KeyStr.Should().StartWith(app.AppId + ".");
            }
            finally
            {
                await app.DeleteAsync();
            }

            app.Deleted.Should().BeTrue($"DELETE answered {app.LastDeleteStatus?.ToString() ?? "nothing"} ({app.LastDeleteError?.Message})");
        }

        private sealed class RecordedRequest
        {
            public HttpMethod Method { get; set; }

            public Uri Uri { get; set; }

            public string Authorization { get; set; }

            public string Body { get; set; }
        }

        /// <summary>The seam: answers every request with <c>respond</c> (or its exception) and records it.</summary>
        private sealed class RecordingHandler : HttpMessageHandler
        {
            private readonly Func<HttpRequestMessage, HttpResponseMessage> _respond;
            private readonly List<RecordedRequest> _requests = new List<RecordedRequest>();

            public RecordingHandler(Func<HttpRequestMessage, HttpResponseMessage> respond)
            {
                _respond = respond;
            }

            public List<RecordedRequest> Requests
            {
                get
                {
                    lock (_requests)
                    {
                        return _requests.ToList();
                    }
                }
            }

            protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
            {
                var recorded = new RecordedRequest
                {
                    Method = request.Method,
                    Uri = request.RequestUri,
                    Authorization = request.Headers.Authorization?.ToString(),
                    Body = request.Content == null ? null : await request.Content.ReadAsStringAsync().ConfigureAwait(false),
                };
                lock (_requests)
                {
                    _requests.Add(recorded);
                }

                return _respond(request);
            }
        }
    }
}
