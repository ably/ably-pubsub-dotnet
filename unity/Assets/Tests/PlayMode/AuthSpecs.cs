using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Assets.Tests.AblySandbox;
using Cysharp.Threading.Tasks;
using IO.Ably;
using IO.Ably.Realtime;
using NUnit.Framework;
using UnityEngine;
using UnityEngine.TestTools;

namespace Assets.Tests.PlayMode
{
    [TestFixture]
    [Category("EditorPlayer")]
    public class AuthSpecs
    {
        private AblySandboxFixture _sandboxFixture;

        [OneTimeSetUp]
        public void OneTimeInit()
        {
            _sandboxFixture = new AblySandboxFixture();
        }

        [UnitySetUp]
        public IEnumerator Init()
        {
            AblySandbox = new AblySandbox.AblySandbox(_sandboxFixture);
            yield return null;
        }

        [UnityTearDown]
        public IEnumerator TearDown()
        {
            AblySandbox.Dispose();
            yield return null;
        }

        public AblySandbox.AblySandbox AblySandbox { get; set; }

        private static Protocol[] _protocols = { Protocol.Json };

        private static TokenParams CreateTokenParams(Capability capability, TimeSpan? ttl = null)
        {
            var res = new TokenParams
            {
                ClientId = "John",
                Capability = capability
            };

            if (ttl.HasValue)
            {
                res.Ttl = ttl.Value;
            }

            return res;
        }

        private string _errorUrl = "https://echo.ably.io/respondwith?status=500";

        [UnityTest]
        public IEnumerator RSA4Helper_RestClient_ShouldTrackRequests([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var authClient = await AblySandbox.GetRestClient(protocol);
                var token = await authClient.Auth.RequestTokenAsync(new TokenParams { ClientId = "123" });
                var helper = new RSA4Helper(this);
                var restClient = await helper.GetRestClientWithRequests(protocol, token, invalidateKey: true);
                Assert.AreEqual(0, helper.Requests.Count);
                await restClient.TimeAsync();
                Assert.AreEqual(1, helper.Requests.Count);
                var realtimeClient = await helper.GetRealTimeClientWithRequests(protocol, token, invalidateKey: true);
                Assert.AreEqual(1, helper.Requests.Count);
                await realtimeClient.HttpClient.TimeAsync();
                Assert.AreEqual(2, helper.Requests.Count);
            });
        }

        [UnityTest]
        public IEnumerator RestClient_WhenTokenExpired_ShouldNotRetryAndRaiseError([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var helper = new RSA4Helper(this);

                // Get a very short lived token and wait for it to expire
                var authClient = await AblySandbox.GetRestClient(protocol);
                var almostExpiredToken = await authClient.AblyAuth.RequestTokenAsync(new TokenParams
                {
                    ClientId = "123",
                    Ttl = TimeSpan.FromMilliseconds(1)
                });

                await Task.Delay(TimeSpan.FromMilliseconds(500));

                // Modify the expiry date to fool the client it has a valid token
                almostExpiredToken.Expires = DateTimeOffset.UtcNow.AddHours(1);

                // create a new client with the token
                // set the Key to an empty string to override the sandbox settings
                var restClient = await helper.GetRestClientWithRequests(protocol, almostExpiredToken, invalidateKey: true);

                var now = DateTimeOffset.UtcNow;

                // check the client thinks the token is valid
                Assert.IsTrue(restClient.AblyAuth.CurrentToken.IsValidToken(now));

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
                    Assert.AreEqual(HttpStatusCode.Unauthorized, e.ErrorInfo.StatusCode);
                    Assert.AreEqual(ErrorCodes.NoMeansProvidedToRenewAuthToken, e.ErrorInfo.Code);
                }

                // did not retry the request
                Assert.AreEqual(1, helper.Requests.Count, "only one request should have been attempted");
                Assert.AreEqual($"/channels/{channelName}/messages", helper.Requests[0].Url,
                    "only the publish request should have been attempted");
            });
        }

        [UnityTest]
        public IEnumerator RealtimeClient_NewInstanceWithExpiredToken_ShouldNotRetryAndHaveError([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var helper = new RSA4Helper(this);
                var authClient = await AblySandbox.GetRestClient(protocol);
                var almostExpiredToken = await authClient.AblyAuth.RequestTokenAsync(new TokenParams
                {
                    ClientId = "123",
                    Ttl = TimeSpan.FromMilliseconds(1)
                });

                await Task.Delay(TimeSpan.FromMilliseconds(2));

                // Modify the expiry date to fool the client it has a valid token
                almostExpiredToken.Expires = DateTimeOffset.UtcNow.AddHours(1);

                // get a realtime client with no key
                var realtimeClient =
                    await helper.GetRealTimeClientWithRequests(protocol, almostExpiredToken, invalidateKey: true);

                bool connected = false;
                realtimeClient.Connection.Once(ConnectionEvent.Connected, (_) => { connected = true; });

                // assert that there is no pre-existing error
                Assert.IsNull(realtimeClient.Connection.ErrorReason);

                await realtimeClient.WaitForState(ConnectionState.Failed);
                Assert.AreEqual(ConnectionState.Failed, realtimeClient.Connection.State);
                Assert.IsFalse(connected);

                Assert.AreEqual(ErrorCodes.NoMeansProvidedToRenewAuthToken, realtimeClient.Connection.ErrorReason.Code);
                Assert.AreEqual(0, helper.Requests.Count);
            });
        }

        [UnityTest]
        public IEnumerator RealtimeClient_ConnectedWithExpiringToken_WhenTokenExpired_ShouldNotRetryAndHaveError([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
                {
                    var helper = new RSA4Helper(this);

                    // Create a token that is valid long enough for a successful connection to occur
                    var authClient = await AblySandbox.GetRestClient(protocol);
                    var almostExpiredToken = await authClient.AblyAuth.RequestTokenAsync(new TokenParams
                    {
                        ClientId = "123",
                        Ttl = TimeSpan.FromMilliseconds(8000),
                    });

                    // get a realtime client with no Key, AuthUrl, or authCallback
                    var realtimeClient =
                        await helper.GetRealTimeClientWithRequests(protocol, almostExpiredToken, invalidateKey: true);

                    await realtimeClient.WaitForState(ConnectionState.Connected);

                    // assert that there is no pre-existing error
                    Assert.IsNull(realtimeClient.Connection.ErrorReason);

                    await realtimeClient.WaitForState(ConnectionState.Failed);
                    Assert.AreEqual(ConnectionState.Failed, realtimeClient.Connection.State);

                    Assert.AreEqual(ErrorCodes.NoMeansProvidedToRenewAuthToken, realtimeClient.Connection.ErrorReason.Code);
                    Assert.AreEqual(0, helper.Requests.Count);
                });
        }

        [UnityTest]
        public IEnumerator RealtimeWithAuthError_WhenTokenExpired_ShouldRetry_WhenRetryFails_ShouldSetError([ValueSource(nameof(_protocols))] Protocol protocol) 
        {
            return UniTask.ToCoroutine(async () =>
            {
                var helper = new RSA4Helper(this);

                var restClient = await AblySandbox.GetRestClient(protocol);
                var token = await restClient.Auth.AuthorizeAsync(new TokenParams
                {
                    Ttl = TimeSpan.FromMilliseconds(1000),
                });

                // this realtime client will have a key for the sandbox, thus a means to renew
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, (options, _) =>
                {
                    options.TokenDetails = token;
                    options.AutoConnect = false;
                });

                realtimeClient.HttpClient.ExecuteHttpRequest = helper.AblyResponseWith500Status;

                var awaiter = new TaskCompletionAwaiter(5000);

                realtimeClient.Connection.Once(ConnectionEvent.Disconnected, state =>
                {
                    Assert.AreEqual(ErrorCodes.ClientAuthProviderRequestFailed, state.Reason.Code);
                    awaiter.SetCompleted();
                });

                await Task.Delay(2000);
                realtimeClient.Connect();

                var result = await awaiter.Task;
                Assert.IsTrue(result);
                Assert.AreEqual(1, helper.Requests.Count);
                Assert.IsTrue(helper.Requests[0].Url.EndsWith("requestToken"));
            });
        }

        [UnityTest]
        public IEnumerator RealTimeWithAuthCallback_WhenTokenExpired_ShouldRetry_WhenRetryFails_ShouldSetError([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                // create a short lived token
                var authRestClient = await AblySandbox.GetRestClient(protocol);
                var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
                {
                    Ttl = TimeSpan.FromMilliseconds(1000),
                });

                bool didRetry = false;
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, (options, _) =>
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
                    Assert.AreEqual(ErrorCodes.ClientAuthProviderRequestFailed, state.Reason.Code);
                    awaiter.SetCompleted();
                });

                await Task.Delay(2000);
                realtimeClient.Connect();

                var result = await awaiter.Task;
                Assert.IsTrue(result);
                Assert.IsTrue(didRetry);
            });
        }

        [UnityTest]
        public IEnumerator RealTimeWithAuthUrl_WhenTokenExpired_ShouldRetry_WhenRetryFails_ShouldSetError([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var authRestClient = await AblySandbox.GetRestClient(protocol);
                var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
                {
                    Ttl = TimeSpan.FromMilliseconds(1000)
                });

                // this realtime client will have a key for the sandbox, thus a means to renew
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, (options, _) =>
                {
                    options.TokenDetails = token;
                    options.AuthUrl = new Uri(_errorUrl);
                    options.AutoConnect = false;
                });

                var awaiter = new TaskCompletionAwaiter(5000);
                realtimeClient.Connection.Once(ConnectionEvent.Disconnected, state =>
                {
                    Assert.AreEqual(ErrorCodes.ClientAuthProviderRequestFailed, state.Reason.Code);
                    awaiter.SetCompleted();
                });

                await Task.Delay(2000);
                realtimeClient.Connect();

                var result = await awaiter.Task;
                Assert.IsTrue(result);
            });
        }

        [UnityTest]
        public IEnumerator RealTimeWithAuthUrl_WhenTokenExpired_And_WithServerTime_ShouldRenewToken([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var authRestClient = await AblySandbox.GetRestClient(protocol);
                var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
                {
                    Ttl = TimeSpan.FromMilliseconds(1000),
                });

                // this realtime client will have a key for the sandbox, thus a means to renew
                var mainClient = await AblySandbox.GetRestClient(protocol, options =>
                {
                    options.QueryTime = true;
                    options.TokenDetails = token;
                });

                await Task.Delay(2000);
                // This makes sure we get server time
                ((AblyAuth) mainClient.Auth).CreateTokenRequest();

                await mainClient.StatsAsync();
                Assert.AreNotSame(token, ((AblyAuth) mainClient.Auth).CurrentToken);
            });
        }

        [UnityTest]
        public IEnumerator RealTimeWithAuthUrl_WhenTokenExpired_And_WithServerTime_And_NoWayToRenewToken_ShouldErrorBeforeCallingServer([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var authRestClient = await AblySandbox.GetRestClient(protocol);
                var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
                {
                    Ttl = TimeSpan.FromMilliseconds(1000),
                });

                // this realtime client will have a key for the sandbox, thus a means to renew
                var mainClient = await AblySandbox.GetRestClient(protocol, options =>
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
                ((AblyAuth) mainClient.Auth).SetServerTime();

                var ex = await E7Assert.ThrowsAsync<AblyException>(mainClient.StatsAsync());
                Assert.AreSame(ErrorInfo.NonRenewableToken, ex.ErrorInfo);
                Assert.IsFalse(madeHttpCall);
            });
        }

        [UnityTest]
        [Ignore("Test is failing for connecting assertion")]
        public IEnumerator Auth_WithRealtimeClient_WhenAuthFails_ShouldTransitionToOrRemainInTheCorrectState([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                async Task TestConnectingBecomesDisconnected(string context, Action<ClientOptions, TestEnvironmentSettings> optionsAction)
                {
                    TaskCompletionAwaiter tca = new TaskCompletionAwaiter(5000);
                    var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, optionsAction);
                    realtimeClient.Connection.On(ConnectionEvent.Disconnected, change =>
                    {
                        Assert.AreEqual(ConnectionState.Connecting, change.Previous);
                        Assert.AreEqual(ErrorCodes.ClientAuthProviderRequestFailed, change.Reason.Code);
                        tca.SetCompleted();
                    });

                    realtimeClient.Connection.Connect();
                    await realtimeClient.ProcessCommands();

                    Assert.IsTrue(await tca.Task, context);
                }

                // authUrl fails
                void AuthUrlOptions(ClientOptions options, TestEnvironmentSettings settings)
                {
                    options.AutoConnect = false;
                    options.AuthUrl = new Uri(_errorUrl);
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
                    var authRestClient = await AblySandbox.GetRestClient(protocol);
                    var token = await authRestClient.Auth.RequestTokenAsync(new TokenParams
                    {
                        Ttl = TimeSpan.FromMilliseconds(2000)
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

                    var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, Options);
                    realtimeClient.Connect();
                    await realtimeClient.WaitForState(ConnectionState.Connected);
                    bool stateChanged = false;
                    realtimeClient.Connection.On(change =>
                    {
                        // this callback should not be called
                        stateChanged = true;
                    });

                    _ = await E7Assert.ThrowsAsync<AblyException>(realtimeClient.Auth.AuthorizeAsync());

                    Assert.AreEqual(ConnectionState.Connected, realtimeClient.Connection.State, context);
                    Assert.IsFalse(stateChanged, context);
                }

                await TestConnectedStaysConnected("With invalid AuthUrl Connection remains Connected", AuthUrlOptions);
                await TestConnectedStaysConnected("With invalid AuthCallback connection remains Connected", AuthCallbackOptions);
                await TestConnectedStaysConnected("With Invalid Token connection remains Connected", InvalidTokenOptions);
            });
        }

        [UnityTest]
        [NUnit.Framework.Property("spec", "RSA4d")]
        public IEnumerator Auth_WithRealtimeClient_WhenAuthFailsWith403_ShouldTransitionToFailed([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                async Task Test403BecomesFailed(string context, int expectedCode, Action<ClientOptions, TestEnvironmentSettings> optionsAction)
                {
                    TaskCompletionAwaiter tca = new TaskCompletionAwaiter();
                    var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, optionsAction);

                    realtimeClient.Connection.Once(ConnectionEvent.Failed, change =>
                    {
                        Assert.AreEqual(ConnectionState.Connecting, change.Previous);
                        Assert.AreEqual(expectedCode, change.Reason.Code);
                        Assert.AreEqual(expectedCode, realtimeClient.Connection.ErrorReason.Code);
                        Assert.AreEqual(HttpStatusCode.Forbidden, realtimeClient.Connection.ErrorReason.StatusCode); // 403
                        tca.SetCompleted();
                    });

                    realtimeClient.Connect();
                    Assert.IsTrue(await tca.Task, context);
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

                await Test403BecomesFailed("With 403 response connection should become Failed",
                    expectedCode: ErrorCodes.ClientAuthProviderRequestFailed, optionsAction: AuthUrlOptions);
                await Test403BecomesFailed("With ErrorInfo with StatusCode of 403 connection should become Failed",
                    expectedCode: ErrorCodes.ClientAuthProviderRequestFailed, optionsAction: AuthCallbackOptions);
            });
        }

        [NUnit.Framework.Property("spec", "RSA4d")]
        [NUnit.Framework.Property("spec", "RSA4d1")]
        [UnityTest]
        public IEnumerator Auth_WithRealtimeClient_WhenExplicitAuthFailsWith403_ShouldTransitionToFailed([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol);
                await realtimeClient.WaitForState(ConnectionState.Connected);

                TaskCompletionAwaiter failedAwaiter = new TaskCompletionAwaiter();
                realtimeClient.Connection.Once(ConnectionEvent.Failed, change =>
                {
                    Assert.AreEqual(ConnectionState.Connected, change.Previous);
                    Assert.AreEqual(ErrorCodes.ClientAuthProviderRequestFailed, change.Reason.Code);
                    Assert.AreEqual(ErrorCodes.ClientAuthProviderRequestFailed, realtimeClient.Connection.ErrorReason.Code);
                    Assert.AreEqual(HttpStatusCode.Forbidden, realtimeClient.Connection.ErrorReason.StatusCode); // 403
                    failedAwaiter.SetCompleted();
                });

                var authOptionsWhichFail = new AuthOptions
                {
                    UseTokenAuth = true,
                    AuthUrl = new Uri("https://echo.ably.io/respondwith?status=403"),
                };

                var ex = await E7Assert.ThrowsAsync<AblyException>(
                    realtimeClient.Auth.AuthorizeAsync(null, authOptionsWhichFail));

                Assert.IsInstanceOf<AblyException>(ex);

                Assert.IsTrue(await failedAwaiter.Task, "With 403 response connection should become Failed");
            });
        }

        [NUnit.Framework.Property("spec", "RSA4d1")]
        [UnityTest]
        public IEnumerator Auth_WithConnectedRealtimeClient_WhenExplicitRequestTokenFailsWith403_ShouldNotAffectConnectionState([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol);
                realtimeClient.Connection.Connect();

                await realtimeClient.WaitForState(ConnectionState.Connected);

                var authOptions = new AuthOptions
                {
                    UseTokenAuth = true,
                    AuthUrl = new Uri("https://echo.ably.io/respondwith?status=403"),
                };

                var ex = await E7Assert.ThrowsAsync<AblyException>(
                    realtimeClient.Auth.RequestTokenAsync(null, authOptions));
                Assert.IsInstanceOf<AblyException>(ex);
                Assert.AreEqual(ErrorCodes.ClientAuthProviderRequestFailed, ex.ErrorInfo.Code);
                Assert.AreEqual(HttpStatusCode.Forbidden, ex.ErrorInfo.StatusCode);
                await Task.Delay(1000);

                Assert.AreEqual(ConnectionState.Connected, realtimeClient.Connection.State);
            });
        }

        [NUnit.Framework.Property("spec", "RSA8a")]
        [UnityTest]
        public IEnumerator ShouldReturnTheRequestedToken([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var ttl = TimeSpan.FromSeconds(30 * 60);
                var capability = new Capability();
                capability.AddResource("foo").AllowPublish();

                var ably = await AblySandbox.GetRestClient(protocol);
                var options = ably.Options;

                var token = await ably.Auth.RequestTokenAsync(CreateTokenParams(capability, ttl), null);

                var key = options.ParseKey();
                var appId = key.KeyName.Split('.').First();
                StringAssert.IsMatch($@"^{appId}\.[\w-]+$", token.Token);
                Assert.That(token.Issued, Is.EqualTo(DateTimeOffset.UtcNow).Within(TimeSpan.FromSeconds(30)));
                Assert.That(token.Expires, Is.EqualTo(DateTimeOffset.UtcNow + ttl).Within(TimeSpan.FromSeconds(30)));
            });
        }

        [NUnit.Framework.Property("spec", "RSA3a")]
        [UnityTest]
        public IEnumerator WithTokenId_AuthenticatesSuccessfullyOverHttpAndHttps([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var capability = new Capability();
                capability.AddResource("foo").AllowPublish();

                var ably = await AblySandbox.GetRestClient(protocol);
                var token = await ably.Auth.RequestTokenAsync(CreateTokenParams(capability), null);

                var options = await _sandboxFixture.GetSettings();
                var httpTokenAbly =
                    new PubSubHttpClient(new ClientOptions { Token = token.Token, Environment = options.Environment, Tls = false });
                var httpsTokenAbly =
                    new PubSubHttpClient(new ClientOptions { Token = token.Token, Environment = options.Environment, Tls = true });

                // If it doesn't throw we are good :)
                await httpTokenAbly.Channels.Get("foo").PublishAsync("test", "true");
                await httpsTokenAbly.Channels.Get("foo").PublishAsync("test", "true");
            });
        }

        [NUnit.Framework.Property("spec", "RSA4a2")]
        [UnityTest]
        public IEnumerator WithTokenAuth_WhenUnauthorizedErrorAndNoRenew_ShouldThrow40171AblyException([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var ablyRest = await AblySandbox.GetRestClient(protocol);
                var token = ablyRest.Auth.RequestToken(new TokenParams { Ttl = TimeSpan.FromSeconds(1) });

                await Task.Delay(2000);
                var ably = await AblySandbox.GetRestClient(protocol, opts =>
                {
                    opts.Key = string.Empty;
                    opts.TokenDetails = token;
                });

                var ex = await E7Assert.ThrowsAsync<AblyException>(ably.StatsAsync());
                Assert.AreEqual(ErrorCodes.NoMeansProvidedToRenewAuthToken, ex.ErrorInfo.Code);
            });
        }

        [UnityTest]
        [Ignore("Fails test with blocking async await")]
        public IEnumerator WithTokenId_WhenTryingToPublishToUnspecifiedChannel_ThrowsAblyException([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var capability = new Capability();
                capability.AddResource("foo").AllowPublish();

                var ably = await AblySandbox.GetRestClient(protocol);

                var token = ably.Auth.RequestTokenAsync(CreateTokenParams(capability), null).Result;

                var tokenAbly = new PubSubHttpClient(new ClientOptions { Token = token.Token, Environment = "sandbox" });

                var error =
                    await E7Assert.ThrowsAsync<AblyException>(tokenAbly.Channels.Get("boo").PublishAsync("test", "true"));
                Assert.AreEqual(ErrorCodes.OperationNotPermittedWithCapability, error.ErrorInfo.Code);
                Assert.AreEqual(HttpStatusCode.Unauthorized, error.ErrorInfo.StatusCode);
            });
        }

        [UnityTest]
        public IEnumerator WithInvalidTimeStamp_Throws([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var ably = await AblySandbox.GetRestClient(protocol);

                var tokenParams = CreateTokenParams(null);
                tokenParams.Timestamp = DateTimeOffset.UtcNow.AddDays(-1);
                var error = await E7Assert.ThrowsAsync<AblyException>(ably.Auth.RequestTokenAsync(tokenParams,
                    AuthOptions.FromExisting(ably.Options).Merge(new AuthOptions { QueryTime = false })));

                Assert.AreEqual(40104, error.ErrorInfo.Code);
                Assert.AreEqual(HttpStatusCode.Unauthorized, error.ErrorInfo.StatusCode);
            });
        }

        [NUnit.Framework.Property("spec", "RSA7b2")]
        [NUnit.Framework.Property("spec", "RSA10a")]
        [UnityTest]
        public IEnumerator WithoutClientId_WhenAuthorizedWithTokenParamsWithClientId_SetsClientId([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var ably = await AblySandbox.GetRestClient(protocol);
                var tokenDetails1 = await ably.Auth.AuthorizeAsync(new TokenParams { ClientId = "123" });
                Assert.AreEqual("123", ably.AblyAuth.ClientId);

                // uses Token Auth for all future requests (RSA10a)
                Assert.AreEqual(AuthMethod.Token, ably.AblyAuth.AuthMethod);

                // create a token immediately (RSA10a)
                // regardless of whether the existing token is valid or not
                var tokenDetails2 = await ably.Auth.AuthorizeAsync(new TokenParams { ClientId = "123" });
                Assert.AreNotEqual(tokenDetails2.Token, tokenDetails1.Token);
            });
        }

        [NUnit.Framework.Property("spec", "RSA8f1")]
        [UnityTest]
        public IEnumerator TokenAuthWithoutClientId_ShouldNotSetClientIdOnMessagesAndTheClient([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var client = await AblySandbox.GetRestClient(protocol, opts => opts.QueryTime = true);
                var settings = await _sandboxFixture.GetSettings();
                var token = await client.Auth.RequestTokenAsync();
                var tokenClient = new PubSubHttpClient(new ClientOptions
                {
                    TokenDetails = token,
                    Environment = settings.Environment,
                    UseBinaryProtocol = protocol == Defaults.Protocol
                });

                Assert.IsTrue(string.IsNullOrEmpty(tokenClient.AblyAuth.ClientId));
                var channel = tokenClient.Channels["persisted:test".AddRandomSuffix()];
                await channel.PublishAsync("test", "test");
                var message = (await channel.HistoryAsync()).Items.First();
                Assert.IsTrue(string.IsNullOrEmpty(message.ClientId));
                Assert.AreEqual("test", message.Data);
            });
        }

        [NUnit.Framework.Property("spec", "RSA8f2")]
        [UnityTest]
        public IEnumerator TokenAuthWithoutClientIdAndAMessageWithExplicitId_ShouldThrow([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var client = await AblySandbox.GetRestClient(protocol);
                var settings = await _sandboxFixture.GetSettings();
                var token = await client.Auth.RequestTokenAsync();
                var tokenClient = new PubSubHttpClient(new ClientOptions
                {
                    TokenDetails = token,
                    Environment = settings.Environment,
                    UseBinaryProtocol = protocol == Defaults.Protocol
                });

                await E7Assert.ThrowsAsync<AblyException>(tokenClient.Channels["test"]
                    .PublishAsync(new Message("test", "test") { ClientId = "123" }));
            });
        }

        [NUnit.Framework.Property("spec", "RSA8f3")]
        [UnityTest]
        public IEnumerator TokenAuthWithWildcardClientId_ShouldPublishMessageSuccessfullyAndClientIdShouldBeSetToWildcard([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var client = await AblySandbox.GetRestClient(protocol);
                var settings = await _sandboxFixture.GetSettings();
                var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
                var tokenClient = new PubSubHttpClient(new ClientOptions
                {
                    TokenDetails = token,
                    Environment = settings.Environment,
                    UseBinaryProtocol = protocol == Defaults.Protocol
                });

                var channel = tokenClient.Channels["pesisted:test"];
                await channel.PublishAsync("test", "test");
                Assert.AreEqual("*", tokenClient.AblyAuth.ClientId);
                var message = (await channel.HistoryAsync()).Items.First();
                Assert.IsTrue(string.IsNullOrEmpty(message.ClientId));
                Assert.AreEqual("test", message.Data);
            });
        }

        [NUnit.Framework.Property("spec", "RSA8f4")]
        [UnityTest]
        public IEnumerator TokenAuthWithWildcardClientId_WhenPublishingMessageWithClientId_ShouldExpectClientIdToBeSentWithTheMessage([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var client = await AblySandbox.GetRestClient(protocol);
                var settings = await _sandboxFixture.GetSettings();
                var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
                var tokenClient = new PubSubHttpClient(new ClientOptions
                {
                    TokenDetails = token,
                    Environment = settings.Environment,
                    UseBinaryProtocol = protocol == Defaults.Protocol
                });

                var channel = tokenClient.Channels["pesisted:test"];
                await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });
                Assert.AreEqual("*", tokenClient.AblyAuth.ClientId);
                var message = (await channel.HistoryAsync()).Items.First();
                Assert.AreEqual("123", message.ClientId);
                Assert.AreEqual("test", message.Data);
            });
        }

        [UnityTest]
        public IEnumerator TokenAuthUrlWhenPlainTextTokenIsReturn_ShouldBeAblyToPublishWithNewToken([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var client = await AblySandbox.GetRestClient(protocol);
                var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
                var settings = await _sandboxFixture.GetSettings();
                var authUrl = "http://echo.ably.io/?type=text&body=" + token.Token;

                var authUrlClient = new PubSubHttpClient(new ClientOptions
                {
                    AuthUrl = new Uri(authUrl),
                    Environment = settings.Environment,
                    UseBinaryProtocol = protocol == Defaults.Protocol
                });

                var channel = authUrlClient.Channels["pesisted:test"];
                await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });

                var message = (await channel.HistoryAsync()).Items.First();
                Assert.AreEqual("123", message.ClientId);
                Assert.AreEqual("test", message.Data);
            });
        }

        [UnityTest]
        public IEnumerator TokenAuthUrlWithJsonTokenReturned_ShouldBeAbleToPublishWithNewToken([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var client = await AblySandbox.GetRestClient(protocol);
                var token = await client.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
                var settings = await _sandboxFixture.GetSettings();
                var authUrl = "http://echo.ably.io/?type=json&body=" + Uri.EscapeUriString(token.ToJson());

                var authUrlClient = new PubSubHttpClient(new ClientOptions
                {
                    AuthUrl = new Uri(authUrl),
                    Environment = settings.Environment,
                    UseBinaryProtocol = protocol == Defaults.Protocol,
                    HttpRequestTimeout = new TimeSpan(0, 0, 20)
                });

                var channel = authUrlClient.Channels["pesisted:test"];
                await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });

                var message = (await channel.HistoryAsync()).Items.First();
                Assert.AreEqual("123", message.ClientId);
                Assert.AreEqual("test", message.Data);
            });
        }

        [UnityTest]
        public IEnumerator TokenAuthUrlWithJsonTokenReturned_ShouldBeAbleToConnect([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var ablyRest = await AblySandbox.GetRestClient(protocol);
                var token = await ablyRest.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
                var settings = await _sandboxFixture.GetSettings();
                var tokenJson = token.ToJson();
                var authUrl = "http://echo.ably.io/?type=json&body=" + Uri.EscapeUriString(tokenJson);

                var client = new PubSubRealtimeClient(new ClientOptions
                {
                    AuthUrl = new Uri(authUrl),
                    Environment = settings.Environment,
                    UseBinaryProtocol = protocol == Defaults.Protocol,
                    HttpRequestTimeout = new TimeSpan(0, 0, 20),
                    AutomaticNetworkStateMonitoring = false
                });

                await client.WaitForState();
                Assert.AreEqual(ConnectionState.Connected, client.Connection.State);
            });
        }

        [UnityTest]
        public IEnumerator TokenAuthUrlWithIncorrectJsonTokenReturned_ShouldNotBeAbleToConnectAndShouldHaveError([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var ablyRest = await AblySandbox.GetRestClient(protocol);
                var token = await ablyRest.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" });
                var settings = await _sandboxFixture.GetSettings();
                var incorrectJson = $"[{token.ToJson()}]";
                var authUrl = "http://echo.ably.io/?type=json&body=" + Uri.EscapeUriString(incorrectJson);

                var client = new PubSubRealtimeClient(new ClientOptions
                {
                    AuthUrl = new Uri(authUrl),
                    Environment = settings.Environment,
                    UseBinaryProtocol = protocol == Defaults.Protocol,
                    HttpRequestTimeout = new TimeSpan(0, 0, 20),
                    AutomaticNetworkStateMonitoring = false
                });

                var tsc = new TaskCompletionAwaiter();
                ErrorInfo err = null;
                client.Connection.On(ConnectionEvent.Disconnected, state =>
                {
                    err = state.Reason;
                    tsc.SetCompleted();
                });

                var b = await tsc.Task;
                Assert.IsTrue(b);
                Assert.IsNotNull(err);
                Assert.IsTrue(err.Message.StartsWith("Error parsing JSON response"));
                Assert.IsNotNull(err.InnerException);
            });
        }

        [UnityTest]
        public IEnumerator TokenAuthCallbackWithTokenDetailsReturned_ShouldBeAbleToPublishWithNewToken([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var settings = await _sandboxFixture.GetSettings();
                var tokenClient = await AblySandbox.GetRestClient(protocol);
                var authCallbackClient = await AblySandbox.GetRestClient(protocol, options =>
                {
                    options.AuthCallback = tokenParams =>
                        tokenClient.Auth.RequestTokenAsync(new TokenParams { ClientId = "*" }).Convert();
                    options.Environment = settings.Environment;
                    options.UseBinaryProtocol = protocol == Defaults.Protocol;
                });

                var channel = authCallbackClient.Channels["pesisted:test"];
                await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });

                await Task.Delay(1000);

                var message = (await channel.HistoryAsync()).Items.First();
                Assert.AreEqual("123", message.ClientId);
                Assert.AreEqual("test", message.Data);
            });
        }

        [UnityTest]
        public IEnumerator TokenAuthCallbackWithTokenRequestReturned_ShouldBeAbleToGetATokenAndPublishWithNewToken([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                var settings = await _sandboxFixture.GetSettings();
                var tokenClient = await AblySandbox.GetRestClient(protocol);
                var authCallbackClient = await AblySandbox.GetRestClient(protocol, options =>
                {
                    options.AuthCallback = async tokenParams =>
                        await tokenClient.Auth.CreateTokenRequestAsync(new TokenParams { ClientId = "*" });
                    options.Environment = settings.Environment;
                    options.UseBinaryProtocol = protocol == Defaults.Protocol;
                });

                var channel = authCallbackClient.Channels["pesisted:test"];
                await channel.PublishAsync(new Message("test", "test") { ClientId = "123" });

                var message = (await channel.HistoryAsync()).Items.First();
                Assert.AreEqual("123", message.ClientId);
                Assert.AreEqual("test", message.Data);
            });
        }

        [NUnit.Framework.Property("issue", "374")]
        [UnityTest]
        public IEnumerator WhenClientTimeIsWrongAndQueryTimeSetToTrue_ShouldNotTreatTokenAsInvalid([ValueSource(nameof(_protocols))] Protocol protocol)
        {
            return UniTask.ToCoroutine(async () =>
            {
                // Our device's clock is fast. The server returns by default a token valid for an hour
                DateTimeOffset NowFunc() => DateTimeOffset.UtcNow.AddHours(2);

                var realtimeClient = await AblySandbox.GetRealtimeClient(protocol, (opts, _) =>
                {
                    opts.NowFunc = NowFunc;
                    opts.QueryTime = true;
                    opts.ClientId = "clientId";
                    opts.UseTokenAuth = true; // We force the token auth because further on it's not necessary when there is a key present
                });

                await realtimeClient.WaitForState(ConnectionState.Connected);
            });
        }

        private class RSA4Helper
        {
            private AuthSpecs Specs { get; set; }

            public List<AblyRequest> Requests { get; set; }

            public RSA4Helper(AuthSpecs specs)
            {
                Requests = new List<AblyRequest>();
                Specs = specs;
            }

            public async Task<PubSubHttpClient> GetRestClientWithRequests(Protocol protocol, TokenDetails token,
                bool invalidateKey, Action<ClientOptions> optionsAction = null)
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

                var restClient = await Specs.AblySandbox.GetRestClient(protocol, optionsAction);

                // intercept http calls to demonstrate that the client did not attempt to request a new token
                var execute = restClient.ExecuteHttpRequest;
                restClient.ExecuteHttpRequest = request =>
                {
                    Requests.Add(request);
                    return execute.Invoke(request);
                };

                return restClient;
            }

            public async Task<PubSubRealtimeClient> GetRealTimeClientWithRequests(Protocol protocol, TokenDetails token,
                bool invalidateKey, Action<ClientOptions, TestEnvironmentSettings> optionsAction = null)
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

                var realtimeClient = await Specs.AblySandbox.GetRealtimeClient(protocol, optionsAction, (options, device) => restClient);

                return realtimeClient;
            }

            public Task<AblyResponse> AblyResponseWith500Status(AblyRequest request)
            {
                Requests.Add(request);
                var r = new AblyResponse(string.Empty, "application/json", string.Empty.GetBytes())
                    {StatusCode = HttpStatusCode.InternalServerError};
                return Task.FromResult(r);
            }
        }
    }
}
