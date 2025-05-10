namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 扫描结果类
    /// </summary>
    public class FileScanResult
    {
        /// <summary>
        /// 扫描的根路径
        /// </summary>
        public string RootPath { get; set; }

        /// <summary>
        /// 文件数量
        /// </summary>
        public int FileCount { get; set; }

        /// <summary>
        /// 目录数量
        /// </summary>
        public int DirectoryCount { get; set; }

        /// <summary>
        /// 文件列表
        /// </summary>
        public List<string> Files { get; set; }

        /// <summary>
        /// 目录列表
        /// </summary>
        public List<string> Directories { get; set; }

        /// <summary>
        /// 错误列表
        /// </summary>
        public List<string> Errors { get; set; }

        /// <summary>
        /// 扫描耗时
        /// </summary>
        public TimeSpan ElapsedTime { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 扫描是否被取消
        /// </summary>
        public bool WasCancelled { get; set; }

        /// <summary>
        /// 每秒处理项目数
        /// </summary>
        public double ItemsPerSecond => ElapsedTime.TotalSeconds > 0
            ? (FileCount + DirectoryCount) / ElapsedTime.TotalSeconds
            : 0;
    }
}