using FastMember;
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
        /// 相对路径
        /// 不包含 / 前缀
        /// </summary>
        [PrimaryKey]
        public string Key { get; set; }

        /// <summary>
        /// 本地完整路径
        /// </summary>
        public string FullPath { get; set; }

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
                if (IsEncrypt && !string.IsNullOrWhiteSpace(EncryptFileName))
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
        /// 是否隐藏
        /// </summary>
        public bool IsHidden { get; set; }

        /// <summary>
        /// 是否只读
        /// </summary>
        public bool IsReadOnly { get; set; }

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
                if (IsEncrypt && !string.IsNullOrWhiteSpace(EncryptFileName))
                {
                    return $"{KeyPath.TrimPath()}/{EncryptFileName}"?.TrimPath();
                }

                return Key;
            }
        }
    }

    /// <summary>
    /// 本地存储文件基本信息
    /// </summary>
    public class LocalStorageFileInfo : IBaseKey<string>
    {
        private static readonly TypeAccessor _accessor = TypeAccessor.Create(typeof(LocalStorageFileInfo));
        private static readonly MemberSet _members = _accessor.GetMembers();

        public LocalStorageFileInfo()
        {

        }

        /// <summary>
        /// 快速比较 2 个对象
        /// 比反射更快，每秒 3600万+
        /// </summary>
        /// <param name="obj1"></param>
        /// <param name="obj2"></param>
        /// <returns></returns>
        public static bool FastAreObjectsEqual(LocalStorageFileInfo obj1, LocalStorageFileInfo obj2, params string[] ignoreMembers)
        {
            if (ReferenceEquals(obj1, obj2))
                return true;

            if (obj1 == null || obj2 == null)
                return false;

            foreach (var member in _members)
            {
                // 默认忽略 FullName
                if (member.Name == nameof(FullName))
                {
                    continue;
                }

                // 忽略指定字段
                if (ignoreMembers != null && ignoreMembers.Length > 0 && ignoreMembers.Contains(member.Name))
                {
                    continue;
                }

                if (!Equals(_accessor[obj1, member.Name], _accessor[obj2, member.Name]))
                {
                    return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 相对路径 key
        /// {xxx}/{xxx}.xx
        /// </summary>
        [PrimaryKey]
        public string Key { get; set; }

        /// <summary>
        /// 包含文件名的完整路径
        /// </summary>
        [Index]
        public string FullName { get; set; }

        /// <summary>
        /// 文件名
        /// </summary>
        public string Name { get; set; }

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
        /// 文件 Hash 值（本地文件 hash）
        /// 说明：扫描本地文件时，不计算 Hash 值，只有在同步时才计算
        /// </summary>
        public string Hash { get; set; }

        /// <summary>
        /// 是否为文件
        /// </summary>
        public bool IsFile { get; set; }

        /// <summary>
        /// 是否隐藏
        /// </summary>
        public bool IsHidden { get; set; }

        /// <summary>
        /// 是否只读
        /// </summary>
        public bool IsReadOnly { get; set; }

        /// <summary>
        /// 是否存在
        /// </summary>
        public bool IsExists { get; set; }
    }

    /// <summary>
    /// 本地存储目标文件信息
    /// </summary>
    public class LocalStorageTargetFileInfo : LocalStorageFileInfo
    {
        public LocalStorageTargetFileInfo() : base()
        {
        }

        /// <summary>
        /// 本地文件原始 Hash（未加密前）
        /// </summary>
        public string LocalFileHash { get; set; }

        /// <summary>
        /// 本地文件原始名称（未加密前的）
        /// 1.txt
        /// </summary>
        public string LocalFileName { get; set; }

        /// <summary>
        /// 本地文件的 Key
        /// </summary>
        [Index]
        public string LocalFileKey { get; set; }
    }
}