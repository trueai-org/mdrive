using DokanNet;
using DokanNet.Logging;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using RestSharp;
using Serilog;
using ServiceStack;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
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
        private const FileAccess DataAccess = FileAccess.ReadData | FileAccess.WriteData | FileAccess.AppendData |
                                      FileAccess.Execute |
                                      FileAccess.GenericExecute | FileAccess.GenericWrite |
                                      FileAccess.GenericRead;

        private const FileAccess DataWriteAccess = FileAccess.WriteData | FileAccess.AppendData |
                                                   FileAccess.Delete |
                                                   FileAccess.GenericWrite;

        /// <summary>
        /// 打开的文件夹列表，用于刷新文件夹内容
        /// </summary>
        private ConcurrentDictionary<string, DateTime> _openFolders = new();

        /// <summary>
        /// 打开文件夹信号通知
        /// </summary>
        private ManualResetEvent _openFolderMre = new(false);

        /// <summary>
        /// 4 MB per part
        /// </summary>
        private readonly int partSize = 4 * 1024 * 1024;

        /// <summary>
        /// 分块上传文件列表
        /// </summary>
        private Dictionary<string, List<FilePart>> _fileParts = new();

        /// <summary>
        /// 文件上传请求
        /// </summary>
        private readonly HttpClient _uploadHttpClient;

        private readonly ILogger _log;

        /// <summary>
        /// 唯一锁
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// 异步锁/资源锁
        /// </summary>
        private readonly AsyncLockV2 _lockV2 = new();

        /// <summary>
        /// 挂载作业
        /// </summary>
        private Task _mountTask;

        /// <summary>
        ///
        /// </summary>
        private DokanInstance _dokanInstance;

        /// <summary>
        /// 挂载信号
        /// </summary>
        private readonly ManualResetEvent _dokanMre = new(false);

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

            // 上传请求
            // 上传链接最大有效 1 小时
            // 设置 45 分钟超时
            // 在 HttpClient 中，一旦发送了第一个请求，就不能再更改其配置属性，如超时时间 (Timeout)。
            // 这是因为 HttpClient 被设计为可重用的，它的属性设置在第一个请求发出之后就被固定下来。
            _uploadHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(45)
            };

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
        /// 打开文件夹，并触发加载当前文件夹的文件列表
        /// 注意处理，如果文件夹被删除
        /// </summary>
        /// <param name="key"></param>
        private void OpenFolder(string key)
        {
            if (string.IsNullOrWhiteSpace(key))
            {
                return;
            }

            _log.Information("打开文件夹 {@0}", key);

            _openFolders.TryRemove(key, out _);
            _openFolders.TryAdd(key, DateTime.Now);

            // 通知加载列表
            _openFolderMre.Set();
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
        /// 阿里云盘 - 搜索文件
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        private void AliyunDriveSearchFiles(int limit = 100)
        {
            using (_lockV2.Lock("AliyunDriveSearchFiles"))
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
                            var key = "";

                            // 如果相对是根目录
                            if (item.ParentFileId == _driveParentFileId)
                            {
                                key = $"{item.Name}".TrimPath();
                            }
                            else
                            {
                                var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == item.ParentFileId).First()!;
                                key = $"{parent.Key}/{item.Name}".TrimPath();
                            }

                            _driveFolders.TryAdd(key, item);

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

                // 已有的所有文件
                var allFileIds = allItems.Select(c => c.FileId).ToDictionary(c => c, c => true);

                // 如果远程不存在的，则从队列中删除
                // 删除远程不存在的缓存
                // 文件夹处理
                var folderFileIds = _driveFolders.Values.Select(c => c.FileId).ToList();
                var folderFileIdPathDic = _driveFolders.ToDictionary(c => c.Value.FileId, c => c.Key);
                foreach (var fid in folderFileIds)
                {
                    if (!allFileIds.ContainsKey(fid))
                    {
                        if (folderFileIdPathDic.TryGetValue(fid, out var p))
                        {
                            _driveFolders.TryRemove(p, out _);
                        }
                    }
                }

                // 文件处理
                var fileFileIds = _driveFiles.Values.Select(c => c.FileId).ToList();
                var fileFileIdPathDic = _driveFiles.ToDictionary(c => c.Value.FileId, c => c.Key);
                foreach (var fid in fileFileIds)
                {
                    if (!allFileIds.ContainsKey(fid))
                    {
                        if (fileFileIdPathDic.TryGetValue(fid, out var p))
                        {
                            _driveFiles.TryRemove(p, out _);
                        }
                    }
                }

                _log.Information($"云盘文件加载完成，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹。");
            }
        }

        /// <summary>
        /// 阿里云移动文件
        /// </summary>
        /// <param name="oldpath"></param>
        /// <param name="newpath"></param>
        /// <returns></returns>
        private NtStatus AliyunDriveMoveFile(string oldpath, string newpath, bool replace = false)
        {
            using (_lockV2.Lock($"move_{newpath}"))
            {
                if (_driveFiles.TryGetValue(oldpath, out var f) && f != null)
                {
                    var oldParent = Path.GetDirectoryName(oldpath);
                    var newParent = Path.GetDirectoryName(newpath);

                    var newName = AliyunDriveHelper.EncodeFileName(Path.GetFileName(newpath));

                    // 父级相同，重命名
                    if (oldParent.Equals(newParent))
                    {
                        // 仅重命名
                        var oldName1 = Path.GetFileName(oldpath.TrimPath());
                        var newName1 = Path.GetFileName(newpath.TrimPath());
                        if (oldName1.Equals(newName1))
                        {
                            return NtStatus.Success;
                        }

                        // 如果文件已存在
                        if (_driveFiles.ContainsKey(newpath))
                        {
                            return DokanResult.FileExists;
                        }

                        var data = _driveApi.FileUpdate(_driveId, f.FileId, newName, AccessToken);
                        if (data == null || data.FileId != f.FileId)
                        {
                            _log.Error("文件夹重命名失败 {@0} -> {@1}", oldpath, newpath);
                            return NtStatus.Error;
                        }

                        f.FileName = newName;
                        f.Name = newName;

                        // 先移除旧的文件
                        _driveFiles.TryRemove(oldpath, out _);

                        // 如果相对是根目录文件
                        if (f.ParentFileId == _driveParentFileId)
                        {
                            _driveFiles.TryAdd($"{f.Name}".TrimPath(), f);
                        }
                        else
                        {
                            // 文件必须在备份路径中
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == f.ParentFileId).FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(parent.Key))
                            {
                                _driveFiles.TryAdd($"{parent.Key}/{f.Name}".TrimPath(), f);
                            }
                        }

                        return NtStatus.Success;
                    }
                    else
                    {
                        // 判断文件对应的文件夹是否存在，如果不存在则创建
                        var keyPath = Path.GetDirectoryName(newpath).ToUrlPath();
                        var saveParentFileId = "";
                        if (string.IsNullOrWhiteSpace(keyPath))
                        {
                            // 根目录
                            saveParentFileId = _driveParentFileId;
                        }
                        else
                        {
                            if (!_driveFolders.ContainsKey(keyPath))
                            {
                                using (_lockV2.Lock(keyPath))
                                {
                                    if (!_driveFolders.ContainsKey(keyPath))
                                    {
                                        AliyunDriveCreateFolders(keyPath);
                                    }
                                }
                            }

                            if (!_driveFolders.ContainsKey(keyPath))
                            {
                                _log.Error("创建文件夹失败 {@0}", keyPath);
                                return NtStatus.Error;
                            }
                            saveParentFileId = _driveFolders[keyPath].FileId;
                        }

                        if (replace)
                        {
                            // 先删除之前的文件，然后再移动
                            if (_driveFiles.TryGetValue(newpath, out var nf) && nf != null)
                            {
                                _driveApi.FileDelete(_driveId, nf.FileId, AccessToken, _driveConfig.IsRecycleBin);
                            }
                        }

                        var res = _driveApi.Move(_driveId, f.FileId, saveParentFileId, AccessToken, new_name: newName);
                        if (res.Exist)
                        {
                            return DokanResult.FileExists;
                        }

                        // 先移除旧的文件
                        _driveFiles.TryRemove(oldpath, out _);

                        // 移动成功了，设置父级 ID
                        f.ParentFileId = saveParentFileId;
                        f.Name = newName;
                        f.FileName = newName;

                        // 如果相对是根目录文件
                        if (f.ParentFileId == _driveParentFileId)
                        {
                            _driveFiles.TryAdd($"{f.Name}".TrimPath(), f);
                        }
                        else
                        {
                            // 文件必须在备份路径中
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == f.ParentFileId).FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(parent.Key))
                            {
                                _driveFiles.TryAdd($"{parent.Key}/{f.Name}".TrimPath(), f);
                            }
                        }

                        return NtStatus.Success;
                    }
                }
                else
                {
                    // 没有这个文件
                    return NtStatus.NoSuchFile;
                }
            }
        }

        /// <summary>
        /// 阿里云移动文件夹
        /// </summary>
        /// <param name="oldpath"></param>
        /// <param name="newpath"></param>
        /// <returns></returns>
        private NtStatus AliyunDriveMoveFolder(string oldpath, string newpath)
        {
            using (_lockV2.Lock($"move_{newpath}"))
            {
                if (_driveFolders.TryGetValue(oldpath, out var fo) && fo != null)
                {
                    var oldParent = Path.GetDirectoryName(oldpath);
                    var newParent = Path.GetDirectoryName(newpath);

                    // 父级相同，重命名
                    if (oldParent.Equals(newParent))
                    {
                        // 仅重命名
                        var oldFolderName = Path.GetFileName(oldpath.TrimPath());
                        var newFolderName = Path.GetFileName(newpath.TrimPath());

                        if (oldFolderName.Equals(newFolderName))
                        {
                            return NtStatus.Success;
                        }

                        var newName = AliyunDriveHelper.EncodeFileName(newFolderName);
                        var data = _driveApi.FileUpdate(_driveId, fo.FileId, newName, AccessToken);
                        if (data == null || data.FileId != fo.FileId)
                        {
                            _log.Error("文件夹重命名失败 {@0} -> {@1}", oldpath, newpath);
                            return NtStatus.Error;
                        }

                        fo.FileName = newName;
                        fo.Name = newName;

                        // 先移除旧的文件
                        _driveFolders.TryRemove(oldpath, out _);

                        if (fo.ParentFileId == _driveParentFileId)
                        {
                            // 当前目录在根路径
                            // /{当前路径}/
                            _driveFolders.TryAdd($"{fo.Name}".TrimPath(), fo);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == fo.ParentFileId).First()!;
                            var path = $"{parent.Key}/{fo.Name}".TrimPath();

                            // /{父级路径}/{当前路径}/
                            _driveFolders.TryAdd(path, fo);
                        }
                    }
                    else
                    {
                        // 移除最后一个当前文件夹
                        var newsubs = newpath.ToSubPaths().ToList();
                        newsubs.RemoveAt(newsubs.Count - 1);

                        var newParentPath = newsubs.Join("/");
                        var saveParentFileId = "";
                        if (string.IsNullOrWhiteSpace(newParentPath))
                        {
                            // 根目录
                            saveParentFileId = _driveParentFileId;
                        }
                        else
                        {
                            if (!_driveFolders.ContainsKey(newParentPath))
                            {
                                // 移动时，父级文件夹一定是存在的
                                return NtStatus.NotADirectory;
                            }

                            saveParentFileId = _driveFolders[newParentPath].FileId;
                        }

                        var res = _driveApi.Move(_driveId, fo.FileId, saveParentFileId, AccessToken);
                        if (res.Exist)
                        {
                            return DokanResult.AlreadyExists;
                        }

                        fo.ParentFileId = saveParentFileId;

                        // 先移除旧的文件
                        _driveFolders.TryRemove(oldpath, out _);

                        if (saveParentFileId == _driveParentFileId)
                        {
                            // 当前目录在根路径
                            // /{当前路径}/
                            _driveFolders.TryAdd($"{fo.Name}".TrimPath(), fo);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == saveParentFileId).First()!;
                            var path = $"{parent.Key}/{fo.Name}".TrimPath();

                            // /{父级路径}/{当前路径}/
                            _driveFolders.TryAdd(path, fo);
                        }
                    }

                    // 重新计算关联当前的子文件和子文件夹
                    // 确保以斜杠结尾
                    // 排除自身
                    var oldPathPrefix = $"{oldpath}/";
                    var newPathPrefix = $"{newpath}/";
                    var itemFoldersToUpdate = _driveFolders.Where(kvp => kvp.Value.FileId != fo.FileId && kvp.Key.StartsWith(oldPathPrefix)).ToList();
                    foreach (var item in itemFoldersToUpdate)
                    {
                        var subPath = item.Key.Substring(oldPathPrefix.Length);
                        var newPath = $"{newPathPrefix}{subPath}".ToUrlPath();

                        _driveFolders.TryRemove(item.Key, out _); // 移除旧路径
                        _driveFolders.TryAdd(newPath, item.Value); // 添加新路径
                    }

                    // 计算相关文件
                    var itemFilesToUpdate = _driveFiles.Where(kvp => kvp.Key.StartsWith(oldPathPrefix)).ToList();
                    foreach (var item in itemFilesToUpdate)
                    {
                        var subPath = item.Key.Substring(oldPathPrefix.Length);
                        var newPath = $"{newPathPrefix}{subPath}".ToUrlPath();

                        _driveFiles.TryRemove(item.Key, out _); // 移除旧路径
                        _driveFiles.TryAdd(newPath, item.Value); // 添加新路径
                    }

                    return NtStatus.Success;
                }
                else
                {
                    // 没有这个文件夹
                    return NtStatus.NotADirectory;
                }
            }
        }

        /// <summary>
        /// 阿里云盘 - 获取文件下载 URL
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        private AliyunDriveOpenFileGetDownloadUrlResponse AliyunDriveGetDownloadUrl(string fileId, string hash = "")
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
        /// 创建云盘文件夹路径
        /// </summary>
        /// <param name="pathKey">路径, 示例: temp/test1/test2 </param>
        private void AliyunDriveCreateFolders(string pathKey)
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

        /// <summary>
        /// 删除文件夹
        /// </summary>
        /// <param name="key"></param>
        private void AliyunDriveDeleteFolder(string key)
        {
            if (_driveFolders.TryGetValue(key, out var folder))
            {
                var res = _driveApi.FileDelete(_driveId, folder.FileId, AccessToken, _driveConfig.IsRecycleBin);
                if (!string.IsNullOrWhiteSpace(res?.FileId) || !string.IsNullOrWhiteSpace(res.AsyncTaskId))
                {
                    _driveFolders.TryRemove(key, out _);

                    var fkeys = _driveFiles.Where(c => c.Value.FileId == folder.FileId).Select(c => c.Key).ToList();
                    foreach (var fk in fkeys)
                    {
                        _driveFiles.TryRemove(fk, out _);
                    }
                }
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="key"></param>
        private void AliyunDriveDeleteFile(string key)
        {
            if (_driveFiles.TryGetValue(key, out var folder))
            {
                var res = _driveApi.FileDelete(_driveId, folder.FileId, AccessToken, _driveConfig.IsRecycleBin);

                //// 如果没有返回结果，说明可能被删除了
                //if (res == null || !string.IsNullOrWhiteSpace(res?.FileId) || !string.IsNullOrWhiteSpace(res.AsyncTaskId))
                //{
                //    _driveFiles.TryRemove(key, out _);
                //}

                // 直接移除
                _driveFiles.TryRemove(key, out _);
            }
        }

        /// <summary>
        /// 阿里云盘 - 上传文件
        /// </summary>
        /// <param name="localFileInfo"></param>
        /// <param name="needPreHash"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        /// <exception cref="HttpRequestException"></exception>
        private async Task AliyunDrivePartUpload(string tmpFileName, string uploadUrl)
        {
            // 读取文件作为字节流
            var fileData = File.ReadAllBytes(tmpFileName);

            // 创建HttpContent
            var content = new ByteArrayContent(fileData);

            // 发送PUT请求
            HttpResponseMessage uploadRes = null;

            // 定义重试策略 3 次
            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(3, retryAttempt =>
                {
                    // 5s 25s 125s 后重试
                    return TimeSpan.FromSeconds(Math.Pow(5, retryAttempt));
                });

            // 执行带有重试策略的请求
            await retryPolicy.ExecuteAsync(async () =>
            {
                uploadRes = await _uploadHttpClient.PutAsync(uploadUrl, content);

                if (!uploadRes.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
                }
            });

            // 检查请求是否成功
            if (!uploadRes.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
            }
        }

        /// <summary>
        /// 创建分块文件上传请求
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="fileSize"></param>
        /// <param name="saveParentFileId"></param>
        /// <param name="partCount"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private AliyunDriveOpenFileCreateResponse AliyunDriveCreatePartUpload(string fileName, long fileSize, string saveParentFileId, int partCount)
        {
            var name = AliyunDriveHelper.EncodeFileName(Path.GetFileName(fileName));

            var request = new RestRequest("/adrive/v1.0/openFile/create", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");

            // 使用分块上传
            // https://www.yuque.com/aliyundrive/zpfszx/ezlzok

            object body = new
            {
                drive_id = _driveId,
                parent_file_id = saveParentFileId,
                name = name,
                type = "file",

                // refuse 同名不创建
                // ignore 同名文件可创建
                check_name_mode = "refuse",
                size = fileSize,

                // 分块数量
                part_info_list = Enumerable.Range(1, partCount).Select(c => new
                {
                    part_number = c
                }).ToArray()
            };

            request.AddBody(body);
            var response = _driveApi.WithRetry<AliyunDriveOpenFileCreateResponse>(request);

            if (response.StatusCode != HttpStatusCode.OK)
            {
                _log.Error(response.ErrorException, $"创建文件上传请求失败 {response.Content}");

                throw response.ErrorException ?? new Exception($"创建文件上传请求失败");
            }

            if (response.Data != null && response.Data.PartInfoList?.Count > 0)
            {
                return response.Data;
            }
            else
            {
                throw new Exception("创建文件上传请求失败");
            }

            //using var doc = JsonDocument.Parse(response.Content!);
            //var root = doc.RootElement;
            //var drive_id = root.GetProperty("drive_id").GetString();
            //var file_id = root.GetProperty("file_id").GetString();
            //var upload_id = root.GetProperty("upload_id").GetString();
            //var upload_url = root.GetProperty("part_info_list").EnumerateArray().FirstOrDefault().GetProperty("upload_url").GetString();
        }

        /// <summary>
        /// 上传标记完成
        /// </summary>
        /// <param name="file_id"></param>
        /// <param name="upload_id"></param>
        private void AliyunDriveUploadComplete(string file_id, string upload_id)
        {
            // 将文件添加到上传列表
            var data = _driveApi.UploadComplete(_driveId, file_id, upload_id, AccessToken);
            if (data.ParentFileId == _driveParentFileId)
            {
                // 当前目录在根路径
                // /{当前路径}/
                _driveFiles.TryAdd($"{data.Name}".TrimPath(), data);
            }
            else
            {
                // 计算父级路径
                var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == data.ParentFileId).First()!;
                var path = $"{parent.Key}/{data.Name}".TrimPath();

                // /{父级路径}/{当前路径}/
                _driveFiles.TryAdd(path, data);
            }
        }

        #region 公共方法

        /// <summary>
        /// 删除文件夹以及子文件和子文件夹
        /// </summary>
        /// <param name="key"></param>
        public void DeleteFolderAndSubFiles(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _driveFolders.TryRemove(key, out _);

                // 子文件夹处理，以当前路径开头的子文件和子文件夹
                // 重新计算关联当前的子文件和子文件夹
                // 确保以斜杠结尾
                var oldPathPrefix = $"{key.TrimPath()}/";
                var itemFoldersToUpdate = _driveFolders.Where(kvp => kvp.Key.StartsWith(oldPathPrefix)).ToList();
                foreach (var item in itemFoldersToUpdate)
                {
                    _driveFolders.TryRemove(item.Key, out _);
                }

                // 计算相关文件
                var itemFilesToUpdate = _driveFiles.Where(kvp => kvp.Key.StartsWith(oldPathPrefix)).ToList();
                foreach (var item in itemFilesToUpdate)
                {
                    _driveFiles.TryRemove(item.Key, out _);
                }
            }
        }

        /// <summary>
        /// 阿里云盘 - 获取文件列表（限流 4 QPS）
        /// </summary>
        /// <param name="parentFileId"></param>
        /// <param name="limit"></param>
        /// <param name="orderBy"></param>
        /// <param name="orderDirection"></param>
        /// <param name="category"></param>
        /// <param name="type"></param>
        /// <param name="saveRootPath">备份保存的目录，如果匹配到则立即返回</param>
        /// <returns></returns>
        private async Task AliyunDriveLoadFolderFiles(string parentKey, string parentFileId, int limit = 100, string orderBy = null, string orderDirection = null, string category = null, string type = "all")
        {
            try
            {
                // 首先判断进入的文件夹是否存在
                var error = string.Empty;
                if (!_driveApi.TryGetDetail(_driveId, parentFileId, AccessToken, out var detail, ref error) && error == AliyunDriveApi.ERROR_NOTFOUNDFILEID)
                {
                    // 文件不存在
                    DeleteFolderAndSubFiles(parentKey);
                    return;
                }

                var allItems = new List<AliyunDriveFileItem>();
                string marker = null;
                do
                {
                    var sw = new Stopwatch();
                    sw.Start();

                    if (!_driveApi.TryFileList(_driveId, parentFileId, limit, marker, orderBy, orderDirection, category, type, AccessToken, out var responseData, ref error)
                        && (error == AliyunDriveApi.ERROR_NOTFOUNDFILEID || error == AliyunDriveApi.ERROR_FORBIDDENFILEINTHERECYCLEBIN))
                    {
                        DeleteFolderAndSubFiles(parentKey);
                        return;
                    }

                    if (responseData == null)
                    {
                        throw new Exception("请求列表失败");
                    }

                    if (responseData.Items.Count > 0)
                    {
                        allItems.AddRange(responseData.Items.ToList());
                    }
                    marker = responseData.NextMarker;

                    sw.Stop();

                    // 等待 250ms 以遵守限流策略
                    if (sw.ElapsedMilliseconds < AliyunDriveApi.REQUEST_INTERVAL)
                        await Task.Delay((int)(AliyunDriveApi.REQUEST_INTERVAL - sw.ElapsedMilliseconds));
                } while (!string.IsNullOrEmpty(marker));

                foreach (var item in allItems)
                {
                    // 如果是文件夹
                    if (item.Type == "folder")
                    {
                        // 如果是根目录
                        if (item.ParentFileId == _driveParentFileId)
                        {
                            _driveFolders.TryAdd($"{item.Name}".TrimPath(), item);
                        }
                        else
                        {
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == item.ParentFileId).First()!;
                            _driveFolders.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        }
                    }
                    else
                    {
                        // 如果是根目录的文件
                        if (item.ParentFileId == _driveParentFileId)
                        {
                            _driveFiles.TryAdd($"{item.Name}".TrimPath(), item);
                        }
                        else
                        {
                            // 构建文件路径作为字典的键
                            var parent = _driveFolders.Where(c => c.Value.IsFolder && c.Value.FileId == item.ParentFileId).First()!;
                            _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        }
                    }

                    _log.Information($"云盘文件加载完成，包含 {allItems.Count} 个文件/文件夹");
                }

                // 如果文件夹存在，则更新此文件夹列表
                // 如果文件夹文件不存在了，注意更新缓存

                // 已有的所有文件
                var allFileIds = allItems.Select(c => c.FileId).ToDictionary(c => c, c => true);

                // 如果远程不存在的，则从队列中删除
                // 删除远程不存在的缓存
                // 文件夹处理
                var folderFileIds = _driveFolders.Values.Where(c => c.ParentFileId == parentFileId).Select(c => c.FileId).ToList();
                var folderFileIdPathDic = _driveFolders.Where(c => c.Value.ParentFileId == parentFileId).ToDictionary(c => c.Value.FileId, c => c.Key);
                foreach (var fid in folderFileIds)
                {
                    if (!allFileIds.ContainsKey(fid))
                    {
                        if (folderFileIdPathDic.TryGetValue(fid, out var key))
                        {
                            DeleteFolderAndSubFiles(key);
                        }
                    }
                }

                // 文件处理
                var fileFileIds = _driveFiles.Values.Where(c => c.ParentFileId == parentFileId).Select(c => c.FileId).ToList();
                var fileFileIdPathDic = _driveFiles.Where(c => c.Value.ParentFileId == parentFileId).ToDictionary(c => c.Value.FileId, c => c.Key);
                foreach (var fid in fileFileIds)
                {
                    if (!allFileIds.ContainsKey(fid))
                    {
                        if (fileFileIdPathDic.TryGetValue(fid, out var p))
                        {
                            _driveFiles.TryRemove(p, out _);
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                _log.Error(ex, "云盘文件列表加载失败 {@0}", parentFileId);
            }
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
        /// 挂载
        /// </summary>
        public void Mount()
        {
            var dokanLogger = new ConsoleLogger("[Dokan] ");
            var dokanInstanceBuilder = new DokanInstanceBuilder(new Dokan(dokanLogger))
                .ConfigureOptions(options =>
                {
                    // DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI | DokanOptions.NetworkDrive  | DokanOptions.StderrOutput;

                    options.Options = DokanOptions.FixedDrive;

                    if (_driveConfig.MountReadOnly)
                    {
                        options.Options |= DokanOptions.WriteProtection;
                    }

                    options.MountPoint = _driveConfig.MountPoint;
                });

            _mountTask = new Task(() =>
            {
                using var dokanInstance = dokanInstanceBuilder.Build(this);
                _dokanInstance = dokanInstance;

                // 初始化文件列表
                AliyunDriveInitFiles();

                // 创建轮询计划
                // 每 15 分钟更新一次列表
                var scheduler = new QuartzCronScheduler("0 0/15 * * * ?", () =>
                {
                    AliyunDriveSearchFiles();
                });
                scheduler.Start();

                // 开启一个作业，用于处理打开文件夹的作业处理
                Task.Run(async () =>
                {
                    while (true)
                    {
                        while (_openFolders.Count > 0)
                        {
                            // 优先从第一个 key 处理
                            var key = _openFolders.Keys.FirstOrDefault();
                            if (_openFolders.TryGetValue(key, out var ketTime))
                            {
                                try
                                {
                                    // 10 分钟内曾经打开的文件夹
                                    if (ketTime >= DateTime.Now.AddMinutes(-10))
                                    {
                                        if (key == "\\")
                                        {
                                            await AliyunDriveLoadFolderFiles("", _driveParentFileId);
                                        }
                                        else
                                        {
                                            if (_driveFolders.TryGetValue(key, out var fo) && fo != null)
                                            {
                                                await AliyunDriveLoadFolderFiles(key, fo.FileId);
                                            }
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    _log.Error(ex, "加载文件列表异常 {@0}", key);
                                }
                                finally
                                {
                                    _openFolders.TryRemove(key, out _);
                                }
                            }

                            // 超过 100 个移除
                            var lastKeys = _openFolders.Keys.Skip(100).ToList();
                            foreach (var lk in lastKeys)
                            {
                                _openFolders.TryRemove(lk, out _);
                            }
                        }

                        // 处理完后，等待
                        _openFolderMre.WaitOne();
                    }
                });

                // await _dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);

                _dokanMre.WaitOne();

                // 销毁计划
                scheduler.Dispose();
            });

            _mountTask.Start();
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
            _dokanMre.Set();
            _dokanInstance?.Dispose();
        }

        /// <summary>
        /// 释放资源
        /// </summary>
        public void Dispose()
        {
            //// 卸载 Dokan 文件系统
            //if (_dokanInstance != null)
            //{
            //    //Dokan.RemoveMountPoint(_mountPoint);
            //}

            //_dokanInstance?.Dispose();
            //_mountTask?.Dispose();
        }

        #endregion 公共方法

        #region 已实现

        /// <summary>
        /// 查找文件列表
        /// </summary>
        /// <param name="fileName"></param>
        /// <param name="searchPattern"></param>
        /// <returns></returns>
        public IList<FileInformation> FindFilesHelper(string fileName, string searchPattern)
        {
            var files = new List<FileInformation>();

            var parentId = _driveParentFileId;
            if (fileName != "\\")
            {
                var parentKey = GetPathKey(fileName);
                if (_driveFolders.TryGetValue(parentKey, out var p) && p != null)
                {
                    parentId = p.FileId;
                }
                else
                {
                    // 如果没有找到父级，则返回空
                    return files;
                }
            }

            // 加载文件夹
            var dirs = _driveFolders.Values.Where(c => c.ParentFileId == parentId);
            foreach (var d in dirs)
            {
                files.Add(new FileInformation()
                {
                    CreationTime = d.CreatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
                    LastAccessTime = d.UpdatedAt?.DateTime.ToLocalTime() ?? DateTime.Now,
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

            // 注意过滤 searchPattern 否则可能导致创建文件夹显示不出来
            return files.Where(finfo => DokanHelper.DokanIsNameInExpression(searchPattern, finfo.FileName, true))
                .Select(finfo => new FileInformation
                {
                    Attributes = finfo.Attributes,
                    CreationTime = finfo.CreationTime,
                    LastAccessTime = finfo.LastAccessTime,
                    LastWriteTime = finfo.LastWriteTime,
                    Length = finfo.Length,
                    FileName = finfo.FileName
                }).ToList();
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
        public NtStatus CreateFile(string fileName, FileAccess access, FileShare share, FileMode mode, FileOptions options, FileAttributes attributes, IDokanFileInfo info)
        {
            try
            {
                var key = GetPathKey(fileName);

                var pathIsDirectory = _driveFolders.ContainsKey(key);
                var pathIsFile = _driveFiles.ContainsKey(key);
                var pathExists = pathIsDirectory || pathIsFile;

                var readWriteAttributes = (access & DataAccess) == 0;
                var readAccess = (access & DataWriteAccess) == 0;

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
                                    OpenFolder(key);

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
                                            if (_driveFolders.ContainsKey(key))
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
            if (!_driveFiles.ContainsKey(key))
            {
                return DokanResult.FileNotFound;
            }

            try
            {
                if (_driveFiles.TryGetValue(key, out var f) && f != null)
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

                        //int endOffset = (int)offset + buffer.Length - 1;
                        int endOffset = (int)Math.Min(offset + buffer.Length - 1, (int)f.Size - 1);
                        partialContent = DownloadFileSegment(url, (int)offset, endOffset).GetAwaiter().GetResult();
                    }

                    if (fileName.Contains("jpg"))
                    {
                    }

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
            //_log.Information("查找文件 {@0}", fileName);

            if (fileName == "\\")
            {
                OpenFolder("\\");
            }
            else
            {
                var key = GetPathKey(fileName);
                OpenFolder(key);
            }

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
                if (_driveFolders.ContainsKey(key))
                {
                    // 删除文件夹
                    AliyunDriveDeleteFolder(key);
                }
                else if (_driveFiles.ContainsKey(key))
                {
                    // 删除文件
                    AliyunDriveDeleteFile(key);
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
                if (_fileParts.TryGetValue(key, out var ps) && ps != null && ps.Count > 0)
                {
                    // 未上传的分块，执行上传
                    foreach (var item in ps)
                    {
                        if (!item.IsUploaded)
                        {
                            // 验证是否填充完整的数据
                            if (item.PartNumber < ps.Count && item.CurrentSize >= partSize)
                            {
                                // 非最后一个分块
                                AliyunDrivePartUpload(item.LocalFilePath, item.UploadUrl).GetAwaiter().GetResult();
                                item.IsUploaded = true;
                            }
                            else if (item.PartNumber == ps.Count)
                            {
                                // 最后一个分块

                                // 计算最后一个分块的实际大小
                                int lastPartSize = (int)(item.TotalSize % partSize);
                                if (lastPartSize == 0)
                                    lastPartSize = partSize; // 如果文件大小是分块大小的整数倍

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
                _fileParts.TryRemove(key, out _);
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
                    saveParentFileId = _driveParentFileId;
                }
                else
                {
                    if (!_driveFolders.ContainsKey(keyPath))
                    {
                        using (_lockV2.Lock(keyPath))
                        {
                            if (!_driveFolders.ContainsKey(keyPath))
                            {
                                AliyunDriveCreateFolders(keyPath);
                            }
                        }
                    }
                    if (!_driveFolders.ContainsKey(keyPath))
                    {
                        _log.Error("创建文件夹失败 {@0}", keyPath);
                        return NtStatus.Error;
                    }
                    saveParentFileId = _driveFolders[keyPath].FileId;
                }
                // 分块数量
                var partsCount = (int)Math.Ceiling((double)length / partSize);

                // 创建上传请求，并获取上传地址
                var data = AliyunDriveCreatePartUpload(fileName, length, saveParentFileId, partsCount);
                if (data == null || data.PartInfoList.Count != partsCount)
                {
                    _log.Error("创建文件上传请求失败 {@0}", keyPath);
                }

                var parts = new List<FilePart>(partsCount);
                for (int i = 0; i < partsCount; i++)
                {
                    var tmpFile = Path.Combine(Directory.GetCurrentDirectory(), ".duplicatiuploadcache", $"{key}.{i}.duplicatipart");

                    // 如果存在临时文件，则删除
                    if (File.Exists(tmpFile))
                    {
                        File.Delete(tmpFile);
                    }

                    var uploadUrl = data.PartInfoList[i].UploadUrl;

                    parts.Add(new FilePart(i + 1, tmpFile, uploadUrl)
                    {
                        FileId = data.FileId,
                        UploadId = data.UploadId,
                        TotalSize = length
                    });
                }

                _fileParts[key] = parts;
            }

            return NtStatus.Success;
        }

        protected static Int32 GetNumOfBytesToCopy(Int32 bufferLength, long offset, IDokanFileInfo info, FileStream stream)
        {
            if (info.PagingIo)
            {
                var longDistanceToEnd = stream.Length - offset;
                var isDistanceToEndMoreThanInt = longDistanceToEnd > Int32.MaxValue;
                if (isDistanceToEndMoreThanInt) return bufferLength;
                var distanceToEnd = (Int32)longDistanceToEnd;
                if (distanceToEnd < bufferLength) return distanceToEnd;
                return bufferLength;
            }
            return bufferLength;
        }

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

            //using (var stream = new FileStream(filePath,  FileMode.OpenOrCreate, System.IO.FileAccess.Write))
            //{
            //    if (!append) // Offset of -1 is an APPEND: https://docs.microsoft.com/en-us/windows/win32/api/fileapi/nf-fileapi-writefile
            //    {
            //        stream.Position = offset;
            //    }
            //    var bytesToCopy = GetNumOfBytesToCopy(buffer.Length, offset, info, stream);
            //    stream.Write(buffer, 0, bytesToCopy);
            //    bytesWritten = bytesToCopy;
            //}
        }

        // 重写 WriteFile 方法以包含上传逻辑
        public NtStatus WriteFile(string fileName, byte[] buffer, out int bytesWritten, long offset, IDokanFileInfo info)
        {
            try
            {
                var key = GetPathKey(fileName);

                // 文件上传锁
                using (_lockV2.Lock($"upload:{key}"))
                {
                    var parts = _fileParts[key];

                    int currentPartIndex = (int)(offset / partSize);
                    int partOffset = (int)(offset % partSize);
                    int bufferOffset = 0;
                    int remainingBuffer = buffer.Length;

                    while (remainingBuffer > 0 && currentPartIndex < parts.Count)
                    {
                        FilePart currentPart = parts[currentPartIndex];
                        int writeSize = Math.Min(remainingBuffer, partSize - partOffset);

                        // 写入到本地临时文件
                        WriteToFile(currentPart.LocalFilePath, buffer, bufferOffset, writeSize, partOffset);

                        currentPart.CurrentSize += writeSize;
                        bufferOffset += writeSize;
                        partOffset = 0; // 重置偏移量
                        remainingBuffer -= writeSize;

                        // 如果当前分块已填充内容完毕
                        if (currentPart.CurrentSize >= partSize && !currentPart.IsUploaded)
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
                            int lastPartSize = (int)(currentPart.TotalSize % partSize);
                            if (lastPartSize == 0)
                                lastPartSize = partSize; // 如果文件大小是分块大小的整数倍

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

        #endregion 无需实现

        #region 暂不实现

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

            var exist = info.IsDirectory ? _driveFolders.ContainsKey(newpath) : _driveFiles.ContainsKey(newpath);

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
            files = FindFilesHelper(fileName, "*");
            return DokanResult.Success;
        }

        public NtStatus SetFileAttributes(string fileName, FileAttributes attributes, IDokanFileInfo info)
        {
            return DokanResult.Success;
        }

        public NtStatus SetFileTime(string fileName, DateTime? creationTime, DateTime? lastAccessTime, DateTime? lastWriteTime, IDokanFileInfo info)
        {
            return NtStatus.Success;
        }

        #endregion 暂不实现
    }

    public class FilePart
    {
        /// <summary>
        /// 分块序号，从 1 开始
        /// </summary>
        public int PartNumber { get; private set; }

        /// <summary>
        /// 本地上传文件路径
        /// </summary>
        public string LocalFilePath { get; private set; }

        /// <summary>
        /// 云盘上传路径
        /// </summary>
        public string UploadUrl { get; private set; }

        /// <summary>
        /// 当前已写入的文件大小
        /// </summary>
        public int CurrentSize { get; set; }

        /// <summary>
        /// 文件的总大小
        /// </summary>
        public long TotalSize { get; set; }

        /// <summary>
        /// 是否已上传到云盘
        /// </summary>
        public bool IsUploaded { get; set; }

        /// <summary>
        /// 文件ID。
        /// </summary>
        public string FileId { get; set; }

        /// <summary>
        /// 上传ID。
        /// </summary>
        public string UploadId { get; set; }

        public FilePart(int partNumber, string localFilePath, string uploadUrl)
        {
            PartNumber = partNumber;
            LocalFilePath = localFilePath;
            UploadUrl = uploadUrl;
            CurrentSize = 0;
            IsUploaded = false;
        }
    }
}