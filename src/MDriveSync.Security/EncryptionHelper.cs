using System.Security.Cryptography;
using System.Text;

namespace MDriveSync.Security
{
    /// <summary>
    /// 加密解密函数
    /// </summary>
    public static class EncryptionHelper
    {
        // 定义AES256-GCM和ChaCha20-Poly1305使用的Nonce和Tag的长度
        private const int AesGcmTagSize = 16; // 128-bit tag

        private const int ChaCha20NonceSize = 12; // 96-bit nonce
        private const int ChaCha20TagSize = 16;   // 128-bit tag

        /// <summary>
        /// 使用AES256-GCM算法加密数据
        /// </summary>
        /// <param name="plaintext">明文数据</param>
        /// <param name="key">加密密钥</param>
        /// <returns>加密后的数据，包括nonce、密文和tag</returns>
        public static byte[] EncryptWithAES256GCM(byte[] plaintext, string key)
        {
            // 生成AES-GCM加密实例，指定标签大小
            using AesGcm aesGcm = new AesGcm(GenerateKey(key), AesGcmTagSize);

            // 生成随机nonce
            byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
            RandomNumberGenerator.Fill(nonce);

            // 准备存储加密后的密文和tag
            byte[] ciphertext = new byte[plaintext.Length];
            byte[] tag = new byte[AesGcmTagSize];

            // 执行加密操作
            aesGcm.Encrypt(nonce, plaintext, ciphertext, tag);

            // 合并nonce、密文和tag到一个结果数组
            byte[] result = new byte[nonce.Length + ciphertext.Length + tag.Length];
            Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
            Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
            Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);

            return result;
        }

        /// <summary>
        /// 使用AES256-GCM算法解密数据
        /// </summary>
        /// <param name="encryptedData">加密数据，包括nonce、密文和tag</param>
        /// <param name="key">解密密钥</param>
        /// <returns>解密后的明文数据</returns>
        public static byte[] DecryptWithAES256GCM(byte[] encryptedData, string key)
        {
            using AesGcm aesGcm = new AesGcm(GenerateKey(key), AesGcmTagSize);

            // 提取nonce、密文和tag
            byte[] nonce = new byte[AesGcm.NonceByteSizes.MaxSize];
            byte[] tag = new byte[AesGcmTagSize];
            byte[] ciphertext = new byte[encryptedData.Length - nonce.Length - tag.Length];

            Buffer.BlockCopy(encryptedData, 0, nonce, 0, nonce.Length);
            Buffer.BlockCopy(encryptedData, nonce.Length, ciphertext, 0, ciphertext.Length);
            Buffer.BlockCopy(encryptedData, nonce.Length + ciphertext.Length, tag, 0, tag.Length);

            byte[] decryptedData = new byte[ciphertext.Length];
            aesGcm.Decrypt(nonce, ciphertext, tag, decryptedData);

            return decryptedData;
        }

        /// <summary>
        /// 使用ChaCha20-Poly1305算法加密数据
        /// </summary>
        /// <param name="plaintext">明文数据</param>
        /// <param name="key">加密密钥</param>
        /// <returns>加密后的数据，包括nonce、密文和tag</returns>
        public static byte[] EncryptWithChaCha20Poly1305(byte[] plaintext, string key)
        {
            if (ChaCha20Poly1305.IsSupported)
            {
                using ChaCha20Poly1305 chacha = new ChaCha20Poly1305(GenerateKey(key));

                // 生成随机nonce
                byte[] nonce = new byte[ChaCha20NonceSize];
                RandomNumberGenerator.Fill(nonce);

                // 准备存储加密后的密文和tag
                byte[] ciphertext = new byte[plaintext.Length];
                byte[] tag = new byte[ChaCha20TagSize];

                // 执行加密操作
                chacha.Encrypt(nonce, plaintext, ciphertext, tag);

                // 合并nonce、密文和tag到一个结果数组
                byte[] result = new byte[nonce.Length + ciphertext.Length + tag.Length];
                Buffer.BlockCopy(nonce, 0, result, 0, nonce.Length);
                Buffer.BlockCopy(ciphertext, 0, result, nonce.Length, ciphertext.Length);
                Buffer.BlockCopy(tag, 0, result, nonce.Length + ciphertext.Length, tag.Length);

                return result;
            }
            else
            {
                // 使用 BouncyCastle.Cryptography 实现 ChaCha20-Poly1305 算法
                return BouncyCastleCryptographyHelper.EncryptChaCha20Poly1305(plaintext, GenerateKey(key));
            }
        }

        /// <summary>
        /// 使用ChaCha20-Poly1305算法解密数据
        /// </summary>
        /// <param name="encryptedData">加密数据，包括nonce、密文和tag</param>
        /// <param name="key">解密密钥</param>
        /// <returns>解密后的明文数据</returns>
        public static byte[] DecryptWithChaCha20Poly1305(byte[] encryptedData, string key)
        {
            if (ChaCha20Poly1305.IsSupported)
            {
                using ChaCha20Poly1305 chacha = new ChaCha20Poly1305(GenerateKey(key));

                // 提取nonce、密文和tag
                byte[] nonce = new byte[ChaCha20NonceSize];
                byte[] tag = new byte[ChaCha20TagSize];
                byte[] ciphertext = new byte[encryptedData.Length - nonce.Length - tag.Length];

                Buffer.BlockCopy(encryptedData, 0, nonce, 0, nonce.Length);
                Buffer.BlockCopy(encryptedData, nonce.Length, ciphertext, 0, ciphertext.Length);
                Buffer.BlockCopy(encryptedData, nonce.Length + ciphertext.Length, tag, 0, tag.Length);

                byte[] decryptedData = new byte[ciphertext.Length];
                chacha.Decrypt(nonce, ciphertext, tag, decryptedData);

                return decryptedData;
            }
            else
            {
                // 使用 BouncyCastle.Cryptography 实现 ChaCha20-Poly1305 算法
                return BouncyCastleCryptographyHelper.DecryptChaCha20Poly1305(encryptedData, GenerateKey(key));
            }
        }

        /// <summary>
        /// 生成32字节密钥
        /// </summary>
        /// <param name="key">输入的字符串密钥</param>
        /// <returns>生成的32字节密钥</returns>
        private static byte[] GenerateKey(string key)
        {
            using SHA256 sha256 = SHA256.Create();
            // 对输入的字符串进行哈希，确保密钥长度为32字节
            return sha256.ComputeHash(Encoding.UTF8.GetBytes(key.PadRight(32).Substring(0, 32)));
        }
    }
}