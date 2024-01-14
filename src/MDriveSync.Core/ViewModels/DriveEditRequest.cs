namespace MDriveSync.Core.ViewModels
{
    /// <summary>
    /// 云盘编辑请求
    /// </summary>
    public class DriveEditRequest : RefreshTokenRequest
    {
        /// <summary>
        /// 挂载点
        /// 空的驱动器盘符
        /// 指定挂载点
        /// </summary>
        public string MountPoint { get; set; }

        /// <summary>
        /// 自动挂载
        /// 启动时挂载磁盘
        /// </summary>
        public bool MountOnStartup { get; set; }

        /// <summary>
        /// 挂载的云盘路径
        /// 可以单独挂在云盘的某个路径，或挂载整个云盘
        /// </summary>
        public string MountPath { get; set; }

        /// <summary>
        /// 是否以只读的方式挂载云盘
        /// </summary>
        public bool MountReadOnly { get; set; }

        /// <summary>
        /// 默认挂载的云盘（资源库、备份盘）
        /// resource | backup
        /// </summary>
        public string MountDrive { get; set; } = "backup";

        /// <summary>
        /// 挂载的云盘是否启用秒传功能
        /// 是否启用秒传功能
        /// 启用阿里云盘秒传
        /// </summary>
        public bool RapidUpload { get; set; } = false;

        /// <summary>
        /// 是否启用回收站，如果启用则删除文件时，保留到回收站
        /// </summary>
        public bool IsRecycleBin { get; set; } = false;
    }
}