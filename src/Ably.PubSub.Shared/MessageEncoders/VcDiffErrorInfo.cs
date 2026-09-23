using System;
using System.Net;

namespace IO.Ably.MessageEncoders
{
    /// <summary>
    /// Specific error class that is used to distinguish a critical VcDiff error
    /// and a normal decoding error which could be caused by a bad encoding string or
    /// a bad cipher.
    /// </summary>
    internal class VcDiffErrorInfo : ErrorInfo
    {
        public VcDiffErrorInfo(string reason)
            : base(reason, ErrorCodes.VcDiffDecodeError)
        {
        }

        public VcDiffErrorInfo(string reason, Exception innerException)
            : base(reason, ErrorCodes.VcDiffDecodeError, HttpStatusCode.BadRequest, innerException)
        {
        }
    }
}
