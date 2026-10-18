using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using Newtonsoft.Json;

namespace Ably.PubSub.Tests.Uts.Infra.Unit
{
    /// <summary>
    /// How <see cref="MockHttpClient"/> answers one attempt. Tests never build one: they call
    /// <c>RespondWith(...)</c>, <c>RespondWithRefused()</c> and the rest on the pending connection or
    /// request, which build it.
    /// </summary>
    internal sealed class MockHttpOutcome
    {
        private MockHttpOutcome()
        {
        }

        internal HttpResponseMessage Response { get; private set; }

        internal Exception Failure { get; private set; }

        /// <summary>A delay before the response is returned (<c>respond_with_delay</c>).</summary>
        internal TimeSpan Delay { get; private set; }

        /// <summary>
        /// The spec's <c>respond_with_timeout()</c>. <see cref="MockHttpClient"/> turns it into a
        /// <see cref="System.Threading.Tasks.TaskCanceledException"/> once the client's request timeout has
        /// passed on the fake clock (at once without one).
        /// </summary>
        internal bool IsTimeout { get; private set; }

        internal static MockHttpOutcome Success(HttpResponseMessage response, TimeSpan delay = default)
            => new MockHttpOutcome { Response = response, Delay = delay };

        internal static MockHttpOutcome Failed(Exception failure)
            => new MockHttpOutcome { Failure = failure };

        internal static MockHttpOutcome Timeout()
            => new MockHttpOutcome { IsTimeout = true };

        /// <summary>
        /// A TCP-level refusal, shaped as the SDK recognises it: <c>AblyHttpRequester.IsRetryableError</c>
        /// treats an <see cref="HttpRequestException"/> as retryable only when its inner exception is a
        /// <see cref="WebException"/> with one of a known set of statuses, so a bare
        /// HttpRequestException would make the SDK give up instead of falling back.
        /// </summary>
        internal static MockHttpOutcome ConnectionRefused(string host)
            => Failed(new HttpRequestException(
                $"Connection refused by {host}",
                new WebException($"Unable to connect to the remote server ({host})", WebExceptionStatus.ConnectFailure)));

        /// <summary>A DNS failure: <see cref="WebExceptionStatus.NameResolutionFailure"/>, also retryable.</summary>
        internal static MockHttpOutcome DnsError(string host)
            => Failed(new HttpRequestException(
                $"DNS resolution failed for {host}",
                new WebException($"The remote name could not be resolved: '{host}'", WebExceptionStatus.NameResolutionFailure)));

        /// <summary>
        /// Builds the response for <c>respond_with(status, body, headers)</c>. A <c>byte[]</c> body is sent
        /// as is; a string is sent as UTF-8 (default content type <c>application/json</c>); anything else
        /// (the spec's <c>{...}</c> / <c>[...]</c> literal as an anonymous object, a JObject, a dictionary)
        /// is serialised to JSON, the only wire format this SDK build speaks. <c>Content-*</c> headers go on
        /// the content, the rest on the response.
        /// </summary>
        internal static HttpResponseMessage BuildResponse(
            int status,
            object body,
            IDictionary<string, string> headers)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status)
            {
                Content = BuildContent(body, headers),
            };

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    if (header.Key.StartsWith("Content-", StringComparison.OrdinalIgnoreCase))
                    {
                        if (!string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                        {
                            response.Content.Headers.TryAddWithoutValidation(header.Key, header.Value);
                        }

                        continue;
                    }

                    response.Headers.TryAddWithoutValidation(header.Key, header.Value);
                }
            }

            return response;
        }

        private static HttpContent BuildContent(object body, IDictionary<string, string> headers)
        {
            var contentType = FindContentType(headers);
            switch (body)
            {
                case null:
                    return WithContentType(new ByteArrayContent(Array.Empty<byte>()), contentType);

                case byte[] bytes:
                    return WithContentType(new ByteArrayContent(bytes), contentType);

                case string text:
                    return WithContentType(
                        new ByteArrayContent(Encoding.UTF8.GetBytes(text)),
                        contentType ?? "application/json");

                default:
                    return WithContentType(
                        new ByteArrayContent(Encoding.UTF8.GetBytes(JsonConvert.SerializeObject(body))),
                        contentType ?? "application/json");
            }
        }

        private static HttpContent WithContentType(HttpContent content, string contentType)
        {
            if (contentType != null)
            {
                content.Headers.Remove("Content-Type");
                content.Headers.TryAddWithoutValidation("Content-Type", contentType);
            }

            return content;
        }

        private static string FindContentType(IDictionary<string, string> headers)
        {
            if (headers == null)
            {
                return null;
            }

            foreach (var header in headers)
            {
                if (string.Equals(header.Key, "Content-Type", StringComparison.OrdinalIgnoreCase))
                {
                    return header.Value;
                }
            }

            return null;
        }
    }
}
