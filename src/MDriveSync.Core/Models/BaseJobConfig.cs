namespace MDriveSync.Core.Models
{
    /// <summary>
    /// 作业配置
    /// </summary>
    public class BaseJobConfig
    {
        public T GetClone<T>() where T : BaseJobConfig
        {
            return (T)MemberwiseClone();
        }

        /// <summary>
        /// 任务 ID
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// 任务/作业名称
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// 作业/任务描述
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// 作业状态
        /// </summary>
        public JobState State { get; set; } = JobState.Idle;

        /// <summary>
        /// 同步模式
        /// </summary>
        public JobMode Mode { get; set; } = JobMode.Mirror;

        /// <summary>
        /// 作业计划
        /// 定时计划
        /// 执行间隔
        /// </summary>
        public List<string> Schedules { get; set; } = new List<string>();

        /// <summary>
        /// 过滤文件/文件夹
        /// </summary>
        public List<string> Filters { get; set; } = new List<string>();

        /// <summary>
        /// 源目录
        /// 本地备份目录 []
        /// </summary>
        public List<string> Sources { get; set; } = new List<string>();

        /// <summary>
        /// 目标存储目录
        /// 远程存储目录
        /// </summary>
        public string Target { get; set; }

        /// <summary>
        /// 还原目录
        /// 当需要还原到本地时，默认还原到本地工作目录/本地文件夹
        /// </summary>
        public string Restore { get; set; }

        /// <summary>
        /// 文件对比检查算法
        /// md5 | sha1 | sha256 | sha384 | sha512
        /// </summary>
        public string CheckAlgorithm { get; set; } = "sha256";

        /// <summary>
        /// 文件对比检查级别
        /// 1 对文件采样计算 hash
        /// 2 比较整个文件的 hash
        /// </summary>
        public int CheckLevel { get; set; } = 1;

        /// <summary>
        /// 启用文件系统监听
        /// 启用监听可以更加快捷的计算需要同步的文件
        /// </summary>
        public bool FileWatcher { get; set; } = true;

        /// <summary>
        /// 显示顺序
        /// </summary>
        public int Order { get; set; }

        /// <summary>
        /// 是否为临时任务/暂时的/一次性的
        /// </summary>
        public bool IsTemporary { get; set; } = false;

        /// <summary>
        /// 是否启用回收站，如果启用则删除文件时，保留到回收站
        /// </summary>
        public bool IsRecycleBin { get; set; } = false;

        /// <summary>
        /// 上传并行任务数（0：自动，最大：10）
        /// </summary>
        public int UploadThread { get; set; } = 0;

        /// <summary>
        /// 下载并行任务数（0：自动，最大：10）
        /// </summary>
        public int DownloadThread { get; set; } = 0;

        /// <summary>
        /// 作业元信息（文件大小、数量、执行结果）
        /// </summary>
        public JobMetadata Metadata { get; set; }

        /// <summary>
        /// 启用加密
        /// </summary>
        public bool IsEncrypt { get; set; }

        /// <summary>
        /// 启用文件名加密（与文件打包模式互斥，因为文件打包时文件名都存储到包中了）
        /// </summary>
        public bool IsEncryptName { get; set; }

        /// <summary>
        /// 启用文件打包
        /// </summary>
        public bool IsPack { get; set; }

        /// <summary>
        /// Hash 算法（SHA256、BLAKE3）
        /// </summary>
        public string HashAlgorithm { get; set; }

        /// <summary>
        /// 加密算法（AES256-GCM、ChaCha20-Poly1305）
        /// </summary>
        public string EncryptAlgorithm { get; set; }

        /// <summary>
        /// 加密密钥
        /// </summary>
        public string EncryptKey { get; set; }

        /// <summary>
        /// 压缩算法（Zstd、LZ4、Snappy）
        /// </summary>
        public string CompressAlgorithm { get; set; }
    }
}