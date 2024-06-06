using Blake3;
using System.Security.Cryptography;

namespace MDriveSync.Security
{
    /// <summary>
    /// 哈希算法（SHA256、BLAKE3）
    /// 用于生成数据块或文件的哈希值，以验证数据的完整性和唯一性
    /// 默认：SHA256
    /// </summary>
    public static class HashHelper
    {
        /// <summary>
        /// 计算数据的哈希值
        /// </summary>
        /// <param name="data">要计算哈希值的字节数组</param>
        /// <param name="algorithm">哈希算法（"SHA256"或"BLAKE3"），如果为空则默认为"SHA256"</param>
        /// <returns>哈希值的字节数组</returns>
        /// <exception cref="ArgumentException">当指定的哈希算法类型不支持时抛出异常</exception>
        public static byte[] ComputeHash(byte[] data, string algorithm = "SHA256")
        {
            switch (algorithm.ToUpperInvariant())
            {
                case "SHA256":
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        return sha256.ComputeHash(data);
                    }

                case "BLAKE3":
                    return Hasher.Hash(data).AsSpan().ToArray();

                default:
                    throw new ArgumentException("Unsupported hash algorithm", nameof(algorithm));
            }
        }

        /// <summary>
        /// 计算数据的哈希值并返回十六进制字符串
        /// </summary>
        /// <param name="data">要计算哈希值的字节数组</param>
        /// <param name="algorithm">哈希算法（"SHA256"或"BLAKE3"），如果为空则默认为"SHA256"</param>
        /// <returns>哈希值的十六进制字符串</returns>
        public static string ComputeHashHex(byte[] data, string algorithm = "SHA256")
        {
            byte[] hash = ComputeHash(data, algorithm);
            return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();

            // OR
            //byte[] hash = ComputeHash(data, algorithm);
            //StringBuilder sb = new StringBuilder(hash.Length * 2);
            //foreach (byte b in hash)
            //{
            //    sb.Append(b.ToString("x2"));
            //}
            //return sb.ToString();
        }

        /// <summary>
        /// 计算文件的 SHA256 哈希值
        /// </summary>
        /// <param name="filePath"></param>
        /// <returns></returns>
        public static string ComputeSha256(string filePath)
        {
            using (SHA256 sha256 = SHA256.Create())
            {
                using (FileStream fileStream = File.OpenRead(filePath))
                {
                    byte[] hashBytes = sha256.ComputeHash(fileStream);
                    return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
                }
            }
        }
    }
}