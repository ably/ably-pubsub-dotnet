using System;
using System.Threading.Tasks;

namespace IO.Ably
{
    /// <summary>
    /// Token-generation and authentication operations for the Ably API.
    /// See the Ably Authentication documentation for details of the
    /// authentication methods available.
    /// </summary>
    public interface IAblyAuth
    {
        /// <summary>
        /// Client id for this library instance.
        /// Spec: RSA7b.
        /// </summary>
        string ClientId { get; }

        /// <summary>
        /// Makes a token request. This will make a token request now, even if the library already
        /// has a valid token. It would typically be used to issue tokens for use by other clients.
        /// </summary>
        /// <param name="tokenParams">The <see cref="TokenRequest"/> data used for the token.</param>
        /// <param name="authOptions">Extra <see cref="AuthOptions"/> used for creating a token.</param>
        /// <returns>A valid ably token.</returns>
        /// <exception cref="AblyException">something went wrong.</exception>
        Task<TokenDetails> RequestTokenAsync(TokenParams tokenParams = null, AuthOptions authOptions = null);

        /// <summary>
        /// Ensure valid auth credentials are present. This may rely in an already-known
        /// and valid token, and will obtain a new token if necessary or explicitly
        /// requested.
        /// Authorisation will use the parameters supplied on construction except
        /// where overridden with the options supplied in the call.
        /// </summary>
        /// <param name="tokenParams"><see cref="TokenParams"/> custom parameter. Pass null and default token request options will be generated used the options passed when creating the client.</param>
        /// <param name="options"><see cref="AuthOptions"/> custom options.</param>
        /// <returns>Returns a valid token.</returns>
        /// <exception cref="AblyException">Throws an ably exception representing the server response.</exception>
        Task<TokenDetails> AuthorizeAsync(TokenParams tokenParams = null, AuthOptions options = null);

        /// <summary>
        /// See <see cref="AuthorizeAsync(TokenParams, AuthOptions)"/>.
        /// </summary>
        /// <param name="tokenParams"><see cref="TokenParams"/> custom parameter. Pass null and default token request options will be generated used the options passed when creating the client.</param>
        /// <param name="options"><see cref="AuthOptions"/> custom options.</param>
        /// <returns>Returns a valid token.</returns>
        /// <exception cref="AblyException">Throws an ably exception representing the server response.</exception>
        [Obsolete("This method will be removed in the future, please replace with a call to AuthorizeAsync")]
        Task<TokenDetails> AuthoriseAsync(TokenParams tokenParams = null, AuthOptions options = null);

        /// <summary>
        /// Create a signed token request based on known credentials.
        /// and the given token params. This would typically be used if creating
        /// signed requests for submission by another client.
        /// </summary>
        /// <param name="tokenParams"><see cref="TokenParams"/>. If null a token request is generated from options passed when the client was created.</param>
        /// <param name="authOptions"><see cref="AuthOptions"/>. If null the default AuthOptions are used.</param>
        /// <returns>serialized signed token request.</returns>
        Task<string> CreateTokenRequestAsync(TokenParams tokenParams = null, AuthOptions authOptions = null);

        /// <summary>
        /// Create a signed token request based on known credentials
        /// and the given token params. This would typically be used if creating
        /// signed requests for submission by another client.
        /// </summary>
        /// <param name="tokenParams"><see cref="TokenParams"/>. If null a token request is generated from options passed when the client was created.</param>
        /// <param name="authOptions"><see cref="AuthOptions"/>. If null the default AuthOptions are used.</param>
        /// <returns>signed token request.</returns>
        [Obsolete("This method will be removed in a future version, please use CreateTokenRequestAsync instead")]
        Task<TokenRequest> CreateTokenRequestObjectAsync(TokenParams tokenParams = null, AuthOptions authOptions = null);

        /// <summary>
        /// Sync version for <see cref="RequestTokenAsync(TokenParams, AuthOptions)"/>.
        /// Prefer the Async version where possible.
        /// </summary>
        /// <param name="tokenParams">The <see cref="TokenRequest"/> data used for the token.</param>
        /// <param name="options">Extra <see cref="AuthOptions"/> used for creating a token.</param>
        /// <returns>A valid ably token.</returns>
        /// <exception cref="AblyException">something went wrong.</exception>
        TokenDetails RequestToken(TokenParams tokenParams = null, AuthOptions options = null);

        /// <summary>
        /// Sync version for <see cref="AuthorizeAsync(TokenParams, AuthOptions)"/>.
        /// Prefer the Async version where possible.
        /// </summary>
        /// <param name="tokenParams"><see cref="TokenParams"/> custom parameter. Pass null and default token request options will be generated used the options passed when creating the client.</param>
        /// <param name="options"><see cref="AuthOptions"/> custom options.</param>
        /// <returns>Returns a valid token.</returns>
        /// <exception cref="AblyException">Throws an ably exception representing the server response.</exception>
        TokenDetails Authorize(TokenParams tokenParams = null, AuthOptions options = null);

        /// <summary>
        /// See <see cref="Authorize(TokenParams, AuthOptions)"/>.
        /// </summary>
        /// <param name="tokenParams"><see cref="TokenParams"/> custom parameter. Pass null and default token request options will be generated used the options passed when creating the client.</param>
        /// <param name="options"><see cref="AuthOptions"/> custom options.</param>
        /// <returns>Returns a valid token.</returns>
        /// <exception cref="AblyException">Throws an ably exception representing the server response.</exception>
        [Obsolete("This method will be removed in the future, please replace with a call to Authorize")]
        TokenDetails Authorise(TokenParams tokenParams = null, AuthOptions options = null);

        /// <summary>
        /// Sync version for <see cref="CreateTokenRequestAsync"/>
        /// Prefer the async version where possible.
        /// </summary>
        /// <param name="tokenParams"><see cref="TokenParams"/>. If null a token request is generated from options passed when the client was created.</param>
        /// <param name="authOptions"><see cref="AuthOptions"/>. If null the default AuthOptions are used.</param>
        /// <returns>serialized signed token request.</returns>
        string CreateTokenRequest(TokenParams tokenParams = null, AuthOptions authOptions = null);

        /// <summary>
        /// Sync version for <see cref="CreateTokenRequestObjectAsync"/>
        /// Prefer the async version where possible.
        /// </summary>
        /// <param name="tokenParams"><see cref="TokenParams"/>. If null a token request is generated from options passed when the client was created.</param>
        /// <param name="authOptions"><see cref="AuthOptions"/>. If null the default AuthOptions are used.</param>
        /// <returns>signed token request.</returns>
        [Obsolete("This method will be removed in a future version, please use CreateTokenRequest instead")]
        TokenRequest CreateTokenRequestObject(TokenParams tokenParams = null, AuthOptions authOptions = null);
    }
}
