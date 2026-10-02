using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Auth
{
    /// <summary>
    /// Derived from uts/rest/unit/auth/client_id.md in ably/specification.
    ///
    /// Spec points: RSA7, RSA7a, RSA7b, RSA7c, RSA12, RSA12a, RSA12b, RSA15, RSA15a, RSA15b, RSA15c
    ///
    /// <para>
    /// <c>auth.clientId</c> is <c>IAblyAuth.ClientId</c>. Its resolution order lives in
    /// <c>AblyAuth.cs:100</c>: the connection's clientId, then the current token's, then the current
    /// token params', then <c>ClientOptions</c>. Several tests below turn on that order.
    /// </para>
    ///
    /// <para>
    /// RSA15c's realtime half is out of scope here by the spec's own direction - it points at
    /// realtime/integration/auth.md.
    /// </para>
    /// </summary>
    public class ClientIdTests : UtsTestBase
    {
        public ClientIdTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA7a/clientid-from-options-0
        [Fact]
        public void RSA7a_ClientIdFromOptions()
        {
            var client = RestClient(new MockHttpClient(), configure: options => options.ClientId = "my-client-id");

            client.Auth.ClientId.Should().Be("my-client-id");
        }

        // UTS: rest/unit/RSA7b/clientid-from-token-details-0
        [Fact]
        public void RSA7b_ClientIdFromTokenDetails()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.TokenDetails = TokenFor("token-with-clientId", "token-client-id");
            });

            client.Auth.ClientId.Should().Be("token-client-id");
        }

        // UTS: rest/unit/RSA7b/clientid-from-callback-token-1
        [Fact]
        public async Task RSA7b_ClientIdFromCallbackToken()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                    Task.FromResult<object>(TokenFor("callback-token", "callback-client-id"));
            });

            await client.Channels.Get("test").StatusAsync();

            client.Auth.ClientId.Should().Be("callback-client-id");
        }

        // UTS: rest/unit/RSA7c/clientid-null-unidentified-0
        [Fact]
        public void RSA7c_ClientIdNullUnidentified()
        {
            var client = RestClient(new MockHttpClient());

            client.Auth.ClientId.Should().BeNull();
        }

        // UTS: rest/unit/RSA7c/clientid-null-unidentified-token-1
        [Fact]
        public void RSA7c_ClientIdNullUnidentifiedToken()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.TokenDetails = TokenFor("token-without-clientId");
            });

            client.Auth.ClientId.Should().BeNull();
        }

        // UTS: rest/unit/RSA12a/clientid-passed-to-callback-0
        [Fact]
        public async Task RSA12a_ClientIdPassedToCallback()
        {
            var receivedParams = new List<TokenParams>();

            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.ClientId = "library-client-id";
                options.AuthCallback = tokenParams =>
                {
                    receivedParams.Add(tokenParams);
                    return Task.FromResult<object>(TokenFor("tok"));
                };
            });

            await client.Channels.Get("test").StatusAsync();

            receivedParams.Should().NotBeEmpty();
            receivedParams[0].ClientId.Should().Be("library-client-id");
        }

        // UTS: rest/unit/RSA12b/clientid-sent-to-authurl-0
        [Fact]
        public async Task RSA12b_ClientIdSentToAuthUrl()
        {
            var capturedRequests = new List<PendingHttpRequest>();
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    if (req.Url.Host == "auth.example.com")
                    {
                        req.RespondWith(200, new
                        {
                            token = "url-token",
                            expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                        });
                    }
                    else
                    {
                        req.RespondWith(200, StatusBody);
                    }
                });

            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthUrl = new Uri("https://auth.example.com/token");
                options.ClientId = "url-client-id";
            });

            await client.Channels.Get("test").StatusAsync();

            var authRequest = capturedRequests[0];
            authRequest.Url.Host.Should().Be("auth.example.com");

            // The spec accepts the clientId either as a query param on a GET or form-encoded in the
            // body of a POST. This SDK issues a GET.
            if (authRequest.Method == "GET")
            {
                authRequest.Url.QueryParams["clientId"].Should().Be("url-client-id");
            }
            else
            {
                authRequest.BodyText.Should().Contain("clientId=url-client-id");
            }
        }

        // UTS: rest/unit/RSA7/clientid-updated-after-authorize-0
        [Fact]
        public async Task RSA7_ClientIdUpdatedAfterAuthorize()
        {
            var tokenCount = 0;

            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    tokenCount = tokenCount + 1;
                    return Task.FromResult<object>(TokenFor("token-" + tokenCount, "client-" + tokenCount));
                };
            });

            await client.Channels.Get("test").StatusAsync();

            client.Auth.ClientId.Should().Be("client-1");

            await client.Auth.AuthorizeAsync();

            client.Auth.ClientId.Should().Be("client-2");
        }

        // UTS: rest/unit/RSA12/wildcard-clientid-0
        [Fact]
        public void RSA12_WildcardClientId()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.TokenDetails = TokenFor("wildcard-token", "*");
            });

            client.Auth.ClientId.Should().Be("*", "a wildcard token identity is reported as-is");
        }

        // UTS: rest/unit/RSA7/clientid-mismatch-error-1, case 1 - matching ids
        [Fact]
        public void RSA7_ClientIdMatching()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.ClientId = "client-a";
                options.TokenDetails = TokenFor("matching-token", "client-a");
            });

            client.Auth.ClientId.Should().Be("client-a");
        }

        // UTS: rest/unit/RSA7/clientid-mismatch-error-1, case 3 - token carries no identity
        [Fact]
        public void RSA7_ClientIdFromOptionsWhenTokenUnidentified()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.ClientId = "client-a";
                options.TokenDetails = TokenFor("unidentified-token");
            });

            client.Auth.ClientId.Should().Be("client-a", "the explicit clientId stands");
        }

        // UTS: rest/unit/RSA7/clientid-mismatch-error-1, case 5 - identity inherited from the token
        [Fact]
        public void RSA7_ClientIdInheritedFromToken()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.TokenDetails = TokenFor("identified-token", "client-b");
            });

            client.Auth.ClientId.Should().Be("client-b");
        }

        // UTS: rest/unit/RSA7/clientid-mismatch-error-1, case 2 - mismatch is an error
        // UTS: rest/unit/RSA15a/token-clientid-must-match-0
        // UTS: rest/unit/RSA15c/incompatible-clientid-error-0 (REST half)
        //
        // DEVIATION. RSA15a requires that a clientId in ClientOptions match any non-wildcard
        // clientId in the TokenDetails, and RSA15c requires a REST client to report the
        // incompatibility as an error - 40102 per the spec file's assertion. This SDK never compares
        // the two: ErrorCodes.IncompatibleCredentials (40102) is declared and never used, and
        // AblyAuth.ClientId (AblyAuth.cs:100) simply prefers the token's clientId over the
        // configured one, so a mismatched pair is silently resolved in the token's favour. The
        // mismatch is only caught per-message, much later, by ValidateClientIds. See
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RSA15a_MismatchedClientIdIsAnError()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.ClientId = "client-a";
                options.TokenDetails = TokenFor("mismatched-token", "client-b");
            });

            Func<Task> act = () => client.Channels.Get("test").StatusAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40102);
        }

        // UTS: rest/unit/RSA15b/wildcard-token-permits-any-0
        //
        // DEVIATION. RSA15b says a wildcard token clientId permits the client to be "either
        // unidentified or identified by providing a clientId", and the spec asserts auth.clientId is
        // then the configured one. AblyAuth.ClientId (AblyAuth.cs:100) returns the token's clientId
        // first, so it reports "*" and the client's own identity is lost. Same accessor as the
        // RSA15a entry but a distinct requirement, so recorded separately. See Uts/deviations.md.
        [DeviationFact]
        public void RSA15b_WildcardTokenPermitsAnyClientId()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.ClientId = "any-client";
                options.TokenDetails = TokenFor("wildcard-token", "*");
            });

            client.Auth.ClientId.Should().Be(
                "any-client",
                "RSA15b - a wildcard token does not override the client's own identity");
        }

        private static MockHttpClient StatusMock()
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, StatusBody));

        private static object StatusBody => new { channelId = "test", status = new { isActive = true } };

        private static TokenDetails TokenFor(string token, string clientId = null)
            => new TokenDetails(token)
            {
                Expires = DateTimeOffset.UtcNow.AddHours(1),
                ClientId = clientId,
            };
    }
}
