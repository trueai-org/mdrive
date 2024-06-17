using DokanNet;
using DokanNet.Logging;
using Microsoft.Extensions.Caching.Memory;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.AccessControl;

using FileAccess = DokanNet.FileAccess;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 挂载云盘
    /// </summary>
    public class AliyunDriveMounterByJob : IDokanOperations // , IDisposable
    {
        private Task _mountTask;
        private DokanInstance _dokanInstance;
        private ManualResetEvent _mre = new(false);

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

        private readonly MemoryCache _cache = new MemoryCache(new MemoryCacheOptions());

        /// <summary>
        /// 当前作业
        /// </summary>
        public AliyunJob _job;

        private readonly HttpClient _httpClient = new();

        private async Task<byte[]> ReadFileContentAsync(string url)
        {
            return await _httpClient.GetByteArrayAsync(url);
        }

        private async Task<byte[]> DownloadFileSegment(string url, int start, int end)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, end);

            var response = await _httpClient.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }

        public AliyunDriveMounterByJob(string mountPoint, AliyunJob job, ConcurrentDictionary<string, AliyunDriveFileItem> driveFolders, ConcurrentDictionary<string, AliyunDriveFileItem> driveFiles)
        {
            _mountPoint = mountPoint;
            _job = job;
            _driveFolders = driveFolders;
            _driveFiles = driveFiles;
        }

        /// <summary>
        /// 创建文件/文件夹
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="access">访问权限</param>
        /// <param name="share">共享模式</param>
        /// <param name="mode">文件模式</param>
        /// <param name="options">文件选项</param>
        /// <param name="attributes">文件属性</param>
        /// <param name="info">文件信息</param>
        /// <returns>操作状态</returns>
        public NtStatus CreateFile(
            string fileName,
            FileAccess access,
            FileShare share,
            FileMode mode,
            FileOptions options,
            FileAttributes attributes,
            IDokanFileInfo info)
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

            // 根据文件名和模式确定操作类型
            switch (mode)
            {
                case FileMode.CreateNew:
                    // 在这里实现创建新文件的逻辑
                    {
                        // 处理目录的创建或打开
                        var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
                        if (!_driveFiles.ContainsKey(key))
                        {
                            Console.WriteLine("创建文件夹 " + fileName);
                        }
                    }
                    break;

                case FileMode.Create:
                    // 在这里实现创建文件的逻辑，如果文件存在则覆盖
                    break;

                case FileMode.Open:
                    // 在这里实现打开文件的逻辑，如果文件不存在则失败
                    break;

                case FileMode.OpenOrCreate:
                    // 在这里实现打开文件的逻辑，如果文件不存在则创建
                    break;

                case FileMode.Truncate:
                    // 在这里实现截断文件的逻辑
                    break;

                    // 其他模式的处理
            }

            // 一些特殊的文件操作可能需要单独处理
            if (info.IsDirectory)
            {
            }
            else
            {
                // 处理文件的创建或打开
            }

            // 实现完成后，根据操作结果返回相应的状态
            // 例如，如果操作成功，则返回 NtStatus.Success
            // 如果遇到错误，则返回相应的错误状态，如 NtStatus.AccessDenied 或 NtStatus.ObjectNameNotFound 等
            return NtStatus.Success;
        }

        /// <summary>
        /// 读文件
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="buffer"></param>
        /// <param name="bytesRead"></param>
        /// <param name="offset"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus ReadFile(string fileName, byte[] buffer, out int bytesRead, long offset, IDokanFileInfo info)
        {
            bytesRead = 0;

            if (fileName == "\\")
            {
                return NtStatus.Success;
            }

            var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
            if (!_driveFiles.ContainsKey(key))
            {
                return DokanResult.FileNotFound;
            }

            try
            {
                if (_driveFiles.TryGetValue(key, out var f) && f != null)
                {
                    byte[] partialContent = [];

                    // 小于 64KB 的资源请求直接使用缓存
                    var isCached = false;
                    if (buffer.Length <= 1024 * 64)
                    {
                        partialContent = _cache.GetOrCreate($"{f.FileId}_{f.ContentHash}_{offset}_{buffer.Length}", c =>
                        {
                            c.SetSlidingExpiration(TimeSpan.FromSeconds(60 * 5));

                            // 获取下载链接
                            var url = _job.AliyunDriveGetDownloadUrl(f.FileId, f.ContentHash)?.Url;
                            if (string.IsNullOrWhiteSpace(url))
                            {
                                throw new Exception("获取下载链接失败");
                            };
                            // 使用 Range 请求下载文件的特定部分
                            int endOffset = (int)offset + buffer.Length - 1;
                            var content = DownloadFileSegment(url, (int)offset, endOffset).GetAwaiter().GetResult();
                            return content;
                        });
                        isCached = true;

                        //Console.WriteLine($"{fileName}, cache, {offset}, {buffer.Length}");
                    }
                    else
                    {
                        //Console.WriteLine($"{fileName}, nocache");
                    }

                    // 从云盘中读取文件数据

                    //var fileContent = ReadFileContentAsync(url).GetAwaiter().GetResult();
                    //int toRead = Math.Min(buffer.Length, fileContent.Length - (int)offset);
                    //Array.Copy(fileContent, offset, buffer, 0, toRead);
                    //bytesRead = toRead;

                    //// 使用 Range 请求下载文件的特定部分
                    //int endOffset = (int)offset + buffer.Length - 1;
                    //var partialContent = DownloadFileSegment(url, (int)offset, endOffset).GetAwaiter().GetResult();

                    //Array.Copy(partialContent, 0, buffer, 0, partialContent.Length);
                    //bytesRead = partialContent.Length;

                    // 使用 Range 请求下载文件的特定部分
                    if (!isCached)
                    {
                        // 获取下载链接
                        var url = _job.AliyunDriveGetDownloadUrl(f.FileId, f.ContentHash)?.Url;
                        if (string.IsNullOrWhiteSpace(url))
                        {
                            throw new Exception("获取下载链接失败");
                        }

                        int endOffset = (int)offset + buffer.Length - 1;
                        partialContent = DownloadFileSegment(url, (int)offset, endOffset).GetAwaiter().GetResult();
                    }

                    // 确保不会复制超出 buffer 大小的数据
                    int bytesToCopy = Math.Min(buffer.Length, partialContent.Length);
                    Array.Copy(partialContent, 0, buffer, 0, bytesToCopy);
                    bytesRead = bytesToCopy;
                }

                return DokanResult.Success;
            }
            catch (Exception ex)
            {
                
                // 处理异常情况
                return DokanResult.Error;
            }
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
        public void Mount()
        {
            var dokanLogger = new ConsoleLogger("[Dokan] ");
            var dokanInstanceBuilder = new DokanInstanceBuilder(new Dokan(dokanLogger))
                .ConfigureOptions(options =>
                {
                    // DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI;

                    options.Options = DokanOptions.NetworkDrive | DokanOptions.DebugMode | DokanOptions.StderrOutput;

                    options.MountPoint = _mountPoint;
                });

            //_dokanInstance = dokanInstanceBuilder.Build(this);

            _mountTask = new Task(() =>
            {
                using var dokanInstance = dokanInstanceBuilder.Build(this);
                _mre.WaitOne();
            });
            _mountTask.Start();

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

            //await _dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);

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
            _mre.Set();
            _dokanInstance?.Dispose();
        }

        #endregion 公共方法

        #region 已实现

        /// <summary>
        /// 文件安全策略
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="security"></param>
        /// <param name="sections"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus GetFileSecurity(string fileName, out FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            security = null;

            // 如果您的文件系统不支持安全性和访问控制，您可以直接返回 NtStatus.NotImplemented
            // return NtStatus.NotImplemented;

            // 如果您想提供基本的安全性设置，您可以创建一个新的 FileSystemSecurity 对象
            // 下面是为文件或目录创建一个基本的安全描述符的示例

            if (info.IsDirectory)
            {
                security = new DirectorySecurity();
            }
            else
            {
                security = new FileSecurity();
            }

            if (fileName == "\\")
            {
                return NtStatus.Success;
            }

            //var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
            //if (_driveFiles.TryGetValue(key, out var f) && f != null)
            //{
            //    // 设置安全性和访问控制
            //    // 此处应根据您的需求和文件系统的特点设置安全性和访问控制列表（ACL）
            //    // 例如，您可以设置允许所有用户读取文件的权限
            //    //var everyone = new SecurityIdentifier(WellKnownSidType.WorldSid, null);
            //    //security.AddAccessRule(new FileSystemAccessRule(everyone., FileSystemRights.Read, AccessControlType.Allow));
            //}

            return DokanResult.Success;
        }

        /// <summary>
        /// 获取文件信息
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="fileInfo"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus GetFileInformation(string fileName, out FileInformation fileInfo, IDokanFileInfo info)
        {
            fileInfo = new FileInformation() { FileName = fileName };

            if (fileName == "\\")
            {
                // 当访问的是根目录时
                fileInfo.FileName = fileName;
                fileInfo.Attributes = FileAttributes.Directory; // 根目录是一个文件夹
                fileInfo.CreationTime = DateTime.Now; // 可以设置为实际的创建时间
                fileInfo.LastAccessTime = null; // 最后访问时间
                fileInfo.LastWriteTime = null; // 最后写入时间
                fileInfo.Length = 0; // 对于目录，长度通常是0

                return DokanResult.Success;
            }

            // 对于非根目录的文件或目录，您需要根据实际情况填充fileInfo
            // 比如根据fileName在您的云盘中查找对应的文件或目录信息

            var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();

            // info.IsDirectory 不准确，因此不适用
            if (_driveFolders.TryGetValue(key, out var d) && d != null)
            {
                fileInfo = new FileInformation()
                {
                    CreationTime = d.CreatedAt.Value.DateTime.ToLocalTime(),
                    LastAccessTime = d.UpdatedAt.Value.DateTime.ToLocalTime(),
                    LastWriteTime = d.UpdatedAt.Value.DateTime.ToLocalTime(),
                    FileName = d.Name ?? d.FileName,
                    Length = d.Size ?? 0,
                    Attributes = FileAttributes.Directory,
                };
                return NtStatus.Success;
            }

            if (_driveFiles.TryGetValue(key, out var file) && file != null)
            {
                fileInfo = new FileInformation()
                {
                    CreationTime = file.CreatedAt.Value.DateTime.ToLocalTime(),
                    LastAccessTime = file.UpdatedAt.Value.DateTime.ToLocalTime(),
                    LastWriteTime = file.UpdatedAt.Value.DateTime.ToLocalTime(),
                    FileName = file.Name ?? file.FileName,
                    Length = file.Size ?? 0,
                    Attributes = FileAttributes.Normal,
                };
                return NtStatus.Success;
            }

            fileInfo = new FileInformation()
            {
                Length = 0,
                FileName = fileName,
                CreationTime = DateTime.Now,
                LastAccessTime = null,
                LastWriteTime = null,
                Attributes = info.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            };

            return NtStatus.Success;
        }

        /// <summary>
        /// 文件列表
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="searchPattern"></param>
        /// <param name="files"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus FindFilesWithPattern(string fileName, string searchPattern, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = new List<FileInformation>();

            var parentKey = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
            if (_driveFolders.TryGetValue(parentKey, out var p) && p != null)
            {
                // 加载文件夹
                var dirs = _driveFolders.Values.Where(c => c.ParentFileId == p.FileId);
                foreach (var d in dirs)
                {
                    files.Add(new FileInformation()
                    {
                        CreationTime = d.CreatedAt.Value.DateTime.ToLocalTime(),
                        LastAccessTime = d.UpdatedAt.Value.DateTime.ToLocalTime(),
                        LastWriteTime = null,
                        FileName = d.Name ?? d.FileName,
                        Length = d.Size ?? 0,
                        Attributes = FileAttributes.Directory,
                    });
                }

                // 加载文件
                var fs = _driveFiles.Values.Where(c => c.ParentFileId == p.FileId);
                foreach (var file in fs)
                {
                    files.Add(new FileInformation()
                    {
                        CreationTime = file.CreatedAt.Value.DateTime.ToLocalTime(),
                        LastAccessTime = file.UpdatedAt.Value.DateTime.ToLocalTime(),
                        LastWriteTime = file.UpdatedAt.Value.DateTime.ToLocalTime(),
                        FileName = file.Name ?? file.FileName,
                        Length = file.Size ?? 0,
                        Attributes = FileAttributes.Normal,
                    });
                }
            }

            // 根目录
            if (fileName != "\\")
            {
            }

            return NtStatus.Success;
        }

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

            totalNumberOfBytes = _job.CurrrentStorageConfig?.Metadata?.TotalSize ?? long.MaxValue;
            totalNumberOfFreeBytes = _job.CurrrentStorageConfig?.Metadata?.UsedSize ?? 0;
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
            maximumComponentLength = 256;

            // 返回操作成功的状态
            return DokanResult.Success;
        }

        #endregion 已实现

        #region 无需实现

        public NtStatus Mounted(string mountPoint, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus Unmounted(IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        #endregion 无需实现

        #region 暂不实现

        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            // 改变文件的大小。当文件被截断或扩展时，这个方法会被调用
            return NtStatus.Success;
        }

        public NtStatus SetAllocationSize(string fileName, long length, IDokanFileInfo info)
        {
            // 用于预先分配文件空间。这不一定改变文件的大小，但它保留了文件可能需要的空间。
            // 这个方法通常用于性能优化，因为它可以减少文件增长时所需的磁盘空间重新分配的次数。
            return NtStatus.Success;
        }

        public NtStatus FindStreams(string fileName, out IList<FileInformation> streams, IDokanFileInfo info)
        {
            streams = new List<FileInformation>();

            // 如果您的文件系统不支持备用数据流，可以直接返回 Success
            // 这表示该文件没有备用数据流
            return DokanResult.Success;
        }

        public NtStatus SetFileSecurity(string fileName, FileSystemSecurity security, AccessControlSections sections, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            bytesWritten = 0;
            return NtStatus.Success;
        }

        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus LockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus UnlockFile(string fileName, long offset, long length, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = new List<FileInformation>();
            return DokanResult.Success;
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
            var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
            if (_driveFolders.TryGetValue(key, out var p) && p != null)
            {
            }

            return NtStatus.Success;
        }

        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            // 清理文件操作
        }

        public void CloseFile(string fileName, IDokanFileInfo info)
        {
            // 关闭文件操作
        }

        #endregion 暂不实现
    }
}