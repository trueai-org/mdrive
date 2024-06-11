using EasyCompressor;
using Org.BouncyCastle.Asn1.Cms;
using System.Text;

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

        // 加密文件名的缓冲区大小
        // Buffer size for the encrypted file name
        private const int FileNameBufferSize = 1068;

        /// <summary>
        /// 使用指定的算法压缩数据，并在需要时进行加密
        /// </summary>
        /// <param name="buffer">要压缩的字节数组</param>
        /// <param name="compressionType">压缩算法类型（"LZ4"、"Zstd"或"Snappy"）</param>
        /// <param name="encryptionType">加密算法类型（"AES256-GCM"或"ChaCha20-Poly1305"），如果不需要加密则为null</param>
        /// <param name="encryptionKey">加密密钥，如果不需要加密则为null</param>
        /// <returns>压缩（并加密）后的字节数组</returns>
        public static byte[] Compress(byte[] buffer, string compressionType,
            string encryptionType = null, string encryptionKey = null, byte[] nonce = null)
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
                    "AES256-GCM" => EncryptionHelper.EncryptWithAES256GCM(buffer, encryptionKey, nonce),
                    "ChaCha20-Poly1305" => EncryptionHelper.EncryptWithChaCha20Poly1305(buffer, encryptionKey, nonce),
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
            string encryptionType,
            string encryptionKey,
            string hashAlgorithm,
            bool encryptFileName,
            string fileName)
        //, out string encryptFileHash
        {
            byte[] nonce = null;

            // 如果需要加密文件名
            if (encryptFileName && !string.IsNullOrWhiteSpace(fileName) && !string.IsNullOrWhiteSpace(encryptionType) && !string.IsNullOrEmpty(encryptionKey))
            {
                // 文件名仅加密，不需要压缩
                var fileBytes = Encoding.UTF8.GetBytes(fileName);

                // 使用文件名的 SHA256 哈希值作为 nonce，确保长度为 12 字节
                // 不需要多次计算
                nonce = HashHelper.ComputeHash(fileBytes, "SHA256").Take(12).ToArray();

                byte[] encryptedFileName = Compress(fileBytes, null, encryptionType, encryptionKey, nonce);
                if (encryptedFileName.Length > FileNameBufferSize)
                {
                    throw new InvalidOperationException("Encrypted file name is too long.");
                }

                // 写入文件名加密后的长度 [4]
                byte[] lengthBytes = BitConverter.GetBytes(encryptedFileName.Length);
                outputStream.Write(lengthBytes, 0, lengthBytes.Length);

                // 再写入文件名 [n <= 1068]
                //byte[] paddedFileName = new byte[FileNameBufferSize];
                //Array.Copy(encryptedFileName, paddedFileName, Math.Min(encryptedFileName.Length, FileNameBufferSize));
                //outputStream.Write(paddedFileName, 0, paddedFileName.Length);
                outputStream.Write(encryptedFileName, 0, encryptedFileName.Length);

                // 最后计算文件名哈希值 [32]
                byte[] fileNameHash = HashHelper.ComputeHash(encryptedFileName, hashAlgorithm);
                if (fileNameHash.Length != StreamHashSize)
                {
                    throw new InvalidOperationException("Invalid hash size.");
                }
                outputStream.Write(fileNameHash, 0, fileNameHash.Length);

                //// 计算文件名的 MD5 哈希值
                //// 此处使用 MD5 哈希值作为文件名的唯一标识，而不是加密后的文件名，因为加密后的文件名会有随机 IV 导致每次加密结果不同
                //encryptFileHash = HashHelper.ComputeHash(fileBytes, "MD5").ToHex();
            }

            byte[] buffer = new byte[StreamBufferSize];
            int bytesRead;
            while ((bytesRead = inputStream.Read(buffer, 0, buffer.Length)) > 0)
            {
                byte[] dataToCompress = new byte[bytesRead];
                Array.Copy(buffer, dataToCompress, bytesRead);

                var compressedData = Compress(
                    dataToCompress,
                    compressionType,
                    encryptionType,
                    encryptionKey,
                    nonce);

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
            string encryptionType,
            string encryptionKey,
            string hashAlgorithm,
            bool encryptFileName,
            out string fileName)
        {
            fileName = null;

            if (encryptFileName && !string.IsNullOrWhiteSpace(encryptionType) && !string.IsNullOrWhiteSpace(encryptionKey))
            {
                // 读取文件名长度
                byte[] fileLengthBytes = new byte[4];
                if (inputStream.Read(fileLengthBytes, 0, fileLengthBytes.Length) != fileLengthBytes.Length)
                {
                    throw new InvalidOperationException("Failed to read file name length.");
                }

                int fileNameLength = BitConverter.ToInt32(fileLengthBytes, 0);
                if (fileNameLength > FileNameBufferSize)
                {
                    throw new InvalidOperationException("Encrypted file name is too long.");
                }

                // 读取文件名
                byte[] fileNameBuffer = new byte[fileNameLength];
                if (inputStream.Read(fileNameBuffer, 0, fileNameBuffer.Length) != fileNameBuffer.Length)
                {
                    throw new InvalidOperationException("Failed to read encrypted file name.");
                }

                // 读取文件名哈希值
                byte[] fileNameHash = new byte[StreamHashSize];
                if (inputStream.Read(fileNameHash, 0, fileNameHash.Length) != fileNameHash.Length)
                {
                    throw new InvalidOperationException("Failed to read file name hash.");
                }

                byte[] computedFileNameHash = HashHelper.ComputeHash(fileNameBuffer, hashAlgorithm);
                if (!HashHelper.CompareHashes(fileNameHash, computedFileNameHash))
                {
                    throw new InvalidOperationException("File name hash check failed.");
                }

                var decBytes = Decompress(fileNameBuffer, null, encryptionType, encryptionKey);
                fileName = Encoding.UTF8.GetString(decBytes).TrimEnd('\0').Trim();
            }

            //if (encryptFileName && !string.IsNullOrWhiteSpace(encryptionType) && !string.IsNullOrWhiteSpace(encryptionKey))
            //{
            //    byte[] fileNameBuffer = new byte[FileNameBufferSize];
            //    byte[] fileNameHash = new byte[StreamHashSize];

            //    // 读取文件名部分
            //    if (inputStream.Read(fileNameBuffer, 0, fileNameBuffer.Length) == fileNameBuffer.Length)
            //    {
            //        // 读取文件名长度
            //        byte[] fileLengthBytes = new byte[4];
            //        if (inputStream.Read(fileLengthBytes, 0, fileLengthBytes.Length) == fileLengthBytes.Length)
            //        {
            //            int fileNameLength = BitConverter.ToInt32(fileLengthBytes, 0);
            //            if (fileNameLength > 1068)
            //            {
            //                throw new InvalidOperationException("Encrypted file name is too long.");
            //            }

            //            // 读取文件名哈希值
            //            if (inputStream.Read(fileNameHash, 0, fileNameHash.Length) == fileNameHash.Length)
            //            {
            //                // 计算真实的需要解密的文件名的 buffer
            //                byte[] fileNameBufferReal = new byte[fileNameLength];
            //                Array.Copy(fileNameBuffer, fileNameBufferReal, fileNameLength);

            //                byte[] computedFileNameHash = HashHelper.ComputeHash(fileNameBufferReal, hashAlgorithm);
            //                if (!HashHelper.CompareHashes(fileNameHash, computedFileNameHash))
            //                {
            //                    throw new InvalidOperationException("File name hash check failed.");
            //                }

            //                var decBytes = Decompress(fileNameBufferReal, null, encryptionType, encryptionKey);
            //                fileName = Encoding.UTF8.GetString(decBytes).TrimEnd('\0').Trim();
            //            }
            //        }
            //    }
            //}

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