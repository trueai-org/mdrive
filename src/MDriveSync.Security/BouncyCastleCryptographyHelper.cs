using Org.BouncyCastle.Crypto.Modes;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Security;

namespace MDriveSync.Security
{
    public class BouncyCastleCryptographyHelper
    {
        private const int ChaCha20NonceSize = 12; // 96-bit nonce
        private const int ChaCha20TagSize = 16;   // 128-bit tag

        public static byte[] EncryptChaCha20Poly1305(byte[] plaintext, byte[] key, byte[] nonce = null)
        {
            if (nonce == null)
            {
                SecureRandom random = new SecureRandom(); 
                nonce = new byte[ChaCha20NonceSize];
                random.NextBytes(nonce);
            }
            else
            {
                if (nonce.Length != ChaCha20NonceSize)
                {
                    throw new ArgumentException("Nonce length must be 12 bytes", nameof(nonce));
                }
            }

            ChaCha20Poly1305 engine = new ChaCha20Poly1305();
            engine.Init(true, new ParametersWithIV(new KeyParameter(key), nonce));

            byte[] ciphertext = new byte[plaintext.Length + ChaCha20TagSize];
            int len = engine.ProcessBytes(plaintext, 0, plaintext.Length, ciphertext, 0);
            engine.DoFinal(ciphertext, len);

            byte[] result = new byte[nonce.Length + ciphertext.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);

            return result;
        }

        public static byte[] DecryptChaCha20Poly1305(byte[] ciphertextWithNonce, byte[] key)
        {
            byte[] nonce = new byte[ChaCha20NonceSize];
            byte[] ciphertext = new byte[ciphertextWithNonce.Length - ChaCha20NonceSize];

            Buffer.BlockCopy(ciphertextWithNonce, 0, nonce, 0, nonce.Length);
            Buffer.BlockCopy(ciphertextWithNonce, nonce.Length, ciphertext, 0, ciphertext.Length);

            ChaCha20Poly1305 engine = new ChaCha20Poly1305();
            engine.Init(false, new ParametersWithIV(new KeyParameter(key), nonce));

            byte[] plaintext = new byte[ciphertext.Length - ChaCha20TagSize];
            int len = engine.ProcessBytes(ciphertext, 0, ciphertext.Length, plaintext, 0);
            engine.DoFinal(plaintext, len);

            return plaintext;
        }
    }
}