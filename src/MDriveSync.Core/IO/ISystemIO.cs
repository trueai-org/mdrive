namespace MDriveSync.Core.IO
{
    /// <summary>
    /// Interface for wrapping System.IO operations.
    /// </summary>
    public interface ISystemIO
    {
        IFileEntry DirectoryEntry(string path);

        void DirectoryCreate(string path);

        void DirectoryDelete(string path, bool recursive);

        bool DirectoryExists(string path);

        void DirectoryMove(string sourceDirName, string destDirName);

        void DirectorySetLastWriteTimeUtc(string path, DateTime time);

        void DirectorySetCreationTimeUtc(string path, DateTime time);

        IFileEntry FileEntry(string path);

        void FileMove(string source, string target);

        void FileDelete(string path);

        void FileCopy(string source, string target, bool overwrite);

        void FileSetLastWriteTimeUtc(string path, DateTime time);

        void FileSetCreationTimeUtc(string path, DateTime time);

        DateTime FileGetLastWriteTimeUtc(string path);

        DateTime FileGetCreationTimeUtc(string path);

        bool FileExists(string path);

        long FileLength(string path);

        FileStream FileOpenRead(string path);

        FileStream FileOpenWrite(string path);

        FileStream FileCreate(string path);

        FileAttributes GetFileAttributes(string path);

        void SetFileAttributes(string path, FileAttributes attributes);

        void CreateSymlink(string symlinkfile, string target, bool asDir);

        string GetSymlinkTarget(string path);

        string PathGetDirectoryName(string path);

        string PathGetFileName(string path);

        string PathGetExtension(string path);

        string PathChangeExtension(string path, string extension);

        string PathCombine(params string[] paths);

        string PathGetFullPath(string path);

        string GetPathRoot(string path);

        string[] GetDirectories(string path);

        string[] GetFiles(string path);

        string[] GetFiles(string path, string searchPattern);

        DateTime GetCreationTimeUtc(string path);

        DateTime GetLastWriteTimeUtc(string path);

        IEnumerable<string> EnumerateFileSystemEntries(string path);

        IEnumerable<string> EnumerateFiles(string path);

        IEnumerable<string> EnumerateFiles(string path, string searchPattern, SearchOption searchOption);

        IEnumerable<string> EnumerateDirectories(string path);

        void SetMetadata(string path, Dictionary<string, string> metdata, bool restorePermissions);

        Dictionary<string, string> GetMetadata(string path, bool isSymlink, bool followSymlink);
    }
}