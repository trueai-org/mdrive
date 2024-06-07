using EasyCompressor;

namespace MDriveSync.Security
{
    /// <summary>
    /// 压缩解压函数
    /// </summary>
    public static class CompressionHelper
    {
        // 文件流缓冲区大小
        // 16 MB
        private const int StreamBufferSize = 16 * 1024 * 1024;

        // 数据流哈希值大小
        // SHA-256 | BLAKE3-256 hash size in bytes
        private const int StreamHashSize = 32;

        /// <summary>
        /// 使用指定的算法压缩数据，并在需要时进行加密
        /// </summary>
        /// <param name="buffer">要压缩的字节数组</param>
        /// <param name="compressionType">压缩算法类型（"LZ4"、"Zstd"或"Snappy"）</param>
        /// <param name="encryptionType">加密算法类型（"AES256-GCM"或"ChaCha20-Poly1305"），如果不需要加密则为null</param>
        /// <param name="encryptionKey">加密密钥，如果不需要加密则为null</param>
        /// <returns>压缩（并加密）后的字节数组</returns>
        public static byte[] Compress(byte[] buffer, string compressionType, string encryptionType = null, string encryptionKey = null)
        {
            // 压缩数据
            buffer = compressionType switch
            {
                "LZ4" => LZ4Compressor.Shared.Compress(buffer),
                "Zstd" => ZstdSharpCompressor.Shared.Compress(buffer),
                "Snappy" => SnappierCompressor.Shared.Compress(buffer),
                _ => buffer
            };

            // 如果提供了加密类型和密钥，则进行加密
            if (!string.IsNullOrEmpty(encryptionType) && !string.IsNullOrEmpty(encryptionKey))
            {
                buffer = encryptionType switch
                {
                    "AES256-GCM" => EncryptionHelper.EncryptWithAES256GCM(buffer, encryptionKey),
                    "ChaCha20-Poly1305" => EncryptionHelper.EncryptWithChaCha20Poly1305(buffer, encryptionKey),
                    _ => buffer
                };
            }

            return buffer;
        }

        /// <summary>
        /// 使用指定的算法解压数据，并在需要时进行解密
        /// </summary>
        /// <param name="buffer">要解压的字节数组</param>
        /// <param name="compressionType">压缩算法类型（"LZ4"、"Zstd"或"Snappy"）</param>
        /// <param name="encryptionType">加密算法类型（"AES256-GCM"或"ChaCha20-Poly1305"），如果不需要解密则为null</param>
        /// <param name="encryptionKey">加密密钥，如果不需要解密则为null</param>
        /// <returns>解压（并解密）后的字节数组</returns>
        public static byte[] Decompress(byte[] buffer, string compressionType, string encryptionType = null, string encryptionKey = null)
        {
            // 如果提供了加密类型和密钥，则进行解密
            if (!string.IsNullOrEmpty(encryptionType) && !string.IsNullOrEmpty(encryptionKey))
            {
                buffer = encryptionType switch
                {
                    "AES256-GCM" => EncryptionHelper.DecryptWithAES256GCM(buffer, encryptionKey),
                    "ChaCha20-Poly1305" => EncryptionHelper.DecryptWithChaCha20Poly1305(buffer, encryptionKey),
                    _ => buffer
                };
            }

            // 解压数据
            buffer = compressionType switch
            {
                "LZ4" => LZ4Compressor.Shared.Decompress(buffer),
                "Zstd" => ZstdSharpCompressor.Shared.Decompress(buffer),
                "Snappy" => SnappierCompressor.Shared.Decompress(buffer),
                _ => buffer
            };

            return buffer;
        }

        /// <summary>
        /// 压缩和加密输入流，并将结果写入输出流
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="compressionType"></param>
        /// <param name="encryptionType"></param>
        /// <param name="encryptionKey"></param>
        /// <param name="hashAlgorithm"></param>
        public static void CompressStream(
            Stream inputStream,
            Stream outputStream,
            string compressionType,
            string encryptionType = null,
            string encryptionKey = null,
            string hashAlgorithm = "SHA256")
        {
            byte[] buffer = new byte[StreamBufferSize];
            int bytesRead;
            while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                byte[] dataToCompress = new byte[bytesRead];
                Array.Copy(buffer, dataToCompress, bytesRead);
                var compressedData = Compress(dataToCompress, compressionType, encryptionType, encryptionKey);

                // 计算哈希值
                byte[] hash = HashHelper.ComputeHash(compressedData, hashAlgorithm);

                // 写入压缩和加密数据块的长度
                byte[] lengthBytes = BitConverter.GetBytes(compressedData.Length);
                outputStream.Write(lengthBytes, 0, lengthBytes.Length);

                // 写入压缩和加密的数据块
                outputStream.Write(compressedData, 0, compressedData.Length);

                // 写入哈希值
                outputStream.Write(hash, 0, hash.Length);
            }
        }

        /// <summary>
        /// 解密和解压输入流，并将结果写入输出流
        /// </summary>
        /// <param name="inputStream"></param>
        /// <param name="outputStream"></param>
        /// <param name="compressionType"></param>
        /// <param name="encryptionType"></param>
        /// <param name="encryptionKey"></param>
        /// <param name="hashAlgorithm"></param>
        /// <exception cref="InvalidOperationException"></exception>
        public static void DecompressStream(Stream inputStream,
            Stream outputStream,
            string compressionType,
            string encryptionType = null,
            string encryptionKey = null,
            string hashAlgorithm = "SHA256")
        {
            byte[] lengthBytes = new byte[4];
            byte[] hash = new byte[StreamHashSize];

            while (inputStream.Read(lengthBytes, 0, lengthBytes.Length) == lengthBytes.Length)
            {
                int blockLength = BitConverter.ToInt32(lengthBytes, 0);
                byte[] encryptedData = new byte[blockLength];

                int bytesRead = 0, totalBytesRead = 0;
                while (totalBytesRead < blockLength && (bytesRead = inputStream.Read(encryptedData, totalBytesRead, blockLength - totalBytesRead)) > 0)
                {
                    totalBytesRead += bytesRead;
                }

                if (totalBytesRead != blockLength)
                {
                    throw new InvalidOperationException("Unexpected end of stream.");
                }

                // 读取哈希值
                if (inputStream.Read(hash, 0, hash.Length) != hash.Length)
                {
                    throw new InvalidOperationException("Failed to read hash from stream.");
                }

                // 验证哈希值
                byte[] computedHash = HashHelper.ComputeHash(encryptedData, hashAlgorithm);
                if (!HashHelper.CompareHashes(hash, computedHash))
                {
                    throw new InvalidOperationException("Data integrity check failed.");
                }

                var decryptedData = Decompress(encryptedData, compressionType, encryptionType, encryptionKey);
                outputStream.Write(decryptedData, 0, decryptedData.Length);
            }
        }
    }
}