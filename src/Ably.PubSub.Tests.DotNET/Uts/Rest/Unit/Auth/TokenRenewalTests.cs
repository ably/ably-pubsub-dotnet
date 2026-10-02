using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Auth
{
    /// <summary>
    /// Derived from uts/rest/unit/auth/token_renewal.md in ably/specification.
    ///
    /// Spec points: RSA4a2, RSA4b, RSA4b1, RSC10, RSC10b
    ///
    /// <para>
    /// The renewal branch under test is <c>PubSubHttpClient.ExecuteRequest</c>'s
    /// <c>catch (AblyException)</c> (<c>PubSubHttpClient.cs:202-232</c>): a 401 whose Ably code is a
    /// token error triggers one <c>AuthorizeAsync</c> and one retry, or 40171 when the client has no
    /// means to renew.
    /// </para>
    ///
    /// <para>
    /// The spec writes the expected header as <c>"Bearer first-token"</c>; this SDK base64-encodes
    /// the token, which RSA3b permits - see <see cref="AuthSchemeTests"/>. Assertions go through
    /// <see cref="BearerHeader"/>.
    /// </para>
    ///
    /// <para>
    /// <c>rest/unit/RSA4b/renewal-msgpack-response-4</c> is not translated: msgpack is compiled out
    /// of this build, so a msgpack error body cannot be produced or decoded. See M1 in
    /// Uts/deviations.md and the entry in Uts/coverage.md.
    /// </para>
    /// </summary>
    public class TokenRenewalTests : UtsTestBase
    {
        public TokenRenewalTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA4b/renewal-on-40142-0
        [Fact]
        public async Task RSA4b_RenewalOn40142()
        {
            var callbackCount = 0;
            var tokens = new[] { "first-token", "second-token" };
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount = requestCount + 1;
                    if (requestCount == 1)
                    {
                        req.RespondWith(401, TokenErrorBody(40142, "Token expired"));
                    }
                    else
                    {
                        req.RespondWith(200, new[] { new { channel = "test" } });
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    var token = tokens[callbackCount];
                    callbackCount = callbackCount + 1;
                    return Task.FromResult<object>(FreshToken(token));
                };
            });

            var result = await client.Channels.Get("test").HistoryAsync();

            callbackCount.Should().Be(2, "the initial token plus one renewal");
            requestCount.Should().Be(2);
            capturedRequests[0].Headers["Authorization"].Should().Be(BearerHeader("first-token"));
            capturedRequests[1].Headers["Authorization"].Should().Be(BearerHeader("second-token"));
            result.Items.Should().NotBeNull();
        }

        // UTS: rest/unit/RSA4b/renewal-on-40140-1
        [Fact]
        public async Task RSA4b_RenewalOn40140()
        {
            var callbackCount = 0;
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    if (requestCount == 1)
                    {
                        req.RespondWith(401, TokenErrorBody(40140, "Token error"));
                    }
                    else
                    {
                        req.RespondWith(200, Array.Empty<object>());
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + callbackCount));
                };
            });

            await client.Channels.Get("test").HistoryAsync();

            callbackCount.Should().Be(2);
            requestCount.Should().Be(2);
        }

        // UTS: rest/unit/RSA4b1/preemptive-renewal-0
        //
        // DEVIATION. RSA4b1 requires a token already known to be expired to be renewed *before* the
        // request goes out. This SDK sends the expired token and only renews after the server rejects
        // it, because TokenDetailsExtensions.IsValidToken (TokenDetails.cs:131) treats a null server
        // time as "valid" and AblyAuth.ServerNow is null unless QueryTime is set - so the expiry
        // check never fires. Here the mock answers every request with 200, so no renewal is prompted
        // at all and the callback is invoked once. See Uts/deviations.md.
        [DeviationFact]
        public async Task RSA4b1_PreemptiveRenewal()
        {
            var clock = new TestClock();
            var callbackCount = 0;
            var capturedRequests = new List<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, Array.Empty<object>());
                });

            var client = RestClient(mockHttp, clock, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    return Task.FromResult<object>(callbackCount == 1
                        ? new TokenDetails("expired-token") { Expires = clock.Now.AddMilliseconds(-1000) }
                        : new TokenDetails("fresh-token") { Expires = clock.Now.AddHours(1) });
                };
            });

            await client.Auth.AuthorizeAsync();

            await client.Channels.Get("test").HistoryAsync();

            callbackCount.Should().Be(2, "RSA4b1 - the expired token is replaced before it is used");

            var historyRequests = capturedRequests
                .Where(request => request.Path == "/channels/test/messages")
                .ToList();
            historyRequests.Should().HaveCount(1, "no request is made with the expired token");
            historyRequests[0].Headers["Authorization"].Should().Be(BearerHeader("fresh-token"));
        }

        // UTS: rest/unit/RSA4a2/no-renewal-without-callback-0
        [Fact]
        public async Task RSA4a2_NoRenewalWithoutCallback()
        {
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    req.RespondWith(401, TokenErrorBody(40142, "Token expired"));
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.Token = "static-token";
            });

            Func<Task> act = () => client.Channels.Get("test").HistoryAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40171, "RSA4a2 - there is no means to renew the token");

            requestCount.Should().Be(1, "no retry is attempted");
        }

        // UTS: rest/unit/RSA4b/renewal-via-authurl-2
        //
        // ADAPTED FIXTURE. The spec's authUrl answers with {"token": ..., "expires": ...}. This SDK
        // decides whether a JSON authUrl body is a TokenDetails or a TokenRequest by looking for an
        // `issued` field and nothing else (TokenDetails.IsToken, TokenDetails.cs:72), so that body is
        // misread as a TokenRequest, and since it has no keyName the SDK then POSTs to
        // `/keys//requestToken`. An `issued` field is added below so the test can exercise RSA4b's
        // actual subject - that a 40142 on an authUrl client drives a second authUrl call and a
        // retry. The discriminator is recorded separately in Uts/deviations.md; RSA8c requires an
        // authUrl to be able to return a TokenDetails, and one without `issued` is still a valid one.
        [Fact]
        public async Task RSA4b_RenewalViaAuthUrl()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    requestCount = requestCount + 1;

                    if (req.Url.Host == "example.com")
                    {
                        req.RespondWith(200, requestCount == 1
                            ? TokenBody("first-token")
                            : TokenBody("second-token"));
                    }
                    else if (requestCount == 2)
                    {
                        req.RespondWith(401, TokenErrorBody(40142, "Token expired"));
                    }
                    else
                    {
                        req.RespondWith(200, Array.Empty<object>());
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthUrl = new Uri("https://example.com/auth");
            });

            await client.Channels.Get("test").HistoryAsync();

            var authRequests = capturedRequests.Where(r => r.Url.Host == "example.com").ToList();
            authRequests.Should().HaveCount(2);

            var apiRequests = capturedRequests.Where(r => r.Url.Host != "example.com").ToList();
            apiRequests.Should().HaveCount(2);
            apiRequests[1].Headers["Authorization"].Should().Be(BearerHeader("second-token"));
        }

        // UTS: rest/unit/RSA4b/renewal-limit-no-loop-3
        [Fact]
        public async Task RSA4b_RenewalLimitNoLoop()
        {
            var callbackCount = 0;
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    req.RespondWith(401, TokenErrorBody(40142, "Token expired"));
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + callbackCount));
                };
            });

            Func<Task> act = () => client.Channels.Get("test").HistoryAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40142);

            callbackCount.Should().Be(2, "the initial token plus exactly one renewal");
            requestCount.Should().Be(2, "the original request plus exactly one retry");
        }

        // UTS: rest/unit/RSC10/request-retried-after-renewal-0
        [Fact]
        public async Task RSC10_RequestRetriedAfterRenewal()
        {
            var callbackCount = 0;
            var capturedRequests = new List<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Headers["Authorization"] == BearerHeader("token-1"))
                    {
                        req.RespondWith(401, TokenErrorBody(40142, "Token expired"));
                    }
                    else
                    {
                        req.RespondWith(200, new
                        {
                            channelId = "test",
                            status = new
                            {
                                isActive = true,
                                occupancy = new { metrics = new { connections = 0 } },
                            },
                        });
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + callbackCount));
                };
            });

            var result = await client.Channels.Get("test").StatusAsync();

            result.Should().NotBeNull("the caller never sees the 401 or the renewal");

            var channelRequests = capturedRequests.Where(r => r.Path == "/channels/test").ToList();
            channelRequests.Should().HaveCount(2);
            callbackCount.Should().Be(2);
            channelRequests[0].Headers["Authorization"].Should().Be(BearerHeader("token-1"));
            channelRequests[1].Headers["Authorization"].Should().Be(BearerHeader("token-2"));
        }

        // UTS: rest/unit/RSC10b/non-token-401-no-renewal-0
        [Fact]
        public async Task RSC10b_NonTokenUnauthorizedDoesNotRenew()
        {
            var callbackCount = 0;
            var requestCount = 0;

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    req.RespondWith(401, TokenErrorBody(40100, "Unauthorized"));
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    return Task.FromResult<object>(FreshToken("token-" + callbackCount));
                };
            });

            Func<Task> act = () => client.Channels.Get("test").StatusAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40100);

            requestCount.Should().Be(1, "RSC10b - a non-token 401 is not retried");
            callbackCount.Should().Be(1, "RSC10b - and prompts no renewal");
        }

        private static object TokenErrorBody(int code, string message) => new
        {
            error = new { code, statusCode = 401, message },
        };

        private static object TokenBody(string token) => new
        {
            token,
            expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),

            // Not in the spec's fixture - see the note on RSA4b_RenewalViaAuthUrl.
            issued = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
        };

        private static TokenDetails FreshToken(string token)
            => new TokenDetails(token) { Expires = DateTimeOffset.UtcNow.AddHours(1) };

        private static string BearerHeader(string token)
            => "Bearer " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
    }
}
