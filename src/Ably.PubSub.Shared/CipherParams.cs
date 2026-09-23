using IO.Ably.Encryption;

namespace IO.Ably
{
    /// <summary>
    /// A class encapsulating the client-specifiable parameters for
    /// the cipher.
    ///
    /// algorithm is the name of the algorithm in the default system provider,
    /// or the lower-cased version of it; eg "aes" or "AES".
    ///
    /// Clients may instance a CipherParams directly and populate it, or may
    /// query the implementation to obtain a default system CipherParams.
    ///
    /// </summary>
    public class CipherParams
    {
        /// <summary>
        /// Algorithm.
        /// </summary>
        public string Algorithm { get; }

        /// <summary>
        /// Encryption key.
        /// </summary>
        public byte[] Key { get; }

        /// <summary>
        /// Encryption Iv.
        /// </summary>
        public byte[] Iv { get; }

        /// <summary>
        /// Cipher mode.
        /// </summary>
        public CipherMode Mode { get; }

        /// <summary>
        /// Length of the specified key.
        /// Returns: 0 if there is no key.
        /// </summary>
        public int KeyLength => Key?.Length * 8 ?? 0;

        /// <summary>
        /// Concatenated string of Algorithm-KeyLength-Mode.
        /// </summary>
        public string CipherType => $"{Algorithm}-{KeyLength}-{Mode}";

        /// <summary>
        /// Obtain a default CipherParams. This uses default algorithm (AES), mode and
        /// padding and initialises a key based on the given key data.
        /// </summary>
        /// <param name="key">encryption key.</param>
        public CipherParams(byte[] key)
            : this(Crypto.DefaultAlgorithm, key)
        {
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CipherParams"/> class by passing all
        /// parameters, algorithm, key, mode and iv.
        /// </summary>
        /// <param name="algorithm">encryption algorithm.</param>
        /// <param name="key">key.</param>
        /// <param name="mode">mode.</param>
        /// <param name="iv">iv.</param>
        public CipherParams(string algorithm, byte[] key, CipherMode? mode = null, byte[] iv = null)
        {
            Algorithm = algorithm.IsEmpty() ? Crypto.DefaultAlgorithm : algorithm;
            Key = key;
            Mode = mode ?? Crypto.DefaultMode;
            Iv = iv;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="CipherParams"/> class by passing all
        /// parameters, algorithm, base64 encoded key, mode and base64 encoded iv.
        /// </summary>
        /// <param name="algorithm">encryption algorithm.</param>
        /// <param name="base64Key">base64 encoded key.</param>
        /// <param name="mode">mode.</param>
        /// <param name="base64Iv">base64 encoded iv.</param>
        public CipherParams(string algorithm, string base64Key = null, CipherMode mode = Crypto.DefaultMode, string base64Iv = null)
        {
            Algorithm = algorithm.IsEmpty() ? Crypto.DefaultAlgorithm : algorithm;
            Key = base64Key.FromBase64();
            Mode = mode;
            Iv = base64Iv.FromBase64();
        }

        /// <summary>
        /// Equals method comparing two CipherParams objects.
        /// </summary>
        /// <param name="other">second CipherParams object.</param>
        /// <returns>true or false.</returns>
        protected bool Equals(CipherParams other)
        {
            return string.Equals(Algorithm, other.Algorithm) && Equals(Key, other.Key) && Equals(Iv, other.Iv) && Mode == other.Mode && KeyLength == other.KeyLength;
        }

        /// <inheritdoc/>
        public override bool Equals(object obj)
        {
            if (ReferenceEquals(null, obj))
            {
                return false;
            }

            if (ReferenceEquals(this, obj))
            {
                return true;
            }

            if (obj.GetType() != GetType())
            {
                return false;
            }

            return Equals((CipherParams)obj);
        }

        /// <inheritdoc/>
        public override int GetHashCode()
        {
            unchecked
            {
                var hashCode = Algorithm?.GetHashCode() ?? 0;
                hashCode = (hashCode * 397) ^ (Key != null ? Key.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (Iv != null ? Iv.GetHashCode() : 0);
                hashCode = (hashCode * 397) ^ (int)Mode;
                hashCode = (hashCode * 397) ^ KeyLength;
                return hashCode;
            }
        }
    }
}
