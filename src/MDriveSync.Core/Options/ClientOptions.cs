using MDriveSync.Core.DB;
using MDriveSync.Core.Options;

namespace MDriveSync.Core
{
    /// <summary>
    /// 客户端备份配置项
    /// </summary>
    public class ClientOptions
    {
        /// <summary>
        /// 阿里云盘作业配置
        /// </summary>
        public List<AliyunStorageConfig> AliyunDrives { get; set; } = new List<AliyunStorageConfig>();

        /// <summary>
        /// 本地存储作业配置
        /// </summary>
        public List<LocalStorageConfig> LocalStorages { get; set; } = new List<LocalStorageConfig>();

        /// <summary>
        /// 版本
        /// 每次发布版本时更新，用于检测是否有新版本
        /// </summary>
        public string Version { get; set; } = "v2.1.0";
    }


}