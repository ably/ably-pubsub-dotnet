using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Threading.Tasks;
using Ably.PubSub.Realtime;
using Ably.PubSub.Tests.Uts.Infra;
using Ably.PubSub.Tests.Uts.Infra.Unit;
using FluentAssertions;
using Newtonsoft.Json.Linq;
using Xunit;
using Xunit.Abstractions;

namespace Ably.PubSub.Tests.Uts.InfraTests.Unit
{
    /// <summary>
    /// The REST unit tier end to end: a factory-built client wired to <see cref="MockHttpClient"/> through
    /// <c>ClientOptions.HttpClient</c>, and a realtime client whose HTTP request the same mock serves.
    /// </summary>
    [Trait("uts", "unit")]
    [Trait("uts-infra", "unit")]
    public class RestUnitSmokeTests : UtsUnitTestBase
    {
        public RestUnitSmokeTests(ITestOutputHelper output)
            : base(output)
        {
        }

        [Fact]
        public async Task RequestIsCapturedWithMethodPathHeadersAndBody()
        {
            var mock = new MockHttpClient(onRequest: request => request.RespondWith(201, new { serials = new[] { "serial-1" } }));
            InstallMock(mock);
            var rest = Rest(new ClientOptions { Key = ValidKey });

            var result = await rest.Channels.Get("smoke-channel").PublishAsync("greeting", "hello");

            result.Serials.Should().Equal("serial-1");
            var request = mock.CapturedRequests.Should().ContainSingle().Subject;
            request.Method.Should().Be("POST");
            request.Path.Should().Be("/channels/smoke-channel/messages");
            request.Url.Host.Should().Be("main.realtime.ably.net");
            request.Url.Tls.Should().BeTrue();
            request.Headers["Authorization"].Should().StartWith("Basic ");
            request.Headers["Content-Type"].Should().StartWith("application/json");
            request.Headers.Should().ContainKey("X-Ably-Version");
            var body = JToken.Parse(request.BodyText);
            var message = (JObject)(body is JArray array ? array.Single() : body);
            ((string)message["name"]).Should().Be("greeting");
            ((string)message["data"]).Should().Be("hello");
            mock.ConnectionAttempts.Should().ContainSingle().Which.Host.Should().Be("main.realtime.ably.net");
        }

        [Fact]
        public async Task RespondWithStatusAndBodyAreDecodedBySdk()
        {
            var mock = new MockHttpClient(onRequest: request => request.RespondWith(200, new[] { 1700000000123L }));
            InstallMock(mock);
            var rest = Rest(new ClientOptions { Key = ValidKey });

            var time = await rest.TimeAsync();

            time.Should().Be(DateTimeOffset.FromUnixTimeMilliseconds(1700000000123L));
            mock.CapturedRequests.Single().Path.Should().Be("/time");
        }

        [Fact]
        public async Task RespondWithHeadersAreDecodedBySdk()
        {
            var mock = new MockHttpClient(onRequest: request => request.RespondWith(
                200,
                new[] { new { id = "m1", name = "first", data = "payload" } },
                new Dictionary<string, string>
                {
                    ["Link"] = "<./messages?start=0&limit=1&direction=backwards>; rel=\"next\"",
                }));
            InstallMock(mock);
            var rest = Rest(new ClientOptions { Key = ValidKey });

            var page = await rest.Channels.Get("history-channel").HistoryAsync();

            page.Items.Should().ContainSingle().Which.Name.Should().Be("first");
            page.HasNext.Should().BeTrue("the Link header the mock sent was parsed by the SDK");
            page.NextQueryParams.Limit.Should().Be(1);
        }

        [Fact]
        public async Task ErrorStatusAndBodyAreDecodedIntoTheErrorInfo()
        {
            var mock = new MockHttpClient(onRequest: request => request.RespondWith(
                404,
                new { error = new { code = 40400, statusCode = 404, message = "not found for the test" } }));
            InstallMock(mock);
            var rest = Rest(new ClientOptions { Key = ValidKey });

            Func<Task> act = () => rest.TimeAsync();

            var error = (await act.Should().ThrowAsync<AblyException>()).Which.ErrorInfo;
            error.Code.Should().Be(40400);
            error.StatusCode.Should().Be(HttpStatusCode.NotFound);
            error.Message.Should().Contain("not found for the test");
        }

        [Fact]
        public async Task RealtimeDrivenTokenRequestIsServedByTheHttpMockWhileTheWebSocketMockServesTheConnection()
        {
            var http = new MockHttpClient(onRequest: request => request.RespondWith(
                200,
                "uts-token-from-auth-url",
                new Dictionary<string, string> { ["Content-Type"] = "text/plain" }));
            InstallMock(http);
            var webSocket = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(webSocket);

            var client = Realtime(new ClientOptions { AuthUrl = new Uri("https://auth.example.test/token?app=uts") });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var authRequest = http.CapturedRequests.Should().ContainSingle().Subject;
            authRequest.Method.Should().Be("GET");
            authRequest.Url.Host.Should().Be("auth.example.test");
            authRequest.Path.Should().Be("/token");
            authRequest.QueryParams["app"].Should().Be("uts");
            var attempt = webSocket.ConnectionAttempts.Should().ContainSingle().Subject;
            attempt.QueryParams["accessToken"].Should().Be("uts-token-from-auth-url", "the token the HTTP mock served reached the WebSocket attempt");
            attempt.QueryParams.Should().NotContainKey("key");
        }

        [Fact]
        public async Task RealtimeDrivenAuthCallbackTokenRequestIsServedByTheHttpMock()
        {
            var http = new MockHttpClient(onRequest: request => request.RespondWith(
                200,
                new { token = "uts-token-from-request", expires = DateTimeOffset.UtcNow.AddHours(1).ToUnixTimeMilliseconds(), issued = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds() }));
            InstallMock(http);
            var webSocket = new MockWebSocket(attempt => attempt.RespondWithConnected());
            InstallMock(webSocket);

            // The callback returns a TokenRequest the client exchanges at /keys/.../requestToken (RSA8d).
            var signer = Rest(new ClientOptions { Key = ValidKey });
            var client = Realtime(new ClientOptions
            {
                AuthCallback = async tokenParams => (object)await signer.Auth.CreateTokenRequestAsync(tokenParams),
            });
            await UtsWait.AwaitConnectionState(client.Connection, ConnectionState.Connected);

            var tokenRequest = http.CapturedRequests.Should().ContainSingle().Subject;
            tokenRequest.Method.Should().Be("POST");
            tokenRequest.Path.Should().Be("/keys/appId.keyId/requestToken");
            webSocket.ConnectionAttempts.Single().QueryParams["accessToken"].Should().Be("uts-token-from-request");
        }
    }
}
