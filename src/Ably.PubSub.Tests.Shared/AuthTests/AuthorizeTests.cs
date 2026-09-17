using System;
using System.Reflection;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.AuthTests
{
    [Collection("UnitTests")]
    public class AuthorizeTests : AuthorizationTests
    {
        [Fact]
        public void TokenShouldNotBeSetBeforeAuthorizeIsCalled()
        {
            var client = GetRestClient();
            client.AblyAuth.CurrentToken.Should().BeNull();
        }

        [Fact]
        [Trait("spec", "RSA10a")]
        [Trait("spec", "RSA10f")]
        public async Task Authorize_WithBasicAuthCreatesTokenAndUsesTokenAuthInTheFuture()
        {
            var client = GetRestClient();
            client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Basic);
            var tokenDetails = await client.Auth.AuthorizeAsync();
            tokenDetails.Should().NotBeNull();
            client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
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

            rest.AblyAuth.CurrentAuthOptions.Should().BeEquivalentTo(authOptions);
            rest.AblyAuth.CurrentTokenParams.Should().BeEquivalentTo(tokenParams);
        }

        [Fact]
        [Trait("spec", "RSA10j")]
        public async Task Authorize_PreservesTokenRequestOptionsForSubsequentRequests()
        {
            var client = GetRestClient();
            var tokenParams = TokenParams.WithDefaultsApplied();
            tokenParams.Ttl = TimeSpan.FromMinutes(260);
            await client.Auth.AuthorizeAsync(tokenParams);
            await client.Auth.AuthorizeAsync();
            var data = LastRequest.PostData as TokenRequest;
            client.AblyAuth.CurrentTokenParams.Should().BeEquivalentTo(tokenParams);
            data.Ttl.Should().Be(TimeSpan.FromMinutes(260));
        }

        [Fact]
        [Trait("spec", "RSA10g")]
        public async Task ShouldKeepTokenParamsAndAuthOptionsExceptForceAndCurrentTimestamp()
        {
            var client = GetRestClient();
            var testAblyAuth = new TestAblyAuth(client.Options, client);
            var customTokenParams = TokenParams.WithDefaultsApplied();
            _ = customTokenParams.Merge(new TokenParams { Ttl = TimeSpan.FromHours(2), Timestamp = Now.AddHours(1) });
            var customAuthOptions = AuthOptions.FromExisting(testAblyAuth.Options);
            customAuthOptions.UseTokenAuth = true;

            await testAblyAuth.AuthorizeAsync(customTokenParams, customAuthOptions);
            var expectedTokenParams = customTokenParams.Clone();
            expectedTokenParams.Timestamp = null;
            testAblyAuth.CurrentTokenParams.Should().BeEquivalentTo(expectedTokenParams);

            testAblyAuth.CurrentAuthOptions.Should().BeSameAs(customAuthOptions);
            testAblyAuth.CurrentTokenParams.Timestamp.Should().Be(null);
        }

        [Fact]
        [Trait("spec", "RSA10g")]
        public async Task ShouldKeepCurrentTokenParamsAndOptionsEvenIfCurrentTokenIsValidAndNoNewTokenIsRequested()
        {
            var client = GetRestClient(
                null,
                opts => opts.TokenDetails = new TokenDetails("boo") { Expires = Now.AddHours(10) });

            var testAblyAuth = new TestAblyAuth(client.Options, client);
            var customTokenParams = TokenParams.WithDefaultsApplied();
            customTokenParams.Ttl = TimeSpan.FromHours(2);
            customTokenParams.Timestamp = Now.AddHours(1);

            var customAuthOptions = AuthOptions.FromExisting(testAblyAuth.Options);
            customAuthOptions.UseTokenAuth = true;

            await testAblyAuth.AuthorizeAsync(customTokenParams, customAuthOptions);
            var expectedTokenParams = customTokenParams.Clone();
            expectedTokenParams.Timestamp = null;
            testAblyAuth.CurrentTokenParams.Should().BeEquivalentTo(expectedTokenParams);
            testAblyAuth.CurrentAuthOptions.Should().BeSameAs(customAuthOptions);
        }

        [Fact]
        [Trait("spec", "RSA10b")]
        [Trait("spec", "RSA10e")]
        [Trait("spec", "RSA10h")]
        [Trait("spec", "RSA10i")]
        public async Task Authorize_UseRequestTokenToCreateTokensAndPassesTokenParamsAndAuthOptions()
        {
            var client = GetRestClient();
            var testAblyAuth = new TestAblyAuth(client.Options, client);
            testAblyAuth.Options.UseTokenAuth = true;
            var customAuthOptions = testAblyAuth.Options;
            var customTokenParams = TokenParams.WithDefaultsApplied();
            customTokenParams.Ttl = TimeSpan.FromHours(2);

            await testAblyAuth.AuthorizeAsync(customTokenParams, customAuthOptions);

            testAblyAuth.RequestTokenCalled.Should().BeTrue("Token creation was not delegated to RequestToken");
            testAblyAuth.LastRequestTokenParams.Should().BeSameAs(customTokenParams);
            testAblyAuth.LastRequestAuthOptions.Should().BeSameAs(customAuthOptions);
        }

        [Fact]
        [Trait("spec", "RSA10l")]
        public async Task Authorize_RestClientAuthoriseMethodsShouldBeMarkedObsoleteAndLogADeprecationWarning()
        {
            /* Check for Obsolete Attribute  */
            MethodBase method = typeof(AblyAuth).GetMethod("Authorise");
            method.Should().NotBeNull();
            var attr = (ObsoleteAttribute)method?.GetCustomAttribute(typeof(ObsoleteAttribute));
            attr.Should().NotBeNull();

            method = typeof(AblyAuth).GetMethod("AuthoriseAsync");
            method.Should().NotBeNull();
            attr = (ObsoleteAttribute)method?.GetCustomAttribute(typeof(ObsoleteAttribute));
            attr.Should().NotBeNull();

            method = typeof(AblyAuth).GetMethod("Authorize");
            method.Should().NotBeNull();
            attr = (ObsoleteAttribute)method?.GetCustomAttribute(typeof(ObsoleteAttribute));
            attr.Should().BeNull();

            method = typeof(AblyAuth).GetMethod("AuthorizeAsync");
            method.Should().NotBeNull();
            attr = (ObsoleteAttribute)method?.GetCustomAttribute(typeof(ObsoleteAttribute));
            attr.Should().BeNull();

#pragma warning disable CS0618 // Type or member is obsolete
            /* Check for logged warning */
            var testLogger1 = new TestLogger("AuthoriseAsync is deprecated and will be removed in the future, please replace with a call to AuthorizeAsync");
            var client = GetRestClient(setOptionsAction: options => { options.Logger = testLogger1; });
            var testAblyAuth = new TestAblyAuth(client.Options, client);
            _ = await testAblyAuth.AuthoriseAsync();
            testLogger1.MessageSeen.Should().BeTrue();

            var testLogger2 = new TestLogger("Authorise is deprecated and will be removed in the future, please replace with a call to Authorize");
            client = GetRestClient(setOptionsAction: options => { options.Logger = testLogger2; });
            testAblyAuth = new TestAblyAuth(client.Options, client);
            _ = testAblyAuth.Authorise();
            testLogger2.MessageSeen.Should().BeTrue();
#pragma warning restore CS0618 // Type or member is obsolete
        }

        [Fact]
        [Trait("bug", "346")]
        public async Task Issue336_SetServerTimeExceptionsShouldBeHandled()
        {
            /*
             * This is a test to demonstrate the fix for issue
             * https://github.com/ably/ably-dotnet/issues/346
             * against the 1.1.6 release this test would fail
             */

            var client = GetRestClient();
            bool serverTimeCalled = false;

            // configure serverTime delegate to throw an exception
            var testAblyAuth = new TestAblyAuth(client.Options, client, () =>
                {
                    serverTimeCalled = true;
                    throw new Exception("baz");
                });

            var tokenParams = new TokenParams();
            testAblyAuth.Options.QueryTime = true;

            Exception exception = null;
            try
            {
                await testAblyAuth.RequestTokenAsync(tokenParams, client.Options);
            }
            catch (Exception e)
            {
                exception = e;
            }

            serverTimeCalled.Should().BeTrue();
            exception.Should().NotBeNull();
        }

        public AuthorizeTests(ITestOutputHelper helper)
            : base(helper) { }
    }
}
