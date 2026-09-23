using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using FluentAssertions;
using IO.Ably.Realtime;
using IO.Ably.Tests.Infrastructure;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests
{
    [Collection("SandBox Collection")]
    [Trait("type", "integration")]
    public class AuthSandboxSpecs : SandboxSpecs
    {
        private const string ErrorUrl = "https://echo.ably.io/respondwith?status=500";

        public AuthSandboxSpecs(AblySandboxFixture fixture, ITestOutputHelper output)
            : base(fixture, output)
        {
        }

        private static TokenParams CreateTokenParams(Capability capability, TimeSpan? ttl = null)
        {
            var res = new TokenParams();
            res.ClientId = "John";
            res.Capability = capability;
            if (ttl.HasValue)
            {
                res.Ttl = ttl.Value;
            }

            return res;
        }

        [Theory]
        [ProtocolData]
        public async Task RSA4Helper_RestClient_ShouldTrackRequests(Protocol protocol)
        {
            var authClient = await GetRestClient(protocol);
            var token = await authClient.AblyAuth.RequestTokenAsync(new TokenParams { ClientId = "123" });
            var helper = new Rsa4Helper(this);
            var restClient = await helper.GetRestClientWithRequests(protocol, token, invalidateKey: true);
            helper.Requests.Count.Should().Be(0);
            await restClient.TimeAsync();
            helper.Requests.Count.Should().Be(1);
            var realtimeClient = await helper.GetRealTimeClientWithRequests(protocol, token, invalidateKey: true);
            helper.Requests.Count.Should().Be(1);
            await realtimeClient.RestClient.TimeAsync();
            helper.Requests.Count.Should().Be(2);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4a2")]
        public async Task RestClient_WhenTokenExpired_ShouldNotRetryAndRaiseError(Protocol protocol)
        {
            var helper = new Rsa4Helper(this);

            // Get a very short lived token and wait for it to expire
            var authClient = await GetRestClient(protocol);
            var almostExpiredToken = await authClient.AblyAuth.RequestTokenAsync(new TokenParams { ClientId = "123", Ttl = TimeSpan.FromMilliseconds(1) });
            await Task.Delay(TimeSpan.FromMilliseconds(500));

            // Modify the expiry date to fool the client it has a valid token
            almostExpiredToken.Expires = DateTimeOffset.UtcNow.AddHours(1);

            // create a new client with the token
            // set the Key to an empty string to override the sandbox settings
            var restClient = await helper.GetRestClientWithRequests(protocol, almostExpiredToken, invalidateKey: true);

            var now = DateTimeOffset.UtcNow;

            // check the client thinks the token is valid
            restClient.AblyAuth.CurrentToken.IsValidToken(now).Should().BeTrue();

            var channelName = "RSA4a".AddRandomSuffix();

            try
            {
                await restClient.Channels.Get(channelName).PublishAsync("event", "data");
                throw new Exception("Unexpected success, the preceding code should have raised an AblyException");
            }
            catch (AblyException e)
            {
                // the server responds with a token error
                // (401 HTTP status code and an Ably error value 40140 <= code < 40150)
                // As the token is expired we can expect a specific code "40142": "token expired"
                e.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
                e.ErrorInfo.Code.Should().Be(ErrorCodes.NoMeansProvidedToRenewAuthToken);
            }

            // did not retry the request
            helper.Requests.Count.Should().Be(1, "only one request should have been attempted");
            helper.Requests[0].Url.Should().Be($"/channels/{channelName}/messages", "only the publish request should have been attempted");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4a2")]
        public async Task RealtimeClient_NewInstanceWithExpiredToken_ShouldNotRetryAndHaveError(Protocol protocol)
        {
            var helper = new Rsa4Helper(this);
            var authClient = await GetRestClient(protocol);
            var almostExpiredToken = await authClient.AblyAuth.RequestTokenAsync(new TokenParams { ClientId = "123", Ttl = TimeSpan.FromMilliseconds(1) });
            await Task.Delay(TimeSpan.FromMilliseconds(2));

            // Modify the expiry date to fool the client it has a valid token
            almostExpiredToken.Expires = DateTimeOffset.UtcNow.AddHours(1);

            // get a realtime client with no key
            var realtimeClient = await helper.GetRealTimeClientWithRequests(protocol, almostExpiredToken, invalidateKey: true);

            bool connected = false;
            realtimeClient.Connection.Once(ConnectionEvent.Connected, (_) => { connected = true; });

            // assert that there is no pre-existing error
            realtimeClient.Connection.ErrorReason.Should().BeNull();

            await realtimeClient.WaitForState(ConnectionState.Failed);
            realtimeClient.Connection.State.Should().Be(ConnectionState.Failed);
            connected.Should().BeFalse();

            realtimeClient.Connection.ErrorReason.Code.Should().Be(ErrorCodes.NoMeansProvidedToRenewAuthToken);
            helper.Requests.Count.Should().Be(0);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4a")]
        public async Task RealtimeClient_ConnectedWithExpiringToken_WhenTokenExpired_ShouldNotRetryAndHaveError(Protocol protocol)
        {
            var helper = new Rsa4Helper(this);

            // Create a token that is valid long enough for a successful connection to occur
            var authClient = await GetRestClient(protocol);
            var almostExpiredToken = await authClient.AblyAuth.RequestTokenAsync(new TokenParams { ClientId = "123", Ttl = TimeSpan.FromMilliseconds(8000) });

            // get a realtime client with no Key, AuthUrl, or authCallback
            var realtimeClient = await helper.GetRealTimeClientWithRequests(protocol, almostExpiredToken, invalidateKey: true);

            await realtimeClient.WaitForState(ConnectionState.Connected);

            // assert that there is no pre-existing error
            realtimeClient.Connection.ErrorReason.Should().BeNull();

            await realtimeClient.WaitForState(ConnectionState.Failed);
            realtimeClient.Connection.State.Should().Be(ConnectionState.Failed);

            realtimeClient.Connection.ErrorReason.Code.Should().Be(ErrorCodes.NoMeansProvidedToRenewAuthToken);
            helper.Requests.Count.Should().Be(0);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4b")]
        public async Task RealtimeWithAuthError_WhenTokenExpired_ShouldRetry_WhenRetryFails_ShouldSetError(Protocol protocol)
        {
            var helper = new Rsa4Helper(this);

            var restClient = await GetRestClient(protocol);
            var token = await restClient.Auth.AuthorizeAsync(new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(1000),
            });

            // this realtime client will have a key for the sandbox, thus a means to renew
            var realtimeClient = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.TokenDetails = token;
                options.AutoConnect = false;
            });

            realtimeClient.RestClient.ExecuteHttpRequest = helper.AblyResponseWith500Status;

            var awaiter = new TaskCompletionAwaiter(5000);

            realtimeClient.Connection.Once(ConnectionEvent.Disconnected, state =>
            {
                state.Reason.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
                awaiter.SetCompleted();
            });

            await Task.Delay(2000);
            realtimeClient.Connect();

            var result = await awaiter.Task;
            result.Should().BeTrue();
            helper.Requests.Count.Should().Be(1);
            helper.Requests[0].Url.EndsWith("requestToken").Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4b")]
        public async Task RealTimeWithAuthCallback_WhenTokenExpired_ShouldRetry_WhenRetryFails_ShouldSetError(Protocol protocol)
        {
            // create a short lived token
            var authRestClient = await GetRestClient(protocol);
            var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(1000),
            });

            bool didRetry = false;
            var realtimeClient = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.TokenDetails = token;
                options.AuthCallback = tokenParams =>
                {
                    didRetry = true;
                    throw new Exception("AuthCallback failed");
                };
                options.AutoConnect = false;
            });

            var awaiter = new TaskCompletionAwaiter(5000);
            realtimeClient.Connection.Once(ConnectionEvent.Disconnected, state =>
            {
                state.Reason.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
                awaiter.SetCompleted();
            });

            await Task.Delay(2000);
            realtimeClient.Connect();

            var result = await awaiter.Task;
            result.Should().BeTrue();
            didRetry.Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4b")]
        public async Task RealTimeWithAuthUrl_WhenTokenExpired_ShouldRetry_WhenRetryFails_ShouldSetError(Protocol protocol)
        {
            var authRestClient = await GetRestClient(protocol);
            var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(1000)
            });

            // this realtime client will have a key for the sandbox, thus a means to renew
            var realtimeClient = await GetRealtimeClient(protocol, (options, _) =>
            {
                options.TokenDetails = token;
                options.AuthUrl = new Uri(ErrorUrl);
                options.AutoConnect = false;
            });

            var awaiter = new TaskCompletionAwaiter(5000);
            realtimeClient.Connection.Once(ConnectionEvent.Disconnected, state =>
            {
                state.Reason.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
                awaiter.SetCompleted();
            });

            await Task.Delay(2000);
            realtimeClient.Connect();

            var result = await awaiter.Task;
            result.Should().BeTrue();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4b1")]
        public async Task RealTimeWithAuthUrl_WhenTokenExpired_And_WithServerTime_ShouldRenewToken(Protocol protocol)
        {
            var authRestClient = await GetRestClient(protocol);
            var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(1000),
            });

            // this realtime client will have a key for the sandbox, thus a means to renew
            var mainClient = await GetRestClient(protocol, options =>
            {
                options.QueryTime = true;
                options.TokenDetails = token;
            });
            await Task.Delay(2000);
            // This makes sure we get server time
            _ = ((AblyAuth)mainClient.Auth).CreateTokenRequest();

            await mainClient.StatsAsync();
            ((AblyAuth)mainClient.Auth).CurrentToken.Should().NotBeSameAs(token);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4b1")]
        public async Task RealTimeWithAuthUrl_WhenTokenExpired_And_WithServerTime_And_NoWayToRenewToken_ShouldErrorBeforeCallingServer(Protocol protocol)
        {
            var authRestClient = await GetRestClient(protocol);
            var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
            {
                Ttl = TimeSpan.FromMilliseconds(1000),
            });

            // this realtime client will have a key for the sandbox, thus a means to renew
            var mainClient = await GetRestClient(protocol, options =>
            {
                options.Key = null;
                options.QueryTime = true;
                options.TokenDetails = token;
            });

            bool madeHttpCall = false;
            var previousExecuteRequest = mainClient.ExecuteHttpRequest;
            mainClient.ExecuteHttpRequest = request =>
            {
                if (request.Url != "/time")
                {
                    madeHttpCall = true;
                }

                return previousExecuteRequest(request);
            };
            await Task.Delay(2000);
            // This makes sure we get server time
            await ((AblyAuth)mainClient.Auth).SetServerTime();

            var ex = await Assert.ThrowsAsync<AblyException>(() => mainClient.StatsAsync());
            ex.ErrorInfo.Should().BeSameAs(ErrorInfo.NonRenewableToken);
            madeHttpCall.Should().BeFalse();
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4c")]
        [Trait("spec", "RSA4c1")]
        [Trait("spec", "RSA4c2")]
        [Trait("spec", "RSA4c3")]
        public async Task Auth_WithRealtimeClient_WhenAuthFails_ShouldTransitionToOrRemainInTheCorrectState(Protocol protocol)
        {
            async Task TestConnectingBecomesDisconnected(string context, Action<ClientOptions, TestEnvironmentSettings> optionsAction)
            {
                TaskCompletionAwaiter tca = new TaskCompletionAwaiter();
                var realtimeClient = await GetRealtimeClient(protocol, optionsAction);
                realtimeClient.Connection.On(ConnectionEvent.Disconnected, change =>
                {
                    change.Previous.Should().Be(ConnectionState.Connecting);
                    change.Reason.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
                    tca.SetCompleted();
                });

                realtimeClient.Connection.Connect();
                await realtimeClient.ProcessCommands();

                (await tca.Task).Should().BeTrue(context);
            }

            // authUrl fails
            void AuthUrlOptions(ClientOptions options, TestEnvironmentSettings settings)
            {
                options.AutoConnect = false;
                options.AuthUrl = new Uri(ErrorUrl);
                options.RealtimeRequestTimeout = TimeSpan.FromSeconds(2);
                options.HttpRequestTimeout = TimeSpan.FromSeconds(2);
            }

            // authCallback fails
            static void AuthCallbackOptions(ClientOptions options, TestEnvironmentSettings settings)
            {
                options.AutoConnect = false;
                options.AuthCallback = (tokenParams) => throw new Exception("AuthCallback force error");
            }

            // invalid token returned
            static void InvalidTokenOptions(ClientOptions options, TestEnvironmentSettings settings)
            {
                options.AutoConnect = false;
                options.AuthCallback = (tokenParams) => Task.FromResult<object>("invalid:token");
            }

            await TestConnectingBecomesDisconnected("With invalid AuthUrl connection becomes Disconnected", AuthUrlOptions);
            await TestConnectingBecomesDisconnected("With invalid AuthCallback Connection becomes Disconnected", AuthCallbackOptions);
            await TestConnectingBecomesDisconnected("With Invalid Token Connection becomes Disconnected", InvalidTokenOptions);

            /* RSA4c3 */

            async Task<TokenDetails> GetToken()
            {
                var authRestClient = await GetRestClient(protocol);
                var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
                {
                    Ttl = TimeSpan.FromMilliseconds(10000)
                });
                return token;
            }

            async Task TestConnectedStaysConnected(string context, Action<ClientOptions, TestEnvironmentSettings> optionsAction)
            {
                var token = await GetToken();
                token.Expires = DateTimeOffset.Now.AddMinutes(30);
                void Options(ClientOptions options, TestEnvironmentSettings settings)
                {
                    optionsAction(options, settings);
                    options.TokenDetails = token;
                }

                var realtimeClient = await GetRealtimeClient(protocol, Options);
                realtimeClient.Connect();
                await realtimeClient.WaitForState(ConnectionState.Connected);
                bool stateChanged = false;
                realtimeClient.Connection.On(change =>
                {
                    // this callback should not be called
                    stateChanged = true;
                });

                _ = await Assert.ThrowsAsync<AblyException>(() => realtimeClient.Auth.AuthorizeAsync());

                realtimeClient.Connection.State.Should().Be(ConnectionState.Connected, because: context);
                stateChanged.Should().BeFalse(because: context);
            }

            await TestConnectedStaysConnected("With invalid AuthUrl Connection remains Connected", AuthUrlOptions);
            await TestConnectedStaysConnected("With invalid AuthCallback connection remains Connected", AuthCallbackOptions);
            await TestConnectedStaysConnected("With Invalid Token connection remains Connected", InvalidTokenOptions);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4d")]
        public async Task Auth_WithRealtimeClient_WhenAuthFailsWith403_ShouldTransitionToFailed(Protocol protocol)
        {
            async Task Test403BecomesFailed(string context, int expectedCode, Action<ClientOptions, TestEnvironmentSettings> optionsAction)
            {
                TaskCompletionAwaiter tca = new TaskCompletionAwaiter();
                var realtimeClient = await GetRealtimeClient(protocol, optionsAction);

                realtimeClient.Connection.Once(ConnectionEvent.Failed, change =>
                {
                    change.Previous.Should().Be(ConnectionState.Connecting);
                    change.Reason.Code.Should().Be(expectedCode);
                    realtimeClient.Connection.ErrorReason.Code.Should().Be(expectedCode);
                    realtimeClient.Connection.ErrorReason.StatusCode.Should().Be(HttpStatusCode.Forbidden); // 403
                    tca.SetCompleted();
                });

                realtimeClient.Connect();
                (await tca.Task).Should().BeTrue(context);
            }

            // authUrl fails and returns no body
            static void AuthUrlOptions(ClientOptions options, TestEnvironmentSettings settings)
            {
                options.AutoConnect = false;
                options.AuthUrl = new Uri("https://echo.ably.io/respondwith?status=403");
            }

            // AuthCallback that results in an ErrorInfo with code 403
            static void AuthCallbackOptions(ClientOptions options, TestEnvironmentSettings settings)
            {
                options.AutoConnect = false;
                options.AuthCallback = (tokenParams) =>
                {
                    var aex = new AblyException(new ErrorInfo("test", 40300, HttpStatusCode.Forbidden));
                    throw aex;
                };
            }

            await Test403BecomesFailed("With 403 response connection should become Failed", expectedCode: ErrorCodes.ClientAuthProviderRequestFailed, optionsAction: AuthUrlOptions);
            await Test403BecomesFailed("With ErrorInfo with StatusCode of 403 connection should become Failed", expectedCode: ErrorCodes.ClientAuthProviderRequestFailed, optionsAction: AuthCallbackOptions);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4d")]
        [Trait("spec", "RSA4d1")]
        public async Task Auth_WithRealtimeClient_WhenExplicitAuthFailsWith403_ShouldTransitionToFailed(Protocol protocol)
        {
            var realtimeClient = await GetRealtimeClient(protocol);
            await realtimeClient.WaitForState(ConnectionState.Connected);

            TaskCompletionAwaiter failedAwaiter = new TaskCompletionAwaiter();
            realtimeClient.Connection.Once(ConnectionEvent.Failed, change =>
            {
                change.Previous.Should().Be(ConnectionState.Connected);
                change.Reason.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
                realtimeClient.Connection.ErrorReason.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
                realtimeClient.Connection.ErrorReason.StatusCode.Should().Be(HttpStatusCode.Forbidden); // 403
                failedAwaiter.SetCompleted();
            });

            var authOptionsWhichFail = new AuthOptions
            {
                UseTokenAuth = true,
                AuthUrl = new Uri("https://echo.ably.io/respondwith?status=403"),
            };

            var ex = await Assert.ThrowsAsync<AblyException>(() => realtimeClient.Auth.AuthorizeAsync(null, authOptionsWhichFail));

            ex.Should().BeOfType<AblyException>();

            (await failedAwaiter.Task).Should().BeTrue("With 403 response connection should become Failed");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4d1")]
        public async Task Auth_WithConnectedRealtimeClient_WhenExplicitRequestTokenFailsWith403_ShouldNotAffectConnectionState(Protocol protocol)
        {
            var realtimeClient = await GetRealtimeClient(protocol);
            realtimeClient.Connection.Connect();

            await realtimeClient.WaitForState(ConnectionState.Connected);

            var authOptions = new AuthOptions
            {
                UseTokenAuth = true,
                AuthUrl = new Uri("https://echo.ably.io/respondwith?status=403"),
            };

            var ex = await Assert.ThrowsAsync<AblyException>(() => realtimeClient.Auth.RequestTokenAsync(null, authOptions));
            ex.Should().BeOfType<AblyException>();
            ex.ErrorInfo.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
            ex.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.Forbidden);
            await Task.Delay(1000);

            realtimeClient.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA8a")]
        public async Task ShouldReturnTheRequestedToken(Protocol protocol)
        {
            var ttl = TimeSpan.FromSeconds(30 * 60);
            var capability = new Capability();
            capability.AddResource("foo").AllowPublish();

            var ably = await GetRestClient(protocol);
            var options = ably.Options;

            var token = await ably.Auth.RequestTokenAsync(CreateTokenParams(capability, ttl));

            var key = options.ParseKey();
            var appId = key.KeyName.Split('.').First();
            token.Token.Should().MatchRegex($@"^{appId}\.[\w-]+$");
            token.Issued.Should().BeWithin(TimeSpan.FromSeconds(30)).Before(DateTimeOffset.UtcNow);
            token.Expires.Should().BeWithin(TimeSpan.FromSeconds(30)).Before(DateTimeOffset.UtcNow + ttl);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA3a")]
        public async Task WithTokenId_AuthenticatesSuccessfullyOverHttpAndHttps(Protocol protocol)
        {
            var capability = new Capability();
            capability.AddResource("foo").AllowPublish();

            var ably = await GetRestClient(protocol);
            var token = await ably.Auth.RequestTokenAsync(CreateTokenParams(capability));

            var options = await AblySandboxFixture.GetSettings();
            var httpTokenAbly =
                new AblyRest(new ClientOptions { Token = token.Token, Environment = options.Environment, Tls = false });
            var httpsTokenAbly =
                new AblyRest(new ClientOptions { Token = token.Token, Environment = options.Environment, Tls = true });

            // If it doesn't throw we are good :)
            await httpTokenAbly.Channels.Get("foo").PublishAsync("test", "true");
            await httpsTokenAbly.Channels.Get("foo").PublishAsync("test", "true");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA4a2")]
        public async Task WithTokenAuth_WhenUnauthorizedErrorAndNoRenew_ShouldThrow40171AblyException(Protocol protocol)
        {
            var ablyRest = await GetRestClient(protocol);
            var token = ablyRest.Auth.RequestToken(new TokenParams { Ttl = TimeSpan.FromSeconds(1) });

            await Task.Delay(2000);
            var ably = await GetRestClient(protocol, opts =>
            {
                opts.Key = string.Empty;
                opts.TokenDetails = token;
            });

            var ex = await Assert.ThrowsAsync<AblyException>(() => ably.StatsAsync());
            ex.ErrorInfo.Code.Should().Be(ErrorCodes.NoMeansProvidedToRenewAuthToken);
        }

        [Theory]
        [ProtocolData]
        public async Task WithTokenId_WhenTryingToPublishToUnspecifiedChannel_ThrowsAblyException(Protocol protocol)
        {
            var capability = new Capability();
            capability.AddResource("foo").AllowPublish();

            var ably = await GetRestClient(protocol);

            var token = ably.Auth.RequestTokenAsync(CreateTokenParams(capability)).Result;

            var tokenAbly = new AblyRest(new ClientOptions { Token = token.Token, Environment = "sandbox" });

            var error =
                await
                    Assert.ThrowsAsync<AblyException>(() => tokenAbly.Channels.Get("boo").PublishAsync("test", "true"));
            error.ErrorInfo.Code.Should().Be(ErrorCodes.OperationNotPermittedWithCapability);
            error.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [ProtocolData]
        public async Task WithInvalidTimeStamp_Throws(Protocol protocol)
        {
            var ably = await GetRestClient(protocol);

            var error = await Assert.ThrowsAsync<AblyException>(() =>
            {
                var tokenParams = CreateTokenParams(null);
                tokenParams.Timestamp = DateTimeOffset.UtcNow.AddDays(-1);
                return ably.Auth.RequestTokenAsync(tokenParams, AuthOptions.FromExisting(ably.Options).Merge(new AuthOptions { QueryTime = false }));
            });

            error.ErrorInfo.Code.Should().Be(40104);
            error.ErrorInfo.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA7b2")]
        [Trait("spec", "RSA10a")]
        public async Task WithoutClientId_WhenAuthorizedWithTokenParamsWithClientId_SetsClientId(Protocol protocol)
        {
            var ably = await GetRestClient(protocol);
            var tokenDetails1 = await ably.Auth.AuthorizeAsync(new TokenParams { ClientId = "123" });
            ably.AblyAuth.ClientId.Should().Be("123");

            // uses Token Auth for all future requests (RSA10a)
            ably.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);

            // create a token immediately (RSA10a)
            // regardless of whether the existing token is valid or not
            var tokenDetails2 = await ably.Auth.AuthorizeAsync(new TokenParams { ClientId = "123" });
            tokenDetails1.Token.Should().NotBe(tokenDetails2.Token);
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA8f1")]
        public async Task TokenAuthWithoutClientId_ShouldNotSetClientIdOnMessagesAndTheClient(Protocol protocol)
        {
            var client = await GetRestClient(protocol, opts => opts.QueryTime = true);
            var settings = await AblySandboxFixture.GetSettings();
            var token = await client.Auth.RequestTokenAsync();
            var tokenClient = new AblyRest(new ClientOptions
            {
                TokenDetails = token,
                Environment = settings.Environment,
                UseBinaryProtocol = protocol == Defaults.Protocol
            });

            tokenClient.AblyAuth.ClientId.Should().BeNullOrEmpty();
            var channel = tokenClient.Channels["persisted:test".AddRandomSuffix()];
            await channel.PublishAsync("test", "test");
            var message = (await channel.HistoryAsync()).Items.First();
            message.ClientId.Should().BeNullOrEmpty();
            message.Data.Should().Be("test");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA8f2")]
        public async Task TokenAuthWithoutClientIdAndAMessageWithExplicitId_ShouldThrow(Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var settings = await AblySandboxFixture.GetSettings();
            var token = await client.Auth.RequestTokenAsync();
            var tokenClient = new AblyRest(new ClientOptions
            {
                TokenDetails = token,
                Environment = settings.Environment,
                UseBinaryProtocol = protocol == Defaults.Protocol
            });
            await
                Assert.ThrowsAsync<AblyException>(
                    () => tokenClient.Channels["test"].PublishAsync(new Message("test", "test") { ClientId = "123" }));
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA8f3")]
        public async Task TokenAuthWithWildcardClientId_ShouldPublishMessageSuccessfullyAndClientIdShouldBeSetToWildcard(
            Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var settings = await AblySandboxFixture.GetSettings();
            var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
            var tokenClient = new AblyRest(new ClientOptions
            {
                TokenDetails = token,
                Environment = settings.Environment,
                UseBinaryProtocol = protocol == Defaults.Protocol
            });

            var channel = tokenClient.Channels["pesisted:test"];
            await channel.PublishAsync("test", "test");
            tokenClient.AblyAuth.ClientId.Should().Be("*");
            var message = (await channel.HistoryAsync()).Items.First();
            message.ClientId.Should().BeNullOrEmpty();
            message.Data.Should().Be("test");
        }

        [Theory]
        [ProtocolData]
        [Trait("spec", "RSA8f4")]
        public async Task
            TokenAuthWithWildcardClientId_WhenPublishingMessageWithClientId_ShouldExpectClientIdToBeSentWithTheMessage(
                Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var settings = await AblySandboxFixture.GetSettings();
            var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
            var tokenClient = new AblyRest(new ClientOptions
            {
                TokenDetails = token,
                Environment = settings.Environment,
                UseBinaryProtocol = protocol == Defaults.Protocol
            });

            var channel = tokenClient.Channels["pesisted:test"];
            await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });
            tokenClient.AblyAuth.ClientId.Should().Be("*");
            var message = (await channel.HistoryAsync()).Items.First();
            message.ClientId.Should().Be("123");
            message.Data.Should().Be("test");
        }

        [Theory]
        [ProtocolData]
        public async Task TokenAuthUrlWhenPlainTextTokenIsReturn_ShouldBeAblyToPublishWithNewToken(Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
            var settings = await AblySandboxFixture.GetSettings();
            var authUrl = "http://echo.ably.io/?type=text&body=" + token.Token;

            var authUrlClient = new AblyRest(new ClientOptions
            {
                AuthUrl = new Uri(authUrl),
                Environment = settings.Environment,
                UseBinaryProtocol = protocol == Defaults.Protocol
            });

            var channel = authUrlClient.Channels["pesisted:test"];
            await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });

            var message = (await channel.HistoryAsync()).Items.First();
            message.ClientId.Should().Be("123");
            message.Data.Should().Be("test");
        }

        [Theory]
        [ProtocolData]
        public async Task TokenAuthUrlWithJsonTokenReturned_ShouldBeAbleToPublishWithNewToken(Protocol protocol)
        {
            var client = await GetRestClient(protocol);
            var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
            var settings = await AblySandboxFixture.GetSettings();
            var authUrl = "http://echo.ably.io/?type=json&body=" + Uri.EscapeDataString(token.ToJson());

            var authUrlClient = new AblyRest(new ClientOptions
            {
                AuthUrl = new Uri(authUrl),
                Environment = settings.Environment,
                UseBinaryProtocol = protocol == Defaults.Protocol,
                HttpRequestTimeout = new TimeSpan(0, 0, 20)
            });

            var channel = authUrlClient.Channels["pesisted:test"];
            await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });

            var message = (await channel.HistoryAsync()).Items.First();
            message.ClientId.Should().Be("123");
            message.Data.Should().Be("test");
        }

        [Theory]
        [ProtocolData]
        public async Task TokenAuthUrlWithJsonTokenReturned_ShouldBeAbleToConnect(Protocol protocol)
        {
            var ablyRest = await GetRestClient(protocol);
            var token = await ablyRest.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
            var settings = await AblySandboxFixture.GetSettings();
            var tokenJson = token.ToJson();
            var authUrl = "http://echo.ably.io/?type=json&body=" + Uri.EscapeDataString(tokenJson);

            var client = new AblyRealtime(new ClientOptions
                                                 {
                                                     AuthUrl = new Uri(authUrl),
                                                     Environment = settings.Environment,
                                                     UseBinaryProtocol = protocol == Defaults.Protocol,
                                                     HttpRequestTimeout = new TimeSpan(0, 0, 20)
                                                 });

            await client.WaitForState();
            client.Connection.State.Should().Be(ConnectionState.Connected);
        }

        [Theory]
        [ProtocolData]
        public async Task TokenAuthUrlWithIncorrectJsonTokenReturned_ShouldNotBeAbleToConnectAndShouldHaveError(Protocol protocol)
        {
            var ablyRest = await GetRestClient(protocol);
            var token = await ablyRest.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
            var settings = await AblySandboxFixture.GetSettings();
            var incorrectJson = $"[{token.ToJson()}]";
            var authUrl = "http://echo.ably.io/?type=json&body=" + Uri.EscapeDataString(incorrectJson);

            var client = new AblyRealtime(new ClientOptions
                                              {
                                                  AuthUrl = new Uri(authUrl),
                                                  Environment = settings.Environment,
                                                  UseBinaryProtocol = protocol == Defaults.Protocol,
                                                  HttpRequestTimeout = new TimeSpan(0, 0, 20)
                                              });

            var tsc = new TaskCompletionAwaiter();
            ErrorInfo err = null;
            client.Connection.On(ConnectionEvent.Disconnected, state =>
            {
                err = state.Reason;
                tsc.SetCompleted();
            });

            var b = await tsc.Task;
            b.Should().BeTrue();
            err.Should().NotBeNull();
            err.Message.Should().StartWith("Error parsing JSON response");
            err.InnerException.Should().NotBeNull();
        }

        [Theory]
        [ProtocolData]
        public async Task TokenAuthCallbackWithTokenDetailsReturned_ShouldBeAbleToPublishWithNewToken(Protocol protocol)
        {
            var settings = await AblySandboxFixture.GetSettings();
            var tokenClient = await GetRestClient(protocol);
            var authCallbackClient = await GetRestClient(protocol, options =>
            {
                options.AuthCallback = tokenParams => tokenClient.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" }).Convert();
                options.Environment = settings.Environment;
                options.UseBinaryProtocol = protocol == Defaults.Protocol;
            });

            var channel = authCallbackClient.Channels["pesisted:test"];
            await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });

            await Task.Delay(1000);

            var message = (await channel.HistoryAsync()).Items.First();
            message.ClientId.Should().Be("123");
            message.Data.Should().Be("test");
        }

        [Theory]
        [ProtocolData]
        public async Task TokenAuthCallbackWithTokenRequestReturned_ShouldBeAbleToGetATokenAndPublishWithNewToken(Protocol protocol)
        {
            var settings = await AblySandboxFixture.GetSettings();
            var tokenClient = await GetRestClient(protocol);
            var authCallbackClient = await GetRestClient(protocol, options =>
            {
                options.AuthCallback = async tokenParams => await tokenClient.Auth.CreateTokenRequestAsync(new TokenParams { ClientId = "*" });
                options.Environment = settings.Environment;
                options.UseBinaryProtocol = protocol == Defaults.Protocol;
            });

            var channel = authCallbackClient.Channels["pesisted:test"];
            await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });

            var message = (await channel.HistoryAsync()).Items.First();
            message.ClientId.Should().Be("123");
            message.Data.Should().Be("test");
        }

        [Theory]
        [ProtocolData]
        [Trait("issue", "374")]
        public async Task WhenClientTimeIsWrongAndQueryTimeSetToTrue_ShouldNotTreatTokenAsInvalid(Protocol protocol)
        {
            // Our device's clock is fast. The server returns by default a token valid for an hour
            DateTimeOffset NowFunc() => DateTimeOffset.UtcNow.AddHours(2);

            var realtimeClient = await GetRealtimeClient(protocol, (opts, _) =>
            {
                opts.NowFunc = NowFunc;
                opts.QueryTime = true;
                opts.ClientId = "clientId";
                opts.UseTokenAuth = true; // We force the token auth because further on it's not necessary when there is a key present
            });

            await realtimeClient.WaitForState(ConnectionState.Connected);
        }

        /// <summary>
        /// Helper methods that return an AblyRest or AblyRealtime instance and a list of AblyRequest that
        /// will contain all the HTTP requests the client attempts
        /// </summary>
        private class Rsa4Helper
        {
            public Rsa4Helper(AuthSandboxSpecs specs)
            {
                Requests = new List<AblyRequest>();
                Specs = specs;
            }

            public List<AblyRequest> Requests { get; }

            private AuthSandboxSpecs Specs { get; }

            public async Task<AblyRest> GetRestClientWithRequests(Protocol protocol, TokenDetails token, bool invalidateKey, Action<ClientOptions> optionsAction = null)
            {
                void DefaultOptionsAction(ClientOptions options)
                {
                    options.TokenDetails = token;
                    if (invalidateKey)
                    {
                        options.Key = string.Empty;
                    }
                }

                if (optionsAction == null)
                {
                    optionsAction = DefaultOptionsAction;
                }

                var restClient = await Specs.GetRestClient(protocol, optionsAction);

                // intercept http calls to demonstrate that the client did not attempt to request a new token
                var execute = restClient.ExecuteHttpRequest;
                restClient.ExecuteHttpRequest = request =>
                {
                    Requests.Add(request);
                    return execute.Invoke(request);
                };

                return restClient;
            }

            public async Task<AblyRealtime> GetRealTimeClientWithRequests(Protocol protocol, TokenDetails token, bool invalidateKey, Action<ClientOptions, TestEnvironmentSettings> optionsAction = null)
            {
                var restClient = await GetRestClientWithRequests(protocol, token, invalidateKey);

                // Creating a new connection
                void DefaultOptionsAction(ClientOptions options, TestEnvironmentSettings settings)
                {
                    options.TokenDetails = token;
                    if (invalidateKey)
                    {
                        options.Key = string.Empty;
                    }
                }

                if (optionsAction == null)
                {
                    optionsAction = DefaultOptionsAction;
                }

                var realtimeClient = await Specs.GetRealtimeClient(protocol, optionsAction, (options, device) => restClient);
                return realtimeClient;
            }

            public Task<AblyResponse> AblyResponseWith500Status(AblyRequest request)
            {
                Requests.Add(request);
                var r = new AblyResponse(string.Empty, "application/json", string.Empty.GetBytes()) { StatusCode = HttpStatusCode.InternalServerError };
                return Task.FromResult(r);
            }
        }
    }
}
