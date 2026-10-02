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
    /// Derived from uts/rest/unit/auth/authorize.md in ably/specification.
    ///
    /// Spec points: RSA10a, RSA10b, RSA10e, RSA10g, RSA10h, RSA10i, RSA10j, RSA10k, RSA10l
    ///
    /// <para>
    /// The letters above are the spec file's own section headings, kept verbatim so each test is
    /// traceable to its UTS test ID. Several do not line up with the clause of the same name in
    /// features.md - the file's "RSA10e - saves tokenParams for reuse" is features.md's RSA10g, and
    /// its "RSA10h - authOptions replaces defaults" is features.md's RSA10j. The behaviour each test
    /// asserts is the behaviour its own section describes.
    /// </para>
    ///
    /// <para>
    /// The spec's <c>client.auth.tokenDetails</c> is <c>AblyAuth.CurrentToken</c> here. It is public
    /// but not on <c>IAblyAuth</c>, so the tests reach it through the concrete <c>AblyAuth</c> that
    /// <c>PubSubHttpClient.AblyAuth</c> exposes — the test assembly already has internal access.
    /// Idiomatic naming, not a deviation.
    /// </para>
    ///
    /// <para>
    /// Bearer assertions go through <see cref="BearerHeader"/>; see <see cref="AuthSchemeTests"/> for
    /// why the token is Base64-encoded in the header.
    /// </para>
    /// </summary>
    public class AuthorizeTests : UtsTestBase
    {
        public AuthorizeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA10a/authorize-default-params-0
        [Fact]
        public async Task RSA10a_AuthorizeDefaultParams()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = TokenThenApiMock(capturedRequests, "obtained-token");

            var client = RestClient(mockHttp);

            var tokenDetails = await client.Auth.AuthorizeAsync();

            tokenDetails.Should().NotBeNull();
            tokenDetails.Token.Should().Be("obtained-token");

            await client.Channels.Get("test").StatusAsync();

            capturedRequests.Last().Headers["Authorization"]
                .Should().Be(BearerHeader("obtained-token"));
        }

        // UTS: rest/unit/RSA10b/authorize-explicit-params-0
        [Fact]
        public async Task RSA10b_AuthorizeExplicitParams()
        {
            TokenParams receivedParams = null;

            var client = RestClient(new MockHttpClient(), configure: options =>
            {
                options.Key = null;
                options.ClientId = "default-client";
                options.AuthCallback = tokenParams =>
                {
                    receivedParams = tokenParams;
                    return Task.FromResult<object>(TokenFor("callback-token"));
                };
            });

            await client.Auth.AuthorizeAsync(new TokenParams
            {
                ClientId = "override-client",
                Ttl = TimeSpan.FromHours(2),
            });

            receivedParams.Should().NotBeNull();
            receivedParams.ClientId.Should().Be("override-client", "the explicit param overrides the default");
            receivedParams.Ttl.Should().Be(TimeSpan.FromHours(2));
        }

        // UTS: rest/unit/RSA10e/authorize-saves-params-0
        [Fact]
        public async Task RSA10e_AuthorizeSavesParams()
        {
            var invocations = new List<TokenParams>();

            var client = RestClient(new MockHttpClient(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    invocations.Add(tokenParams);
                    return Task.FromResult<object>(TokenFor("token-" + (invocations.Count + 1)));
                };
            });

            await client.Auth.AuthorizeAsync(new TokenParams
            {
                ClientId = "saved-client",
                Ttl = TimeSpan.FromHours(1),
            });

            // RSA10e: the second call carries no params, so the saved ones are reused.
            await client.Auth.AuthorizeAsync();

            invocations.Should().HaveCount(2);
            invocations[1].ClientId.Should().Be("saved-client");
            invocations[1].Ttl.Should().Be(TimeSpan.FromHours(1));
        }

        // UTS: rest/unit/RSA10g/authorize-updates-token-details-0
        [Fact]
        public async Task RSA10g_AuthorizeUpdatesTokenDetails()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = TokenThenApiMock(capturedRequests, "new-token", "token-client");

            var client = RestClient(mockHttp);

            client.AblyAuth.CurrentToken.Should().BeNull("nothing has been authorized yet");

            var result = await client.Auth.AuthorizeAsync();

            client.AblyAuth.CurrentToken.Should().NotBeNull();
            client.AblyAuth.CurrentToken.Token.Should().Be("new-token");
            client.AblyAuth.CurrentToken.ClientId.Should().Be("token-client");
            client.AblyAuth.CurrentToken.Should().BeSameAs(result, "RSA10g - the same object is held");
        }

        // UTS: rest/unit/RSA10h/authorize-replaces-auth-options-0
        [Fact]
        public async Task RSA10h_AuthorizeReplacesAuthOptions()
        {
            var originalCalled = false;
            var replacementCalled = false;

            var client = RestClient(new MockHttpClient(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    originalCalled = true;
                    return Task.FromResult<object>(TokenFor("original-token"));
                };
            });

            await client.Auth.AuthorizeAsync(
                null,
                new AuthOptions
                {
                    AuthCallback = tokenParams =>
                    {
                        replacementCalled = true;
                        return Task.FromResult<object>(TokenFor("replacement-token"));
                    },
                });

            originalCalled.Should().BeFalse("RSA10h - the supplied authOptions replace the defaults");
            replacementCalled.Should().BeTrue();
        }

        // UTS: rest/unit/RSA10i/authorize-preserves-key-0
        [Fact]
        public async Task RSA10i_AuthorizePreservesKey()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, TokenBody("new-auth-token"));
                });

            var client = RestClient(mockHttp);

            // The spec hands authorize() an authUrl and expects the key from the constructor to
            // survive for later use.
            await client.Auth.AuthorizeAsync(
                null,
                new AuthOptions { AuthUrl = new Uri("https://new-auth.example.com/token") });

            capturedRequests.Should().NotBeEmpty();
            capturedRequests[0].Url.Host.Should().Be("new-auth.example.com");

            client.Options.Key.Should().Be(
                UtsClients.ValidKey,
                "RSA10i - the key given at construction is not discarded");
        }

        // UTS: rest/unit/RSA10j/authorize-replaces-existing-token-0
        [Fact]
        public async Task RSA10j_AuthorizeReplacesExistingToken()
        {
            var tokenCount = 0;

            var client = RestClient(new MockHttpClient(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    tokenCount = tokenCount + 1;
                    return Task.FromResult<object>(TokenFor("token-" + tokenCount));
                };
            });

            var first = await client.Auth.AuthorizeAsync();
            var second = await client.Auth.AuthorizeAsync();

            first.Token.Should().Be("token-1");
            second.Token.Should().Be("token-2");
            client.AblyAuth.CurrentToken.Token.Should().Be("token-2");
        }

        // UTS: rest/unit/RSA10k/authorize-query-time-0
        //
        // ADAPTED. The spec's setup passes AuthOptions(queryTime: true) alone and still expects the
        // token request to succeed, which assumes the constructor key stays usable - the theory the
        // spec's own RSA10i section states. This SDK takes the literal RSA10j reading instead
        // ("even if empty, the AuthOptions supersede any previously configured AuthOptions"):
        // AblyAuth.AuthorizeAsync replaces CurrentAuthOptions wholesale, and the token request then
        // reads authOptions.Key, which is empty - so the unadapted test dies on 80019 "TokenAuth is
        // on but there is no way to generate one" before it can reach /time.
        //
        // The key is restated in the AuthOptions so the test exercises RSA10k's actual subject,
        // which is that queryTime sources the signing timestamp from the server. The two readings of
        // RSA10j are recorded as a spec conflict in Uts/deviations.md rather than as an SDK defect:
        // RSA10j's text supports what this SDK does.
        [Fact]
        public async Task RSA10k_AuthorizeQueryTime()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Path == "/time")
                    {
                        req.RespondWith(200, new object[] { DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() });
                    }
                    else
                    {
                        req.RespondWith(200, TokenBody("timed-token"));
                    }
                });

            var client = RestClient(mockHttp);

            await client.Auth.AuthorizeAsync(
                null,
                new AuthOptions { QueryTime = true, Key = UtsClients.ValidKey });

            capturedRequests.Should().Contain(
                request => request.Path == "/time",
                "RSA10k - queryTime makes the client ask the server for the time to sign with");
        }

        // UTS: rest/unit/RSA10l/authorize-error-propagated-0
        [Fact]
        public async Task RSA10l_AuthorizeErrorPropagated()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(401, new
                {
                    error = new { message = "Invalid credentials", code = 40100, statusCode = 401 },
                }));

            var client = RestClient(mockHttp, configure: options => options.Key = "invalid.key:secret");

            Func<Task> act = () => client.Auth.AuthorizeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40100);
            ((int)error.StatusCode.Value).Should().Be(401);
        }

        private static MockHttpClient TokenThenApiMock(
            List<PendingHttpRequest> capturedRequests,
            string token,
            string clientId = null)
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Path.Contains("/requestToken"))
                    {
                        req.RespondWith(200, TokenBody(token, clientId));
                    }
                    else
                    {
                        req.RespondWith(200, new { channelId = "test", status = new { isActive = true } });
                    }
                });

        private static TokenDetails TokenFor(string token)
            => new TokenDetails(token) { Expires = DateTimeOffset.UtcNow.AddHours(1) };

        private static object TokenBody(string token, string clientId = null) => new
        {
            token,
            keyName = "appId.keyId",
            clientId,
            issued = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
            expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
            capability = "{\"*\":[\"*\"]}",
        };

        private static string BearerHeader(string token)
            => "Bearer " + Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(token));
    }
}
