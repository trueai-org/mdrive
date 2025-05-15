﻿using Blake3;
using System.Buffers.Binary;
using System.IO.Hashing;
using System.Security.Cryptography;

namespace MDriveSync.Security
{
    /// <summary>
    /// 哈希算法（MD5、SHA1、SHA256、SHA3、SHA384、SHA512、BLAKE3、XXH3、XXH128）
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
                case "SHA1":
                    using (SHA1 sha1 = SHA1.Create())
                    {
                        return sha1.ComputeHash(data);
                    }
                case "SHA256":
                    using (SHA256 sha256 = SHA256.Create())
                    {
                        return sha256.ComputeHash(data);
                    }
                case "SHA3":
                case "SHA384":
                    using (SHA384 sha3 = SHA384.Create())
                    {
                        return sha3.ComputeHash(data);
                    }
                case "SHA512":
                    using (SHA512 sha512 = SHA512.Create())
                    {
                        return sha512.ComputeHash(data);
                    }
                case "BLAKE3":
                    {
                        return Hasher.Hash(data).AsSpan().ToArray();
                    }
                case "MD5":
                    using (MD5 md5 = MD5.Create())
                    {
                        return md5.ComputeHash(data);
                    }
                case "XXH3":
                    {
                        return XxHash3.Hash(data);
                    }
                case "XXH128":
                    {
                        return XxHash128.Hash(data);
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
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // 保存原始位置，以便在操作后恢复
            long originalPosition = stream.Position;

            try
            {
                switch (algorithm.ToUpper())
                {
                    case "SHA256":
                        using (SHA256 sha256 = SHA256.Create())
                        {
                            return sha256.ComputeHash(stream);
                        }
                    case "SHA512":
                        using (SHA512 sha512 = SHA512.Create())
                        {
                            return sha512.ComputeHash(stream);
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

                    case "SHA3":
                    case "SHA384":
                        using (SHA384 sha384 = SHA384.Create())
                        {
                            return sha384.ComputeHash(stream);
                        }
                    case "XXH3":
                        {
                            var hasher = new XxHash3();
                            byte[] buffer = new byte[81920]; // 80KB buffer for good performance
                            int bytesRead;

                            // Reset stream position to beginning
                            stream.Position = originalPosition;

                            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                hasher.Append(buffer.AsSpan(0, bytesRead));
                            }

                            // Get the hash as ulong
                            ulong hashValue = hasher.GetCurrentHashAsUInt64();

                            // Convert to byte array (8 bytes)
                            byte[] result = new byte[8];
                            BinaryPrimitives.WriteUInt64BigEndian(result, hashValue);
                            return result;
                        }
                    case "XXH128":
                        {
                            var hasher = new XxHash128();
                            byte[] buffer = new byte[81920]; // 80KB buffer for good performance
                            int bytesRead;

                            // Reset stream position to beginning
                            stream.Position = originalPosition;

                            while ((bytesRead = stream.Read(buffer, 0, buffer.Length)) > 0)
                            {
                                hasher.Append(buffer.AsSpan(0, bytesRead));
                            }

                            // Get the hash as ulong
                            var hashValue = hasher.GetCurrentHashAsUInt128();

                            // Convert to byte array (16 bytes)
                            byte[] result = new byte[16];
                            BinaryPrimitives.WriteUInt128BigEndian(result, hashValue);
                            return result;
                        }

                    default:
                        throw new ArgumentException("Unsupported hash algorithm", nameof(algorithm));
                }
            }
            finally
            {
                // 恢复流的原始位置
                stream.Position = originalPosition;
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
            algorithm = algorithm.ToUpper();

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
            // SHA512
            else if (algorithm == "SHA512")
            {
                using (SHA512 sha512 = SHA512.Create())
                {
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        var hashBytes = sha512.ComputeHash(fileStream);
                        return BitConverter.ToString(hashBytes).Replace("-", "").ToLower();
                    }
                }
            }
            else if (algorithm == "SHA3" || algorithm == "SHA384")
            {
                using (SHA384 sha3 = SHA384.Create())
                {
                    using (FileStream fileStream = File.OpenRead(filePath))
                    {
                        var hashBytes = sha3.ComputeHash(fileStream);
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
            else if (algorithm == "XXH3")
            {
                using FileStream fileStream = File.OpenRead(filePath);
                var hasher = new XxHash3();
                byte[] buffer = new byte[81920]; // 80KB buffer for good performance
                int bytesRead;
                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasher.Append(buffer.AsSpan(0, bytesRead));
                }
                // Get the hash as ulong
                ulong hashValue = hasher.GetCurrentHashAsUInt64();
                // Convert to byte array (8 bytes)
                byte[] result = new byte[8];
                BinaryPrimitives.WriteUInt64BigEndian(result, hashValue);
                return BitConverter.ToString(result).Replace("-", "").ToLower();

            }
            else if (algorithm == "XXH128")
            {
                using FileStream fileStream = File.OpenRead(filePath);
                var hasher = new XxHash128();
                byte[] buffer = new byte[81920]; // 80KB buffer for good performance
                int bytesRead;
                while ((bytesRead = fileStream.Read(buffer, 0, buffer.Length)) > 0)
                {
                    hasher.Append(buffer.AsSpan(0, bytesRead));
                }
                // Get the hash as ulong
                var hashValue = hasher.GetCurrentHashAsUInt128();
                // Convert to byte array (16 bytes)
                byte[] result = new byte[16];
                BinaryPrimitives.WriteUInt128BigEndian(result, hashValue);
                return BitConverter.ToString(result).Replace("-", "").ToLower();
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

        /// <summary>
        /// 计算流从当前位置开始指定长度的哈希值
        /// </summary>
        /// <param name="stream">要计算哈希的流</param>
        /// <param name="algorithm">哈希算法名称</param>
        /// <param name="length">要计算哈希的长度，如果为null则计算到流末尾</param>
        /// <returns>哈希值的字节数组</returns>
        public static byte[] ComputeStreamHash(Stream stream, string algorithm, int? length = null)
        {
            if (stream == null)
                throw new ArgumentNullException(nameof(stream));

            // 保存原始位置以便计算结束后恢复
            long originalPosition = stream.Position;

            try
            {
                // 根据不同算法计算流的指定部分的哈希值
                switch (algorithm.ToUpper())
                {
                    case "SHA256":
                        using (SHA256 sha256 = SHA256.Create())
                        {
                            return ComputeHashWithLength(stream, sha256, length);
                        }

                    case "SHA512":
                        using (SHA512 sha512 = SHA512.Create())
                        {
                            return ComputeHashWithLength(stream, sha512, length);
                        }

                    case "BLAKE3":
                        if (length.HasValue)
                        {
                            // 使用临时缓冲区计算指定长度的哈希值
                            byte[] buffer = new byte[length.Value];
                            int bytesRead = stream.Read(buffer, 0, length.Value);
                            return Hasher.Hash(buffer).AsSpan().ToArray();
                        }
                        else
                        {
                            using var blake3Stream = new Blake3Stream(stream);
                            return blake3Stream.ComputeHash().AsSpan().ToArray();
                        }

                    case "MD5":
                        using (MD5 md5 = MD5.Create())
                        {
                            return ComputeHashWithLength(stream, md5, length);
                        }

                    case "SHA1":
                        using (SHA1 sha1 = SHA1.Create())
                        {
                            return ComputeHashWithLength(stream, sha1, length);
                        }

                    case "SHA3":
                    case "SHA384":
                        using (SHA384 sha384 = SHA384.Create())
                        {
                            return ComputeHashWithLength(stream, sha384, length);
                        }

                    case "XXH3":
                        {
                            var hasher = new XxHash3();
                            return ComputeXXHashWithLength(stream, hasher, length, 8);
                        }

                    case "XXH128":
                        {
                            var hasher = new XxHash128();
                            return ComputeXXHashWithLength(stream, hasher, length, 16);
                        }

                    default:
                        throw new ArgumentException("不支持的哈希算法", nameof(algorithm));
                }
            }
            finally
            {
                // 恢复流的原始位置
                stream.Position = originalPosition;
            }
        }

        /// <summary>
        /// 使用指定的哈希算法计算流中指定长度数据的哈希值
        /// </summary>
        private static byte[] ComputeHashWithLength(Stream stream, HashAlgorithm hashAlgorithm, int? length)
        {
            if (!length.HasValue)
            {
                // 如果未指定长度，计算整个流的哈希值
                return hashAlgorithm.ComputeHash(stream);
            }

            // 计算指定长度的哈希值
            int bytesRemaining = length.Value;
            byte[] buffer = new byte[Math.Min(81920, bytesRemaining)]; // 使用最多80KB的缓冲区

            hashAlgorithm.Initialize();

            while (bytesRemaining > 0)
            {
                int bytesToRead = Math.Min(buffer.Length, bytesRemaining);
                int bytesRead = stream.Read(buffer, 0, bytesToRead);

                if (bytesRead == 0)
                    break; // 流结束

                hashAlgorithm.TransformBlock(buffer, 0, bytesRead, buffer, 0);
                bytesRemaining -= bytesRead;
            }

            // 完成哈希计算
            hashAlgorithm.TransformFinalBlock(Array.Empty<byte>(), 0, 0);
            return hashAlgorithm.Hash;
        }

        /// <summary>
        /// 使用XXHash算法计算流中指定长度数据的哈希值
        /// </summary>
        private static byte[] ComputeXXHashWithLength<T>(Stream stream, T hasher, int? length, int resultSize) where T : NonCryptographicHashAlgorithm
        {
            int totalBytesRead = 0;
            int bytesToProcess = length ?? int.MaxValue;
            byte[] buffer = new byte[81920]; // 80KB缓冲区

            while (totalBytesRead < bytesToProcess)
            {
                int bytesToRead = Math.Min(buffer.Length, bytesToProcess - totalBytesRead);
                int bytesRead = stream.Read(buffer, 0, bytesToRead);

                if (bytesRead == 0)
                    break; // 流结束

                hasher.Append(buffer.AsSpan(0, bytesRead));
                totalBytesRead += bytesRead;
            }

            // 根据XXHash类型生成相应的哈希值
            byte[] result = new byte[resultSize];

            if (hasher is XxHash3 xxh3)
            {
                ulong hashValue = xxh3.GetCurrentHashAsUInt64();
                BinaryPrimitives.WriteUInt64BigEndian(result, hashValue);
            }
            else if (hasher is XxHash128 xxh128)
            {
                var hashValue = xxh128.GetCurrentHashAsUInt128();
                BinaryPrimitives.WriteUInt128BigEndian(result, hashValue);
            }

            return result;
        }

    }
}