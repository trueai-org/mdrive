namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 代表一个文件系统后端视角下的文件或文件夹实例的接口。
    /// </summary>
    public interface IFileEntry
    {
        /// <summary>
        /// 如果条目代表一个文件夹，则为true；否则为false。
        /// </summary>
        bool IsFolder { get; }

        /// <summary>
        /// 文件或文件夹最后一次被访问的时间。
        /// </summary>
        DateTime LastAccess { get; }

        /// <summary>
        /// 文件或文件夹最后一次被修改的时间。
        /// </summary>
        DateTime LastModification { get; }

        /// <summary>
        /// 文件或文件夹的名称。
        /// </summary>
        string Name { get; }

        /// <summary>
        /// 文件或文件夹的大小。
        /// </summary>
        long Size { get; }
    }
}
