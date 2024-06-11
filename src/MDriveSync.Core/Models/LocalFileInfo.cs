using MDriveSync.Core.DB;
using ServiceStack.DataAnnotations;

namespace MDriveSync.Core
{
    /// <summary>
    /// 本地文件信息
    /// </summary>
    public class LocalFileInfo : IBaseKey<string>
    {
        public LocalFileInfo()
        {

        }

        /// <summary>
        /// 本地完整路径
        /// </summary>
        public string FullPath { get; set; }

        /// <summary>
        /// 相对路径
        /// 不包含 / 前缀
        /// </summary>
        [PrimaryKey]
        public string Key { get; set; }

        /// <summary>
        /// 文件 key 对应的路径
        /// </summary>
        public string KeyPath { get; set; }

        /// <summary>
        /// 是否为文件
        /// </summary>
        public bool IsFile { get; set; }

        /// <summary>
        /// 文件名称
        /// </summary>
        [Ignore]
        public string Name
        {
            get
            {
                if (IsEncrypt && IsEncryptName && !string.IsNullOrWhiteSpace(EncryptFileName))
                {
                    return EncryptFileName;
                }

                return LocalFileName;
            }
        }

        /// <summary>
        /// 文件大小（字节数）
        /// </summary>
        public long Length { get; set; }

        /// <summary>
        /// 获取或设置文件的创建时间
        /// </summary>
        public DateTime CreationTime { get; set; }

        /// <summary>
        /// 获取或设置上次写入文件的时间
        /// </summary>
        public DateTime LastWriteTime { get; set; }

        /// <summary>
        /// 文件对比 hash 值
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// 文件内容 sha1 值
        /// </summary>
        public string Sha1 { get; set; }

        /// <summary>
        /// 是否已同步
        /// </summary>
        public bool IsSync { get; set; }

        /// <summary>
        /// 云文件ID
        /// </summary>
        public string AliyunFileId { get; set; }

        /// <summary>
        /// 云文件名
        /// </summary>
        public string AliyunName { get; set; }

        /// <summary>
        /// 云文件内容哈希
        /// </summary>
        public string AliyunContentHash { get; set; }

        /// <summary>
        /// 文件加密
        /// </summary>
        public bool IsEncrypt { get; set; }

        /// <summary>
        /// 文件名加密
        /// </summary>
        public bool IsEncryptName { get; set; }

        /// <summary>
        /// 本地文件名称
        /// 1.txt
        /// </summary>
        public string LocalFileName { get; set; }

        /// <summary>
        /// 已加密的文件名称
        /// ***.e
        /// </summary>
        public string EncryptFileName { get; set; }

        /// <summary>
        /// 已加密的文件名称的对应的相对路径 key
        /// xxx/xxx/xxx ***.e
        /// </summary>
        [Ignore]
        public string EncryptKey
        {
            get
            {
                if (IsEncrypt && IsEncryptName && !string.IsNullOrWhiteSpace(EncryptFileName))
                {
                    return $"{KeyPath.TrimPath()}/{EncryptFileName}"?.TrimPath();
                }

                return Key;
            }
        }
    }
}