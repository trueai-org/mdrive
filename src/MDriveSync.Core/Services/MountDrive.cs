using DokanNet;
using DokanNet.Logging;
using System.Collections.Concurrent;
using System.Security.AccessControl;

using FileAccess = DokanNet.FileAccess;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 挂载云盘
    /// </summary>
    public class MountDrive : IDokanOperations // , IDisposable
    {
        private Task _mountTask;
        private DokanInstance _dokanInstance;

        /// <summary>
        /// 云盘挂载点
        /// </summary>
        private readonly string _mountPoint;

        /// <summary>
        /// 所有云盘文件夹
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFolders;

        /// <summary>
        /// 所有云盘文件
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFiles;

        /// <summary>
        /// 当前作业
        /// </summary>
        public Job _job;

        public MountDrive(string mountPoint, Job job, ConcurrentDictionary<string, AliyunDriveFileItem> driveFolders, ConcurrentDictionary<string, AliyunDriveFileItem> driveFiles)
        {
            _mountPoint = mountPoint;
            _job = job;
            _driveFolders = driveFolders;
            _driveFiles = driveFiles;
        }

        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            // 清理文件操作
        }

        public void CloseFile(string fileName, IDokanFileInfo info)
        {
            // 关闭文件操作
        }

        // 例如，实现读文件操作
        public int ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            // 从云盘读取文件数据
            bytesRead = 0;
            //return DokanResult.Success;
            return 1;
        }

        // 实现写文件操作
        public int WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            // 向云盘写入文件数据
            bytesWritten = 0;
            //return DokanResult.Success;
            return 1;
        }

        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            // 当fileName为"\"时，表示访问的是根目录
            if (fileName == "\\")
            {
                // 检查根目录的访问权限等
                // 这里可以根据实际情况判断，比如是否允许访问根目录
                // 如果允许访问，返回Success
                return DokanResult.Success;
            }

            // 对于非根目录的文件或文件夹，根据mode处理不同的情况
            switch (mode)
            {
                case FileMode.CreateNew:
                    // 处理新建文件的逻辑
                    break;

                case FileMode.Open:
                    // 处理打开文件的逻辑
                    break;
                    // 其他模式的处理...
            }

            // 返回相应的状态码
            return DokanResult.Success;

            //// 创建或打开文件。在这里，您需要根据云盘的API添加逻辑
            //return DokanResult.Success;
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
        {
            // 列出文件夹内容。这里应根据云盘API获取文件列表
            // 从云盘获取文件列表并填充到files中
            files = new List<FileInformation>();

            return DokanResult.Success;
        }

        public NtStatus SetFileAttributes(
            string fileName,
            FileAttributes attributes,
            IDokanFileInfo info)
        {
            // 设置文件属性。在这里，您可以根据需要添加逻辑以支持云盘文件属性的修改
            return DokanResult.Success;
        }

        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            // 删除文件。在这里，您需要实现与云盘服务的文件删除逻辑
            return DokanResult.Success;
        }

        NtStatus IDokanOperations.ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            //throw new NotImplementedException();
            bytesRead = 0;

            return NtStatus.Success;
        }

        NtStatus IDokanOperations.WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            // 初始化fileInfo对象
            fileInfo = new FileInformation();

            if (fileName == "\\")
            {
                // 当访问的是根目录时
                fileInfo.FileName = fileName;
                fileInfo.Attributes = FileAttributes.Directory; // 根目录是一个文件夹
                fileInfo.CreationTime = DateTime.Now; // 可以设置为实际的创建时间
                fileInfo.LastAccessTime = DateTime.Now; // 最后访问时间
                fileInfo.LastWriteTime = DateTime.Now; // 最后写入时间
                fileInfo.Length = 0; // 对于目录，长度通常是0

                return DokanResult.Success;
            }

            //// 对于非根目录的文件或目录，您需要根据实际情况填充fileInfo
            //// 比如根据fileName在您的云盘中查找对应的文件或目录信息

            //return DokanResult.Error; // 如果文件或目录不存在，返回错误

            //throw new NotImplementedException();
            fileInfo = new FileInformation()
            {
                Length = 0,
                FileName = fileName,
                CreationTime = DateTime.Now,
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
            };
            return NtStatus.Success;
        }

        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
        {
            //throw new NotImplementedException();
            files = new List<FileInformation>();
            files.Add(new FileInformation()
            {
                FileName = fileName,
            });

            return NtStatus.Success;
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
        {
            //throw new NotImplementedException();

            return NtStatus.Success;
        }

        public NtStatus Unmounted(IDokanFileInfo info)
        {
            //throw new NotImplementedException();

            return NtStatus.Success;
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        //public TaskStatus GetMountTaskStatus()
        //{
        //    return _mountTask?.Status ?? TaskStatus.Canceled;
        //}

        //public void Dispose()
        //{
        //    //// 卸载 Dokan 文件系统
        //    //if (_dokanInstance != null)
        //    //{
        //    //    //Dokan.RemoveMountPoint(_mountPoint);
        //    //}

        //    //_dokanInstance?.Dispose();
        //    //_mountTask?.Dispose();
        //}

        #region 公共方法

        /// <summary>
        /// 挂载
        /// </summary>
        public async void Mount()
        {
            var dokanLogger = new ConsoleLogger("[Dokan] ");
            var dokanInstanceBuilder = new DokanInstanceBuilder(new Dokan(dokanLogger))
                .ConfigureOptions(options =>
                {
                    options.Options = DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI;
                    options.MountPoint = _mountPoint;
                });

            _dokanInstance = dokanInstanceBuilder.Build(this);

            //// 我的意思在这里管理 task // TODO
            //// 运行 Dokan 实例在新的任务中
            //await Task.Run(async () =>
            //{
            //    try
            //    {
            //        await _dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);
            //    }
            //    catch (DokanException ex)
            //    {
            //        // 处理 Dokan 相关的异常
            //    }
            //});

            await _dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);

            //_mountTask = Task.Run(async () =>
            //{
            //    try
            //    {
            //        await _dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);
            //    }
            //    catch (DokanException ex)
            //    {
            //        // 处理 Dokan 相关的异常
            //        dokanLogger.Error(ex.Message);
            //    }
            //});

            //if (_mountTask != null && _mountTask.Status == TaskStatus.Created)
            //{
            //    _mountTask.Start();
            //}
        }

        /// <summary>
        /// 卸载
        /// </summary>
        public void Unmount()
        {
            // 卸载 Dokan 文件系统
            if (_dokanInstance != null)
            {
                //Dokan.RemoveMountPoint(_mountPoint);
            }

            _dokanInstance?.Dispose();
        }

        #endregion 公共方法

        #region 已实现

        /// <summary>
        /// 获取磁盘的空间信息
        /// </summary>
        /// <param name="freeBytesAvailable"></param>
        /// <param name="totalNumberOfBytes"></param>
        /// <param name="totalNumberOfFreeBytes"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
        {
            // diskSpaceInfo.TotalSpace - 云盘的总空间
            // diskSpaceInfo.UsedSpace - 云盘已使用的空间
            // diskSpaceInfo.FreeSpace - 云盘的剩余空间

            totalNumberOfBytes = _job.CurrrentDrive?.Metadata?.TotalSize ?? long.MaxValue;
            totalNumberOfFreeBytes = _job.CurrrentDrive?.Metadata?.UsedSize ?? 0;
            freeBytesAvailable = totalNumberOfBytes > 0 ? totalNumberOfBytes - totalNumberOfFreeBytes : long.MaxValue;

            return NtStatus.Success;
        }

        /// <summary>
        /// 获取云盘信息
        /// </summary>
        /// <param name="volumeLabel"></param>
        /// <param name="features"></param>
        /// <param name="fileSystemName"></param>
        /// <param name="maximumComponentLength"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            // 设置卷标，这个标签可以根据您的需求自定义，例如"我的云盘"
            volumeLabel = _job.CurrrentJob?.Name ?? "我的云盘";

            // 设置文件系统的特性。这些特性描述了文件系统支持的不同功能。
            // 例如，可以设置为支持Unicode文件名、持久ACLs(访问控制列表)、大文件等
            features = FileSystemFeatures.UnicodeOnDisk |
                       FileSystemFeatures.SupportsRemoteStorage;

            // 设置文件系统名称，如NTFS、FAT32等。这里可以根据您实际的文件存储情况来设置
            // 由于是云盘系统，您可以设置为自定义的文件系统名称
            fileSystemName = "CloudFS";

            // 设置最大文件组件长度，这通常是文件系统中允许的最大文件名长度
            // 对于大多数现代文件系统，如NTFS，这个值通常是255
            maximumComponentLength = 255;

            // 返回操作成功的状态
            return DokanResult.Success;
        }

        #endregion 已实现
    }
}