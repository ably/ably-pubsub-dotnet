using System;
using System.Linq;
using System.Net;
using System.Security.Cryptography;
using AblyPlatform.Cryptography;

namespace IO.Ably.Encryption
{
    /// <summary>Specifies the block cipher mode to use for encryption.</summary>
    public enum CipherMode
    {
        /// <summary>
        /// The Cipher Block Chaining (CBC) mode introduces feedback. Before each plain text
        /// block is encrypted, it is combined with the cipher text of the previous block
        /// by a bitwise exclusive OR operation. This ensures that even if the plain text
        /// contains many identical blocks, they will each encrypt to a different cipher
        /// text block. The initialization vector is combined with the first plain text block
        /// by a bitwise exclusive OR operation before the block is encrypted. If a single
        /// bit of the cipher text block is mangled, the corresponding plain text block will
        /// also be mangled. In addition, a bit in the subsequent block, in the same position
        /// as the original mangled bit, will be mangled.
        /// </summary>
        CBC = 1,

        /// <summary>
        /// The Electronic Codebook (ECB) mode encrypts each block individually. Any blocks
        /// of plain text that are identical and in the same message, or that are in a different
        /// message encrypted with the same key, will be transformed into identical cipher
        /// text blocks. Important: This mode is not recommended because it opens the door
        /// for multiple security exploits. If the plain text to be encrypted contains substantial
        /// repetition, it is feasible for the cipher text to be broken one block at a time.
        /// It is also possible to use block analysis to determine the encryption key. Also,
        /// an active adversary can substitute and exchange individual blocks without detection,
        /// which allows blocks to be saved and inserted into the stream at other points
        /// without detection.
        /// </summary>
        ECB = 2,

        /// <summary>
        /// The Output Feedback (OFB) mode processes small increments of plain text into
        /// cipher text instead of processing an entire block at a time. This mode is similar
        /// to CFB; the only difference between the two modes is the way that the shift register
        /// is filled. If a bit in the cipher text is mangled, the corresponding bit of plain
        /// text will be mangled. However, if there are extra or missing bits from the cipher
        /// text, the plain text will be mangled from that point on.
        /// </summary>
        OFB = 3,

        /// <summary>
        /// The Cipher Feedback (CFB) mode processes small increments of plain text into
        /// cipher text, instead of processing an entire block at a time. This mode uses
        /// a shift register that is one block in length and is divided into sections. For
        /// example, if the block size is 8 bytes, with one byte processed at a time, the
        /// shift register is divided into eight sections. If a bit in the cipher text is
        /// mangled, one plain text bit is mangled and the shift register is corrupted. This
        /// results in the next several plain text increments being mangled until the bad
        /// bit is shifted out of the shift register. The default feedback size can vary
        /// by algorithm, but is typically either 8 bits or the number of bits of the block
        /// size. You can alter the number of feedback bits by using the System.Security.Cryptography.SymmetricAlgorithm.FeedbackSize
        /// property. Algorithms that support CFB use this property to set the feedback.
        /// </summary>
        CFB = 4,

        /// <summary>
        /// The Cipher Text Stealing (CTS) mode handles any length of plain text and produces
        /// cipher text whose length matches the plain text length. This mode behaves like
        /// the CBC mode for all but the last two blocks of the plain text.
        /// </summary>
        CTS = 5
    }

    /// <summary>
    ///     Utility classes for message payload encryption.
    ///
    /// This class supports AES/CBC with a default key length of 256 bits
    /// but supporting other key lengths.Other algorithms and chaining modes are
    /// not supported directly, but supportable by extending/implementing the base
    /// classes and interfaces here.
    ///
    /// Secure random data for creation of Initialisation Vectors (IVs) and keys
    /// is obtained from the default system.
    ///
    /// Each message payload is encrypted with an IV in CBC mode, and the IV is
    /// concatenated with the resulting raw ciphertext to construct the "ciphertext"
    /// data passed to the recipient.
    /// </summary>
    public static class Crypto
    {
#pragma warning disable SYSLIB0023
        private static readonly RNGCryptoServiceProvider SecureRandom = new RNGCryptoServiceProvider();
#pragma warning restore SYSLIB0023

        private const int IdempotentGeneratedIdLength = 9;

        /// <summary>
        /// Default Encryption algorithm. (AES).
        /// </summary>
        public const string DefaultAlgorithm = "AES";

        /// <summary>
        /// Default key length (256).
        /// </summary>
        public const int DefaultKeylength = 256; // bits

        /// <summary>
        /// Default block length (16).
        /// </summary>
        public const int DefaultBlocklength = 16; // bytes

        /// <summary>
        /// Default CipherMode (CBC).
        /// </summary>
        public const CipherMode DefaultMode = CipherMode.CBC;

        /// <summary>
        /// Gets default CipherParams based on a base64EncodedKey.
        /// </summary>
        /// <param name="base64EncodedKey">required at least base64 encoded key.</param>
        /// <param name="base64Iv">optional base64Encoded Iv.</param>
        /// <param name="mode">optional Cipher mode.</param>
        /// <returns>CipherParams.</returns>
        public static CipherParams GetDefaultParams(string base64EncodedKey, string base64Iv = null, CipherMode? mode = null)
        {
            if (base64EncodedKey == null)
            {
                throw new ArgumentNullException(nameof(base64EncodedKey), "Base64Encoded key cannot be null");
            }

            return GetDefaultParams(base64EncodedKey.FromBase64(), base64Iv?.FromBase64(), mode);
        }

        /// <summary>
        /// Gets default params and generates a random key if not supplied.
        /// </summary>
        /// <param name="key">optional; key.</param>
        /// <param name="iv">optional; iv.</param>
        /// <param name="mode">optional: CipherMode.</param>
        /// <returns>CipherParams.</returns>
        public static CipherParams GetDefaultParams(byte[] key = null, byte[] iv = null, CipherMode? mode = null)
        {
            if (key != null && key.Any())
            {
                ValidateKeyLength(key.Length * 8);

                return new CipherParams(DefaultAlgorithm, key, mode, iv);
            }

            return new CipherParams(GenerateRandomKey(mode: mode));
        }

        private static void ValidateKeyLength(int keyLength)
        {
            if (keyLength != 128 && keyLength != 256)
            {
                throw new AblyException($"Only 128 and 256 bit keys are supported. Provided key is {keyLength}", 40003, HttpStatusCode.BadRequest);
            }
        }

        /// <summary>
        /// Gets a ChannelCipher for given cipher params. Currently supports only AES.
        /// </summary>
        /// <param name="cipherParams">cipher params.</param>
        /// <exception cref="AblyException">throws if the encryption algorithm is different from AES.</exception>
        /// <returns>a computed channel cipher.</returns>
        public static IChannelCipher GetCipher(CipherParams cipherParams)
        {
            if (string.Equals(cipherParams.Algorithm, Crypto.DefaultAlgorithm, StringComparison.CurrentCultureIgnoreCase))
            {
                return new AesCipher(cipherParams);
            }

            throw new AblyException("Currently only the AES encryption algorithm is supported", ErrorCodes.InternalError, HttpStatusCode.InternalServerError);
        }

        /// <summary>
        /// Computes HMacSha256 hash.
        /// </summary>
        /// <param name="text">Text to be hashed.</param>
        /// <param name="key">Key used for hashing.</param>
        /// <returns>HMacSha256 hash.</returns>
        public static string ComputeHMacSha256(string text, string key)
        {
            byte[] bytes = text.GetBytes();
            byte[] keyBytes = key.GetBytes();
            using (var hmac = new HMACSHA256(keyBytes))
            {
                var hash = hmac.ComputeHash(bytes);
                return Convert.ToBase64String(hash);
            }
        }

        /// <summary>
        /// Given a keyLength and Cipher mode, generates a random key.
        /// </summary>
        /// <param name="keyLength">128 or 256 bit keys are supporter.</param>
        /// <param name="mode">optional Cipher mode.</param>
        /// <returns>returns generated encryption key.</returns>
        public static byte[] GenerateRandomKey(int? keyLength = null, CipherMode? mode = null)
        {
            if (keyLength.HasValue)
            {
                ValidateKeyLength(keyLength.Value);
            }

            return AesCipher.GenerateKey(mode, keyLength);
        }

        /// <summary>
        /// Generates a cryptographically random message id.
        /// </summary>
        /// <returns>base64 encoded random array of 9 bytes.</returns>
        public static string GetRandomMessageId()
        {
            var bytes = new byte[IdempotentGeneratedIdLength];
            SecureRandom.GetBytes(bytes);
            return bytes.ToBase64();
        }

        /// <summary>
        /// Generates a random SHA256 hash and returns a Base64 encoded string.
        /// </summary>
        /// <returns>Returns a random string.</returns>
        internal static string GenerateSecret()
        {
            var entropy = new byte[64];
            var rnd = RandomNumberGenerator.Create();
            rnd.GetNonZeroBytes(entropy);

            var hasher = SHA256.Create();
            var hash = hasher.ComputeHash(entropy);
            return Convert.ToBase64String(hash);
        }
    }
}
