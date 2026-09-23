using System;
using System.Threading;
using System.Threading.Tasks;

using FluentAssertions;
using Xunit;
using Xunit.Abstractions;

namespace IO.Ably.Tests.AuthTests
{
    [Collection("UnitTests")]
    public class ServerTimeTests : AuthorizationTests
    {
        [Fact]
        [Trait("spec", "RSA10k")]
        public async Task Authorize_WillObtainServerTimeAndPersistTheOffsetFromTheLocalClock()
        {
            var client = GetRestClient();
            bool serverTimeCalled = false;

            // configure the AblyAuth test wrapper to return UTC+30m when ServerTime() is called
            // (By default the library uses DateTimeOffset.UtcNow whe Now() is called)
            var testAblyAuth = new TestAblyAuth(client.Options, client, () =>
            {
                serverTimeCalled = true;
                return Task.FromResult(DateTimeOffset.UtcNow.AddMinutes(30));
            });

            // RSA10k: If the AuthOption argument’s queryTime attribute is true
            // it will obtain the server time once and persist the offset from the local clock.
            var tokenParams = new TokenParams();
            testAblyAuth.Options.QueryTime = true;
            await testAblyAuth.AuthorizeAsync(tokenParams);
            serverTimeCalled.Should().BeTrue();
            testAblyAuth.GetServerNow().Should().HaveValue();
            const int precision = 1000;
            testAblyAuth.GetServerNow()?.Should().BeCloseTo(await testAblyAuth.GetServerTime(), TimeSpan.FromMilliseconds(precision)); // Allow 1s clock skew
            testAblyAuth.GetServerNow()?.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromMilliseconds(precision)); // Allow 1s clock skew
        }

        [Fact]
        [Trait("spec", "RSA10k")]
        public async Task Authorize_WillObtainServerTimeAndPersist_ShouldShowValuesAreCalculated()
        {
            var client = GetRestClient();

            // configure the AblyAuth test wrapper to return UTC+30m when ServerTime() is called
            // (By default the library uses DateTimeOffset.UtcNow whe Now() is called)
            var testAblyAuth = new TestAblyAuth(client.Options, client, () => Task.FromResult(DateTimeOffset.UtcNow.AddMinutes(30)));

            // RSA10k: If the AuthOption argument’s queryTime attribute is true
            // it will obtain the server time once and persist the offset from the local clock.
            testAblyAuth.Options.QueryTime = true;
            var tokenParams = new TokenParams();
            testAblyAuth.Options.QueryTime = true;
            await testAblyAuth.AuthorizeAsync(tokenParams);

            // to show the values are calculated and not fixed
            // get the current server time offset, pause for a short time,
            // then get it again.
            // The new value should represent a time after the first
            var snapshot = testAblyAuth.GetServerNow();
            await Task.Delay(500);
            testAblyAuth.GetServerNow()?.Should().BeAfter(snapshot.Value);
        }

        [Fact]
        [Trait("spec", "RSA10k")]
        public async Task Authorize_WillObtainServerTimeAndPersist_ShouldNotUserServerTimeWhenAuthObjectIsReset()
        {
            var client = GetRestClient();
            var serverTimeCalled = 0;

            // configure the AblyAuth test wrapper to return UTC+30m when ServerTime() is called
            // (By default the library uses DateTimeOffset.UtcNow whe Now() is called)
            var testAblyAuth = new TestAblyAuth(client.Options, client, () =>
            {
                Interlocked.Increment(ref serverTimeCalled);
                return Task.FromResult(DateTimeOffset.UtcNow.AddMinutes(30));
            });

            // RSA10k: If the AuthOption argument’s queryTime attribute is true
            // it will obtain the server time once and persist the offset from the local clock.
            testAblyAuth.Options.QueryTime = true;
            var tokenParams = new TokenParams();
            testAblyAuth.Options.QueryTime = true;
            await testAblyAuth.AuthorizeAsync(tokenParams);

            // intercept the (mocked) HttpRequest so we can get a reference to the AblyRequest
            TokenRequest tokenRequest = null;
            var exFunc = client.ExecuteHttpRequest;
            client.ExecuteHttpRequest = request =>
            {
                tokenRequest = request.PostData as TokenRequest;
                return exFunc(request);
            };

            // reset auth object
            testAblyAuth = new TestAblyAuth(client.Options, client, () => Task.FromResult(DateTimeOffset.UtcNow.AddDays(1)));
            testAblyAuth.Options.QueryTime = false;
            await testAblyAuth.AuthorizeAsync();

            // the TokenRequest should not have been set using an offset, but should have been set
            tokenRequest.Timestamp.Should().HaveValue();
            tokenRequest.Timestamp.Should().BeCloseTo(DateTimeOffset.UtcNow, TimeSpan.FromMilliseconds(1000));
        }

        [Fact]
        [Trait("spec", "RSA10k")]
        public async Task Authorize_WillObtainServerTimeAndPersist_ShouldUserServerTimeEvenIfFurtherRequestsDoNotHaveQueryTimeSetToTrue()
        {
            var client = GetRestClient();
            var serverTimeCalled = 0;

            // configure the AblyAuth test wrapper to return UTC+30m when ServerTime() is called
            // (By default the library uses DateTimeOffset.UtcNow whe Now() is called)
            var testAblyAuth = new TestAblyAuth(client.Options, client, () =>
            {
                Interlocked.Increment(ref serverTimeCalled);
                return Task.FromResult(DateTimeOffset.UtcNow.AddMinutes(30));
            });

            // RSA10k: If the AuthOption argument’s queryTime attribute is true
            // it will obtain the server time once and persist the offset from the local clock.
            testAblyAuth.Options.QueryTime = true;
            var tokenParams = new TokenParams();
            testAblyAuth.Options.QueryTime = true;
            await testAblyAuth.AuthorizeAsync(tokenParams);

            // intercept the (mocked) HttpRequest so we can get a reference to the AblyRequest
            TokenRequest tokenRequest = null;
            var exFunc = client.ExecuteHttpRequest;
            client.ExecuteHttpRequest = request =>
            {
                tokenRequest = request.PostData as TokenRequest;
                return exFunc(request);
            };

            // demonstrate that we don't need QueryTime set to get a server time offset
            testAblyAuth.Options.QueryTime = false;
            await testAblyAuth.AuthorizeAsync(tokenParams);

            // offset should be cached
            serverTimeCalled.Should().Be(1);

            // the TokenRequest timestamp should have been set using the offset
            tokenRequest.Timestamp.Should().HaveValue();
            tokenRequest.Timestamp.Should().BeCloseTo(await testAblyAuth.GetServerTime(), TimeSpan.FromMilliseconds(1000));
        }

        [Fact]
        [Trait("spec", "RSA10k")]
        public async Task Authorize_ObtainServerTimeAndPersistOffset_ShouldShowServerTimeIsCalledOnlyOnce()
        {
            var client = GetRestClient();
            bool serverTimeCalled = false;

            // configure the AblyAuth test wrapper to return UTC+30m when ServerTime() is called
            // (By default the library uses DateTimeOffset.UtcNow whe Now() is called)
            var testAblyAuth = new TestAblyAuth(client.Options, client, () =>
            {
                serverTimeCalled = true;
                return Task.FromResult(DateTimeOffset.UtcNow.AddMinutes(30));
            });

            // RSA10k: If the AuthOption argument’s queryTime attribute is true
            // it will obtain the server time once and persist the offset from the local clock.
            var tokenParams = new TokenParams();
            testAblyAuth.Options.QueryTime = true;
            await testAblyAuth.AuthorizeAsync(tokenParams);

            // to show the values are calculated and not fixed
            // get the current server time offset, pause for a short time,
            // then get it again.
            // The new value should represent a time after the first
            testAblyAuth.GetServerNow();

            // reset flag, used to show ServerTime() is not called again
            serverTimeCalled = false;

            // RSA10k: All future token requests generated directly or indirectly via a call to
            // authorize will not obtain the server time, but instead use the local clock
            // offset to calculate the server time.
            await testAblyAuth.AuthorizeAsync();

            // ServerTime() should not have been called again
            serverTimeCalled.Should().BeFalse();

            // and we should still be getting calculated offsets
            testAblyAuth.GetServerNow().Should().HaveValue();
            const int precision = 1000;
            testAblyAuth.GetServerNow()?.Should().BeCloseTo(await testAblyAuth.GetServerTime(), TimeSpan.FromMilliseconds(precision));
            testAblyAuth.GetServerNow()?.Should().BeCloseTo(DateTimeOffset.UtcNow.AddMinutes(30), TimeSpan.FromMilliseconds(precision));
        }

        public ServerTimeTests(ITestOutputHelper output)
            : base(output)
        {
        }
    }
}
