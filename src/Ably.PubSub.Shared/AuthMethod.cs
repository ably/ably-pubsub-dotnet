namespace IO.Ably
{
    /// <summary>
    /// Authentication method.
    /// Can be Basic or Token.
    /// </summary>
    public enum AuthMethod : byte
    {
        /// <summary>
        /// The Basic auth to authenticate
        /// </summary>
        Basic,

        /// <summary>
        /// Uses a token to authenticate
        /// </summary>
        Token,
    }

    /// <summary>The library supports several token authentication methods, this enum lists those methods + descriptions.</summary>
    internal enum TokenAuthMethod : byte
    {
        None = 0,
        Callback,
        Url,
        Signing,
        JustToken
    }
}
