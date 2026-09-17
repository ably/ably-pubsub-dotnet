using System;

namespace IO.Ably
{
    /// <summary>
    /// Ably exception if an action cannot be performed over http.
    /// </summary>
    public class AblyInsecureRequestException : AblyException
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="AblyInsecureRequestException"/> class.
        /// </summary>
        public AblyInsecureRequestException()
            : base("Current action cannot be performed over http")
        {
        }

        /// <summary>
        /// Initializes a new AblyInsecureRequestException using the specified 'message'.
        /// </summary>
        /// <param name="message">The exception message.</param>
        public AblyInsecureRequestException(string message)
            : base(message)
        {
        }

        /// <summary>
        /// Initializes a new AblyInsecureRequestException using the specified 'message' and 'innerException'.
        /// </summary>
        /// <param name="message">The exception message.</param>
        /// <param name="innerException">The inner exception to wrap.</param>
        public AblyInsecureRequestException(string message, Exception innerException)
            : base(new ErrorInfo(message, 500), innerException)
        {
        }
    }
}
