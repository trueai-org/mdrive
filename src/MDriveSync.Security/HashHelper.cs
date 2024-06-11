using Blake3;
using System.Security.Cryptography;

namespace MDriveSync.Security
{
    /// <summary>
    /// 哈希算法（MD5、SHA1、SHA256、BLAKE3）
    /// 用于生成数据块或文件的哈希值，以验证数据的完整性和唯一性
    /// 默认：SHA256
    /// </summary>
    public static class HashHelper
    {
        /// <summary>
        /// 计算数据的哈希值
        /// </summary>
        /// <param name="data">要计算哈希值的字节数组</param>
        /// <param name="algorithm">哈希算法（"SHA256"或"BLAKE3"）</param>
        /// <returns>哈希值的字节数组</returns>
        /// <exception cref="ArgumentException">当指定的哈希算法类型不支持时抛出异常</exception>
        public static byte[] ComputeHash(byte[] data, string algorithm = "SHA256")
        {
            switch (algorithm.ToUpper())
            {
                case "SHA256":
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        return sha256.ComputeHash(data);
                    }
                case "BLAKE3":
                    {
                        return Hasher.Hash(data).AsSpan().ToArray();
                    }
                case "SHA1":
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        return sha1.ComputeHash(data);
                    }
                case "MD5":
                    using (MD5 md5 = MD5.Create())
                    {
                        return md5.ComputeHash(data);
                    }
                default:
                    throw new ArgumentException("Unsupported hash algorithm", nameof(algorithm));
            }
        }

        /// <summary>
        /// 计算数据的哈希值
        /// </summary>
        /// <param name="stream"></param>
        /// <param name="algorithm"></param>
        /// <returns></returns>
        /// <exception cref="ArgumentException"></exception>
        public static byte[] ComputeHash(Stream stream, string algorithm = "SHA256")
        {
            switch (algorithm.ToUpper())
            {
                case "SHA256":
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        return sha256.ComputeHash(stream);
                    }
                case "BLAKE3":
                    {
                        using var blake3Stream = new Blake3Stream(stream);
                        return blake3Stream.ComputeHash().AsSpan().ToArray();
                    }
                case "MD5":
                    using (MD5 md5 = MD5.Create())
                    {
                        return md5.ComputeHash(stream);
                    }
                case "SHA1":
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        return sha1.ComputeHash(stream);
                    }
                default:
                    throw new ArgumentException("Unsupported hash algorithm", nameof(algorithm));
            }
        }

        /// <summary>
        /// 计算数据的哈希值并返回十六进制字符串
        /// </summary>
        /// <param name="data">要计算哈希值的字节数组</param>
        /// <param name="algorithm">哈希算法（"SHA256"或"BLAKE3"）</param>
        /// <returns>哈希值的十六进制字符串</returns>
        public static string ComputeHashHex(byte[] data, string algorithm = "SHA256")
        {
            byte[] hash = ComputeHash(data, algorithm);
            return BitConverter.ToString(hash).Replace("-", "").ToLower();
        }

        /// <summary>
        /// 计算文件的哈希值并返回十六进制字符串
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="algorithm">SHA256 | BLAKE3</param>
        /// <returns></returns>
        public static string ComputeHashHex(string filePath, string algorithm = "SHA256")
        {
            if (algorithm == "SHA1")
            {
                using (SHA1 sha1 = SHA1.Create())
                {
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        var hashBytes = sha1.ComputeHash(fileStream);
                        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                    }
                }
            }
            else if (algorithm == "MD5")
            {
                using (MD5 md5 = MD5.Create())
                {
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        var hashBytes = md5.ComputeHash(fileStream);
                        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                    }
                }
            }
            else if (algorithm == "SHA256")
            {
                using (SHA256 sha256 = SHA256.Create())
                {
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        var hashBytes = sha256.ComputeHash(fileStream);
                        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                    }
                }
            }
            else if (algorithm == "BLAKE3")
            {
                // 当大文件时，要读取文件 byte[] 会导致内存溢出，应该使用流
                using FileStream fileStream = File.OpenRead(filePath);

                // 使用 Blake3Stream 计算文件流的哈希值
                using var blake3Stream = new Blake3Stream(fileStream);
                var hash = blake3Stream.ComputeHash().AsSpan().ToArray();
                return BitConverter.ToString(hash).Replace("-", "").ToLower();
            }
            else
            {
                throw new ArgumentException("Unsupported hash algorithm", nameof(algorithm));
            }
        }

        /// <summary>
        /// 比较 hash1 和 hash2 是否相等
        /// </summary>
        /// <param name="hash1"></param>
        /// <param name="hash2"></param>
        /// <returns></returns>
        public static bool CompareHashes(byte[] hash1, byte[] hash2)
        {
            if (hash1.Length != hash2.Length)
                return false;

            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                    return false;
            }

            return true;
        }
    }
}