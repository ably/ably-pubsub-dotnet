using System;
using System.Diagnostics;
using System.Net;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace IO.Ably
{
    /// <summary>
    /// An exception type encapsulating error information containing an Ably specific error code and generic status code.
    /// </summary>
    public class ErrorInfo
    {
        internal static readonly ErrorInfo ReasonClosed = new ErrorInfo("Connection closed by client", ErrorCodes.NoError);
        internal static readonly ErrorInfo ReasonDisconnected = new ErrorInfo("Connection temporarily unavailable", ErrorCodes.Disconnected);
        internal static readonly ErrorInfo ReasonSuspended = new ErrorInfo("Connection unavailable", ErrorCodes.ConnectionSuspended);
        internal static readonly ErrorInfo ReasonFailed = new ErrorInfo("Connection failed", ErrorCodes.ConnectionFailed);
        internal static readonly ErrorInfo ReasonRefused = new ErrorInfo("Access refused", ErrorCodes.Unauthorized);
        internal static readonly ErrorInfo ReasonTooBig = new ErrorInfo("Connection closed; message too large", ErrorCodes.BadRequest);
        internal static readonly ErrorInfo ReasonNeverConnected = new ErrorInfo("Unable to establish connection", ErrorCodes.ConnectionSuspended);
        internal static readonly ErrorInfo ReasonTimeout = new ErrorInfo("Unable to establish connection", 80014);
        internal static readonly ErrorInfo ReasonUnknown = new ErrorInfo("Unknown error", ErrorCodes.InternalError, HttpStatusCode.InternalServerError);
        internal static readonly ErrorInfo NonRenewableToken = new ErrorInfo("The library was initialized with a token without any way to renew the token when it expires (no authUrl, authCallback, or key). See https://help.ably.io/error/40171 for help", ErrorCodes.NoMeansProvidedToRenewAuthToken, HttpStatusCode.Unauthorized);

        internal const string CodePropertyName = "code";
        internal const string StatusCodePropertyName = "statusCode";
        internal const string ReasonPropertyName = "message";
        internal const string HrefBase = "https://help.ably.io/error/";

        /// <summary>
        /// Ably error code (see https://github.com/ably/ably-common/blob/main/protocol/errors.json).
        /// </summary>
        [JsonProperty("code")]
        public int Code { get; set; }

        /// <summary>
        /// The http status code corresponding to this error.
        /// </summary>
        [JsonProperty("statusCode")]
        public HttpStatusCode? StatusCode { get; set; }

        /// <summary>
        /// Additional reason information, where available.
        /// </summary>
        [JsonProperty("message")]
        public string Message { get; set; }

        /// <summary>
        /// Link to specification detail for this error code, where available. Spec TI4.
        /// </summary>
        [JsonProperty("href")]
        public string Href { get; set; }

        /// <summary>
        /// Additional cause information, where available.
        /// </summary>
        [JsonProperty("cause")]
        public ErrorInfo Cause { get; set; }

        /// <summary>
        /// Is this Error as result of a 401 Unauthorized HTTP response.
        /// </summary>
        public bool IsUnAuthorizedError => StatusCode.HasValue &&
                                           StatusCode.Value == HttpStatusCode.Unauthorized;

        /// <summary>
        /// Is this Error as result of a 403 Forbidden HTTP response.
        /// </summary>
        public bool IsForbiddenError => StatusCode.HasValue &&
                                           StatusCode.Value == HttpStatusCode.Forbidden;

        /// <summary>
        /// Is the error Code a token error code.
        /// </summary>
        public bool IsTokenError => Code >= Defaults.TokenErrorCodesRangeStart &&
                                    Code <= Defaults.TokenErrorCodesRangeEnd;

        /// <summary>
        /// Get or Sets the InnerException.
        /// </summary>
        public Exception InnerException { get; set; }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorInfo"/> class.
        /// </summary>
        [JsonConstructor]
        public ErrorInfo()
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorInfo"/> class.
        /// </summary>
        /// <param name="reason">error reason.</param>
        public ErrorInfo(string reason)
            : this(reason, 0)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorInfo"/> class.
        /// </summary>
        /// <param name="reason">error reason.</param>
        /// <param name="code">error code.</param>
        /// <param name="statusCode">optional, http status code.</param>
        /// <param name="innerException">optional, InnerException.</param>
        public ErrorInfo(string reason, int code, HttpStatusCode? statusCode, Exception innerException)
            : this(reason, code, statusCode, null, null, innerException)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="ErrorInfo"/> class.
        /// </summary>
        /// <param name="reason">error reason.</param>
        /// <param name="code">error code.</param>
        /// <param name="statusCode">optional, http status code.</param>
        /// <param name="href">optional, documentation url.</param>
        /// <param name="cause">optional, another ErrorInfo that caused this one.</param>
        /// <param name="innerException">optional, InnerException.</param>
        public ErrorInfo(string reason, int code, HttpStatusCode? statusCode = null, string href = null, ErrorInfo cause = null, Exception innerException = null)
        {
            Code = code;
            StatusCode = statusCode;
            Message = reason;
            if (href.IsEmpty() && code > 0)
            {
                Href = GetHref(code);
            }
            else
            {
                Href = href;
            }

            Cause = cause;
            InnerException = innerException;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            StringBuilder result = new StringBuilder("[ErrorInfo ");
            result.Append("Reason: ").Append(LogMessage());
            if (Code > 0)
            {
                result.Append("; Code: ").Append(Code);
            }

            if (StatusCode != null)
            {
                result.Append("; StatusCode: ").Append((int)StatusCode).Append(" (").Append(StatusCode.ToString())
                    .Append(")");
            }

            if (Href != null)
            {
                result.Append("; Href: ").Append(Href);
            }

            if (Cause != null)
            {
                result.Append("; Cause: ").Append(Cause);
            }

            if (InnerException != null)
            {
                result.Append("; InnerException: ").Append(InnerException);
            }

            result.Append("]");
            return result.ToString();
        }

        internal static ErrorInfo Parse(AblyResponse response)
        {
            // RSA4d, if we have 403 response default to code 40300, this may be overwritten
            // if the response has a usable JSON body
            int errorCode = response.StatusCode == HttpStatusCode.Forbidden ? 40300 : ErrorCodes.InternalError;
            string reason = string.Empty;

            if (response.Type == ResponseType.Json)
            {
                try
                {
                    var json = JObject.Parse(response.TextResponse);
                    if (json["error"] != null)
                    {
                        reason = (string)json["error"]["message"];
                        errorCode = (int)json["error"]["code"];
                    }
                }
                catch (Exception ex)
                {
                    // If there is no json or there is something wrong we don't want to throw from here.
                    Debug.WriteLine(ex.Message);
                }
            }

            return new ErrorInfo(reason.IsEmpty() ? "Unknown error" : reason, errorCode, response.StatusCode);
        }

        /// <summary>
        /// Checks if the current error's status code is retryable.
        /// </summary>
        /// <returns>true / false.</returns>
        public bool IsRetryableStatusCode()
        {
            return StatusCode.HasValue && IsRetryableStatusCode(StatusCode.Value);
        }

        private static string GetHref(int code)
        {
            return $"{HrefBase}{code}";
        }

        /// <summary>
        /// Spec: TI5.
        /// </summary>
        private string LogMessage()
        {
            string errHref = null;
            var logMessage = Message ?? string.Empty;
            if (Href.IsNotEmpty())
            {
                errHref = Href;
            }
            else if (Code > 0)
            {
                errHref = GetHref(Code);
            }

            if (errHref != null && !logMessage.Contains(errHref))
            {
                logMessage += " (See " + errHref + ")";
            }

            return logMessage;
        }

        /// <summary>
        /// Is statusCode considered retryable.
        /// </summary>
        /// <param name="statusCode">status code to check.</param>
        /// <returns>true / false.</returns>
        public static bool IsRetryableStatusCode(HttpStatusCode statusCode)
        {
            return statusCode >= (HttpStatusCode)500 && statusCode <= (HttpStatusCode)504;
        }
    }
}
