using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json;

namespace Ably.PubSub.Tests.Uts.Helpers
{
    /// <summary>
    /// How the mock answers one attempt. A test never constructs one of these directly — it calls
    /// <c>RespondWith(...)</c> / <c>RespondWithRefused()</c> and friends on the pending connection or
    /// request, which build the outcome behind the scenes.
    /// </summary>
    internal sealed class MockHttpOutcome
    {
        private MockHttpOutcome()
        {
        }

        internal HttpResponseMessage Response { get; private set; }

        internal Exception Failure { get; private set; }

        internal TimeSpan Delay { get; private set; }

        internal static MockHttpOutcome Success(HttpResponseMessage response, TimeSpan delay = default)
            => new MockHttpOutcome { Response = response, Delay = delay };

        internal static MockHttpOutcome Failed(Exception failure, TimeSpan delay = default)
            => new MockHttpOutcome { Failure = failure, Delay = delay };

        /// <summary>
        /// A TCP-level refusal. Shaped as the SDK recognises it: <c>AblyHttpRequester.IsRetryableError</c>
        /// only treats an <see cref="HttpRequestException"/> as retryable when its inner exception is a
        /// <see cref="WebException"/> carrying one of a known set of statuses, so a bare
        /// HttpRequestException here would make the SDK give up instead of falling back.
        /// </summary>
        internal static MockHttpOutcome ConnectionRefused(string host)
            => Failed(new HttpRequestException(
                $"Connection refused by {host}",
                new WebException($"Unable to connect to the remote server ({host})", WebExceptionStatus.ConnectFailure)));

        internal static MockHttpOutcome DnsError(string host)
            => Failed(new HttpRequestException(
                $"DNS resolution failed for {host}",
                new WebException($"The remote name could not be resolved: '{host}'", WebExceptionStatus.NameResolutionFailure)));

        /// <summary>
        /// A timeout. <see cref="TaskCanceledException"/> is what <see cref="HttpClient"/> itself raises when
        /// its own timeout elapses, and the SDK treats any TaskCanceledException as retryable.
        /// </summary>
        internal static MockHttpOutcome Timeout()
            => Failed(new TaskCanceledException("The mock HTTP request timed out."));

        internal static HttpResponseMessage BuildResponse(
            int status,
            object body,
            IDictionary<string, string> headers)
        {
            var response = new HttpResponseMessage((HttpStatusCode)status);
            response.Content = BuildContent(body, headers);

            if (headers != null)
            {
                foreach (var header in headers)
                {
                    if (IsContentHeader(header.Key))
                    {
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
                    return new ByteArrayContent(Array.Empty<byte>());

                case byte[] bytes:
                    // Raw bytes mean the test is controlling the encoding, so nothing is assumed about
                    // the content type beyond what it passed in.
                    return WithContentType(new ByteArrayContent(bytes), contentType);

                case string text:
                    return WithContentType(
                        new ByteArrayContent(Encoding.UTF8.GetBytes(text)),
                        contentType ?? "application/json");

                default:
                    // Anything else is a native object standing in for the spec's `{...}` / `[...]`
                    // literal, so it is rendered as JSON — which is also the only wire format this
                    // SDK build speaks (msgpack is compiled out). See the UTS README.
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

        private static bool IsContentHeader(string name)
            => name.StartsWith("Content-", StringComparison.OrdinalIgnoreCase);
    }
}
