using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Threading.Tasks;
using FluentAssertions;
using Xunit;

namespace IO.Ably.Tests
{
    public class AblyHttpClientSpecs
    {
        [Fact]
        [Trait("spec", "RSC7")]
        public void WithSecureTrue_CreatesSecureRestUrlsWithDefaultHost()
        {
            var client = new AblyHttpClient(new AblyHttpOptions { IsSecure = true });

            var url = client.GetRequestUrl(new AblyRequest("/test", HttpMethod.Get));

            url.Scheme.Should().Be("https");
            url.Host.Should().Be(Defaults.RestHost);
        }

        [Fact]
        [Trait("spec", "RSC7")]
        public void WithSecureFalse_CreatesNonSecureRestUrlsWithDefaultRestHost()
        {
            var client = new AblyHttpClient(new AblyHttpOptions { IsSecure = false });

            var url = client.GetRequestUrl(new AblyRequest("/test", HttpMethod.Get));

            url.Scheme.Should().Be("http");
            url.Host.Should().Be(Defaults.RestHost);
        }

        [Fact]
        [Trait("spec", "RSC7a")]
        [Trait("spec", "G4")]
        public async Task WhenCallingUrl_AddsDefaultAblyLibraryVersionHeader()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("Success") };
            var handler = new FakeHttpMessageHandler(response);
            var client = new AblyHttpClient(new AblyHttpOptions { HttpClient = new HttpClient(handler) });

            await client.Execute(new AblyRequest("/test", HttpMethod.Get));
            var values = handler.LastRequest.Headers.GetValues("X-Ably-Version").ToArray();
            values.Should().NotBeEmpty();
            values.First().Should().Be(Defaults.ProtocolVersion);
        }

        [Fact]
        [Trait("spec", "RSC7c")]
        public async Task WhenCallingUrl_AddsRequestIdIfSetTrue()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("Success") };
            var handler = new FakeHttpMessageHandler(response);
            var client = new AblyHttpClient(new AblyHttpOptions { AddRequestIds = true, HttpClient = new HttpClient(handler) });
            var ablyRequest = new AblyRequest("/test", HttpMethod.Get);
            ablyRequest.AddHeaders(new Dictionary<string, string> { { "request_id", "custom_request_id" } });
            await client.Execute(ablyRequest);
            var values = handler.LastRequest.Headers.GetValues("request_id").ToArray();
            values.Should().NotBeEmpty();
            values.First().Should().StartWith("custom_request_id");
        }

        [Fact]
        public async Task WhenCallingUrlWithPostParamsAndEmptyBody_PassedTheParamsAsUrlEncodedValues()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("Success") };
            var handler = new FakeHttpMessageHandler(response);
            var client = new AblyHttpClient(new AblyHttpOptions { HttpClient = new HttpClient(handler) });

            var ablyRequest = new AblyRequest("/test", HttpMethod.Post)
            {
                PostParameters = new Dictionary<string, string> { { "test", "test" }, { "best", "best" } },
            };

            await client.Execute(ablyRequest);
            var content = handler.LastRequest.Content;
            var formContent = content as FormUrlEncodedContent;
            formContent.Should().NotBeNull("Content should be of type FormUrlEncodedContent");
        }

        [Fact]
        [Trait("spec", "RSC7d")]
        public async Task WhenCallingUrl_AddsDefaultAblyAgentHeader()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("Success") };
            var handler = new FakeHttpMessageHandler(response);
            var client = new AblyHttpClient(new AblyHttpOptions { HttpClient = new HttpClient(handler) });

            await client.Execute(new AblyRequest("/test", HttpMethod.Get));
            string[] values = handler.LastRequest.Headers.GetValues("Ably-Agent").ToArray();
            values.Should().HaveCount(1);
            string[] agentValues = values[0].Split(' ');

            var keys = new List<string>()
            {
                "ably-dotnet/",
                Agent.DotnetRuntimeIdentifier,
                Agent.OsIdentifier
            };

            Agent.DotnetRuntimeIdentifier.Split('/').Length.Should().Be(2);

            keys.RemoveAll(s => s.IsEmpty());

            agentValues.Should().HaveCount(keys.Count);
            for (var i = 0; i < keys.Count; ++i)
            {
                agentValues[i].StartsWith(keys[i]).Should().BeTrue($"'{agentValues[i]}' should start with '{keys[i]}'");
            }
        }

        [Fact]
        [Trait("spec", "RSC7d6")]
        public async Task WhenCallingUrl_AddsCustomizedAblyAgentHeader()
        {
            var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("Success") };
            var handler = new FakeHttpMessageHandler(response);

            var ablyHttpOptions = new AblyHttpOptions
            {
                Agents = new Dictionary<string, string>
                {
                    { "agent1", "value1" },
                    { "agent2", "value2" },
                },
                HttpClient = new HttpClient(handler)
            };

            var client = new AblyHttpClient(ablyHttpOptions);

            await client.Execute(new AblyRequest("/test", HttpMethod.Get));
            string[] values = handler.LastRequest.Headers.GetValues("Ably-Agent").ToArray();
            values.Should().HaveCount(1);
            string[] agentValues = values[0].Split(' ');

            var keys = new List<string>()
            {
                "ably-dotnet/",
                Agent.DotnetRuntimeIdentifier,
                Agent.OsIdentifier,
                "agent1",
                "agent2",
            };

            keys.RemoveAll(s => s.IsEmpty());

            agentValues.Should().HaveCount(keys.Count);
            for (var i = 0; i < keys.Count; ++i)
            {
                agentValues[i].StartsWith(keys[i]).Should().BeTrue($"'{agentValues[i]}' should start with '{keys[i]}'");
            }
        }

        public class IsRetryableResponseSpecs
        {
            [Fact]
            public void IsRetryableError_WithTaskCancellationException_ShouldBeTrue()
            {
                AblyHttpClient.IsRetryableError(new TaskCanceledException()).Should().BeTrue();
            }

            [Theory]
            [InlineData(WebExceptionStatus.Timeout)]
            [InlineData(WebExceptionStatus.ConnectFailure)]
            [InlineData(WebExceptionStatus.NameResolutionFailure)]
            [Trait("spec", "RSC15d")]
            public void IsRetryableError_WithHttpMessageException_ShouldBeTrue(WebExceptionStatus status)
            {
                var exception = new HttpRequestException("Error", new WebException("boo", status));
                AblyHttpClient.IsRetryableError(exception).Should().BeTrue();
            }

            [Theory]
            [InlineData(HttpStatusCode.BadGateway, true)]
            [InlineData(HttpStatusCode.InternalServerError, true)]
            [InlineData(HttpStatusCode.NotImplemented, true)]
            [InlineData(HttpStatusCode.ServiceUnavailable, true)]
            [InlineData(HttpStatusCode.GatewayTimeout, true)]
            [InlineData(HttpStatusCode.NoContent, false)]
            [InlineData(HttpStatusCode.NotFound, false)]
            [Trait("spec", "RSC15d")]
            public void IsRetryableResponse_WithErrorCode_ShouldReturnExpectedValue(
                HttpStatusCode statusCode,
                bool expected)
            {
                var response = new HttpResponseMessage(statusCode);
                AblyHttpClient.IsRetryableResponse(response).Should().Be(expected);
            }
        }

        public class ExternalHttpClientSpecs
        {
            [Fact]
            public void WithExternalHttpClient_ShouldUseProvidedClient()
            {
                // Arrange
                var externalHttpClient = new HttpClient();
                var options = new AblyHttpOptions { HttpClient = externalHttpClient };

                // Act
                var ablyHttpClient = new AblyHttpClient(options);

                // Assert
                ablyHttpClient.Client.Should().BeSameAs(externalHttpClient);
            }

            [Fact]
            public void WithExternalHttpClient_ShouldStillAddAblyHeaders()
            {
                // Arrange
                var externalHttpClient = new HttpClient();
                var options = new AblyHttpOptions { HttpClient = externalHttpClient };

                // Act
                var ablyHttpClient = new AblyHttpClient(options);

                // Assert
                ablyHttpClient.Client.DefaultRequestHeaders.Contains("X-Ably-Version").Should().BeTrue();
                ablyHttpClient.Client.DefaultRequestHeaders.Contains("Ably-Agent").Should().BeTrue();
            }

            [Fact]
            public void WithExternalHttpClient_ShouldSetTimeout()
            {
                // Arrange
                var externalHttpClient = new HttpClient();
                var timeout = TimeSpan.FromSeconds(30);
                var options = new AblyHttpOptions
                {
                    HttpClient = externalHttpClient,
                    HttpRequestTimeout = timeout
                };

                // Act
                var ablyHttpClient = new AblyHttpClient(options);

                // Assert
                ablyHttpClient.Client.Timeout.Should().Be(timeout);
            }

            [Fact]
            public void WithoutExternalHttpClient_ShouldCreateNewClient()
            {
                // Arrange
                var options = new AblyHttpOptions();
                options.HttpClient.Should().BeNull();

                // Act
                var ablyHttpClient = new AblyHttpClient(options);

                // Assert
                ablyHttpClient.Client.Should().NotBeNull();
            }

            [Fact]
            public async Task WithExternalHttpClient_ShouldMakeSuccessfulRequests()
            {
                // Arrange
                var response = new HttpResponseMessage(HttpStatusCode.Accepted) { Content = new StringContent("Success") };
                var handler = new FakeHttpMessageHandler(response);
                var externalHttpClient = new HttpClient(handler);
                var options = new AblyHttpOptions { HttpClient = externalHttpClient };
                var ablyHttpClient = new AblyHttpClient(options);

                // Act
                var result = await ablyHttpClient.Execute(new AblyRequest("/test", HttpMethod.Get));

                // Assert
                result.StatusCode.Should().Be(HttpStatusCode.Accepted);
                handler.NumberOfRequests.Should().Be(1);
            }
        }
    }
}
