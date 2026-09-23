namespace IO.Ably.Push
{
    /// <summary>
    /// Class used to hold registration tokens.
    /// </summary>
    public class RegistrationToken
    {
        /// <summary>
        /// Constructs a new registration token instance.
        /// </summary>
        /// <param name="tokenType">Token type.</param>
        /// <param name="tokenValue">Token value.</param>
        public RegistrationToken(string tokenType, string tokenValue)
        {
            Type = tokenType;
            Token = tokenValue;
        }

        /// <summary>
        /// FGM or GCM for Google and APNS for Apple.
        /// </summary>
        public string Type { get; }

        /// <summary>
        /// Token value.
        /// </summary>
        public string Token { get; }

        /// <summary>
        /// Overrides to string to display Type and Token.
        /// </summary>
        /// <returns>Returns a string including Type and Token Value.</returns>
        public override string ToString()
        {
            return $"RegistrationToken: Type = {Type}, Token = {Token}";
        }
    }
}
