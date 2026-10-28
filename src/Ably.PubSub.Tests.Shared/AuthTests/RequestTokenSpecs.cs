using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.AuthTests
{
    [Collection("UnitTests")]
    public class RequestTokenSpecs : AuthorizationTests
    {
        [Fact]
        [Trait("spec", "RSA8e")]
        [Trait("spec", "RSA8a")]
        public void WithDefaultParamsAndNothingSpecifiedInMethod_UsesDefaultParams()
        {
            var client = GetRestClient(
                null,
                options => options.DefaultTokenParams =
                               new TokenParams { ClientId = "123", Ttl = TimeSpan.FromHours(2) });

            client.Auth.RequestTokenAsync();
            var data = LastRequest.PostData as TokenRequest;
            data.ClientId.Should().Be("123");
            data.Ttl.Should().Be(TimeSpan.FromHours(2));
        }

        [Fact]
        [Trait("spec", "RSA8e")]
        public async Task WithNoTokenParamsAndNoExtraOptions_CreatesDefaultRequestWithIdClientIdAndBlankCapability()
        {
            var client = GetRestClient(null, options => options.ClientId = "Test");
            await client.Auth.RequestTokenAsync();

            var data = LastRequest.PostData as TokenRequest;
            data.KeyName.Should().Be(KeyId);
            data.Capability.Should().Be(Capability.AllowAll);
            data.ClientId.Should().Be(client.Options.ClientId);
        }

        [Fact]
        [Trait("spec", "RSA8b")]
        public async Task WithDefaultTokenParamsAndTokenParamsSpecified_ShouldUseOnlyParamsPassedIntoTheMethod()
        {
            var client = GetRestClient(
                null,
                options => options.DefaultTokenParams = new TokenParams
                                                            {
                                                                ClientId = "123", Ttl = TimeSpan.FromHours(2)
                                                            });

            var capability = new Capability();
            capability.AddResource("a").AllowAll();
            var methodParams = new TokenParams
                                   {
                                       Capability = capability,
                                       ClientId = "999",
                                       Ttl = TimeSpan.FromMinutes(1),
                                       Nonce = "123",
                                       Timestamp = Now.AddHours(1)
                                   };

            await client.Auth.RequestTokenAsync(methodParams);

            var data = LastRequest.PostData as TokenRequest;
            data.Capability.Should().Be(capability);
            data.ClientId.Should().Be(methodParams.ClientId);
            data.Ttl.Should().Be(methodParams.Ttl);
            data.Nonce.Should().Be(methodParams.Nonce);
            data.Timestamp.Should().Be(methodParams.Timestamp);
        }

        [Fact]
        public async Task RequestToken_CreatesPostRequestWithCorrectUrl()
        {
            // Arrange
            await SendRequestTokenWithValidOptions();

            // Assert
            Assert.Equal("/keys/" + KeyId + "/requestToken", LastRequest.Url);
            Assert.Equal(HttpMethod.Post, LastRequest.Method);
        }

        [Fact]
        public async Task RequestToken_SetsRequestTokenRequestToRequestPostData()
        {
            await SendRequestTokenWithValidOptions();

            LastRequest.PostData.Should().BeOfType<TokenRequest>();
        }

        [Fact]
        public void RequestToken_WithTokenRequestWithoutId_UsesRestClientDefaultKeyId()
        {
            var client = GetRestClient();

            client.Auth.RequestTokenAsync(new TokenParams());

            var data = LastRequest.PostData as TokenRequest;
            Assert.Equal(client.Options.ParseKey().KeyName, data.KeyName);
        }

        [Fact]
        public async Task RequestToken_WithTokenRequestWithoutCapability_SetsBlankCapability()
        {
            var tokenParams = new TokenParams();

            var client = GetRestClient();

            await client.Auth.RequestTokenAsync(tokenParams);

            var data = LastRequest.PostData as TokenRequest;
            Assert.Equal(Capability.AllowAll, data.Capability);
        }

        [Fact]
        public async Task RequestToken_TimeStamp_SetsTimestampOnTheDataRequest()
        {
            var date = new DateTimeOffset(2014, 1, 1, 0, 0, 0, TimeSpan.Zero);
            var tokenParams = new TokenParams { Timestamp = date };

            var client = GetRestClient();

            await client.Auth.RequestTokenAsync(tokenParams);

            var data = LastRequest.PostData as TokenRequest;
            date.Should().BeCloseTo(data.Timestamp.Value, TimeSpan.FromMilliseconds(20));
        }

        [Fact]
        public async Task RequestToken_WithoutTimeStamp_SetsCurrentTimeOnTheRequest()
        {
            var tokenParams = new TokenParams();

            var client = GetRestClient();
            await client.Auth.RequestTokenAsync(tokenParams);

            var data = LastRequest.PostData as TokenRequest;
            Now.Should().BeCloseTo(data.Timestamp.Value, TimeSpan.FromMilliseconds(200));
        }

        [Fact]
        public async Task RequestToken_WithQueryTime_SendsTimeRequestAndUsesReturnedTimeForTheRequest()
        {
            var rest = GetRestClient();
            var currentTime = DateTimeOffset.UtcNow;
            rest.ExecuteHttpRequest = x =>
                {
                    if (x.Url.Contains("time"))
                    {
                        return ("[" + currentTime.ToUnixTimeInMilliseconds() + "]").ToAblyJsonResponse();
                    }

                    // Assert
                    var data = x.PostData as TokenRequest;
                    data.Timestamp.Should().BeCloseTo(currentTime, TimeSpan.FromMilliseconds(100));
                    return DummyTokenResponse.ToTask();
                };
            var tokenParams = new TokenParams
                                  {
                                      Capability = new Capability(),
                                      ClientId = "ClientId",
                                      Ttl = TimeSpan.FromMinutes(10)
                                  };

            // Act
            await rest.Auth.RequestTokenAsync(
                tokenParams,
                AuthOptions.FromExisting(rest.Options).Merge(new AuthOptions { QueryTime = true }));
        }

        [Fact]
        public async Task RequestToken_WithoutQueryTime_SendsTimeRequestAndUsesReturnedTimeForTheRequest()
        {
            var rest = GetRestClient();
            rest.ExecuteHttpRequest = x =>
                {
                    Assert.DoesNotContain("time", x.Url);
                    return DummyTokenResponse.ToTask();
                };

            var tokenParams = new TokenParams
                                  {
                                      Capability = new Capability(),
                                      ClientId = "ClientId",
                                      Ttl = TimeSpan.FromMinutes(10)
                                  };

            // Act
            await rest.Auth.RequestTokenAsync(
                tokenParams,
                AuthOptions.FromExisting(rest.Options).Merge(new AuthOptions { QueryTime = false }));
        }

        [Fact]
        [Trait("spec", "RSA8d")]
        public async Task RequestToken_WithAuthCallback_RetrievesTokenFromCallback()
        {
            var rest = GetRestClient();
            var tokenRequest = new TokenParams { Capability = new Capability() };

            var authCallbackCalled = false;
            var token = new TokenDetails();
            var options = new AuthOptions
                              {
                                  AuthCallback = (x) =>
                                      {
                                          authCallbackCalled = true;
                                          return Task.FromResult<object>(token);
                                      }
                              };
            var result = await rest.Auth.RequestTokenAsync(tokenRequest, options);

            authCallbackCalled.Should().BeTrue();
            Assert.Same(token, result);
        }

        [Theory]
        [Trait("spec", "RSA8d")]
        [Trait("spec", "RSA4f")]
        [InlineData("eyJhbGciOiJIUzI1NiIsInR5cCI6IkpXVCJ9.eyJ4IjoxfQ.c2lnbmF0dXJl")]
        [InlineData("xVLyHw.A-pwh7wicf3afTfgiw4k2Ku33kcnSA7z6y8FjuYpe3QaNRTEo4")]
        public async Task RequestToken_WithAuthCallbackReturningTokenString_ReturnsTokenDetailsWithoutHttpRequest(string tokenString)
        {
            var rest = GetRestClient();
            var options = new AuthOptions { AuthCallback = _ => Task.FromResult<object>(tokenString) };

            var result = await rest.Auth.RequestTokenAsync(new TokenParams(), options);

            result.Token.Should().Be(tokenString);
            Requests.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSA8d")]
        [Trait("spec", "RSA4f")]
        public async Task RequestToken_WithAuthCallbackReturningSerializedTokenRequest_PostsToRequestToken()
        {
            var rest = GetRestClient();
            var serializedRequest = await rest.Auth.CreateTokenRequestAsync(new TokenParams { ClientId = "123" }, null);

            var tokenDetails = new TokenDetails("requested-token")
            {
                Expires = DateTimeOffset.UtcNow.AddHours(1),
                Issued = DateTimeOffset.UtcNow,
            };
            rest.ExecuteHttpRequest = request =>
            {
                Requests.Add(request);
                return JsonHelper.Serialize(tokenDetails).ToAblyJsonResponse();
            };

            var options = new AuthOptions { AuthCallback = _ => Task.FromResult<object>(serializedRequest) };
            var result = await rest.Auth.RequestTokenAsync(new TokenParams(), options);

            Requests.Should().HaveCount(1);
            Requests[0].Url.Should().Be($"/keys/{KeyId}/requestToken");
            ((TokenRequest)Requests[0].PostData).ClientId.Should().Be("123");
            result.Token.Should().Be("requested-token");
        }

        [Fact]
        [Trait("spec", "RSA8d")]
        [Trait("spec", "RSA4f")]
        public async Task RequestToken_WithAuthCallbackReturningSerializedTokenDetails_ReturnsTokenDetailsWithoutHttpRequest()
        {
            var rest = GetRestClient();
            var tokenDetails = new TokenDetails("abc.def")
            {
                Expires = DateTimeOffset.UtcNow.AddHours(1),
                Issued = DateTimeOffset.UtcNow,
            };
            var serialized = JsonHelper.Serialize(tokenDetails);
            var options = new AuthOptions { AuthCallback = _ => Task.FromResult<object>(serialized) };

            var result = await rest.Auth.RequestTokenAsync(new TokenParams(), options);

            result.Token.Should().Be("abc.def");
            Requests.Should().BeEmpty();
        }

        [Theory]
        [Trait("spec", "RSA8d")]
        [Trait("spec", "RSA4c1")]
        [InlineData("")]
        [InlineData("   ")]
        public async Task RequestToken_WithAuthCallbackReturningEmptyString_ThrowsClientAuthProviderRequestFailed(string callbackResult)
        {
            var rest = GetRestClient();
            var options = new AuthOptions { AuthCallback = _ => Task.FromResult<object>(callbackResult) };

            var ex = await Assert.ThrowsAsync<AblyException>(() => rest.Auth.RequestTokenAsync(new TokenParams(), options));

            ex.ErrorInfo.Code.Should().Be(ErrorCodes.ClientAuthProviderRequestFailed);
            ex.ErrorInfo.Cause.Message.Should().Contain("empty string");
            Requests.Should().BeEmpty();
        }

        [Fact]
        [Trait("spec", "RSA8e")]
        public async Task RequestToken_TokenParamsAndAuthOptionsReplaceConfiguredDefaults()
        {
            var rest = GetRestClient();

            const string capabilityString = "{\"cansubscribe:*\":[\"subscribe\"]}";
            const string fakeApiKey = "foo.bar:baz";
            var cap = new Capability(capabilityString);

            var tokenParams = new TokenParams { Capability = cap };

            var authOptions = new AuthOptions(fakeApiKey);
            _ = await rest.AblyAuth.RequestTokenAsync(tokenParams, authOptions);

            var tokenRequest = (TokenRequest)Requests[0].PostData;
            tokenRequest.Capability.Should().Be(cap);
            fakeApiKey.Should().StartWith(tokenRequest.KeyName);

            rest.AblyAuth.CurrentAuthOptions.Should().NotBe(authOptions);
            rest.AblyAuth.CurrentTokenParams.Should().NotBe(tokenParams);
        }

        [Fact]
        [Trait("spec", "RSA9h")]
        public async Task CreateTokenRequest_TokenParamsAndAuthOptionsReplaceConfiguredDefaults()
        {
            var rest = GetRestClient();

            const string capabilityString = "{\"cansubscribe:*\":[\"subscribe\"]}";
            const string fakeApiKey = "foo.bar:baz";
            var cap = new Capability(capabilityString);

            var tokenParams = new TokenParams { Capability = cap };

            var authOptions = new AuthOptions(fakeApiKey);
            var result = await rest.AblyAuth.CreateTokenRequestAsync(tokenParams, authOptions);

            var tokenRequest = JsonHelper.Deserialize<TokenRequest>(result);

            tokenRequest.Capability.Should().Be(cap);
            fakeApiKey.Should().StartWith(tokenRequest.KeyName);

            // should not replace any previously configured TokenParams and AuthOptions on instance
            rest.AblyAuth.CurrentAuthOptions.Should().NotBe(authOptions);
            rest.AblyAuth.CurrentTokenParams.Should().NotBe(tokenParams);
        }

        [Fact]
        [Trait("spec", "RSA10j")]
        public async Task Authorize_TokenParamsAndAuthOptionsReplaceConfiguredDefaults()
        {
            var rest = GetRestClient();

            const string capabilityString = "{\"cansubscribe:*\":[\"subscribe\"]}";
            const string fakeApiKey = "foo.bar:baz";
            var cap = new Capability(capabilityString);

            var tokenParams = new TokenParams { Capability = cap };
            var authOptions = new AuthOptions(fakeApiKey);
            _ = await rest.AblyAuth.AuthorizeAsync(tokenParams, authOptions);

            var tokenRequest = (TokenRequest)Requests[0].PostData;
            tokenRequest.Capability.Should().Be(cap);
            fakeApiKey.Should().StartWith(tokenRequest.KeyName);

            // should not replace any previously configured TokenParams and AuthOptions on instance
            rest.AblyAuth.CurrentAuthOptions.Should().BeEquivalentTo(authOptions);
            rest.AblyAuth.CurrentTokenParams.Should().BeEquivalentTo(tokenParams);
        }

        [Fact]
        [Trait("spec", "RSA8c")]
        [Trait("spec", "RSA8c1a")]
        [Trait("spec", "RSA8c2")]
        public async Task WithAuthUrlAndDefaultAuthMethod_SendsGetRequestToTheUrlAndPassesQueryParameters()
        {
            var rest = GetRestClient(
                AuthExecuteHttpRequest,
                opts =>
                    {
                        opts.DefaultTokenParams = new TokenParams { Ttl = TimeSpan.FromHours(2) };
                        opts.AuthUrl = new Uri("http://authUrl");
                        opts.AuthHeaders = new Dictionary<string, string> { { "Test", "Test" } };
                        opts.AuthParams = new Dictionary<string, string> { { "Test", "Test" }, { "TTl", "123" } };
                    });

            // Act
            await rest.Auth.RequestTokenAsync();

            // Expected will be { "ttl" : "intvalue", "Test" :"Test" }
            var expectedAuthParams = new Dictionary<string, string>
                                         {
                                             { "ttl", TimeSpan.FromHours(2).TotalMilliseconds.ToString(CultureInfo.InvariantCulture) },
                                             { "Test", "Test" }
                                         };

            // Assert
            Assert.Equal(HttpMethod.Get, FirstRequest.Method);
            Assert.Equal(rest.Options.AuthHeaders, FirstRequest.Headers);
            Assert.Equal(expectedAuthParams, FirstRequest.QueryParameters);
        }

        private Task<AblyResponse> AuthExecuteHttpRequest(AblyRequest request)
        {
            if (request.Url.Contains("authUrl"))
            {
                return JsonHelper.Serialize(new TokenRequest { ClientId = "123" }).ToAblyResponse();
            }

            return DummyTokenResponse.ToTask();
        }

        [Fact]
        [Trait("spec", "RSA8c")]
        [Trait("spec", "RSA8c3")]
        public async Task WithDefaultAuthParamsAndHeadersAndSpecifiedOnce_ShouldIgnoreTheDefaultOnesAndNowMergeThem()
        {
            var rest = GetRestClient(
                AuthExecuteHttpRequest,
                opts =>
                    {
                        opts.DefaultTokenParams = new TokenParams { Ttl = TimeSpan.FromHours(2) };
                        opts.AuthUrl = new Uri("http://authUrl");
                        opts.AuthHeaders = new Dictionary<string, string> { { "default", "default" } };
                        opts.AuthParams = new Dictionary<string, string> { { "default", "default" } };
                    });

            var options = new AuthOptions
                              {
                                  AuthUrl = new Uri("http://authUrl"),
                                  AuthHeaders = new Dictionary<string, string> { { "Test", "Test" } },
                                  AuthParams = new Dictionary<string, string> { { "Test", "Test" } },
                              };

            // Act
            await rest.Auth.RequestTokenAsync(null, options);

            // Expected will be { "ttl" : "intvalue", "Test" :"Test" }
            var expectedAuthParams = new Dictionary<string, string>
                                         {
                                             { "ttl", TimeSpan.FromHours(2).TotalMilliseconds.ToString(CultureInfo.InvariantCulture) },
                                             { "Test", "Test" }
                                         };

            // Assert
            Assert.Equal(HttpMethod.Get, FirstRequest.Method);
            Assert.Equal(options.AuthHeaders, FirstRequest.Headers);
            Assert.Equal(expectedAuthParams, FirstRequest.QueryParameters);
        }

        [Fact]
        [Trait("spec", "RSA8c")]
        [Trait("spec", "RSA8c1b")]
        public async Task WithAuthUrlAndAuthMethodPost_SendPostRequestToAuthUrlAndPassesPostParameters()
        {
            var rest = GetRestClient(
                AuthExecuteHttpRequest,
                opts =>
                    {
                        opts.AuthUrl = new Uri("http://authUrl");
                        opts.AuthHeaders = new Dictionary<string, string> { { "Test", "Test" } };
                        opts.AuthParams =
                            new Dictionary<string, string> { { "Test", "Test" }, { "Capability", "true" } };
                        opts.AuthMethod = HttpMethod.Post;
                    });

            var tokenParams = new TokenParams { Capability = new Capability() };

            await rest.Auth.RequestTokenAsync(tokenParams);

            var expectedParams = new Dictionary<string, string>
                                     {
                                         { "capability", string.Empty }, { "Test", "Test" }

                                         // Duplicate param so the value from TokenParams takes precedence
                                     };

            Assert.Equal(HttpMethod.Post, FirstRequest.Method);

            Assert.Equal(rest.Options.AuthHeaders, FirstRequest.Headers);
            Assert.Equal(expectedParams, FirstRequest.PostParameters);
            Assert.Equal(rest.Options.AuthUrl.ToString(), FirstRequest.Url);
        }

        [Fact]
        public async Task WithAuthUrlWhenTokenStringIsReturn_ReturnsToken()
        {
            var rest = GetRestClient(null, options => options.AuthUrl = new Uri("http://authUrl"));

            rest.ExecuteHttpRequest = (x) =>
                {
                    if (x.Url == rest.Options.AuthUrl.ToString())
                    {
                        return new AblyResponse { Type = ResponseType.Text, TextResponse = "TokenString" }.ToTask();
                    }

                    return "{}".ToAblyResponse();
                };

            var token = await rest.Auth.RequestTokenAsync();
            token.Token.Should().Be("TokenString");
        }

        [Fact]
        [Trait("spec", "RSA8c")]
        public async Task WithAuthUrlWhenTokenIsReturned_ReturnsToken()
        {
            var rest = GetRestClient();
            var options = new AuthOptions { AuthUrl = new Uri("http://authUrl") };

            var dateTime = DateTimeOffset.UtcNow;
            rest.ExecuteHttpRequest = (x) =>
                {
                    if (x.Url == options.AuthUrl.ToString())
                    {
                        return ("{ " + "\"keyName\":\"123\"," + "\"expires\":" + dateTime.ToUnixTimeInMilliseconds()
                                + "," + "\"issued\":" + dateTime.ToUnixTimeInMilliseconds() + ","
                                + "\"capability\":\"{}\"," + "\"clientId\":\"111\"" + "}").ToAblyResponse();
                    }

                    return "{}".ToAblyResponse();
                };

            var tokenRequest = new TokenParams { Capability = new Capability() };

            var token = await rest.Auth.RequestTokenAsync(tokenRequest, options);
            token.Should().NotBeNull();
            dateTime.Should().BeCloseTo(token.Issued, TimeSpan.FromSeconds(1));
        }

        [Fact]
        public async Task WithAuthUrlTokenRequest_GetsResultAndPostToRetrieveToken()
        {
            var rest = GetRestClient();
            var options = new AuthOptions { AuthUrl = new Uri("http://authUrl") };
            List<AblyRequest> requests = new List<AblyRequest>();
            var requestData = new TokenRequest { KeyName = KeyId, Capability = new Capability(), Mac = "mac" };
            rest.ExecuteHttpRequest = (x) =>
                {
                    requests.Add(x);
                    if (x.Url == options.AuthUrl.ToString())
                    {
                        return JsonHelper.Serialize(requestData).ToAblyResponse();
                    }

                    return DummyTokenResponse.ToTask();
                };

            var tokenParams = new TokenParams { Capability = new Capability() };

            await rest.Auth.RequestTokenAsync(tokenParams, options);

            requests.Count.Should().Be(2);
            var last = requests.Last().PostData as TokenRequest;
            last.Should().BeEquivalentTo(requestData);
        }

        [Fact]
        public async Task RequestToken_WithAuthUrlWhichReturnsAnErrorThrowsAblyException()
        {
            var rest = GetRestClient();
            var options = new AuthOptions { AuthUrl = new Uri("http://authUrl") };

            rest.ExecuteHttpRequest = (x) => throw new AblyException("Testing");

            var tokenParams = new TokenParams { Capability = new Capability() };

            _ = await Assert.ThrowsAsync<AblyException>(() => rest.Auth.RequestTokenAsync(tokenParams, options));
        }

        [Fact]
        public async Task RequestToken_WithAuthUrlWhichReturnsNonJsonContentType_ThrowsException()
        {
            var rest = GetRestClient();
            var options = new AuthOptions { AuthUrl = new Uri("http://authUrl") };
            rest.ExecuteHttpRequest = (x) => Task.FromResult(new AblyResponse { Type = ResponseType.Binary });

            var tokenParams = new TokenParams { Capability = new Capability() };

            await Assert.ThrowsAsync<AblyException>(() => rest.Auth.RequestTokenAsync(tokenParams, options));
        }

        private async Task SendRequestTokenWithValidOptions()
        {
            var rest = GetRestClient();
            var tokenParams = new TokenParams
                                  {
                                      Capability = new Capability(),
                                      ClientId = "ClientId",
                                      Ttl = TimeSpan.FromMinutes(10)
                                  };

            // Act
            await rest.Auth.RequestTokenAsync(tokenParams);
        }

        public RequestTokenSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
