using System;
using System.Threading.Tasks;
using Ably.PubSub.Push;
using Ably.PubSub.Tests.Push;
using Ably.PubSub.Tests.Uts.Helpers;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.Rest.Unit.Auth
{
    /// <summary>
    /// Derived from uts/rest/unit/auth/token_details.md in ably/specification.
    ///
    /// Spec points: RSA16, RSA16a, RSA16b, RSA16c, RSA16d
    ///
    /// <para>
    /// The spec's <c>auth.tokenDetails</c> is <c>AblyAuth.CurrentToken</c>, reached through
    /// <c>PubSubHttpClient.AblyAuth</c> - see <see cref="AuthorizeTests"/>.
    /// </para>
    ///
    /// <para>
    /// <c>TokenDetails.Expires</c> and <c>.Issued</c> are non-nullable <c>DateTimeOffset</c>s here,
    /// so the spec's "IS null" for those two fields reads as "left at its default" - see A1 in
    /// Uts/deviations.md. The string and object fields are compared against null as written.
    /// </para>
    ///
    /// <para>
    /// Named TokenDetailsAccessorTests rather than TokenDetailsTests: the subject is the accessor on
    /// auth, not the type, and the repo already has tests named after the type.
    /// </para>
    /// </summary>
    public class TokenDetailsAccessorTests : UtsTestBase
    {
        public TokenDetailsAccessorTests(ITestOutputHelper output)
            : base(output)
        {
        }

        // UTS: rest/unit/RSA16a/token-from-callback-0
        [Fact]
        public async Task RSA16a_TokenFromCallback()
        {
            var issued = DateTimeOffset.UtcNow;

            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("callback-token-abc")
                {
                    Expires = issued.AddHours(1),
                    Issued = issued,
                    ClientId = "my-client",
                });
            });

            await client.Channels.Get("test").StatusAsync();

            var tokenDetails = client.AblyAuth.CurrentToken;
            tokenDetails.Should().NotBeNull();
            tokenDetails.Token.Should().Be("callback-token-abc");
            tokenDetails.ClientId.Should().Be("my-client");
            tokenDetails.Expires.Should().NotBe(default(DateTimeOffset));
            tokenDetails.Issued.Should().NotBe(default(DateTimeOffset));
        }

        // UTS: rest/unit/RSA16a/token-from-request-token-1
        [Fact]
        public async Task RSA16a_TokenFromRequestToken()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (req.Path.Contains("/requestToken"))
                    {
                        req.RespondWith(200, new
                        {
                            token = "requested-token-xyz",
                            expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(),
                            issued = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                            keyName = "appId.keyId",
                            clientId = "token-client",
                        });
                    }
                    else
                    {
                        req.RespondWith(200, StatusBody);
                    }
                });

            var client = RestClient(mockHttp);

            await client.Auth.AuthorizeAsync();

            var tokenDetails = client.AblyAuth.CurrentToken;
            tokenDetails.Should().NotBeNull();
            tokenDetails.Token.Should().Be("requested-token-xyz");
            tokenDetails.ClientId.Should().Be("token-client");
        }

        // UTS: rest/unit/RSA16b/token-string-in-options-0
        [Fact]
        public void RSA16b_TokenStringInOptions()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.Token = "standalone-token-string";
            });

            var tokenDetails = client.AblyAuth.CurrentToken;
            tokenDetails.Should().NotBeNull();
            tokenDetails.Token.Should().Be("standalone-token-string");

            // RSA16b: only `token` is populated. Expires/Issued are value types here, so "null" is
            // their default - see A1 in Uts/deviations.md.
            tokenDetails.Expires.Should().Be(default(DateTimeOffset));
            tokenDetails.Issued.Should().Be(default(DateTimeOffset));
            tokenDetails.ClientId.Should().BeNull();
            tokenDetails.Capability.Should().BeNull();
        }

        // UTS: rest/unit/RSA16b/token-string-from-callback-1
        //
        // DEVIATION. RSA16b covers a token arriving "without the corresponding TokenDetails", and
        // this case has an authCallback return a bare token string. This SDK reads a string result as
        // a serialised TokenRequest instead (AblyAuth.cs:335 routes `callbackResult is string` into
        // GetTokenRequest), so a plain token string fails to parse. Same root cause as the RSA8d JWT
        // deviation in AuthCallbackTests - recorded once, in Uts/deviations.md.
        [DeviationFact]
        public async Task RSA16b_TokenStringFromCallback()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>("just-a-token-string");
            });

            await client.Channels.Get("test").StatusAsync();

            var tokenDetails = client.AblyAuth.CurrentToken;
            tokenDetails.Should().NotBeNull();
            tokenDetails.Token.Should().Be("just-a-token-string");
            tokenDetails.Expires.Should().Be(default(DateTimeOffset));
            tokenDetails.Issued.Should().Be(default(DateTimeOffset));
        }

        // UTS: rest/unit/RSA16c/set-on-instantiation-0
        [Fact]
        public void RSA16c_SetOnInstantiation()
        {
            var initialToken = new TokenDetails("initial-token")
            {
                Expires = DateTimeOffset.UtcNow.AddHours(1),
                Issued = DateTimeOffset.UtcNow,
                ClientId = "initial-client",
            };

            var client = RestClient(new MockHttpClient(), configure: options =>
            {
                options.Key = null;
                options.TokenDetails = initialToken;
            });

            var tokenDetails = client.AblyAuth.CurrentToken;
            tokenDetails.Should().NotBeNull();
            tokenDetails.Token.Should().Be("initial-token");
            tokenDetails.ClientId.Should().Be("initial-client");
        }

        // UTS: rest/unit/RSA16c/updated-after-authorize-1
        [Fact]
        public async Task RSA16c_UpdatedAfterAuthorize()
        {
            var tokenCount = 0;

            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    tokenCount = tokenCount + 1;
                    return Task.FromResult<object>(new TokenDetails("token-v" + tokenCount)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                        ClientId = "client-v" + tokenCount,
                    });
                };
            });

            await client.Auth.AuthorizeAsync();
            var firstToken = client.AblyAuth.CurrentToken;

            await client.Auth.AuthorizeAsync();
            var secondToken = client.AblyAuth.CurrentToken;

            firstToken.Token.Should().Be("token-v1");
            firstToken.ClientId.Should().Be("client-v1");
            secondToken.Token.Should().Be("token-v2");
            secondToken.ClientId.Should().Be("client-v2");
            firstToken.Token.Should().NotBe(secondToken.Token);
        }

        // No UTS id: this is a defect found by running the unit tier in one process, pinned down
        // where it surfaced - RSA16c_UpdatedAfterAuthorize above, which failed only when a push
        // test had run first.
        //
        // DEVIATION, D48. Authorizing a REST client that has no mobile device throws a
        // NullReferenceException out of AuthorizeAsync, if any other client in the process has
        // initialised the static LocalDevice.Instance. See Uts/deviations.md.
        [DeviationFact]
        public async Task AuthorizeDoesNotThrowWhenAnotherClientHasInitialisedTheLocalDevice()
        {
            // The static is set directly rather than by letting a push client populate it. The
            // harness deliberately keeps every UTS client off it - see UtsClients.RestClientWithDevice
            // - so this test has to stand the condition up itself, and put it back afterwards:
            // the repo's own push tests read the same static while this one runs.
            var previous = LocalDevice.Instance;
            LocalDevice.Instance = LocalDevice.Create(mobileDevice: new FakeMobileDevice());

            try
            {
                // A client that never asked for push, authorizing while some other client in the
                // process has a device.
                var plain = RestClient(StatusMock(), configure: options =>
                {
                    options.Key = null;
                    options.AuthCallback = tokenParams => Task.FromResult<object>(
                        new TokenDetails("plain-token")
                        {
                            Expires = DateTimeOffset.UtcNow.AddHours(1),
                            ClientId = "plain-client",
                        });
                });

                await plain.Auth.AuthorizeAsync();

                plain.AblyAuth.CurrentToken.ClientId.Should().Be(
                    "plain-client",
                    "a client with no mobile device has no device to update");
            }
            finally
            {
                LocalDevice.Instance = previous;
            }
        }

        // UTS: rest/unit/RSA16c/updated-after-expiry-renewal-2
        //
        // DEVIATION. RSA16c requires tokenDetails to be replaced by a "library-initiated renewal
        // resulting from expiry". With the clock advanced past the token's expiry this SDK reuses the
        // expired token: TokenDetailsExtensions.IsValidToken (TokenDetails.cs:131) returns true
        // whenever the server time it is handed is null, and AblyAuth.ServerNow is null unless
        // QueryTime is set - so no expiry check happens at all for an ordinary client. Recorded
        // alongside the RSC10 renewal entry in Uts/deviations.md, which shares the cause.
        [DeviationFact]
        public async Task RSA16c_UpdatedAfterExpiryRenewal()
        {
            var clock = new TestClock();
            var tokenCount = 0;

            var client = RestClient(StatusMock(), clock, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    tokenCount = tokenCount + 1;
                    return Task.FromResult<object>(new TokenDetails("token-v" + tokenCount)
                    {
                        Issued = clock.Now,
                        Expires = clock.Now.AddMilliseconds(1000),
                        ClientId = "client-v" + tokenCount,
                    });
                };
            });

            await client.Channels.Get("test").StatusAsync();
            var firstToken = client.AblyAuth.CurrentToken;

            clock.Advance(2000);

            await client.Channels.Get("test").StatusAsync();
            var secondToken = client.AblyAuth.CurrentToken;

            firstToken.Token.Should().Be("token-v1");
            secondToken.Token.Should().Be("token-v2", "RSA16c - an expired token is renewed");
        }

        // UTS: rest/unit/RSA16c/updated-after-40142-renewal-3
        [Fact]
        public async Task RSA16c_UpdatedAfter40142Renewal()
        {
            var requestCount = 0;
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    requestCount = requestCount + 1;
                    if (requestCount == 1)
                    {
                        req.RespondWith(401, new
                        {
                            error = new { code = 40142, statusCode = 401, message = "Token expired" },
                        });
                    }
                    else
                    {
                        req.RespondWith(200, StatusBody);
                    }
                });

            var tokenCount = 0;
            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    tokenCount = tokenCount + 1;
                    return Task.FromResult<object>(new TokenDetails("token-v" + tokenCount)
                    {
                        Expires = DateTimeOffset.UtcNow.AddHours(1),
                        ClientId = "client-v" + tokenCount,
                    });
                };
            });

            await client.Auth.AuthorizeAsync();
            var firstToken = client.AblyAuth.CurrentToken;

            await client.Channels.Get("test").StatusAsync();
            var secondToken = client.AblyAuth.CurrentToken;

            firstToken.Token.Should().Be("token-v1");
            secondToken.Token.Should().Be("token-v2");
        }

        // UTS: rest/unit/RSA16d/null-with-basic-auth-0
        [Fact]
        public async Task RSA16d_NullWithBasicAuth()
        {
            var client = RestClient(StatusMock());

            await client.Channels.Get("test").StatusAsync();

            client.AblyAuth.CurrentToken.Should().BeNull("basic auth holds no token");
        }

        // UTS: rest/unit/RSA16d/null-before-token-obtained-1
        [Fact]
        public void RSA16d_NullBeforeTokenObtained()
        {
            var client = RestClient(new MockHttpClient(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("my-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddHours(1),
                });
            });

            client.AblyAuth.CurrentToken.Should().BeNull("no request has been made yet");
        }

        // UTS: rest/unit/RSA16d/null-after-invalidation-2
        //
        // DEVIATION. RSA16d requires tokenDetails to be null "after a previous token has been
        // determined to be invalid or expired". Here the 40142 renewal is attempted and the
        // authCallback throws, so no replacement arrives - and this SDK leaves the dead token in
        // place. AblyAuth only ever assigns CurrentToken on success; nothing clears it when renewal
        // fails. See Uts/deviations.md.
        [DeviationFact]
        public async Task RSA16d_NullAfterInvalidation()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(401, new
                {
                    error = new { code = 40142, statusCode = 401, message = "Token expired" },
                }));

            var callbackCount = 0;
            var client = RestClient(mockHttp, configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams =>
                {
                    callbackCount = callbackCount + 1;
                    if (callbackCount == 1)
                    {
                        return Task.FromResult<object>(new TokenDetails("first-token")
                        {
                            Expires = DateTimeOffset.UtcNow.AddHours(1),
                        });
                    }

                    throw new AblyException("Cannot obtain new token");
                };
            });

            await client.Auth.AuthorizeAsync();
            client.AblyAuth.CurrentToken.Should().NotBeNull();
            client.AblyAuth.CurrentToken.Token.Should().Be("first-token");

            Func<Task> act = () => client.Channels.Get("test").StatusAsync();
            await act.Should().ThrowAsync<AblyException>();

            client.AblyAuth.CurrentToken.Should().BeNull(
                "RSA16d - a token known to be invalid is not kept");
        }

        // UTS: rest/unit/RSA16d/null-after-switch-to-basic-3
        //
        // DEVIATION. RSA16d requires tokenDetails to be null when the library is using basic auth,
        // and this case reaches basic auth by passing authOptions with a key and useTokenAuth:false
        // to authorize(). This SDK's AuthorizeAsync always requests a token - AblyAuth.cs:554 goes
        // straight to RequestTokenAsync and never re-runs CheckAndGetAuthMethod - so AuthOptions
        // .UseTokenAuth is read at construction only and ignored here. The key in the authOptions is
        // used to sign a fresh token request and CurrentToken is left non-null. See
        // Uts/deviations.md.
        [DeviationFact]
        public async Task RSA16d_NullAfterSwitchToBasic()
        {
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    if (req.Path.Contains("/requestToken"))
                    {
                        req.RespondWith(200, new
                        {
                            token = "token-from-key",
                            keyName = "appId.keyId",
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
                options.AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("my-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddHours(1),
                });
            });

            await client.Auth.AuthorizeAsync();
            client.AblyAuth.CurrentToken.Should().NotBeNull();

            await client.Auth.AuthorizeAsync(
                null,
                new AuthOptions
                {
                    Key = UtsClients.ValidKey,
                    UseTokenAuth = false,
                });

            client.AblyAuth.CurrentToken.Should().BeNull(
                "RSA16d - a client using basic auth holds no token");
        }

        // UTS: rest/unit/RSA16a/preserved-across-requests-0
        [Fact]
        public async Task RSA16a_PreservedAcrossRequests()
        {
            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("stable-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddHours(1),
                    ClientId = "stable-client",
                });
            });

            await client.Channels.Get("test").StatusAsync();
            var firstCheck = client.AblyAuth.CurrentToken;

            await client.Channels.Get("test").StatusAsync();
            var secondCheck = client.AblyAuth.CurrentToken;

            await client.Channels.Get("test").StatusAsync();
            var thirdCheck = client.AblyAuth.CurrentToken;

            firstCheck.Token.Should().Be("stable-token");
            secondCheck.Token.Should().Be("stable-token");
            thirdCheck.Token.Should().Be("stable-token");
        }

        // UTS: rest/unit/RSA16a/reflects-capability-1
        [Fact]
        public async Task RSA16a_ReflectsCapability()
        {
            const string CapabilityJson = "{\"channel1\":[\"publish\",\"subscribe\"],\"channel2\":[\"subscribe\"]}";

            var client = RestClient(StatusMock(), configure: options =>
            {
                options.Key = null;
                options.AuthCallback = tokenParams => Task.FromResult<object>(new TokenDetails("capable-token")
                {
                    Expires = DateTimeOffset.UtcNow.AddHours(1),
                    Capability = new Capability(CapabilityJson),
                });
            });

            await client.Channels.Get("test").StatusAsync();

            var tokenDetails = client.AblyAuth.CurrentToken;
            tokenDetails.Should().NotBeNull();
            tokenDetails.Capability.Should().NotBeNull();

            // Capability is a parsed object here rather than the raw string the spec compares, so the
            // comparison is between two parsed capabilities instead of two strings.
            tokenDetails.Capability.Should().Be(new Capability(CapabilityJson));
        }

        private static MockHttpClient StatusMock()
            => new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req => req.RespondWith(200, StatusBody));

        private static object StatusBody => new { channelId = "test", status = new { isActive = true } };
    }
}
