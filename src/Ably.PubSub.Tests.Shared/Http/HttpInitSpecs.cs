using System;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests
{
    public class HttpInitSpecs : AblySpecs
    {
        private const string NoMeansProvidedToRenewAuthToken = "Library initialized with a token literal without any way to renew the token when it expires (no authUrl, authCallback, or key). See https://help.ably.io/error/40171 for help";

        [Fact]
        [Trait("spec", "RSA2")]
        public void Init_WithKeyAndNoClientId_SetsAuthMethodToBasic()
        {
            var client = new PubSubHttpClient(ValidKey);
            client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Basic);
        }

        [Trait("spec", "RSA4")]
        [Trait("spec", "RSC14b")]
        public class AuthMethodInitTests
        {
            [Fact]
            [Trait("spec", "RSA4")]
            public void WithUseTokenAuthSetToTrue_AuthMethodIsAlwaysTokenAuth()
            {
                var client = new PubSubHttpClient(new ClientOptions { Key = ValidKey, UseTokenAuth = true });
                client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
            }

            [Fact]
            [Trait("spec", "RSA4")]
            public void WithKeyAndClientId_ShouldUseBasicAuth()
            {
                var client = new PubSubHttpClient(new ClientOptions { Key = ValidKey, ClientId = "123" });
                client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Basic);
            }

            [Fact]
            [Trait("spec", "RSA4a1")]
            public void WithTokenButNoWayToRenew_ShouldLogErrorMessageWithError()
            {
                var testLogger = new TestLogger(NoMeansProvidedToRenewAuthToken);
                _ = new PubSubHttpClient(new ClientOptions { Token = "Test", Logger = testLogger });
                testLogger.MessageSeen.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSA4a1")]
            public void WithTokenDetailsButNoWayToRenew_ShouldLogErrorMessageWithError()
            {
                var testLogger = new TestLogger(NoMeansProvidedToRenewAuthToken);
                _ = new PubSubHttpClient(new ClientOptions { TokenDetails = new TokenDetails("test"), Logger = testLogger });
                testLogger.MessageSeen.Should().BeTrue();
            }

            [Fact]
            public void WithKeyNoClientIdAndAuthToken_ShouldSetCurrentToken()
            {
                ClientOptions options = new ClientOptions { Key = ValidKey, ClientId = "123", Token = "blah" };
                var client = new PubSubHttpClient(options);

                client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
                client.AblyAuth.CurrentToken.Token.Should().Be("blah");
            }

            [Fact]
            public void WithoutKey_ShouldUseTokenAuth()
            {
                var client = new PubSubHttpClient(opts =>
                {
                    opts.Key = "test.best:rest";
                    opts.Token = "blah";
                    opts.ClientId = "123";
                });

                client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
            }

            [Fact]
            public void WithToken_ShouldUseTokenAuth()
            {
                var client = new PubSubHttpClient(opts =>
                {
                    opts.Key = "test.best:rest";
                    opts.Token = "blah";
                });

                client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
            }

            [Fact]
            public void WithTokenDetails_ShouldUseTokenAuth()
            {
                var client = new PubSubHttpClient(opts =>
                {
                    opts.Key = "test.best:rest";
                    opts.TokenDetails = new TokenDetails("123");
                });

                client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
            }

            [Fact]
            public void WithAuthUrl_ShouldUseTokenAuth()
            {
                var client = new PubSubHttpClient(opts =>
                {
                    opts.Key = "test.best:rest";
                    opts.AuthUrl = new Uri("http://authUrl");
                });

                client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
            }

            [Fact]
            public void WithAuthCallback_ShouldUseTokenAuth()
            {
                var client = new PubSubHttpClient(opts =>
                {
                    opts.AuthCallback = @params => Task.FromResult<object>(new TokenDetails());
                });

                client.AblyAuth.AuthMethod.Should().Be(AuthMethod.Token);
            }

            [Fact]
            public void WithTokenOnly_SetsTokenRenewableToFalse()
            {
                var rest = new PubSubHttpClient(new ClientOptions { Token = "token_id" });

                rest.AblyAuth.TokenRenewable.Should().BeFalse();
            }

            [Fact]
            public void WithApiKey_SetsTokenRenewableToTrue()
            {
                var rest = new PubSubHttpClient(new ClientOptions(ValidKey));

                rest.AblyAuth.TokenRenewable.Should().BeTrue();
            }

            [Fact]
            public void WithAuthUrl_SetsTokenRenewableToTrue()
            {
                var rest = new PubSubHttpClient(new ClientOptions { AuthUrl = new Uri("http://boo") });

                rest.AblyAuth.TokenRenewable.Should().BeTrue();
            }

            [Fact]
            public void WithAuthCallback_SetsTokenRenewableToTrue()
            {
                var rest = new PubSubHttpClient(new ClientOptions { AuthCallback = token => Task.FromResult<object>(new TokenDetails()) });

                rest.AblyAuth.TokenRenewable.Should().BeTrue();
            }

            [Fact]
            [Trait("spec", "RSC1b")]
            public void WithoutTokenAuthAndNoKey_ShouldThrow()
            {
                var error = Assert.Throws<AblyException>(() => new PubSubHttpClient(new ClientOptions()));
                error.ErrorInfo.Code.Should().Be(ErrorCodes.UnableToObtainCredentialsFromGivenParameters);
            }
        }

        [Fact]
        public void Init_WithTlsAndSpecificPort_ShouldInitialiseHttpClientWithCorrectPort()
        {
            var client = new PubSubHttpClient(opts =>
            {
                opts.Tls = true;
                opts.TlsPort = 111;
                opts.Key = ValidKey;
            });
            client.HttpClient.Options.Port.Should().Be(111);
        }

        [Fact]
        public void Init_WithTlsFalseAndSpecificPort_ShouldInitialiseHttpClientWithCorrectPort()
        {
            var client = new PubSubHttpClient(opts =>
            {
                opts.Tls = false;
                opts.Port = 111;
                opts.Key = ValidKey;
            });
            client.HttpClient.Options.Port.Should().Be(111);
        }

        public HttpInitSpecs(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
