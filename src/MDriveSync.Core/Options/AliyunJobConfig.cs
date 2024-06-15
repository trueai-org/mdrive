namespace MDriveSync.Core
{
    /// <summary>
    /// 作业配置
    /// </summary>
    public class AliyunJobConfig : BaseJobConfig
    {
        public AliyunJobConfig GetClone()
        {
            return (AliyunJobConfig)this.MemberwiseClone();
        }

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
        /// 是否已挂载
        /// </summary>
        public bool IsMount { get; set; }

        /// <summary>
        /// 挂载点配置
        /// </summary>
        public AliyunDriveMountConfig MountConfig { get; set; }
    }

    /// <summary>
    /// 作业元信息（文件大小、数量、执行结果）
    /// </summary>
    public class JobMetadata
    {
        /// <summary>
        /// 总大小，单位bytes
        /// </summary>
        public long? TotalSize { get; set; }

        /// <summary>
        /// 文件数量
        /// </summary>
        public int FileCount { get; set; }

        /// <summary>
        /// 文件夹数量
        /// </summary>
        public int FolderCount { get; set; }

        /// <summary>
        /// 作业消息
        /// </summary>
        public string Message { get; set; }
    }
}