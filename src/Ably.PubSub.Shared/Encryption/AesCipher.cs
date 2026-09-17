using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using IO.Ably;
using IO.Ably.Encryption;
using CipherMode = IO.Ably.Encryption.CipherMode;

namespace AblyPlatform.Cryptography
{
    /// <summary>Cipher implementation using RinjaelManaged class under the hood.
    /// The Cipher params decide the Cipher mode and key
    /// The Iv vector is generated on each encryption request and added to the encrypted data stream.</summary>
    internal class AesCipher : IChannelCipher
    {
        private readonly CipherParams _params;

        private static readonly Dictionary<CipherMode, System.Security.Cryptography.CipherMode> ModesMap = new Dictionary<CipherMode, System.Security.Cryptography.CipherMode>
        {
            { CipherMode.CBC, System.Security.Cryptography.CipherMode.CBC },
            { CipherMode.ECB, System.Security.Cryptography.CipherMode.ECB },
            { CipherMode.CTS, System.Security.Cryptography.CipherMode.CTS }
        };

        /// <summary>Create a new instance of AesCipher.</summary>
        /// <param name="params">Cipher params used to configure the RinjaelManaged algorithm.</param>
        public AesCipher(CipherParams @params)
        {
            _params = @params;
        }

        public string Algorithm => "AES";

        public static byte[] GenerateKey(CipherMode? mode, int? keyLength)
        {
            using (var aes = Aes.Create())
            {
                aes.KeySize = keyLength ?? Crypto.DefaultKeylength;
                aes.Mode = MapAblyMode(mode);
                aes.Padding = PaddingMode.PKCS7;
                aes.BlockSize = Crypto.DefaultBlocklength * 8;
                aes.GenerateKey();
                return aes.Key;
            }
        }

        private static System.Security.Cryptography.CipherMode MapAblyMode(CipherMode? mode)
        {
            if (mode == null)
            {
                return System.Security.Cryptography.CipherMode.CBC;
            }

            return ModesMap[mode.Value];
        }

        /// <summary>Encrypt a byte[] using the CipherParams provided in the constructor.</summary>
        /// <param name="input">byte[] to be encrypted.</param>
        /// <returns>Encrypted result.</returns>
        public byte[] Encrypt(byte[] input)
        {
            try
            {
                return Encrypt(input, _params.Key, _params.KeyLength, ModesMap[_params.Mode], _params.Iv);
            }
            catch (Exception ex)
            {
                throw new AblyException(ex);
            }
        }

        /// <summary>Decrypt an encrypted byte[] using the CipherParams provided in the constructor.</summary>
        /// <param name="input">encrypted byte[].</param>
        /// <returns>decrypted byte[].</returns>
        public byte[] Decrypt(byte[] input)
        {
            try
            {
                return Decrypt(input, _params.Key, _params.KeyLength, ModesMap[_params.Mode]);
            }
            catch (Exception ex)
            {
                throw new AblyException(ex);
            }
        }

        private static byte[] Encrypt(byte[] input, byte[] key, int keySize, System.Security.Cryptography.CipherMode mode, byte[] iv = null)
        {
            using (var aesEncryption = Aes.Create())
            {
                if (iv == null)
                {
                    aesEncryption.GenerateIV();
                }
                else
                {
                    aesEncryption.IV = iv;
                }

                aesEncryption.KeySize = keySize;
                aesEncryption.BlockSize = Crypto.DefaultBlocklength * 8;
                aesEncryption.Mode = mode;
                aesEncryption.Padding = PaddingMode.PKCS7;
                aesEncryption.Key = key;
                ICryptoTransform crypto = aesEncryption.CreateEncryptor();

                // The result of the encryption and decryption
                byte[] cipherText = crypto.TransformFinalBlock(input, 0, input.Length);
                var result = new byte[cipherText.Length + aesEncryption.IV.Length];
                Buffer.BlockCopy(aesEncryption.IV, 0, result, 0, aesEncryption.IV.Length);
                Buffer.BlockCopy(cipherText, 0, result, aesEncryption.IV.Length, cipherText.Length);
                return result;
            }
        }

        private static byte[] Decrypt(byte[] input, byte[] key, int keySize, System.Security.Cryptography.CipherMode mode)
        {
            byte[] iv = input.Take(Crypto.DefaultBlocklength).ToArray();
            using (var aesEncryption = Aes.Create())
            {
                aesEncryption.KeySize = keySize;
                aesEncryption.BlockSize = Crypto.DefaultBlocklength * 8;
                aesEncryption.Mode = mode;
                aesEncryption.Padding = PaddingMode.PKCS7;
                aesEncryption.IV = iv;
                aesEncryption.Key = key;

                ICryptoTransform decrypt = aesEncryption.CreateDecryptor();
                var encryptedBuffer = input.Skip(Crypto.DefaultBlocklength).ToArray();
                return decrypt.TransformFinalBlock(encryptedBuffer, 0, encryptedBuffer.Length);
            }
        }
    }
}
