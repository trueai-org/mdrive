namespace MDriveSync.Core
{
    /// <summary>
    /// 代表作业同步方式
    /// </summary>
    public enum JobMode
    {
        /// <summary>
        /// 镜像备份
        /// 以本地为主，如果远程和本地不一致则删除远程文件
        /// </summary>
        Mirror = 0,

        /// <summary>
        /// 冗余备份
        /// 以本地为主，将本地备份到远程，不删除远程文件
        /// </summary>
        Redundancy = 1,

        /// <summary>
        /// 双向同步
        /// 同时保留，冲突的文件重新命名
        /// </summary>
        TwoWaySync = 2,
    }
}