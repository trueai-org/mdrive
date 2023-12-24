namespace MDriveSync.Core
{
    /// <summary>
    /// 作业配置
    /// </summary>
    public class JobConfig
    {
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
        /// 作业级别
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
        /// 是否启用秒传功能
        /// 启用阿里云盘秒传
        /// </summary>
        public bool RapidUpload { get; set; } = true;

        /// <summary>
        /// 默认备份的云盘（资源库、备份盘）
        /// resource | backup
        /// </summary>
        public string DefaultDrive { get; set; } = "backup";

        /// <summary>
        /// 文件对比检查算法
        /// md5 | sha1 | sha256 | sha384 | sha512
        /// </summary>
        public string CheckAlgorithm { get; set; } = "sha256";

        /// <summary>
        /// 文件对比检查级别
        /// 0 比较大小和时间
        /// 1 对文件采样计算 hash
        /// 2 比较整个文件的 hash
        /// 3 比较文件头部 hash
        /// 4 比较文件尾部 hash
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
        public object Metadata { get; set; }
    }
}