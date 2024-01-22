using DokanNet;
using MDriveSync.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using ServiceStack;
using System.Security.AccessControl;

using FileAccess = DokanNet.FileAccess;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 阿里云盘挂载
    /// </summary>
    public partial class AliyunDriveMounter
    {
        /// <summary>
        /// 查找文件列表
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="searchPattern"></param>
        /// <returns></returns>
        private IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            var files = new List<FileInformation>();

            var parentId = _driveRootId;
            if (fileName != "\\")
            {
                var key = GetPathKey(fileName);
                if (_files.TryGetValue(key, out var p) && p != null)
                {
                    parentId = p.FileId;
                }
                else
                {
                    // 如果没有找到父级，则返回空
                    return files;
                }
            }

            // 加载文件
            var fs = _files.Values.Where(c => c.ParentFileId == parentId);
            foreach (var file in fs)
            {
                files.Add(new FileInformation()
                {
                    CreationTime = file.CreatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastAccessTime = file.UpdatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastWriteTime = file.UpdatedAt?.DateTime.ToLocalTime(),
                    FileName = file.Name ?? file.FileName,
                    Length = file.Size ?? 0,
                    Attributes = file.IsFolder ? FileAttributes.Directory : FileAttributes.Normal,
                });
            }

            // 注意过滤 searchPattern 否则可能导致创建文件夹显示不出来
            return files.Where(c => DokanHelper.DokanIsNameInExpression(searchPattern, c.FileName, true))
                .Select(c => new FileInformation
                {
                    Attributes = c.Attributes,
                    CreationTime = c.CreationTime,
                    LastAccessTime = c.LastAccessTime,
                    LastWriteTime = c.LastWriteTime,
                    Length = c.Length,
                    FileName = c.FileName
                }).ToList();
        }

        /// <summary>
        /// 写文件到本地
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="buffer"></param>
        /// <param name="bufferOffset"></param>
        /// <param name="writeSize"></param>
        /// <param name="fileOffset"></param>
        private void WriteToFile(string filePath, byte[] buffer, int bufferOffset, int writeSize, int fileOffset)
        {
            lock (_lock)
            {
                if (!Directory.Exists(Path.GetDirectoryName(filePath)))
                {
                    Directory.CreateDirectory(Path.GetDirectoryName(filePath));
                }
            }

            using (_lockV2.Lock(filePath))
            {
                using (var fileStream = new FileStream(filePath, FileMode.OpenOrCreate, System.IO.FileAccess.Write))
                {
                    fileStream.Position = fileOffset;
                    fileStream.Write(buffer, bufferOffset, writeSize);
                }
            }
        }

        #region 已实现

        /// <summary>
        /// 文件重命名或移动文件
        /// </summary>
        /// <param name="oldName"></param>
        /// <param name="newName"></param>
        /// <param name="replace"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus MoveFile(string oldName, string newName, bool replace, IDokanFileInfo info)
        {
            var oldpath = GetPathKey(oldName);
            var newpath = GetPathKey(newName);

            if (info.Context != null && info.Context is FileStream fs)
            {
                fs?.Dispose();
                info.Context = null;
            }

            var exist = _files.ContainsKey(newpath);

            try
            {
                if (!exist)
                {
                    info.Context = null;
                    if (info.IsDirectory)
                    {
                        return AliyunDriveMoveFolder(oldpath, newpath);
                    }
                    else
                    {
                        return AliyunDriveMoveFile(oldpath, newpath);
                    }
                }
                else if (replace)
                {
                    info.Context = null;

                    if (info.IsDirectory) //Cannot replace directory destination - See MOVEFILE_REPLACE_EXISTING
                        return DokanResult.AccessDenied;

                    return AliyunDriveMoveFile(oldpath, newpath, true);
                }
            }
            catch (UnauthorizedAccessException)
            {
                return DokanResult.AccessDenied;
            }

            // 如果重命名文件夹与当前目录文件夹存在一致，则不允许操作，否则可能会触发合并处理，但是合并在本方法中，不会被触发
            // 因此不提供支持
            return NtStatus.Error;

            //return DokanResult.FileExists;
        }

        /// <summary>
        ///
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="files"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus FindFiles(string fileName, out IList<FileInformation> files, IDokanFileInfo info)
        {
            files = FindFilesHelper(fileName, "*");
            return DokanResult.Success;
        }

        /// <summary>
        /// 创建/打开文件/文件夹
        /// </summary>
        /// <param name="fileName">文件名</param>
        /// <param name="access">访问权限</param>
        /// <param name="share">共享模式</param>
        /// <param name="mode">文件模式</param>
        /// <param name="options">文件选项</param>
        /// <param name="attributes">文件属性</param>
        /// <param name="info">文件信息</param>
        /// <returns>操作状态</returns>
        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            try
            {
                var key = GetPathKey(fileName);

                _files.TryGetValue(key, out var fi);

                var pathIsDirectory = fi?.IsFolder == true;
                var pathIsFile = fi?.IsFile == true;
                var pathExists = pathIsDirectory || pathIsFile;

                // 表示访问的是根目录
                if (fileName == "\\")
                {
                    // 检查根目录的访问权限等
                    // 这里可以根据实际情况判断，比如是否允许访问根目录
                    // 如果允许访问，返回Success
                    return DokanResult.Success;
                }

                if (info.IsDirectory)
                {
                    try
                    {
                        switch (mode)
                        {
                            case FileMode.Open:
                                {
                                    // 不再触发打开文件夹
                                    //OpenFolder(key);

                                    // 处理目录的创建或打开
                                    if (!pathIsDirectory)
                                    {
                                        //return DokanResult.PathNotFound;
                                        //return DokanResult.FileNotFound;
                                        return DokanResult.NotADirectory;
                                    }
                                }
                                break;

                            case FileMode.CreateNew:
                                {
                                    if (pathIsFile && pathIsFile)
                                    {
                                        return DokanResult.AlreadyExists;
                                    }

                                    if (!pathExists)
                                    {
                                        lock (_lock)
                                        {
                                            if (_files.ContainsKey(key))
                                            {
                                                return DokanResult.FileExists;
                                            }

                                            AliyunDriveCreateFolders(key);

                                            // 如果需要通知系统，注意：这里是包含挂载点的完整路径
                                            //Dokan.Notify.Create(_dokanInstance, @$"K:\{fileName}", isDirectory: true);
                                            //Dokan.Notify.Create(_dokanInstance, fileName, isDirectory: true);

                                            return NtStatus.Success;
                                        }
                                    }
                                    else
                                    {
                                        //return DokanResult.AlreadyExists;
                                        return DokanResult.FileExists;
                                    }
                                }
                        }
                    }
                    catch (UnauthorizedAccessException)
                    {
                        return DokanResult.AccessDenied;
                    }
                }
                else
                {
                    switch (mode)
                    {
                        case FileMode.Open:
                            if (pathExists)
                            {
                                if (pathIsDirectory)
                                {
                                    info.IsDirectory = pathIsDirectory;
                                    info.Context = new object();

                                    return DokanResult.Success;
                                }
                            }
                            else
                            {
                                return DokanResult.FileNotFound;
                            }
                            break;

                        case FileMode.CreateNew:
                            if (pathExists)
                                return DokanResult.FileExists;
                            break;

                        case FileMode.Truncate:
                            if (!pathExists)
                                return DokanResult.FileNotFound;
                            break;
                    }

                    try
                    {
                        if (mode == FileMode.CreateNew)
                        {
                            //var filePath = GetLocalPath(fileName);
                            //System.IO.FileAccess streamAccess = readAccess ? System.IO.FileAccess.Read : System.IO.FileAccess.ReadWrite;

                            //streamAccess = System.IO.FileAccess.ReadWrite;

                            //info.Context = new FileStream(filePath, FileMode.OpenOrCreate, streamAccess, share, 4096, options);

                            //if (pathExists && (mode == FileMode.OpenOrCreate || mode == FileMode.Create))
                            //{
                            //    //  DokanResult.AlreadyExists;

                            //}
                        };

                        //bool fileCreated = mode == FileMode.CreateNew || mode == FileMode.Create || (!pathExists && mode == FileMode.OpenOrCreate);
                        //if (fileCreated)
                        //{
                        //    FileAttributes new_attributes = attributes;
                        //    new_attributes |= FileAttributes.Archive; // Files are always created as Archive
                        //                                              // FILE_ATTRIBUTE_NORMAL is override if any other attribute is set.
                        //    new_attributes &= ~FileAttributes.Normal;

                        //    File.SetAttributes(filePath, new_attributes);
                        //}
                    }
                    catch (UnauthorizedAccessException) // don't have access rights
                    {
                        if (info.Context is FileStream fileStream)
                        {
                            // returning AccessDenied cleanup and close won't be called,
                            // so we have to take care of the stream now
                            fileStream.Dispose();
                            info.Context = null;
                        }
                        return DokanResult.AccessDenied;
                    }
                    catch (DirectoryNotFoundException)
                    {
                        return DokanResult.PathNotFound;
                    }
                    catch (Exception ex)
                    {
                        //var hr = (uint)Marshal.GetHRForException(ex);
                        //switch (hr)
                        //{
                        //    case 0x80070020: //Sharing violation
                        //        return DokanResult.SharingViolation;
                        //    default:
                        //        throw;
                        //}
                    }
                }

                // 实现完成后，根据操作结果返回相应的状态
                // 例如，如果操作成功，则返回 NtStatus.Success
                // 如果遇到错误，则返回相应的错误状态，如 NtStatus.AccessDenied 或 NtStatus.ObjectNameNotFound 等
                return NtStatus.Success;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "创建或打开文件出错");

                return NtStatus.Error;
            }
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

            var key = GetPathKey(fileName);
            if (!_files.ContainsKey(key))
            {
                return DokanResult.FileNotFound;
            }

            try
            {
                if (_files.TryGetValue(key, out var f) && f != null)
                {
                    //// 如果缓冲超过文件的本身大小
                    //if (buffer.Length >= f.Size)
                    //{
                    //    // 获取下载链接
                    //    var url = AliyunDriveGetDownloadUrl(f.FileId, f.ContentHash)?.Url;
                    //    if (string.IsNullOrWhiteSpace(url))
                    //    {
                    //        throw new Exception("获取下载链接失败");
                    //    }

                    //    int bytesToCopy = Math.Min(buffer.Length, (int)f.Size);
                    //    var partialContent = ReadFileContentAsync(url).GetAwaiter().GetResult();
                    //    Array.Copy(partialContent, 0, buffer, 0, bytesToCopy);
                    //    bytesRead = bytesToCopy;
                    //}
                    //else
                    //{
                    byte[] partialContent = [];

                    // 小于 64KB 的资源请求直接使用缓存
                    var isCached = false;
                    if (buffer.Length <= 1024 * 64)
                    {
                        partialContent = _cache.GetOrCreate($"{f.FileId}_{f.ContentHash}_{offset}_{buffer.Length}", c =>
                        {
                            c.SetSlidingExpiration(TimeSpan.FromSeconds(60 * 5));

                            // 获取下载链接
                            var url = AliyunDriveGetDownloadUrl(f.FileId, f.ContentHash)?.Url;
                            if (string.IsNullOrWhiteSpace(url))
                            {
                                throw new Exception("获取下载链接失败");
                            };
                            // 使用 Range 请求下载文件的特定部分
                            //int endOffset = (int)offset + buffer.Length - 1;
                            int endOffset = (int)Math.Min(offset + buffer.Length - 1, (int)f.Size - 1);
                            var content = DownloadFile(url, (int)offset, endOffset).GetAwaiter().GetResult();
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
                        var url = AliyunDriveGetDownloadUrl(f.FileId, f.ContentHash)?.Url;
                        if (string.IsNullOrWhiteSpace(url))
                        {
                            throw new Exception("获取下载链接失败");
                        }

                        //int endOffset = (int)offset + buffer.Length - 1;
                        int endOffset = (int)Math.Min(offset + buffer.Length - 1, (int)f.Size - 1);
                        partialContent = DownloadFile(url, (int)offset, endOffset).GetAwaiter().GetResult();
                    }

                    //if (fileName.Contains("jpg"))
                    //{
                    //}

                    // 确保不会复制超出 buffer 大小的数据
                    int bytesToCopy = Math.Min(buffer.Length, partialContent.Length);
                    Array.Copy(partialContent, 0, buffer, 0, bytesToCopy);
                    bytesRead = bytesToCopy;
                    //}
                }

                return DokanResult.Success;
            }
            catch (Exception ex)
            {
                _log.Error(ex, "读取文件异常");
                return DokanResult.Error;
            }
        }

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
            //if (_files.TryGetValue(key, out var f) && f != null)
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

            var key = GetPathKey(fileName);

            if (_files.TryGetValue(key, out var f) && f != null)
            {
                fileInfo = new FileInformation()
                {
                    CreationTime = f.CreatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastAccessTime = f.UpdatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastWriteTime = null,
                    FileName = fileName,
                    Length = f.Size ?? 0,
                    Attributes = f.IsFolder ? FileAttributes.Directory : FileAttributes.Normal,
                };
                return NtStatus.Success;
            }

            fileInfo = new FileInformation()
            {
                Length = 0,
                FileName = fileName,
                CreationTime = DateTime.Now,
                LastAccessTime = DateTime.Now,
                LastWriteTime = DateTime.Now,
                Attributes = info.IsDirectory ? FileAttributes.Directory : FileAttributes.Normal,
            };

            return DokanResult.Success;
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
            var key = GetPathKey(fileName);
            OpenFolder(key);

            files = FindFilesHelper(fileName, searchPattern);
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

            totalNumberOfBytes = _driveConfig?.Metadata?.TotalSize ?? long.MaxValue;

            // 如果显示真实使用量，则根据文件计算
            if (_isRealSize)
            {
                totalNumberOfFreeBytes = _files.Sum(x => x.Value.Size ?? 0);
                freeBytesAvailable = totalNumberOfBytes > 0 ? totalNumberOfBytes - totalNumberOfFreeBytes : long.MaxValue;

                _log.Information($"真实大小 {totalNumberOfFreeBytes}");
            }
            else
            {
                totalNumberOfFreeBytes = _driveConfig?.Metadata?.UsedSize ?? 0;
                freeBytesAvailable = totalNumberOfBytes > 0 ? totalNumberOfBytes - totalNumberOfFreeBytes : long.MaxValue;
            }

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
            volumeLabel = _driveConfig?.Name ?? "我的云盘";

            if (!string.IsNullOrWhiteSpace(_alias))
            {
                volumeLabel += $"（{_alias}）";
            }

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

        /// <summary>
        /// 清理文件操作
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="info"></param>
        public void Cleanup(string fileName, IDokanFileInfo info)
        {
            if (info.Context != null && info.Context is FileStream fs)
            {
                fs?.Dispose();
                info.Context = null;
            }

            if (info.DeleteOnClose)
            {
                if (fileName == "\\")
                {
                    return;
                }

                var key = GetPathKey(fileName);
                if (_files.TryGetValue(key, out var f) && f != null)
                {
                    if (f.IsFolder)
                    {
                        // 删除文件夹
                        AliyunDriveDeleteFolder(key);
                    }
                    else
                    {
                        // 删除文件
                        AliyunDriveDeleteFile(key);
                    }
                }

                //if (info.IsDirectory)
                //{
                //    Directory.Delete(GetPath(fileName));
                //}
                //else
                //{
                //    File.Delete(GetPath(fileName));
                //}
            }
        }

        /// <summary>
        /// 关闭文件操作
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="info"></param>
        public void CloseFile(string fileName, IDokanFileInfo info)
        {
            var key = GetPathKey(fileName);

            try
            {
                // 分块上传文件处理
                if (_uploadFileParts.TryGetValue(key, out var ps) && ps != null && ps.Count > 0)
                {
                    // 未上传的分块，执行上传
                    foreach (var item in ps)
                    {
                        if (!item.IsUploaded)
                        {
                            // 验证是否填充完整的数据
                            if (item.PartNumber < ps.Count && item.CurrentSize >= _uploadPartSize)
                            {
                                // 非最后一个分块
                                AliyunDrivePartUpload(item.LocalFilePath, item.UploadUrl).GetAwaiter().GetResult();
                                item.IsUploaded = true;
                            }
                            else if (item.PartNumber == ps.Count)
                            {
                                // 最后一个分块

                                // 计算最后一个分块的实际大小
                                int lastPartSize = (int)(item.TotalSize % _uploadPartSize);
                                if (lastPartSize == 0)
                                    lastPartSize = _uploadPartSize; // 如果文件大小是分块大小的整数倍

                                if (item.CurrentSize >= lastPartSize)
                                {
                                    // 分块上传
                                    // 最后一块上传
                                    AliyunDrivePartUpload(item.LocalFilePath, item.UploadUrl).GetAwaiter().GetResult();
                                    item.IsUploaded = true;
                                }
                            }
                        }
                    }

                    // 清理缓存
                    foreach (var item in ps)
                    {
                        if (File.Exists(item.LocalFilePath))
                        {
                            File.Delete(item.LocalFilePath);
                        }
                    }

                    if (ps.All(x => x.IsUploaded))
                    {
                        // 全部上传完成
                        // 标记为已完成
                        AliyunDriveUploadComplete(ps[0].FileId, ps[0].UploadId);
                    }
                }
            }
            finally
            {
                _uploadFileParts.TryRemove(key, out _);
            }

            if (info.Context != null && info.Context is FileStream fs)
            {
                fs?.Dispose();
                info.Context = null;
            }
        }

        /// <summary>
        /// 设置文件大小
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="length"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus SetEndOfFile(string fileName, long length, IDokanFileInfo info)
        {
            // 改变文件的大小。当文件被截断或扩展时，这个方法会被调用
            // 复制文件到云盘时也会触发。

            var key = GetPathKey(fileName);

            using (_lockV2.Lock($"upload:{key}"))
            {
                // 判断文件对应的文件夹是否存在，如果不存在则创建
                var keyPath = Path.GetDirectoryName(key).ToUrlPath();
                var saveParentFileId = "";
                if (string.IsNullOrWhiteSpace(keyPath))
                {
                    // 根目录
                    saveParentFileId = _driveRootId;
                }
                else
                {
                    if (!_files.TryGetValue(keyPath, out var f) || f == null)
                    {
                        using (_lockV2.Lock(keyPath))
                        {
                            if (!_files.TryGetValue(keyPath, out var f2) || f2 == null)
                            {
                                AliyunDriveCreateFolders(keyPath);
                            }
                        }
                    }
                    if (!_files.TryGetValue(keyPath, out var f3) || f3 == null)
                    {
                        _log.Error("创建文件夹失败 {@0}", keyPath);
                        return NtStatus.Error;
                    }

                    saveParentFileId = _files[keyPath].FileId;
                }

                // 分块数量
                var partsCount = (int)Math.Ceiling((double)length / _uploadPartSize);

                // 创建上传请求，并获取上传地址
                var data = AliyunDriveCreatePartUpload(fileName, length, saveParentFileId, partsCount);
                if (data == null || data.PartInfoList.Count != partsCount)
                {
                    _log.Error("创建文件上传请求失败 {@0}", keyPath);
                }

                var parts = new List<AliyunFileUploadPart>(partsCount);
                for (int i = 0; i < partsCount; i++)
                {
                    var tmpFile = Path.Combine(Directory.GetCurrentDirectory(), ".duplicatiuploadcache", $"{key}.{i}.duplicatipart");

                    // 如果存在临时文件，则删除
                    if (File.Exists(tmpFile))
                    {
                        File.Delete(tmpFile);
                    }

                    var uploadUrl = data.PartInfoList[i].UploadUrl;

                    parts.Add(new AliyunFileUploadPart(i + 1, tmpFile, uploadUrl)
                    {
                        FileId = data.FileId,
                        UploadId = data.UploadId,
                        TotalSize = length
                    });
                }

                _uploadFileParts[key] = parts;
            }

            return NtStatus.Success;
        }

        /// <summary>
        /// 写文件
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="buffer"></param>
        /// <param name="bytesWritten"></param>
        /// <param name="offset"></param>
        /// <param name="info"></param>
        /// <returns></returns>
        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            try
            {
                var key = GetPathKey(fileName);

                // 文件上传锁
                using (_lockV2.Lock($"upload:{key}"))
                {
                    var parts = _uploadFileParts[key];

                    int currentPartIndex = (int)(offset / _uploadPartSize);
                    int partOffset = (int)(offset % _uploadPartSize);
                    int bufferOffset = 0;
                    int remainingBuffer = buffer.Length;

                    while (remainingBuffer > 0 && currentPartIndex < parts.Count)
                    {
                        AliyunFileUploadPart currentPart = parts[currentPartIndex];
                        int writeSize = Math.Min(remainingBuffer, _uploadPartSize - partOffset);

                        // 写入到本地临时文件
                        WriteToFile(currentPart.LocalFilePath, buffer, bufferOffset, writeSize, partOffset);

                        currentPart.CurrentSize += writeSize;
                        bufferOffset += writeSize;
                        partOffset = 0; // 重置偏移量
                        remainingBuffer -= writeSize;

                        // 如果当前分块已填充内容完毕
                        if (currentPart.CurrentSize >= _uploadPartSize && !currentPart.IsUploaded)
                        {
                            // 分块上传
                            AliyunDrivePartUpload(currentPart.LocalFilePath, currentPart.UploadUrl).GetAwaiter().GetResult();
                            currentPart.IsUploaded = true;
                        }

                        // 如果是最后一个分块，并且填充完毕，则也执行上传
                        var isLastPart = currentPartIndex == parts.Count - 1;
                        if (isLastPart && !currentPart.IsUploaded)
                        {
                            // 计算最后一个分块的实际大小
                            int lastPartSize = (int)(currentPart.TotalSize % _uploadPartSize);
                            if (lastPartSize == 0)
                                lastPartSize = _uploadPartSize; // 如果文件大小是分块大小的整数倍

                            if (currentPart.CurrentSize >= lastPartSize)
                            {
                                // 分块上传
                                // 最后一块上传
                                AliyunDrivePartUpload(currentPart.LocalFilePath, currentPart.UploadUrl).GetAwaiter().GetResult();
                                currentPart.IsUploaded = true;
                            }
                        }

                        currentPartIndex++;
                    }

                    bytesWritten = buffer.Length - remainingBuffer;

                    // 写入内存
                    //int currentPartIndex = (int)(offset / partSize);
                    //int partOffset = (int)(offset % partSize);
                    //int remainingBuffer = buffer.Length;
                    //int bufferOffset = 0;

                    //if (parts != null)
                    //{
                    //    while (remainingBuffer > 0 && currentPartIndex < parts.Count)
                    //    {
                    //        FilePart currentPart = parts[currentPartIndex];
                    //        int copySize = Math.Min(remainingBuffer, partSize - partOffset);
                    //        Array.Copy(buffer, bufferOffset, currentPart.Data, partOffset, copySize);

                    //        currentPart.FilledSize += copySize;
                    //        bufferOffset += copySize;
                    //        partOffset = 0; // Reset for next part
                    //        remainingBuffer -= copySize;
                    //        currentPartIndex++;

                    //        if (currentPart.FilledSize == partSize && !currentPart.IsUploaded)
                    //        {
                    //            //UploadPart(fileName, currentPart);
                    //            // 我需要上传了
                    //            currentPart.IsUploaded = true;
                    //        }
                    //    }
                    //}

                    //bytesWritten = buffer.Length - remainingBuffer;

                    //var append = offset == -1;
                    //if (info.Context == null)
                    //{
                    //    var tmpFile = Path.Combine(Directory.GetCurrentDirectory(), ".duplicatiuploadcache", $"{key}.duplicatipart");
                    //    lock (_lock)
                    //    {
                    //        if (!Directory.Exists(Path.GetDirectoryName(tmpFile)))
                    //        {
                    //            Directory.CreateDirectory(Path.GetDirectoryName(tmpFile));
                    //        }
                    //    }

                    //    // 加锁
                    //    using (_lockV2.Lock(key))
                    //    {
                    //        //// 更新文件的写入长度
                    //        //if (!_fileWriteLengths.ContainsKey(key))
                    //        //{
                    //        //    _fileWriteLengths[key] = 0;

                    //        //    // 如果是首次写入，如果文件存在，则删除
                    //        //    if (File.Exists(tmpFile))
                    //        //    {
                    //        //        File.Delete(tmpFile);
                    //        //    }
                    //        //}

                    //        //using (var stream = new FileStream(tmpFile, append ? FileMode.Append : FileMode.OpenOrCreate, System.IO.FileAccess.Write))
                    //        //{
                    //        //    if (!append) // Offset of -1 is an APPEND: https://docs.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-writefile
                    //        //    {
                    //        //        stream.Position = offset;
                    //        //    }
                    //        //    var bytesToCopy = GetNumOfBytesToCopy(buffer.Length, offset, info, stream);
                    //        //    stream.Write(buffer, 0, bytesToCopy);
                    //        //    bytesWritten = bytesToCopy;

                    //        //    _fileWriteLengths[key] += bytesWritten;
                    //        //}

                    //        bytesWritten = buffer.Length;

                    //        _log.Error($"长度：{buffer.Length}");
                    //        // 分块上传
                    //        //AliyunDriveUploadFileUrl(fileName.TrimPath(), buffer, offset, _fileWriteLengths[key]).GetAwaiter().GetResult();
                    //    }
                    //}
                    //else
                    //{
                    //    // TODO
                    //    // 如果上下文存在文件流，待定

                    //    var stream = info.Context as FileStream;
                    //    lock (stream) //Protect from overlapped write
                    //    {
                    //        if (append)
                    //        {
                    //            if (stream.CanSeek)
                    //            {
                    //                stream.Seek(0, SeekOrigin.End);
                    //            }
                    //            else
                    //            {
                    //                bytesWritten = 0;
                    //                return DokanResult.Error;
                    //            }
                    //        }
                    //        else
                    //        {
                    //            stream.Position = offset;
                    //        }
                    //        var bytesToCopy = GetNumOfBytesToCopy(buffer.Length, offset, info, stream);
                    //        stream.Write(buffer, 0, bytesToCopy);
                    //        bytesWritten = bytesToCopy;
                    //    }
                    //}
                }
            }
            catch (Exception ex)
            {
                bytesWritten = 0;

                _log.Error(ex, "文件写入异常 {@0}", fileName);

                return DokanResult.Error;
            }

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

        public NtStatus DeleteFile(string fileName, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus DeleteDirectory(string fileName, IDokanFileInfo info)
        {
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

        public NtStatus FlushFileBuffers(string fileName, IDokanFileInfo info)
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

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        #endregion 无需实现
    }
}