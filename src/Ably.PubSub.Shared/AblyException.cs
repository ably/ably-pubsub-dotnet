using System;
using System.Net;

namespace IO.Ably
{
    /// <summary>
    /// Ably exception wrapper class. It includes error information <see cref="Ably.ErrorInfo"/> used by ably.
    /// All inner exceptions are wrapped in this class. Always check the inner exception property of the caught exception.
    /// </summary>
    public class AblyException : Exception
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AblyException"/> class.
        /// </summary>
        /// <param name="reason">Reason passed to the error info class.</param>
        public AblyException(string reason)
            : this(new ErrorInfo(reason, 500))
        {
        }

        /// <summary>
        /// Creates AblyException. ErrorInfo is automatically generated based on the inner exception message. StatusCode is set to 'ErrorCodes.InternalError'.
        /// </summary>
        /// <param name="ex">Original exception to be wrapped.</param>
        public AblyException(Exception ex)
            : this(new ErrorInfo("Unexpected error :" + ex.Message, ErrorCodes.InternalError), ex)
        {
        }

        /// <summary>
        /// Creates an AblyException and populates ErrorInfo with the supplied parameters.
        /// </summary>
        /// <param name="reason">reason.</param>
        /// <param name="code">error code.</param>
        public AblyException(string reason, int code)
            : this(new ErrorInfo(reason, code))
        {
        }

        /// <summary>
        /// Creates AblyException and populates ErrorInfo with the supplied parameters.
        /// </summary>
        /// <param name="reason">error reason.</param>
        /// <param name="code">error code.</param>
        /// <param name="statusCode">optional, http status code. <see cref="HttpStatusCode"/>.</param>
        public AblyException(string reason, int code, HttpStatusCode? statusCode = null)
            : this(new ErrorInfo(reason, code, statusCode))
        {
        }

        /// <summary>
        /// Creates AblyException with supplied error info.
        /// </summary>
        /// <param name="info">Error info.</param>
        public AblyException(ErrorInfo info)
            : base(info.ToString(), info.InnerException)
        {
            ErrorInfo = info;
        }

        /// <summary>
        /// Creates AblyException with ErrorInfo and sets the supplied exception as innerException.
        /// </summary>
        /// <param name="info">Error info.</param>
        /// <param name="innerException">Inner exception.</param>
        public AblyException(ErrorInfo info, Exception innerException)
            : base(info.ToString(), innerException ?? info.InnerException)
        {
            ErrorInfo = info;
        }

        /// <summary>
        /// Gets the current error info for the exception.
        /// </summary>
        public ErrorInfo ErrorInfo { get; }

        internal static AblyException FromResponse(AblyResponse response)
        {
            return new AblyException(ErrorInfo.Parse(response));
        }
    }
}
