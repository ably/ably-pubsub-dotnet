using System.Net;
using System.Net.Http.Headers;

namespace IO.Ably
{
    internal enum ResponseType
    {
        Json,
        Binary,
        Text,
        Jwt
    }

    internal class AblyResponse
    {
        internal AblyResponse()
        {
            Headers = new EmptyHttpHeaders();
        }

        internal AblyResponse(string encoding, string contentType, byte[] body)
        {
            ContentType = contentType;
            Type = GetResponseType(contentType);
            Encoding = encoding.IsNotEmpty() ? encoding : "utf-8";

            if (body != null && (Type == ResponseType.Json || Type == ResponseType.Text || Type == ResponseType.Jwt))
            {
                TextResponse = System.Text.Encoding.GetEncoding(Encoding).GetString(body, 0, body.Length);
            }

            Body = body;
        }

        internal HttpHeaders Headers { get; set; }

        internal ResponseType Type { get; set; }

        internal HttpStatusCode StatusCode { get; set; }

        internal string TextResponse { get; set; }

        internal string ContentType { get; }

        internal byte[] Body { get; }

        internal string Encoding { get; }

        internal static AblyResponse EmptyResponse => new AblyResponse { TextResponse = "[{}]" };

        private static ResponseType GetResponseType(string contentType)
        {
            if (contentType == null)
            {
                return ResponseType.Binary;
            }

            switch (contentType.ToLower())
            {
                case "application/json":
                    return ResponseType.Json;
                case "application/jwt":
                    return ResponseType.Jwt;
                case "text/plain":
                    return ResponseType.Text;
                default:
                    return ResponseType.Binary;
            }
        }

        private class EmptyHttpHeaders : HttpHeaders
        {
        }
    }
}
