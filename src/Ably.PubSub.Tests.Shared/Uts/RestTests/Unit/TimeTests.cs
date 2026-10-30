// Time API Tests (RSC16)
// Derived from uts/rest/unit/time.md
// at ably/specification@f787470c7e7ea362cbe43e6839529979630800fb
// Tier: unit
// Disclosures: none

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using Ably.PubSub.Types;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.RestTests.Unit
{
    /// <summary>
    /// The uts/rest/unit/time.md tests, written against the client or channel operation through the hooks below so they run
    /// unchanged against a REST client (<see cref="TimeTests"/>) and, for the realtime spec that delegates to them, a realtime
    /// client: RealtimeTimeTests (realtime/unit/RTC6/time-proxies-rest-0).
    /// </summary>
    public abstract class TimeTestsBase<TClient> : UtsUnitTestBase
    {
        protected TimeTestsBase(ITestOutputHelper output)
            : base(output)
        {
        }

        /// <summary>The spec's <c>client = Rest(options: …)</c>: the client under test.</summary>
        protected abstract TClient NewClient(ClientOptions options);

        /// <summary>The spec's <c>client.time()</c>.</summary>
        protected abstract Task<DateTimeOffset> TimeAsync(TClient client);

        // UTS: rest/unit/RSC16/returns-server-time-0
        [UtsFact]
        [Trait("spec", "RSC16")]
        public async Task RSC16_ReturnsServerTime()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();
            const long serverTimeMs = 1704067200000; // 2024-01-01 00:00:00 UTC

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { serverTimeMs });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            var result = await TimeAsync(client);

            // Assertions
            // Result should be a DateTime matching the server timestamp
            // ASSERT result IS DateTime: guaranteed at compile time (TimeAsync returns DateTimeOffset)
            result.ToUnixTimeMilliseconds().Should().Be(serverTimeMs);

            // Verify correct endpoint was called
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.Method.Should().Be("GET");
            request.Path.Should().Be("/time");
        }

        // UTS: rest/unit/RSC16/request-format-get-time-1
        [UtsFact]
        [Trait("spec", "RSC16")]
        public async Task RSC16_RequestFormatGetTime()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { 1704067200000L });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            await TimeAsync(client);

            // Assertions
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];

            // Should be GET request to /time
            request.Method.Should().Be("GET");
            request.Path.Should().Be("/time");

            // Should have standard Ably headers
            request.Headers.Should().ContainKey("X-Ably-Version");
            request.Headers.Should().ContainKey("Ably-Agent");
        }

        // UTS: rest/unit/RSC16/no-auth-required-2
        [UtsFact]
        [Trait("spec", "RSC16")]
        public async Task RSC16_NoAuthRequired()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { 1704067200000L });
                });
            InstallMock(mockHttp);

            // Client has credentials, but time() should not use them
            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            var result = await TimeAsync(client);

            // Assertions
            // Should succeed
            // ASSERT result IS DateTime: guaranteed at compile time (TimeAsync returns DateTimeOffset)
            result.ToUnixTimeMilliseconds().Should().Be(1704067200000L);

            // Request should not have Authorization header even though client has credentials
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.Headers.Should().NotContainKey("Authorization");
        }

        // UTS: rest/unit/RSC16/works-without-tls-3
        [UtsFact]
        [Trait("spec", "RSC16")]
        public async Task RSC16_WorksWithoutTls()
        {
            // Setup
            var capturedRequests = new CaptureList<PendingHttpRequest>();

            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    capturedRequests.Add(req);
                    req.RespondWith(200, new[] { 1704067200000L });
                });
            InstallMock(mockHttp);

            // Client with API key but using token auth to avoid RSC18 restriction
            // on authenticated operations. time() should still work over HTTP.
            var client = NewClient(new ClientOptions
            {
                Key = "app.key:secret",
                Tls = false,
                UseTokenAuth = true,
            });

            // Test Steps
            var result = await TimeAsync(client);

            // Assertions
            // Should succeed without sending authentication over HTTP
            // ASSERT result IS DateTime: guaranteed at compile time (TimeAsync returns DateTimeOffset)
            result.ToUnixTimeMilliseconds().Should().Be(1704067200000L);

            // Request should use HTTP (not HTTPS)
            capturedRequests.Count.Should().Be(1);
            var request = capturedRequests[0];
            request.Url.Scheme.Should().Be("http");

            // Request should not have Authorization header
            request.Headers.Should().NotContainKey("Authorization");

            // Note
            // This test verifies that the RSC18 check (which rejects basic auth over non-TLS connections) is only applied to operations that require authentication. The `time()` endpoint is unauthenticated, so it should work regardless of TLS settings. The client constructor still requires credentials, but time() doesn't use them.
        }

        // UTS: rest/unit/RSC16/error-propagated-4
        // DEVIATION: see deviations.md
        [DeviationFact("RSC15a", Timeout = UtsTestTimeouts.DefaultTestTimeoutMs)]
        [Trait("spec", "RSC16")]
        public async Task RSC16_ErrorPropagated()
        {
            // Setup
            var mockHttp = new MockHttpClient(
                onConnectionAttempt: conn => conn.RespondWithSuccess(),
                onRequest: req =>
                {
                    req.RespondWith(500, new JObject
                    {
                        ["error"] = new JObject
                        {
                            ["message"] = "Internal server error",
                            ["code"] = 50000,
                            ["statusCode"] = 500,
                        },
                    });
                });
            InstallMock(mockHttp);

            var client = NewClient(new ClientOptions { Key = "app.key:secret" });

            // Test Steps
            Func<Task> act = () => TimeAsync(client);
            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            ((int?)error.StatusCode).Should().Be(500);
            error.Code.Should().Be(50000);
        }
    }

    /// <summary>The uts/rest/unit/time.md tests against a REST client.</summary>
    [Trait("uts", "unit")]
    public class TimeTests : TimeTestsBase<PubSubHttpClient>
    {
        public TimeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        protected override PubSubHttpClient NewClient(ClientOptions options) => Rest(options);

        protected override Task<DateTimeOffset> TimeAsync(PubSubHttpClient client) => client.TimeAsync();
    }
}
