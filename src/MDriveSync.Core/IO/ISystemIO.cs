namespace MDriveSync.Core.IO
{
    /// <summary>
    /// 用于封装System.IO操作的接口。
    /// </summary>
    public interface ISystemIO
    {
        // 获取目录条目
        IFileEntry DirectoryEntry(string path);

        // 创建目录
        void DirectoryCreate(string path);

        // 删除目录
        void DirectoryDelete(string path, bool recursive);

        // 检查目录是否存在
        bool DirectoryExists(string path);

        // 移动目录
        void DirectoryMove(string sourceDirName, string destDirName);

        // 设置目录的最后写入时间（UTC）
        void DirectorySetLastWriteTimeUtc(string path, DateTime time);

        // 设置目录的创建时间（UTC）
        void DirectorySetCreationTimeUtc(string path, DateTime time);

        // 获取文件条目
        IFileEntry FileEntry(string path);

        // 移动文件
        void FileMove(string source, string target);

        // 删除文件
        void FileDelete(string path);

        // 复制文件
        void FileCopy(string source, string target, bool overwrite);

        // 设置文件的最后写入时间（UTC）
        void FileSetLastWriteTimeUtc(string path, DateTime time);

        // 设置文件的创建时间（UTC）
        void FileSetCreationTimeUtc(string path, DateTime time);

        // 获取文件的最后写入时间（UTC）
        DateTime FileGetLastWriteTimeUtc(string path);

        // 获取文件的创建时间（UTC）
        DateTime FileGetCreationTimeUtc(string path);

        // 检查文件是否存在
        bool FileExists(string path);

        // 获取文件长度
        long FileLength(string path);

        // 打开文件进行读取
        FileStream FileOpenRead(string path);

        // 打开文件进行写入
        FileStream FileOpenWrite(string path);

        // 创建文件
        FileStream FileCreate(string path);

        // 获取文件属性
        FileAttributes GetFileAttributes(string path);

        // 设置文件属性
        void SetFileAttributes(string path, FileAttributes attributes);

        // 创建符号链接
        void CreateSymlink(string symlinkfile, string target, bool asDir);

        // 获取符号链接的目标
        string GetSymlinkTarget(string path);

        // 获取目录名称
        string PathGetDirectoryName(string path);

        // 获取文件名
        string PathGetFileName(string path);

        // 获取文件扩展名
        string PathGetExtension(string path);

        // 更改文件扩展名
        string PathChangeExtension(string path, string extension);

        // 组合路径
        string PathCombine(params string[] paths);

        // 获取完整路径
        string PathGetFullPath(string path);

        // 获取路径的根部分
        string GetPathRoot(string path);

        // 获取指定路径下的所有目录
        string[] GetDirectories(string path);

        // 获取指定路径下的所有文件
        string[] GetFiles(string path);

        // 使用搜索模式获取指定路径下的所有文件
        string[] GetFiles(string path, string searchPattern);

        // 获取文件的创建时间（UTC）
        DateTime GetCreationTimeUtc(string path);

        // 获取文件的最后写入时间（UTC）
        DateTime GetLastWriteTimeUtc(string path);

        // 枚举文件系统条目
        IEnumerable<string> EnumerateFileSystemEntries(string path);

        // 枚举文件
        IEnumerable<string> EnumerateFiles(string path);

        // 使用搜索模式和搜索选项枚举文件
        IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);

        // 枚举目录
        IEnumerable<string> EnumerateDirectories(string path);

        // 设置元数据
        void SetMetadata(string path, Dictionary<string, string> metdata, bool restorePermissions);

        // 获取元数据
        Dictionary<string, string> GetMetadata(string path, bool isSymlink, bool followSymlink);
    }
}
