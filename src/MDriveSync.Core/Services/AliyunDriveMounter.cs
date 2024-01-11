using DokanNet;
using DokanNet.Logging;
using Microsoft.Extensions.Caching.Memory;
using Serilog;
using System.Collections.Concurrent;
using System.Net.Http.Headers;
using System.Security.AccessControl;

using FileAccess = DokanNet.FileAccess;
using ILogger = Serilog.ILogger;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 阿里云盘挂载
    /// </summary>
    public class AliyunDriveMounter : IDokanOperations, IDisposable
    {
        private readonly ILogger _log;
        private readonly object _lock = new object();

        /// <summary>
        /// 异步锁/资源锁
        /// </summary>
        private AsyncLockV2 _lockV2 = new();

        private Task _mountTask;
        private DokanInstance _dokanInstance;
        private ManualResetEvent _mre = new(false);

        /// <summary>
        /// 本地缓存
        /// 令牌缓存、下载链接缓存等
        /// </summary>
        private readonly IMemoryCache _cache;

        /// <summary>
        /// 令牌标识
        /// </summary>
        private const string TOEKN_KEY = "TOKEN";

        /// <summary>
        /// 所有云盘文件
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFiles = new();

        /// <summary>
        /// 所有云盘文件夹
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFolders = new();

        /// <summary>
        /// 客户端信息
        /// </summary>
        private AliyunDriveConfig _driveConfig;

        /// <summary>
        /// 阿里云盘接口
        /// </summary>
        private readonly AliyunDriveApi _driveApi;

        /// <summary>
        /// 备份盘/资源盘 ID
        /// </summary>
        private string _driveId;

        /// <summary>
        /// 挂载文件目录的父级 ID
        /// 默认为根目录
        /// </summary>
        private string _driveParentFileId = "root";

        /// <summary>
        /// 下载请求
        /// </summary>
        private readonly HttpClient _downloadHttp = new()
        {
            Timeout = TimeSpan.FromMinutes(45)
        };

        public AliyunDriveMounter(AliyunDriveConfig driveConfig)
        {
            _log = Log.Logger;

            _driveApi = new AliyunDriveApi();

            _cache = new MemoryCache(new MemoryCacheOptions());

            _driveConfig = driveConfig;

            AliyunDriveInitialize();
        }


        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="url"></param>
        /// <returns></returns>
        private async Task<byte[]> ReadFileContentAsync(string url)
        {
            return await _downloadHttp.GetByteArrayAsync(url);
        }

        /// <summary>
        /// 下载文件
        /// </summary>
        /// <param name="url"></param>
        /// <param name="start"></param>
        /// <param name="end"></param>
        /// <returns></returns>
        private async Task<byte[]> DownloadFileSegment(string url, int start, int end)
        {
            var request = new HttpRequestMessage(HttpMethod.Get, url);
            request.Headers.Range = new RangeHeaderValue(start, end);

            var response = await _downloadHttp.SendAsync(request);
            response.EnsureSuccessStatusCode();

            return await response.Content.ReadAsByteArrayAsync();
        }

        /// <summary>
        /// 获取文件或文件夹 key
        /// </summary>
        /// <param name="fileName"></param>
        /// <returns></returns>
        private string GetPathKey(string fileName)
        {
            return $"{_driveConfig.MountPath.TrimPath()}/{fileName.TrimPath()}".ToUrlPath();
        }

        /// <summary>
        /// 获取当前有效的访问令牌
        /// </summary>
        private string AccessToken
        {
            get
            {
                return _cache.GetOrCreate(TOEKN_KEY, c =>
                {
                    var token = InitToken();

                    var secs = _driveConfig.ExpiresIn;
                    if (secs <= 300 || secs > 7200)
                    {
                        secs = 7200;
                    }

                    // 提前 5 分钟过期
                    c.SetAbsoluteExpiration(TimeSpan.FromSeconds(secs - 60 * 5));

                    return token;
                });
            }
        }

        /// <summary>
        /// 初始化令牌
        /// </summary>
        /// <returns></returns>
        private string InitToken()
        {
            // 重新获取令牌
            var data = ProviderApiHelper.RefreshToken(_driveConfig.RefreshToken);
            if (data != null)
            {
                _driveConfig.TokenType = data.TokenType;
                _driveConfig.AccessToken = data.AccessToken;
                _driveConfig.RefreshToken = data.RefreshToken;
                _driveConfig.ExpiresIn = data.ExpiresIn;
                _driveConfig.Save();

                return _driveConfig.AccessToken;
            }

            throw new Exception("初始化访问令牌失败");
        }

        /// <summary>
        /// 阿里云盘 - 初始化作业（路径、云盘信息等）
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitialize()
        {
            LocalLock.TryLock("init_drive_lock", TimeSpan.FromSeconds(60), () =>
            {
                _log.Information("云盘挂载初始化中");

                // 获取云盘信息
                AliyunDriveInitInfo();

                // 空间信息
                AliyunDriveInitSpaceInfo();

                // VIP 信息
                AliyunDriveInitVipInfo();

                // 保存云盘信息
                _driveConfig.MountPath = _driveConfig.MountPath.TrimPath();

                // TODO
                _driveConfig.MountOnStartup = true;
                _driveConfig.MountPoint = "K:\\";

                _driveConfig.Save();

                _log.Information("云盘挂载初始化完成");
            });
        }

        /// <summary>
        /// 阿里云盘 - 获取用户 drive 信息
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitInfo()
        {
            var data = _driveApi.DriveInfo(AccessToken);

            _driveId = data.DefaultDriveId;

            if (_driveConfig.MountDrive == "backup" && string.IsNullOrWhiteSpace(data.BackupDriveId))
            {
                _driveId = data.BackupDriveId;
            }
            else if (_driveConfig.MountDrive == "resource" && !string.IsNullOrWhiteSpace(data.ResourceDriveId))
            {
                _driveId = data.ResourceDriveId;
            }

            _driveConfig.Name = !string.IsNullOrWhiteSpace(data.NickName) ? data.NickName : data.Name;
        }

        /// <summary>
        /// 阿里云盘 - 获取用户空间信息
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitSpaceInfo()
        {
            var data = _driveApi.SpaceInfo(AccessToken);

            _driveConfig.Metadata ??= new();
            _driveConfig.Metadata.UsedSize = data?.PersonalSpaceInfo?.UsedSize;
            _driveConfig.Metadata.TotalSize = data?.PersonalSpaceInfo?.TotalSize;
        }

        /// <summary>
        /// 阿里云盘 - 获取用户 VIP 信息
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitVipInfo()
        {
            var data = _driveApi.VipInfo(AccessToken);
            _driveConfig.Metadata ??= new();
            _driveConfig.Metadata.Identity = data?.Identity;
            _driveConfig.Metadata.Level = data?.Level;
            _driveConfig.Metadata.Expire = data?.ExpireDateTime;
        }

        /// <summary>
        /// 初始化文件列表
        /// </summary>
        public void AliyunDriveInitFiles()
        {
            try
            {
                _log.Information("云盘挂载验证");

                // 计算根目录的父级 parent id
                // 首先加载根目录结构
                // 并计算需要保存的目录
                // 计算/创建备份文件夹
                // 如果备份文件夹不存在
                if (!string.IsNullOrWhiteSpace(_driveConfig.MountPath))
                {
                    var saveRootSubPaths = _driveConfig.MountPath.ToSubPaths();
                    var searchParentFileId = "root";
                    foreach (var subPath in saveRootSubPaths)
                    {
                        var subItem = _driveApi.GetSubFolders(_driveId, searchParentFileId, subPath, AccessToken);
                        var okPath = subItem.Items.FirstOrDefault(x => x.Name == subPath && x.Type == "folder" && x.ParentFileId == searchParentFileId);
                        if (okPath == null)
                        {
                            // 未找到目录
                            // 创建子目录
                            var name = AliyunDriveHelper.EncodeFileName(subPath);
                            var data = _driveApi.CreateFolder(_driveId, searchParentFileId, name, AccessToken);
                            searchParentFileId = data.FileId;
                        }
                        else
                        {
                            searchParentFileId = okPath.FileId;
                        }
                    }
                    _driveParentFileId = searchParentFileId;
                }

                AliyunDriveSearchFiles();
            }
            catch (Exception ex)
            {
                _log.Error(ex, "初始化文件列表异常");
            }
        }

        /// <summary>
        /// 阿里云盘 - 搜索文件
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        private void AliyunDriveSearchFiles(int limit = 100)
        {
            _log.Information("初始化云盘文件列表");

            var allItems = new List<AliyunDriveFileItem>();
            var marker = "";
            do
            {
                var data = _driveApi.SearchAllFileList(_driveId, limit, marker, AccessToken);

                if (data?.Items.Count > 0)
                {
                    allItems.AddRange(data.Items);
                }
                marker = data.NextMarker;
            } while (!string.IsNullOrEmpty(marker));

            // 先加载文件夹
            LoadPath(_driveParentFileId);
            void LoadPath(string parentFileId)
            {
                foreach (var item in allItems.Where(c => c.IsFolder).Where(c => c.ParentFileId == parentFileId))
                {
                    // 如果是文件夹，则递归获取子文件列表
                    if (item.IsFolder)
                    {
                        var keyPath = "";

                        // 如果相对是根目录
                        if (item.ParentFileId == _driveParentFileId)
                        {
                            keyPath = $"{item.Name}".TrimPath();
                        }
                        else
                        {
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == item.ParentFileId).First()!;
                            keyPath = $"{parent.Key}/{item.Name}".TrimPath();
                        }

                        _driveFolders.TryAdd(keyPath, item);

                        // 加载子文件夹
                        LoadPath(item.FileId);
                    }
                }
            }

            // 加载文件列表
            foreach (var item in allItems.Where(c => c.IsFile))
            {
                if (item.IsFile)
                {
                    // 如果相对是根目录文件
                    if (item.ParentFileId == _driveParentFileId)
                    {
                        _driveFiles.TryAdd($"{item.Name}".TrimPath(), item);
                    }
                    else
                    {
                        // 文件必须在备份路径中
                        var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == item.ParentFileId).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(parent.Key))
                        {
                            _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        }
                    }
                }
            }

            _log.Information($"云盘文件加载完成，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹。");
        }

        /// <summary>
        /// 阿里云盘 - 获取文件下载 URL
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        public AliyunDriveOpenFileGetDownloadUrlResponse AliyunDriveGetDownloadUrl(string fileId, string hash = "")
        {
            // 没有 hash 时从远程获取
            if (string.IsNullOrWhiteSpace(hash))
            {
                return _driveApi.GetDownloadUrl(_driveId, fileId, AccessToken);
            }

            // hash 不一样时，重新获取下载链接
            return _cache.GetOrCreate($"download_{fileId}_{hash}", (c) =>
            {
                var res = _driveApi.GetDownloadUrl(_driveId, fileId, AccessToken);

                c.SetSlidingExpiration(TimeSpan.FromSeconds(600));
                c.SetAbsoluteExpiration(TimeSpan.FromSeconds(14400 - 600));

                return res;
            });
        }

        /// <summary>
        /// 阿里云盘 - 创建文件夹（同名不创建）
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="parentId"></param>
        /// <returns></returns>
        private string AliyunDriveCreateFolder(string filePath, string parentId)
        {
            // 同一级文件夹共用一个锁
            using (_lockV2.Lock($"create_folder_{parentId}"))
            {
                var name = AliyunDriveHelper.EncodeFileName(filePath);

                // 判断是否需要创建文件夹
                if (parentId == "root")
                {
                    // 如果是根目录
                    var path = $"{name}".TrimPath();
                    if (_driveFolders.ContainsKey(path))
                    {
                        return _driveFolders[path].FileId;
                    }
                }
                else
                {
                    // 如果是子目录
                    var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == parentId).First()!;
                    var path = $"{parent.Key}/{name}".TrimPath();
                    if (_driveFolders.ContainsKey(path))
                    {
                        return _driveFolders[path].FileId;
                    }
                }

                //var data = new AliyunDriveFileItem()
                //{
                //    FileId = Guid.NewGuid().ToString(),
                //    Type = "folder",
                //    ParentFileId = parentId,
                //    FileName = name,
                //    Name = name,
                //};

                var data = _driveApi.CreateFolder(_driveId, parentId, name, AccessToken);
                data.Name = data.FileName;

                if (parentId == _driveParentFileId)
                {
                    // 当前目录在根路径
                    // /{当前路径}/
                    _driveFolders.TryAdd($"{data.Name}".TrimPath(), data);
                }
                else
                {
                    // 计算父级路径
                    var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == parentId).First()!;
                    var path = $"{parent.Key}/{data.Name}".TrimPath();

                    // /{父级路径}/{当前路径}/
                    _driveFolders.TryAdd(path, data);
                }

                return data.FileId;
            }
        }

        /// <summary>
        /// 创建云盘文件夹路径
        /// </summary>
        /// <param name="pathKey">路径, 示例: temp/test1/test2 </param>
        public void CreateFolders(string pathKey)
        {
            var saveSubPaths = pathKey.ToSubPaths();
            var searchParentFileId = _driveParentFileId;
            var contactPath = "";
            foreach (var subPath in saveSubPaths)
            {
                // 判断当前路径是否已存在
                contactPath += "/" + subPath;
                contactPath = contactPath.ToUrlPath();

                // 同级文件夹创建加锁
                using (_lockV2.Lock($"create_folder_{contactPath}"))
                {
                    // 如果已存在了
                    if (_driveFolders.TryGetValue(contactPath, out var folder) && folder != null)
                    {
                        searchParentFileId = folder.FileId;
                        continue;
                    }

                    // 查找当前文件夹是否存在
                    var subItem = _driveApi.GetSubFolders(_driveId, searchParentFileId, subPath, AccessToken);
                    var okPath = subItem.Items.FirstOrDefault(x => x.Name == subPath && x.Type == "folder" && x.ParentFileId == searchParentFileId);
                    if (okPath == null)
                    {
                        // 未找到目录
                        // 执行创建
                        var name = AliyunDriveHelper.EncodeFileName(subPath);
                        var data = _driveApi.CreateFolder(_driveId, searchParentFileId, name, AccessToken);
                        data.Name = data.FileName;

                        if (searchParentFileId == _driveParentFileId)
                        {
                            // 当前目录在根路径
                            // /{当前路径}/
                            _driveFolders.TryAdd($"{data.Name}".TrimPath(), data);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == searchParentFileId).First()!;
                            var path = $"{parent.Key}/{data.Name}".TrimPath();

                            // /{父级路径}/{当前路径}/
                            _driveFolders.TryAdd(path, data);
                        }

                        _log.Information("创建文件夹 {@0}", contactPath);
                    }
                    else
                    {
                        if (searchParentFileId == _driveParentFileId)
                        {
                            // 当前目录在根路径
                            // /{当前路径}/
                            _driveFolders.TryAdd($"{okPath.Name}".TrimPath(), okPath);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == searchParentFileId).First()!;
                            var path = $"{parent.Key}/{okPath.Name}".TrimPath();

                            // /{父级路径}/{当前路径}/
                            _driveFolders.TryAdd(path, okPath);
                        }

                        searchParentFileId = okPath.FileId;
                    }
                }
            }
        }

        public void Dispose()
        {
            // 释放资源
        }

        // ---------------------

        ///// <summary>
        ///// 创建文件/文件夹
        ///// </summary>
        ///// <param name="fileName">文件名</param>
        ///// <param name="access">访问权限</param>
        ///// <param name="share">共享模式</param>
        ///// <param name="mode">文件模式</param>
        ///// <param name="options">文件选项</param>
        ///// <param name="attributes">文件属性</param>
        ///// <param name="info">文件信息</param>
        ///// <returns>操作状态</returns>
        //public NtStatus CreateFile(
        //    string fileName,
        //    FileAccess access,
        //    FileShare share,
        //    FileMode mode,
        //    FileOptions options,
        //    FileAttributes attributes,
        //    IDokanFileInfo info)
        //{
        //    // 当fileName为"\"时，表示访问的是根目录
        //    if (fileName == "\\")
        //    {
        //        // 检查根目录的访问权限等
        //        // 这里可以根据实际情况判断，比如是否允许访问根目录
        //        // 如果允许访问，返回Success
        //        return DokanResult.Success;
        //    }

        //    // 对于非根目录的文件或文件夹，根据mode处理不同的情况

        //    // 根据文件名和模式确定操作类型
        //    switch (mode)
        //    {
        //        case FileMode.CreateNew:
        //            // 在这里实现创建新文件的逻辑
        //            {
        //                // 处理目录的创建或打开
        //                //var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
        //                //if (!_driveFiles.ContainsKey(key))
        //                //{
        //                //    Console.WriteLine("创建文件夹 " + fileName);
        //                //}
        //            }
        //            break;

        //        case FileMode.Create:
        //            // 在这里实现创建文件的逻辑，如果文件存在则覆盖
        //            break;

        //        case FileMode.Open:
        //            // 在这里实现打开文件的逻辑，如果文件不存在则失败
        //            break;

        //        case FileMode.OpenOrCreate:
        //            // 在这里实现打开文件的逻辑，如果文件不存在则创建
        //            break;

        //        case FileMode.Truncate:
        //            // 在这里实现截断文件的逻辑
        //            break;

        //            // 其他模式的处理
        //    }

        //    // 一些特殊的文件操作可能需要单独处理
        //    if (info.IsDirectory)
        //    {
        //    }
        //    else
        //    {
        //        // 处理文件的创建或打开
        //    }

        //    // 实现完成后，根据操作结果返回相应的状态
        //    // 例如，如果操作成功，则返回 NtStatus.Success
        //    // 如果遇到错误，则返回相应的错误状态，如 NtStatus.AccessDenied 或 NtStatus.ObjectNameNotFound 等
        //    return NtStatus.Success;
        //}

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
        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode,
            FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            try
            {
                var result = DokanResult.Success;


                // 当fileName为"\"时，表示访问的是根目录
                if (fileName == "\\")
                {
                    // 检查根目录的访问权限等
                    // 这里可以根据实际情况判断，比如是否允许访问根目录
                    // 如果允许访问，返回Success
                    return DokanResult.Success;
                }

                if (fileName.Contains("新"))
                {
                }

                //if (info.IsDirectory)
                //{
                try
                {
                    switch (mode)
                    {
                        case FileMode.Open:
                            {
                                // 处理目录的创建或打开
                                var key = GetPathKey(fileName);
                                if (!_driveFolders.ContainsKey(key))
                                {
                                    //return DokanResult.PathNotFound;
                                    return DokanResult.NotADirectory;
                                }

                                //var rootPath = fileName.ToUrlPath().Split(_pathSeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                                //if (!string.IsNullOrWhiteSpace(rootPath) && _job.CurrrentJob.Sources.Any(x => x.EndsWith(rootPath)))
                                //{
                                //    // 处理目录的创建或打开
                                //    var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
                                //    if (!_driveFolders.ContainsKey(key))
                                //    {
                                //        //return DokanResult.PathNotFound;
                                //        return DokanResult.NotADirectory;
                                //    }
                                //}

                                //if (!_driveFolders.ContainsKey(fileName.ToUrlPath()))
                                //{
                                //    //return DokanResult.PathNotFound;
                                //    return DokanResult.NotADirectory;
                                //}
                            }
                            //if (!Directory.Exists(filePath))
                            //{
                            //    try
                            //    {
                            //        if (!File.GetAttributes(filePath).HasFlag(FileAttributes.Directory))
                            //            return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            //                attributes, DokanResult.NotADirectory);
                            //    }
                            //    catch (Exception)
                            //    {
                            //        return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            //            attributes, DokanResult.FileNotFound);
                            //    }
                            //    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            //        attributes, DokanResult.PathNotFound);
                            //}

                            //new DirectoryInfo(filePath).EnumerateFileSystemInfos().Any();
                            // you can't list the directory
                            break;

                        case FileMode.CreateNew:
                            {
                                if (info.IsDirectory)
                                {
                                    //var key = fileName.ToUrlPath();
                                    //if (!_driveFolders.ContainsKey(key))
                                    //{
                                    //    //_job.CreateFolders(key);

                                    //    _driveFolders.TryAdd(key, new AliyunDriveFileItem()
                                    //    {
                                    //        FileId = Guid.NewGuid().ToString(),
                                    //        FileName = key,
                                    //    });

                                    //    //Dokan.Notify.Create(_dokanInstance, fileName, isDirectory: true);

                                    //    return DokanResult.Success;
                                    //}
                                    //else
                                    //{
                                    //    //return NtStatus.Success;

                                    //    //return DokanResult.AlreadyExists;
                                    //    return DokanResult.FileExists;
                                    //}

                                    //var rootPath = fileName.ToUrlPath().Split(_pathSeparator, StringSplitOptions.RemoveEmptyEntries).FirstOrDefault();
                                    //if (!string.IsNullOrWhiteSpace(rootPath) && _job.CurrrentJob.Sources.Any(x => x.EndsWith(rootPath)))
                                    //{
                                    // 处理目录的创建或打开
                                    var key = GetPathKey(fileName);
                                    if (!_driveFolders.ContainsKey(key))
                                    {
                                        lock (_lock)
                                        {
                                            if (_driveFolders.ContainsKey(key))
                                            {
                                                return DokanResult.FileExists;
                                            }

                                            CreateFolders(key);

                                            ////Dokan.Notify.Create(_dokanInstance, fileName, isDirectory: true);

                                            Dokan.Notify.Create(_dokanInstance, @$"K:\{fileName}", isDirectory: true);

                                            //Dokan.Notify.Rename(_dokanInstance, GetPath(fileName), GetPath(fileName), isDirectory: true, true);
                                        }

                                        //_driveFolders.TryAdd(key, new AliyunDriveFileItem()
                                        //{
                                        //    FileId = Guid.NewGuid().ToString(),
                                        //    FileName = fileName,
                                        //});

                                        //Dokan.Notify.Create(_dokanInstance, fileName, isDirectory: true);

                                        //access = FileAccess.GenericAll;

                                        //// 设置默认安全描述符
                                        //var security = new DirectorySecurity();
                                        //security.AddAccessRule(new FileSystemAccessRule("Everyone", FileSystemRights.FullControl, AccessControlType.Allow));
                                        //new DirectoryInfo(GetPath(fileName)).SetAccessControl(security);


                                        //return DokanResult.Success;
                                    }
                                    else
                                    {
                                        //return NtStatus.Success;

                                        //return DokanResult.AlreadyExists;
                                        return DokanResult.FileExists;
                                    }
                                    //}
                                }
                                else
                                {
                                }
                            }
                            //if (Directory.Exists(filePath))
                            //    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            //        attributes, DokanResult.FileExists);

                            //try
                            //{
                            //    File.GetAttributes(filePath).HasFlag(FileAttributes.Directory);
                            //    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                            //        attributes, DokanResult.AlreadyExists);
                            //}
                            //catch (IOException)
                            //{
                            //}

                            //Directory.CreateDirectory(GetPath(fileName));
                            break;
                    }
                }
                catch (UnauthorizedAccessException)
                {
                    //return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                    //    DokanResult.AccessDenied);
                    return DokanResult.AccessDenied;
                }
                //}

                // 对于非根目录的文件或文件夹，根据mode处理不同的情况

                // 根据文件名和模式确定操作类型
                switch (mode)
                {
                    case FileMode.CreateNew:
                        // 在这里实现创建新文件的逻辑
                        {
                            if (info.IsDirectory)
                            {
                                //var rootPath = fileName.ToUrlPath().Split(_pathSeparator, StringSplitOptions.RemoveEmptyEntries)
                                //    .FirstOrDefault();
                                //if (!string.IsNullOrWhiteSpace(rootPath) && _job.CurrrentJob.Sources.Any(x => x.EndsWith(rootPath)))
                                //{
                                //    // 处理目录的创建或打开
                                //    var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
                                //    if (!_driveFiles.ContainsKey(key))
                                //    {
                                //        _job.CreateFolders(key);
                                //    }
                                //}
                            }

                            //// 处理目录的创建或打开
                            //var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
                            //if (!_driveFiles.ContainsKey(key))
                            //{
                            //    _job.CreateFolders(key);
                            //}
                        }
                        break;

                    case FileMode.Create:
                        // 在这里实现创建文件的逻辑，如果文件存在则覆盖
                        {
                        }
                        break;

                    case FileMode.Open:
                        // 在这里实现打开文件的逻辑，如果文件不存在则失败
                        {
                            //if (info.IsDirectory)
                            //{
                            //    var rootPath = fileName.ToUrlPath().Split(_pathSeparator, StringSplitOptions.RemoveEmptyEntries)
                            //        .FirstOrDefault();
                            //    if (!string.IsNullOrWhiteSpace(rootPath) && _job.CurrrentJob.Sources.Any(x => x.EndsWith(rootPath)))
                            //    {
                            //        // 处理目录的创建或打开
                            //        var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
                            //        if (!_driveFiles.ContainsKey(key))
                            //        {
                            //            _job.CreateFolders(key);
                            //        }
                            //    }
                            //}
                        }
                        break;

                    case FileMode.OpenOrCreate:
                        // 在这里实现打开文件的逻辑，如果文件不存在则创建
                        {
                        }
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

                if (!info.IsDirectory)
                {
                    //var filePath = GetPath(fileName);

                    var pathExists = false;
                    var pathIsDirectory = false;

                    //var readWriteAttributes = (access & DataAccess) == 0;
                    //var readAccess = (access & DataWriteAccess) == 0;

                    try
                    {
                        var key = GetPathKey(fileName);
                        if (_driveFolders.ContainsKey(key))
                        {
                            pathIsDirectory = true;
                            pathExists = true;
                        }
                        else if (_driveFiles.ContainsKey(key))
                        {
                            pathExists = true;
                        }



                        //pathExists = (Directory.Exists(filePath) || File.Exists(filePath));
                        //pathIsDirectory = pathExists ? File.GetAttributes(filePath).HasFlag(FileAttributes.Directory) : false;
                    }
                    catch (IOException)
                    {
                    }

                    switch (mode)
                    {
                        case FileMode.Open:

                            if (pathExists)
                            {
                                // check if driver only wants to read attributes, security info, or open directory
                                if (pathIsDirectory)
                                {
                                    //if (pathIsDirectory && (access & FileAccess.Delete) == FileAccess.Delete
                                    //    && (access & FileAccess.Synchronize) != FileAccess.Synchronize)
                                    //    //It is a DeleteFile request on a directory
                                    //    //return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    //    //    attributes, DokanResult.AccessDenied);
                                    //    return DokanResult.AccessDenied;

                                    info.IsDirectory = pathIsDirectory;
                                    info.Context = new object();
                                    // must set it to something if you return DokanError.Success

                                    //return Trace(nameof(CreateFile), fileName, info, access, share, mode, options,
                                    //    attributes, DokanResult.Success);
                                    //return DokanResult.Success;
                                }
                            }
                            else
                            {
                                //return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                                //    DokanResult.FileNotFound);
                                return DokanResult.FileNotFound;
                            }
                            break;

                        case FileMode.CreateNew:
                            //if (pathExists)
                            //    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                            //        DokanResult.FileExists);
                            break;

                        case FileMode.Truncate:
                            //if (!pathExists)
                            //    return Trace(nameof(CreateFile), fileName, info, access, share, mode, options, attributes,
                            //        DokanResult.FileNotFound);
                            break;
                    }
                }

                // 实现完成后，根据操作结果返回相应的状态
                // 例如，如果操作成功，则返回 NtStatus.Success
                // 如果遇到错误，则返回相应的错误状态，如 NtStatus.AccessDenied 或 NtStatus.ObjectNameNotFound 等
                return NtStatus.Success;
            }
            catch (Exception ex)
            {
                return NtStatus.Success;
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
                            var url = AliyunDriveGetDownloadUrl(f.FileId, f.ContentHash)?.Url;
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
                        var url = AliyunDriveGetDownloadUrl(f.FileId, f.ContentHash)?.Url;
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
                    //  DokanOptions.NetworkDrive

                    options.Options = DokanOptions.FixedDrive | DokanOptions.DebugMode | DokanOptions.StderrOutput;
                    options.MountPoint = _driveConfig.MountPoint;
                });

            //_dokanInstance = dokanInstanceBuilder.Build(this);

            _mountTask = new Task(() =>
            {
                using var dokanInstance = dokanInstanceBuilder.Build(this);
                _dokanInstance = dokanInstance;
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

            var key = GetPathKey(fileName);

            // info.IsDirectory 不准确，因此不适用
            if (_driveFolders.TryGetValue(key, out var d) && d != null)
            {
                fileInfo = new FileInformation()
                {
                    CreationTime = d.CreatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastAccessTime = d.UpdatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastWriteTime = null,
                    FileName = fileName,
                    Length = d.Size ?? 0,
                    Attributes = FileAttributes.Directory,
                };
                return NtStatus.Success;
            }

            if (_driveFiles.TryGetValue(key, out var file) && file != null)
            {
                fileInfo = new FileInformation()
                {
                    CreationTime = file.CreatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastAccessTime = file.UpdatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastWriteTime = null,
                    FileName = fileName,
                    Length = file.Size ?? 0,
                    Attributes = FileAttributes.Normal,
                };
                return NtStatus.Success;
            }


            fileInfo = new FileInformation()
            {
                Length = 0,
                FileName = fileName,
                CreationTime = null,
                LastAccessTime = null,
                LastWriteTime = null,
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
            files = new List<FileInformation>();

            var parentId = "";

            // 根目录
            if (fileName == "\\")
            {
                parentId = _driveParentFileId;
            }
            else
            {
                var parentKey = GetPathKey(fileName);
                if (_driveFolders.TryGetValue(parentKey, out var p) && p != null)
                {
                    parentId = p.FileId;
                }
            }

            // 加载文件夹
            var dirs = _driveFolders.Values.Where(c => c.ParentFileId == parentId);
            foreach (var d in dirs)
            {
                files.Add(new FileInformation()
                {
                    CreationTime = d.CreatedAt?.DateTime.ToLocalTime(),
                    LastAccessTime = d.UpdatedAt?.DateTime.ToLocalTime(),
                    LastWriteTime = null,
                    FileName = d.Name ?? d.FileName,
                    Length = d.Size ?? 0,
                    Attributes = FileAttributes.Directory,
                });
            }

            // 加载文件
            var fs = _driveFiles.Values.Where(c => c.ParentFileId == parentId);
            foreach (var file in fs)
            {
                files.Add(new FileInformation()
                {
                    CreationTime = file.CreatedAt?.DateTime.ToLocalTime(),
                    LastAccessTime = file.UpdatedAt?.DateTime.ToLocalTime(),
                    LastWriteTime = file.UpdatedAt?.DateTime.ToLocalTime(),
                    FileName = file.Name ?? file.FileName,
                    Length = file.Size ?? 0,
                    Attributes = FileAttributes.Normal,
                });
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

            totalNumberOfBytes = _driveConfig?.Metadata?.TotalSize ?? long.MaxValue;
            totalNumberOfFreeBytes = _driveConfig?.Metadata?.UsedSize ?? 0;
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
            volumeLabel = _driveConfig?.Name ?? "我的云盘";

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
            //var key = (_job.CurrrentJob.Target.TrimPrefix() + "/" + fileName.TrimPath()).ToUrlPath();
            //if (_driveFolders.TryGetValue(key, out var p) && p != null)
            //{
            //}

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