namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 单个备份的所有设置。
    /// </summary>
    public interface IBackup
    {
        /// <summary>
        /// 备份ID。
        /// </summary>
        string ID { get; set; }

        /// <summary>
        /// 备份名称。
        /// </summary>
        string Name { get; set; }

        /// <summary>
        /// 备份描述。
        /// </summary>
        string Description { get; set; }

        /// <summary>
        /// 备份标签。
        /// </summary>
        string[] Tags { get; set; }

        /// <summary>
        /// 备份目标URL。
        /// </summary>
        string TargetURL { get; set; }

        /// <summary>
        /// 本地数据库路径。
        /// </summary>
        string DBPath { get; }

        /// <summary>
        /// 备份源文件夹和文件。
        /// </summary>
        string[] Sources { get; set; }

        /// <summary>
        /// 备份设置。
        /// </summary>
        ISetting[] Settings { get; set; }

        /// <summary>
        /// 应用于源文件的过滤器。
        /// </summary>
        IFilter[] Filters { get; set; }

        /// <summary>
        /// 备份元数据。
        /// </summary>
        IDictionary<string, string> Metadata { get; set; }

        /// <summary>
        /// 指示这个实例是否未持久化到数据库。
        /// </summary>
        bool IsTemporary { get; }

        // 清理目标URL。
        void SanitizeTargetUrl();

        // 清理设置。
        void SanitizeSettings();
    }
}
