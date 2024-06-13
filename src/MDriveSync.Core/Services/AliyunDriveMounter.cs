using DokanNet;
using DokanNet.Logging;
using MDriveSync.Core.IO;
using MDriveSync.Core.Models;
using Microsoft.Extensions.Caching.Memory;
using Polly;
using RestSharp;
using Serilog;
using ServiceStack;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Net;
using System.Net.Http.Headers;

using ILogger = Serilog.ILogger;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 阿里云盘挂载
    /// </summary>
    public partial class AliyunDriveMounter : IDokanOperations, IDisposable
    {
        /// <summary>
        /// 令牌标识
        /// </summary>
        private const string TOEKN_KEY = "TOKEN";

        /// <summary>
        /// 日志
        /// </summary>
        private readonly ILogger _log;

        /// <summary>
        /// 本地缓存
        /// 令牌缓存、下载链接缓存等
        /// </summary>
        private readonly IMemoryCache _cache;

        /// <summary>
        /// 唯一锁
        /// </summary>
        private readonly object _lock = new();

        /// <summary>
        /// 异步锁/资源锁
        /// </summary>
        private readonly AsyncLockV2 _lockV2 = new();

        /// <summary>
        /// 打开文件夹信号通知
        /// </summary>
        private readonly ManualResetEvent _openFolderMre = new(false);

        /// <summary>
        /// 打开的文件夹列表，用于刷新文件列表
        /// </summary>
        private readonly ConcurrentDictionary<string, DateTime> _openFolders = new();

        /// <summary>
        /// 4 MB per part
        /// </summary>
        private readonly int _uploadPartSize = 4 * 1024 * 1024;

        /// <summary>
        /// 分块上传文件列表
        /// </summary>
        private readonly Dictionary<string, List<AliyunFileUploadPart>> _uploadFileParts = new();

        /// <summary>
        /// 文件上传请求
        /// </summary>
        private readonly HttpClient _uploadHttpClient;

        /// <summary>
        /// 挂载作业
        /// </summary>
        private Task _dokanMountTask;

        /// <summary>
        ///
        /// </summary>
        private DokanInstance _dokanInstance;

        /// <summary>
        /// 挂载信号
        /// </summary>
        private readonly ManualResetEvent _dokanMre = new(false);

        /// <summary>
        /// 所有云盘文件
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _files = new();

        /// <summary>
        /// 客户端信息
        /// </summary>
        private AliyunDriveConfig _driveConfig;

        /// <summary>
        /// 挂载点配置
        /// </summary>
        private AliyunDriveMountConfig _driveMountConfig;

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
        private string _driveRootId = "root";

        /// <summary>
        /// 根目录路径 key
        /// </summary>
        private readonly string _driveRootKey = "";

        /// <summary>
        /// 是否加载完整列表完成
        /// </summary>
        public bool _driveLoadComplete = false;

        /// <summary>
        /// 别名
        /// </summary>
        private readonly string _alias = "";

        /// <summary>
        /// 显示真实大小
        /// </summary>
        private readonly bool _isRealSize;

        /// <summary>
        /// 下载请求
        /// </summary>
        private readonly HttpClient _downloadHttp = new()
        {
            Timeout = TimeSpan.FromMinutes(45)
        };

        /// <summary>
        /// 创建挂载
        /// </summary>
        /// <param name="driveConfig">云盘配置</param>
        /// <param name="driveMountConfig">挂载配置</param>
        /// <param name="alias">别名</param>
        public AliyunDriveMounter(AliyunDriveConfig driveConfig, AliyunDriveMountConfig driveMountConfig, string alias = "")
        {
            _log = Log.Logger;
            _cache = new MemoryCache(new MemoryCacheOptions());

            _alias = alias;

            // 如果挂载为目录，则显示真实大小
            if (!string.IsNullOrWhiteSpace(driveMountConfig.MountPath))
            {
                _isRealSize = true;
                _driveRootKey = driveMountConfig.MountPath?.ToUrlPath();
            }

            _driveApi = new AliyunDriveApi();

            _driveConfig = driveConfig;
            _driveMountConfig = driveMountConfig;

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
        private async Task<byte[]> DownloadFile(string url)
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
        private async Task<byte[]> DownloadFile(string url, int start, int end)
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
            if (!string.IsNullOrWhiteSpace(_driveRootKey))
            {
                return $"{_driveRootKey}/{fileName.TrimPath()}".ToUrlPath();
            }

            return fileName.ToUrlPath();
        }

        /// <summary>
        /// 获取当前有效的访问令牌
        /// </summary>
        private string AccessToken
        {
            get
            {
                return AliyunDriveToken.Instance.GetAccessToken(_driveConfig.Id);
            }
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
                _driveMountConfig.MountPath = _driveMountConfig.MountPath.TrimPath();

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

            if (_driveMountConfig.MountDrive == "backup" && string.IsNullOrWhiteSpace(data.BackupDriveId))
            {
                _driveId = data.BackupDriveId;
            }
            else if (_driveMountConfig.MountDrive == "resource" && !string.IsNullOrWhiteSpace(data.ResourceDriveId))
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
        ///  计算文件/文件夹的完整路径
        /// </summary>
        /// <param name="itemNames"></param>
        /// <param name="itemParents"></param>
        /// <param name="pid"></param>
        /// <param name="path"></param>
        private void GetFileFullPath(Dictionary<string, string> itemNames, Dictionary<string, string> itemParents, string pid, ref string path)
        {
            if (pid == "root")
            {
                return;
            }

            if (itemNames.TryGetValue(pid, out string name))
            {
                path = $"{name}/{path}".ToUrlPath();

                if (pid != "root")
                {
                    if (itemParents.TryGetValue(pid, out string itemPid))
                    {
                        GetFileFullPath(itemNames, itemParents, itemPid, ref path);
                    }
                }
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
            using (_lockV2.Lock("AliyunDriveSearchFiles"))
            {
                var sw = new Stopwatch();
                sw.Start();

                _log.Information("云盘文件加载中");

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

                sw.Stop();
                _log.Information("云盘文件加载完成，用时 {@0} ms，总文件数：{@1}", sw.ElapsedMilliseconds, allItems.Count);
                sw.Restart();

                // 所有文件名称
                var itemNames = allItems.ToDictionary(c => c.FileId, c => c.Name);

                // 所有文件父级
                var itemParents = allItems.ToDictionary(c => c.FileId, c => c.ParentFileId);

                // 所有文件
                var itemDic = allItems.ToDictionary(c => c.FileId, c => c);

                // 计算文件的 key
                foreach (var item in allItems)
                {
                    var key = string.Empty;
                    GetFileFullPath(itemNames, itemParents, item.FileId, ref key);
                    if (!string.IsNullOrWhiteSpace(key))
                    {
                        item.Key = key;
                    }
                }

                // 计算文件的 parentKey
                foreach (var item in allItems)
                {
                    if (item.ParentFileId != "root")
                    {
                        if (itemDic.TryGetValue(item.ParentFileId, out var p) && p != null)
                        {
                            item.ParentKey = p.Key;
                        }
                    }
                }

                // 加载所有文件或指定目录前缀的文件
                var parentKeyPrefix = "";
                if (_driveRootId != "root")
                {
                    // 只加载指定的文件
                    var currentRoot = allItems.FirstOrDefault(x => x.FileId == _driveRootId);
                    if (currentRoot == null)
                    {
                        _log.Error("根目录不存在");
                        return;
                    }
                    parentKeyPrefix = $"{currentRoot.Key}/";
                }

                // 更新或添加新的文件
                foreach (var item in allItems)
                {
                    if (string.IsNullOrWhiteSpace(parentKeyPrefix)
                        || item.Key == _driveRootKey
                        || item.Key.StartsWith(parentKeyPrefix))
                    {
                        _files.AddOrUpdate(item.Key, item, (k, v) => item);
                    }
                }

                // 如果远程不存在的，则从队列中删除
                var currentKeys = allItems.ToDictionary(c => c.Key, c => true);
                var oldKeys = _files.Keys.ToList();
                foreach (var key in oldKeys)
                {
                    if (!currentKeys.ContainsKey(key))
                    {
                        _files.TryRemove(key, out _);
                    }
                }

                _driveLoadComplete = true;

                var fileCount = _files.Values.Count(x => x.IsFile);
                var folderCount = _files.Values.Count(x => x.IsFolder);

                _log.Information($"云盘文件加载处理完成，包含 {fileCount} 个文件，{folderCount} 个文件夹，用时：{sw.ElapsedMilliseconds} ms");
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
                if (_files.TryGetValue(oldpath, out var f) && f != null)
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
                        if (_files.ContainsKey(newpath))
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
                        _files.TryRemove(oldpath, out _);

                        // 如果相对是根目录文件
                        if (f.ParentFileId == "root")
                        {
                            f.Key = $"{f.Name}".TrimPath();
                            _files.AddOrUpdate(f.Key, f, (k, v) => f);
                        }
                        else
                        {
                            // 文件必须在备份路径中
                            var parent = _files.Where(c => c.Value.IsFolder && c.Value.FileId == f.ParentFileId).FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(parent.Key))
                            {
                                f.Key = $"{parent.Key}/{f.Name}".TrimPath();
                                f.ParentKey = parent.Key;
                                _files.AddOrUpdate(f.Key, f, (k, v) => f);
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
                            saveParentFileId = _driveRootId;
                        }
                        else
                        {
                            if (!_files.ContainsKey(keyPath))
                            {
                                using (_lockV2.Lock(keyPath))
                                {
                                    if (!_files.ContainsKey(keyPath))
                                    {
                                        AliyunDriveCreateFolders(keyPath);
                                    }
                                }
                            }

                            if (!_files.ContainsKey(keyPath))
                            {
                                _log.Error("创建文件夹失败 {@0}", keyPath);
                                return NtStatus.Error;
                            }
                            saveParentFileId = _files[keyPath].FileId;
                        }

                        if (replace)
                        {
                            // 先删除之前的文件，然后再移动
                            if (_files.TryGetValue(newpath, out var nf) && nf != null)
                            {
                                _driveApi.FileDelete(_driveId, nf.FileId, AccessToken, _driveMountConfig.IsRecycleBin);
                            }
                        }

                        var res = _driveApi.Move(_driveId, f.FileId, saveParentFileId, AccessToken, new_name: newName);
                        if (res.Exist)
                        {
                            return DokanResult.FileExists;
                        }

                        // 先移除旧的文件
                        _files.TryRemove(oldpath, out _);

                        // 移动成功了，设置父级 ID
                        f.ParentFileId = saveParentFileId;
                        f.Name = newName;
                        f.FileName = newName;

                        // 如果相对是根目录文件
                        if (f.ParentFileId == "root")
                        {
                            f.Key = $"{f.Name}".TrimPath();
                            _files.AddOrUpdate(f.Key, f, (k, v) => f);
                        }
                        else
                        {
                            // 文件必须在备份路径中
                            var parent = _files.Where(c => c.Value.IsFolder && c.Value.FileId == f.ParentFileId).FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(parent.Key))
                            {
                                f.Key = $"{parent.Key}/{f.Name}".TrimPath();
                                f.ParentKey = parent.Key;
                                _files.AddOrUpdate(f.Key, f, (k, v) => f);
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
                if (_files.TryGetValue(oldpath, out var fo) && fo != null && fo.IsFolder)
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
                        _files.TryRemove(oldpath, out _);

                        if (fo.ParentFileId == "root")
                        {
                            fo.Key = $"{fo.Name}".TrimPath();

                            // 当前目录在根路径
                            // /{当前路径}/
                            _files.AddOrUpdate(fo.Key, fo, (k, v) => fo);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _files.Where(c => c.Value.IsFolder && c.Value.FileId == fo.ParentFileId).FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(parent.Key))
                            {
                                fo.Key = $"{parent.Key}/{fo.Name}".TrimPath();
                                fo.ParentKey = parent.Key;

                                _files.AddOrUpdate(fo.Key, fo, (k, v) => fo);
                            }
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
                            saveParentFileId = _driveRootId;
                        }
                        else
                        {
                            if (!_files.ContainsKey(newParentPath))
                            {
                                // 移动时，父级文件夹一定是存在的
                                return NtStatus.NotADirectory;
                            }

                            saveParentFileId = _files[newParentPath].FileId;
                        }

                        var res = _driveApi.Move(_driveId, fo.FileId, saveParentFileId, AccessToken);
                        if (res.Exist)
                        {
                            return DokanResult.AlreadyExists;
                        }

                        fo.ParentFileId = saveParentFileId;

                        // 先移除旧的文件
                        _files.TryRemove(oldpath, out _);

                        if (saveParentFileId == "root")
                        {
                            fo.Key = $"{fo.Name}".TrimPath();

                            // 当前目录在根路径
                            // /{当前路径}/
                            _files.AddOrUpdate(fo.Key, fo, (k, v) => fo);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _files.Where(c => c.Value.IsFolder && c.Value.FileId == fo.ParentFileId).FirstOrDefault();
                            if (!string.IsNullOrWhiteSpace(parent.Key))
                            {
                                fo.Key = $"{parent.Key}/{fo.Name}".TrimPath();
                                fo.ParentKey = parent.Key;

                                _files.AddOrUpdate(fo.Key, fo, (k, v) => fo);
                            }
                        }
                    }

                    // 重新计算关联当前的子文件和子文件夹
                    // 确保以斜杠结尾
                    // 排除自身
                    var oldPathPrefix = $"{oldpath}/";
                    var newPathPrefix = $"{newpath}/";
                    var itemsToUpdate = _files.Where(kvp => kvp.Value.FileId != fo.FileId && kvp.Key.StartsWith(oldPathPrefix)).ToList();
                    foreach (var item in itemsToUpdate)
                    {
                        var subPath = item.Key.Substring(oldPathPrefix.Length);
                        var newPath = $"{newPathPrefix}{subPath}".ToUrlPath();

                        _files.TryRemove(item.Key, out _); // 移除旧路径
                        _files.TryAdd(newPath, item.Value); // 添加新路径
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
            var searchParentFileId = "root";
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
                    if (_files.TryGetValue(contactPath, out var folder) && folder != null)
                    {
                        searchParentFileId = folder.FileId;
                        continue;
                    }

                    // 查找当前文件夹是否存在
                    var subItemRes = _driveApi.GetSubFolders(_driveId, searchParentFileId, subPath, AccessToken);
                    var subItem = subItemRes.Items.FirstOrDefault(x => x.Name == subPath && x.Type == "folder" && x.ParentFileId == searchParentFileId);
                    if (subItem == null)
                    {
                        // 未找到目录
                        // 执行创建
                        var name = AliyunDriveHelper.EncodeFileName(subPath);
                        var data = _driveApi.CreateFolder(_driveId, searchParentFileId, name, AccessToken);
                        data.Name = data.FileName;

                        if (searchParentFileId == "root")
                        {
                            data.Key = $"{data.Name}".TrimPath();

                            // 当前目录在根路径
                            _files.AddOrUpdate(data.Key, data, (k, v) => data);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _files.Values.Where(c => c.IsFolder && c.FileId == searchParentFileId).FirstOrDefault();
                            if (parent == null)
                            {
                                throw new Exception("父级文件不存在");
                            }

                            data.Key = $"{parent.Key}/{data.Name}".TrimPath();
                            data.ParentKey = parent.Key;

                            // 当前目录在根路径
                            _files.AddOrUpdate(data.Key, data, (k, v) => data);
                        }

                        _log.Information("创建文件夹 {@0}", contactPath);
                    }
                    else
                    {
                        // 如果找到了，并且字典中不存在
                        var data = subItem;
                        data.Name = data.FileName;

                        if (searchParentFileId == "root")
                        {
                            data.Key = $"{data.Name}".TrimPath();

                            // 当前目录在根路径
                            _files.AddOrUpdate(data.Key, data, (k, v) => data);
                        }
                        else
                        {
                            // 计算父级路径
                            var parent = _files.Values.Where(c => c.IsFolder && c.FileId == searchParentFileId).FirstOrDefault();
                            if (parent == null)
                            {
                                throw new Exception("父级文件不存在");
                            }

                            data.Key = $"{parent.Key}/{data.Name}".TrimPath();
                            data.ParentKey = parent.Key;

                            // 当前目录在根路径
                            _files.AddOrUpdate(data.Key, data, (k, v) => data);
                        }

                        searchParentFileId = subItem.FileId;
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
            if (_files.TryGetValue(key, out var folder) && folder.IsFolder)
            {
                var res = _driveApi.FileDelete(_driveId, folder.FileId, AccessToken, _driveMountConfig.IsRecycleBin);
                if (!string.IsNullOrWhiteSpace(res?.FileId) || !string.IsNullOrWhiteSpace(res.AsyncTaskId))
                {
                    _files.TryRemove(key, out _);

                    DeleteFolderAndSubFiles(key);
                }
            }
        }

        /// <summary>
        /// 删除文件
        /// </summary>
        /// <param name="key"></param>
        private void AliyunDriveDeleteFile(string key)
        {
            if (_files.TryGetValue(key, out var folder))
            {
                // 如果没有返回结果，说明可能被删除了
                _driveApi.FileDelete(_driveId, folder.FileId, AccessToken, _driveMountConfig.IsRecycleBin);

                // 直接移除
                _files.TryRemove(key, out _);
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
            if (data.ParentFileId == "root")
            {
                var key = $"{data.Name}".TrimPath();
                data.Key = key;

                // 当前目录在根路径
                // /{当前路径}/
                _files.AddOrUpdate(key, data, (k, v) => data);
            }
            else
            {
                // 计算父级路径
                var parent = _files.Where(c => c.Value.IsFolder && c.Value.FileId == data.ParentFileId).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(parent.Key))
                {
                    var key = $"{parent.Key}/{data.Name}".TrimPath();
                    data.Key = key;
                    data.ParentKey = parent.Key;

                    // /{父级路径}/{当前路径}/
                    _files.AddOrUpdate(key, data, (k, v) => data);
                }
            }
        }

        /// <summary>
        /// 删除文件夹以及子文件和子文件夹
        /// </summary>
        /// <param name="key"></param>
        public void DeleteFolderAndSubFiles(string key)
        {
            if (!string.IsNullOrWhiteSpace(key))
            {
                _files.TryRemove(key, out _);

                // 子文件夹处理，以当前路径开头的子文件和子文件夹
                // 重新计算关联当前的子文件和子文件夹
                // 确保以斜杠结尾
                var keyPrefix = $"{key.TrimPath()}/";
                var keys = _files.Keys.Where(c => c.StartsWith(keyPrefix)).ToList();
                foreach (var k in keys)
                {
                    _files.TryRemove(k, out _);
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
                if (string.IsNullOrWhiteSpace(parentFileId))
                {
                    return;
                }

                // 首次未加载完成，不处理
                if (!_driveLoadComplete)
                {
                    return;
                }

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

                // 如果是根目录
                if (parentFileId == "root")
                {
                    foreach (var item in allItems)
                    {
                        if (item.ParentFileId == "root")
                        {
                            // 文件路径
                            var key = $"{item.Name}".ToUrlPath();

                            item.Key = key;
                            item.ParentKey = "";
                            _files.AddOrUpdate(key, item, (k, v) => item);
                        }
                    }

                    // 不存在时删除
                    var olds = _files.Values.Where(c => c.ParentFileId == "root").ToList();
                    foreach (var old in olds)
                    {
                        if (!allItems.Any(x => x.FileId == old.FileId))
                        {
                            DeleteFolderAndSubFiles(old.Key);
                        }
                    }
                }
                else
                {
                    // 找到父级文件夹，并计算
                    if (_files.TryGetValue(parentKey, out var parent) && parent.IsFolder)
                    {
                        foreach (var item in allItems)
                        {
                            // 文件路径
                            var key = $"{parent.Key}/{item.Name}".ToUrlPath();

                            item.Key = key;
                            item.ParentKey = parentKey;
                            _files.AddOrUpdate(key, item, (k, v) => item);
                        }

                        // 不存在时删除
                        var olds = _files.Values.Where(c => c.ParentFileId == parent.FileId).ToList();
                        foreach (var old in olds)
                        {
                            if (!allItems.Any(x => x.FileId == old.FileId))
                            {
                                DeleteFolderAndSubFiles(old.Key);
                            }
                        }
                    }
                }

                _log.Information($"云盘文件加载完成，包含 {allItems.Count} 个文件/文件夹");
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
                if (!string.IsNullOrWhiteSpace(_driveMountConfig.MountPath))
                {
                    var saveRootSubPaths = _driveMountConfig.MountPath.ToSubPaths();
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

                    _driveRootId = searchParentFileId;
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
            // 非 windows 系统暂不支持挂载
            if (!Platform.IsClientWindows)
            {
                return;
            }

            var dokanLogger = new ConsoleLogger("[Dokan] ");
            var dokanInstanceBuilder = new DokanInstanceBuilder(new Dokan(dokanLogger))
                .ConfigureOptions(options =>
                {
                    // DokanOptions.DebugMode | DokanOptions.EnableNotificationAPI | DokanOptions.NetworkDrive  | DokanOptions.StderrOutput;

                    options.Options = DokanOptions.FixedDrive;

                    if (_driveMountConfig.MountReadOnly)
                    {
                        options.Options |= DokanOptions.WriteProtection;
                    }

                    options.MountPoint = _driveMountConfig.MountPoint;
                });

            _dokanMountTask = new Task(() =>
            {
                using var dokanInstance = dokanInstanceBuilder.Build(this);
                _dokanInstance = dokanInstance;

                // 初始化文件列表
                AliyunDriveInitFiles();

                // 创建轮询计划
                // 每 15 分钟更新一次列表
                var scheduler = new QuartzCronScheduler("0 0/15 * * * ?", () =>
                {
                    try
                    {
                        AliyunDriveSearchFiles();
                    }
                    catch (Exception ex)
                    {
                        _log.Error(ex, "轮询计划作业查询文件异常");
                    }
                });
                scheduler.Start();

                // 开启一个作业，用于处理打开文件夹的作业处理
                Task.Run(async () =>
                {
                    while (true)
                    {
                        // 等待信号通知
                        _openFolderMre.WaitOne();

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
                                            // 说明刷新的是根目录
                                            await AliyunDriveLoadFolderFiles("", "root");
                                        }
                                        else
                                        {
                                            // 刷新的是文件夹
                                            if (_files.TryGetValue(key, out var fo) && fo != null && fo.IsFolder)
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
                        _openFolderMre.Reset();
                    }
                });

                // await _dokanInstance.WaitForFileSystemClosedAsync(uint.MaxValue);

                _dokanMre.WaitOne();

                // 销毁计划
                scheduler.Dispose();
            });

            _dokanMountTask.Start();
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
    }
}