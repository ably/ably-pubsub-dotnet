namespace IO.Ably
{
    /// <summary>
    /// Extension point for implementing custom Channel Ciphers.
    /// Currently only AesCipher channel cipher exists.
    /// </summary>
    public interface IChannelCipher
    {
        /// <summary>
        /// Encryption algorithm.
        /// </summary>
        string Algorithm { get; }

        /// <summary>
        /// Method to encrypt some input.
        /// </summary>
        /// <param name="input">byte array.</param>
        /// <returns>encrypted byte array.</returns>
        byte[] Encrypt(byte[] input);

        /// <summary>
        /// Method to decrypt some input.
        /// </summary>
        /// <param name="input">encrypted byte array.</param>
        /// <returns>decrypted byte array.</returns>
        byte[] Decrypt(byte[] input);
    }
}
