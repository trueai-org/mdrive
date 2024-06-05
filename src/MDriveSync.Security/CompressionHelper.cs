using EasyCompressor;

namespace MDriveSync.Security
{
    /// <summary>
    /// 压缩解压函数
    /// </summary>
    public static class CompressionHelper
    {
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
    }
}