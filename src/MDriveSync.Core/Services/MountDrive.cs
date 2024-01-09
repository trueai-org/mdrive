using DokanNet;
using DokanNet.Logging;
using System.Security.AccessControl;
using FileAccess = DokanNet.FileAccess;

namespace MDriveSync.Core.Services
{
    // 实现Dokan的IDokanOperations接口
    public class MountDrive : IDokanOperations, IDisposable
    {
        private readonly string _mountPoint;
        private DokanInstance _dokanInstance;
        private Task _mountTask;

        public MountDrive(string mountPoint)
        {
            _mountPoint = mountPoint;
        }

        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            // 清理文件操作
        }

        public void CloseFile(string fileName, IDokanFileInfo info)
        {
            // 关闭文件操作
        }

        // 其他必须实现的方法...

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
            // 创建或打开文件。在这里，您需要根据云盘的API添加逻辑
            return DokanResult.Success;
        }

        public NtStatus FindFiles(
            string fileName,
            out IList<FileInformation> files,
            IDokanFileInfo info)
        {
            // 列出文件夹内容。这里应根据云盘API获取文件列表
            files = new List<FileInformation>();
            // 从云盘获取文件列表并填充到files中
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

        public NtStatus GetDiskFreeSpace(out long freeBytesAvailable, out long totalNumberOfBytes, out long totalNumberOfFreeBytes, IDokanFileInfo info)
        {
            //throw new NotImplementedException();

            freeBytesAvailable = 100; totalNumberOfBytes = 100;
            totalNumberOfFreeBytes = 100;

            return NtStatus.Success;
        }

        public NtStatus GetVolumeInformation(out string volumeLabel, out FileSystemFeatures features, out string fileSystemName, out uint maximumComponentLength, IDokanFileInfo info)
        {
            //throw new NotImplementedException();

            volumeLabel = "a";
            features = FileSystemFeatures.None;
            fileSystemName = "b";
            maximumComponentLength = 0;
            return NtStatus.Success;
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
            throw new NotImplementedException();
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
        {
            throw new NotImplementedException();
        }

        // 其他方法...

        public void Mount()
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

            _mountTask = Task.Run(async () =>
            {
                try
                {
                    await _dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);
                }
                catch (DokanException ex)
                {
                    // 处理 Dokan 相关的异常
                    dokanLogger.Error(ex.Message);
                }
            });

            if (_mountTask != null && _mountTask.Status == TaskStatus.Created)
            {
                _mountTask.Start();
            }
        }

        public void Unmount()
        {
            // 卸载 Dokan 文件系统
            if (_dokanInstance != null)
            {
                //Dokan.RemoveMountPoint(_mountPoint);

                // TODO
                //Dokan.RemoveMountPoint(_mountPoint);
            }

            _dokanInstance?.Dispose();
        }

        public TaskStatus GetMountTaskStatus()
        {
            return _mountTask?.Status ?? TaskStatus.Canceled;
        }

        public void Dispose()
        {
            // 卸载 Dokan 文件系统
            if (_dokanInstance != null)
            {
                //Dokan.RemoveMountPoint(_mountPoint);

                // TODO
                //Dokan.RemoveMountPoint(_mountPoint);
            }

            _dokanInstance?.Dispose();
            _mountTask?.Dispose();
        }
    }
}