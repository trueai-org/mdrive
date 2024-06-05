using ServiceStack.DataAnnotations;

namespace MDriveSync.Security.Models
{
    /// <summary>
    /// 块信息
    /// </summary>
    public class Blockset : IBaseId
    {
        /// <summary>
        /// 块 ID
        /// </summary>
        [PrimaryKey]
        [AutoIncrement]
        public int Id { get; set; }

        /// <summary>
        /// 文件 ID，对应 Fileset.Id
        /// </summary>
        [Index]
        public int FilesetId { get; set; }

        /// <summary>
        /// 块索引，从 0 开始
        /// </summary>
        public int Index { get; set; }

        /// <summary>
        /// 块大小（块加密压缩后）
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 块 hash（块加密压缩前 hash）
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// 数据的起始位置（块加密压缩后）
        /// </summary>
        public long StartIndex { get; set; }

        /// <summary>
        /// 数据的结束位置（块加密压缩后）
        /// </summary>
        public long EndIndex { get; set; }

        /// <summary>
        /// 加密算法（AES256-GCM、CHACHA20-POLY1305）
        /// 用于加密存储在备份仓库中的数据块的算法
        /// 默认：AES256-GCM
        /// </summary>
        public string EncryptionAlgorithm { get; set; }

        /// <summary>
        /// 哈希算法（SHA256、BLAKE3）
        /// 用于生成数据块或文件的哈希值，以验证数据的完整性和唯一性
        /// 默认：SHA256
        /// </summary>
        public string HashAlgorithm { get; set; }

        /// <summary>
        /// 内部压缩（Zstd、LZ4、Snappy）
        /// 在将数据存储到仓库之前，数据是否进行压缩
        /// 默认：Zstd
        /// </summary>
        public string InternalCompression { get; set; }
    }
}