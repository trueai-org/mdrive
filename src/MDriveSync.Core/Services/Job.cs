using MDriveSync.Core.Services;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Polly;
using RestSharp;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MDriveSync.Core
{
    /// <summary>
    /// https://www.yuque.com/aliyundrive/zpfszx/ezlzok
    ///
    /// TODO：
    /// OK: 令牌自动刷新
    /// job 管理
    ///
    /// 恢复到本地功能
    /// 请求中如果令牌过期了，则自动刷新
    /// 创建文件夹加锁执行
    /// 分块上传 50~100mb
    /// 快速对比本地文件与服务器文件差异
    /// 集成 kopia + 云盘
    /// 开启文件监听，如果开启文件监听，如果文件发生变化时则重新计算完整 hash 并同步
    /// 注意，针对快捷方式，可能会出现问题
    /// 排除文件/夹
    /// 增加存储到备份盘/资源库
    /// 首次备份时，计算全文 hash
    /// 由于加载列表限流，加载列表时，同时计算本地 hash
    ///
    ///
    /// 恢复权限和时间
    ///
    /// =====
    /// 客户端
    /// WEB UI
    ///
    /// ===============
    ///
    /// 空文件、空文件夹也同步
    /// 
    /// ======
    /// 待定
    /// 
    /// 注意，针对快捷方式，可能会出现问题
    /// </summary>
    public class Job : IDisposable
    {
        private ILogger _log;

        private readonly object _lock = new object();

        // 设置列表每次请求之间的间隔为 250ms
        private const int _listRequestInterval = 250;

        private readonly RestClient _apiClient;
        private readonly HttpClient _uploadHttpClient;

        /// <summary>
        /// 所有本地文件列表
        /// </summary>
        public ConcurrentDictionary<string, LocalFileInfo> _localFiles = new();

        /// <summary>
        /// 所有本地文件夹
        /// </summary>
        public ConcurrentDictionary<string, LocalFileInfo> _localFolders = new();

        /// <summary>
        /// 所有本地还原文件列表
        /// </summary>
        public ConcurrentDictionary<string, LocalFileInfo> _localRestoreFiles = new();

        /// <summary>
        /// 所有本地还原文件夹
        /// </summary>
        public ConcurrentDictionary<string, LocalFileInfo> _localRestoreFolders = new();

        /// <summary>
        /// 所有云盘文件
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFiles = new();

        /// <summary>
        /// 所有云盘文件夹
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> _driveFolders = new();

        /// <summary>
        /// 备份计划任务
        /// </summary>
        public ConcurrentDictionary<string, QuartzCronScheduler> _schedulers = new();

        // 备份盘/资源盘 ID
        private string _driveId;

        // 客户端信息
        private AliyunDriveConfig _driveConfig;

        // 作业配置
        private JobConfig _jobConfig;

        // 远程备份还原到本地目录
        private string _localRestorePath;

        // 远程备份保存的目录
        private string _driveSavePath;

        private readonly IMemoryCache _cache;

        private const string TOEKN_KEY = "TOKEN";

        public JobState State { get; private set; }

        /// <summary>
        /// 本地文件缓存
        /// 持久化存储定时器，将内存中的信息保留到本地
        /// 每 5 分钟持久化存储
        /// </summary>
        private Timer _localPersistentTimer;

        /// <summary>
        /// 是否已记载完成本地文件
        /// </summary>
        private bool _isLoadLocalFiles;

        /// <summary>
        /// 本地缓存文件名称
        /// </summary>
        private string _localFileCacheName;

        /// <summary>
        /// 本地文件监听
        /// </summary>
        private List<FileSystemWatcher> _localWatchers = new List<FileSystemWatcher>();

        public Job(AliyunDriveConfig driveConfig, JobConfig jobConfig, ILogger log)
        {
            _log = log;

            // 本地缓存
            _cache = new MemoryCache(new MemoryCacheOptions());

            // 接口请求
            var options = new RestClientOptions(ProviderApiHelper.ALIYUNDRIVE_API_HOST)
            {
                MaxTimeout = -1
            };
            _apiClient = new RestClient(options);
            _driveConfig = driveConfig;
            _jobConfig = jobConfig;

            // 上传请求
            // 上传链接最大有效 1 小时
            // 设置 45 分钟超时
            // 在 HttpClient 中，一旦发送了第一个请求，就不能再更改其配置属性，如超时时间 (Timeout)。
            // 这是因为 HttpClient 被设计为可重用的，它的属性设置在第一个请求发出之后就被固定下来。
            _uploadHttpClient = new HttpClient();
            _uploadHttpClient.Timeout = TimeSpan.FromMinutes(45);

            // 非禁用状态时，创建默认为 none 状态
            if (jobConfig.State != JobState.Disabled)
            {
                State = JobState.None;
            }
            else
            {
                State = JobState.Disabled;
            }
        }

        /// <summary>
        /// 定期检查
        /// </summary>
        /// <returns></returns>
        public async Task Maintenance()
        {
            switch (State)
            {
                case JobState.None:
                    {
                        // 初始化
                        await Initialize();
                    }
                    break;

                case JobState.Starting:
                    {
                        // 启动中
                        await Start();
                    }
                    break;

                case JobState.Idle:
                    {
                        // 开始计算业务
                        // 计算下一次执行备份等计划作业
                        foreach (var cron in _jobConfig.Schedules)
                        {
                            if (!_schedulers.TryGetValue(cron, out var sch) || sch == null)
                            {
                                // 创建备份计划
                                var scheduler = new QuartzCronScheduler(cron, async () =>
                                {
                                    _log.LogInformation("执行任务：" + DateTime.Now);
                                    await StartBackup();
                                });
                                scheduler.Start();
                                _schedulers[cron] = scheduler;
                            }
                        }
                    }
                    break;

                case JobState.Scanning:
                    {
                        // TODO
                        // 校验完成，切换为空闲
                        ChangeState(JobState.Idle);
                    }
                    break;

                case JobState.BackingUp:
                    {
                        // 备份中
                    }
                    break;

                case JobState.Restoring:
                    break;

                case JobState.Verifying:
                    break;

                case JobState.Queued:
                    break;

                case JobState.Completed:
                    break;

                case JobState.Paused:
                    break;

                case JobState.Error:
                    break;

                case JobState.Cancelling:
                    break;

                case JobState.Cancelled:
                    break;

                case JobState.Disabled:
                    break;

                default:
                    break;
            }

            GC.Collect();
        }

        /// <summary>
        /// 初始化作业（路径、云盘信息等）
        /// </summary>
        /// <returns></returns>
        private async Task Initialize()
        {
            ChangeState(JobState.Initializing);

            _log.LogInformation("初始化检查中");

            var sw = new Stopwatch();
            sw.Start();

            // 格式化路径
            _localRestorePath = _jobConfig.Restore.TrimPath();
            _driveSavePath = _jobConfig.Target.ToUrlPath();

            // 格式化备份目录
            var sources = _jobConfig.Sources.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.TrimPath()).Distinct().ToList();
            _jobConfig.Sources.Clear();
            foreach (var item in sources)
            {
                var dir = new DirectoryInfo(item);
                if (!dir.Exists)
                {
                    dir.Create();
                }
                _jobConfig.Sources.Add(dir.FullName);
            }

            _localFileCacheName = Path.Combine(".cache", $"local_files_cache_{_jobConfig.Id}.txt");

            // 获取云盘信息
            InitAliyunDriveInfo();

            sw.Stop();
            _log.LogInformation($"初始化检查完成，用时：{sw.ElapsedMilliseconds}ms");

            // 初始化完成处于启动中
            ChangeState(JobState.Starting);

            await Maintenance();
        }

        /// <summary>
        /// 启动后台作业、启动缓存、启动监听等
        /// </summary>
        /// <returns></returns>
        public async Task Start()
        {
            ChangeState(JobState.Starting);

            _log.LogInformation("作业启动中");
            var sw = new Stopwatch();
            sw.Start();

            // 每 5 分钟持久化本地缓存
            _localPersistentTimer = new Timer(PersistentDoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));

            // 监听函数
            // TODO 如果文件发生变化，则重新计算 hash 请清空 sha1

            //foreach (var path in _pathsToWatch)
            //{
            //    var watcher = new FileSystemWatcher(path);
            //    watcher.NotifyFilter = NotifyFilters.FileName |
            //            NotifyFilters.DirectoryName |
            //            NotifyFilters.LastWrite |
            //            NotifyFilters.Size;

            //    watcher.Filter = "*.*"; // 监控所有文件
            //    watcher.IncludeSubdirectories = true; // 包括子目录

            //    watcher.Created += OnChanged;
            //    watcher.Deleted += OnChanged;
            //    watcher.Renamed += OnRenamed;
            //    watcher.Changed += OnChanged;
            //    watcher.Error += OnError;

            //    watcher.EnableRaisingEvents = true;
            //    _watchers.Add(watcher);
            //}

            //// 执行任务
            //ChangeState(JobState.Scanning);
            //await ScanAsync();

            //ChangeState(JobState.BackingUp);
            //await BackupAsync();

            //// ... 其他操作 ...

            //ChangeState(JobState.Completed);

            // 启动完成，处于空闲
            ChangeState(JobState.Idle);

            sw.Stop();
            _log.LogInformation($"作业启动完成，用时：{sw.ElapsedMilliseconds}ms");

            // 再次执行检查
            await Maintenance();
        }

        private void OnChanged(object source, FileSystemEventArgs e)
        {
            //_logger.LogInformation($"文件更改: {e.FullPath}, 类型: {e.ChangeType}");
        }

        private void OnRenamed(object source, RenamedEventArgs e)
        {
            //_logger.LogInformation($"文件重命名: {e.OldFullPath} 更改为 {e.FullPath}");
        }

        private void OnError(object source, ErrorEventArgs e)
        {
            //_logger.LogError($"文件系统监听发生错误: {e.GetException()}");
        }

        /// <summary>
        /// 持久化本地文件缓存
        /// </summary>
        /// <param name="state"></param>
        private void PersistentDoWork(object state)
        {
            // 如果加载完成本地文件
            if (!_isLoadLocalFiles)
                return;

            lock (_lock)
            {
                var dir = Path.GetDirectoryName(_localFileCacheName);
                if (!Directory.Exists(dir))
                {
                    Directory.CreateDirectory(dir);
                }
            }

            // 序列化回 JSON
            var updatedJsonString = JsonSerializer.Serialize(_localFiles, new JsonSerializerOptions
            {
                Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
                WriteIndented = true
            });
            // 写入文件
            File.WriteAllText(_localFileCacheName, updatedJsonString, Encoding.UTF8);
        }

        public void Pause()
        {
            // 暂停逻辑
            ChangeState(JobState.Paused);
        }

        public void Resume()
        {
            // 恢复逻辑
            // 需要根据实际情况确定恢复后的状态
        }

        public void Cancel()
        {
            // 取消逻辑
            ChangeState(JobState.Cancelling);
        }

        private void ChangeState(JobState newState)
        {
            State = newState;
            // 触发状态改变事件 (如果有)
        }

        private Task ScanAsync()
        {
            // 扫描逻辑
            return Task.CompletedTask;
        }

        private Task BackupAsync()
        {
            // 备份逻辑
            return Task.CompletedTask;
        }

        // 其他任务相关的异步方法...

        private void Running()
        {
            //LogMessage msg = null;
            //while (true)
            //{
            //    // 等待信号通知
            //    _mre.WaitOne();

            //    try
            //    {
            //        // 判断是否有内容需要如磁盘 从列队中获取内容，并删除列队中的内容
            //        while (_cq.Count > 0 && _cq.TryDequeue(out msg))
            //        {
            //            if (msg == null)
            //                break;
            //            if (msg.IsSplit)
            //                LogSingleton.Instance.WriteLogSplit(msg.FileName, msg.Message);
            //            else
            //                LogSingleton.Instance.WriteLog(msg.FileName, msg.Message);
            //        }
            //    }
            //    catch (Exception ex)
            //    {
            //        LogSingleton.Instance.WriteLog("error_", "log queue error: " + ex.Message + (msg == null ? "" : "\r\nmsg:" + msg.Message));
            //    }

            //    // 重新设置信号
            //    _mre.Reset();
            //}
        }

        /// <summary>
        /// 获取当前有效的访问令牌
        /// </summary>
        public string AccessToken
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
        public string InitToken()
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
        /// 获取用户 drive 信息
        /// </summary>
        /// <returns></returns>
        public void InitAliyunDriveInfo()
        {
            var request = new RestRequest("/adrive/v1.0/user/getDriveInfo", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");
            var response = _apiClient.Execute<AliyunDriveInfo>(request);
            if (response.StatusCode == System.Net.HttpStatusCode.OK)
            {
                var data = response.Data!;

                _driveId = data.DefaultDriveId;

                if (_jobConfig.DefaultDrive == "backup" && string.IsNullOrWhiteSpace(data.BackupDriveId))
                {
                    _driveId = data.BackupDriveId;
                }
                else if (_jobConfig.DefaultDrive == "resource" && !string.IsNullOrWhiteSpace(data.ResourceDriveId))
                {
                    _driveId = data.ResourceDriveId;
                }
            }
        }

        /// <summary>
        /// 获取用户空间信息
        /// </summary>
        /// <returns></returns>
        public async Task InitAliyunDriveSpaceInfo()
        {
            var request = new RestRequest("/adrive/v1.0/user/getSpaceInfo", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");
            var response = await _apiClient.ExecuteAsync<AliyunDriveSpaceInfo>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
            }
        }

        /// <summary>
        /// 获取用户 VIP 信息
        /// </summary>
        /// <returns></returns>
        public async Task InitAliyunDriveVipInfo()
        {
            var request = new RestRequest("/v1.0/user/getVipInfo", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");
            var response = await _apiClient.ExecuteAsync<AliyunDriveVipInfo>(request);
            if (response.StatusCode == HttpStatusCode.OK)
            {
            }
        }

        /// <summary>
        /// 获取文件夹路径 key
        /// 将本地路径转为 {备份目录}/{子目录}
        /// </summary>
        /// <param name="localRootPath"></param>
        /// <param name="directoryInfo"></param>
        /// <returns></returns>
        private string GetDirectoryKey(string localRootPath, DirectoryInfo directoryInfo)
        {
            var localRootInfo = new DirectoryInfo(localRootPath);
            var rootPathName = localRootInfo.Name;
            var subPath = directoryInfo.FullName.ToUrlPath(localRootPath);
            return $"{rootPathName}/{subPath}".TrimPath();
        }

        /// <summary>
        /// 获取文件路径 key
        /// </summary>
        /// <param name="localRootPath"></param>
        /// <param name="fileInfo"></param>
        /// <returns></returns>
        private string GetFileKey(string localRootPath, FileInfo fileInfo)
        {
            var localRootInfo = new DirectoryInfo(localRootPath);
            var rootPathName = localRootInfo.Name;
            var subPath = fileInfo.FullName.ToUrlPath(localRootInfo.FullName);
            return $"{rootPathName}/{subPath}".TrimPath();
        }

        /// <summary>
        /// 获取文件路径 key
        /// </summary>
        /// <param name="localRootPath"></param>
        /// <param name="fileInfo"></param>
        /// <returns></returns>
        private string GetFileKeyPath(string localRootPath, FileInfo fileInfo)
        {
            var localRootInfo = new DirectoryInfo(localRootPath);
            var rootPathName = localRootInfo.Name;
            var subPath = Path.GetDirectoryName(fileInfo.FullName).ToUrlPath(localRootInfo.FullName);
            return $"{rootPathName}/{subPath}".TrimPath();
        }

        // 定义一个方法来检查一个给定的路径是否应该被过滤
        public bool ShouldFilter(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            // 检查是否有过滤规则
            if (_jobConfig.Filters.Count > 0)
            {
                foreach (var item in _jobConfig.Filters)
                {
                    // 忽略注释
                    if (item.StartsWith("#")) continue;

                    // 处理其他规则
                    var pattern = ConvertToRegexPattern(item);
                    var regex = new Regex(pattern);

                    if (regex.IsMatch(path))
                    {
                        return true;
                    }
                }
            }

            // 如果没有匹配的过滤规则，则返回 false
            return false;
        }

        // 将Kopia规则转换为正则表达式
        private string ConvertToRegexPattern(string kopiaPattern)
        {
            var pattern = Regex.Escape(kopiaPattern)
                               .Replace("\\*", ".*")       // 处理 *
                               .Replace("\\?", ".")        // 处理 ?
                               .Replace("\\[", "[")        // 处理范围匹配
                               .Replace("\\]", "]");

            if (pattern.StartsWith("/")) // 根目录匹配
            {
                pattern = "^" + pattern + "$";
            }
            else if (pattern.StartsWith("**/")) // 双通配符
            {
                pattern = ".*" + pattern.TrimStart('*');
            }

            return pattern;
        }

        /// <summary>
        /// 开始比较本地文件与云盘文件
        /// </summary>
        /// <returns></returns>
        public async Task Search()
        {
            var now = DateTime.Now;

            var processorCount = Environment.ProcessorCount;
            if (processorCount <= 0)
            {
                processorCount = 1;
            }
            else if (processorCount >= 12)
            {
                processorCount = 4;
            }
            else if (processorCount >= 24)
            {
                processorCount = 8;
            }

#if DEBUG
            processorCount = 1;
#endif

            // 序列化回 JSON
            var oldLocalFiles = new ConcurrentDictionary<string, LocalFileInfo>();
            if (File.Exists(_localFileCacheName))
            {
                var localJson = File.ReadAllText(_localFileCacheName, Encoding.UTF8);
                if (!string.IsNullOrWhiteSpace(localJson))
                {
                    // 从本地缓存中加载
                    oldLocalFiles = JsonSerializer.Deserialize<ConcurrentDictionary<string, LocalFileInfo>>(localJson);
                }
            }

            var options = new ParallelOptions() { MaxDegreeOfParallelism = processorCount };

            // 循环多个目录处理
            foreach (var backupRootPath in _jobConfig.Sources)
            {
                var backupRootInfo = new DirectoryInfo(backupRootPath);
                var backupDirs = Directory.EnumerateDirectories(backupRootPath, "*", SearchOption.AllDirectories);

                // 加载文件
                LoadFiles(backupRootPath);
                Parallel.ForEach(backupDirs, options, LoadFiles);

                void LoadFiles(string dir)
                {
                    try
                    {
                        var dirInfo = new DirectoryInfo(dir);
                        if (!dirInfo.Exists)
                        {
                            return;
                        }

                        // 所有本地文件夹
                        var loDir = new LocalFileInfo()
                        {
                            FullPath = dirInfo.FullName,
                            Key = GetDirectoryKey(backupRootPath, dirInfo),
                            CreationTime = dirInfo.CreationTime,
                            LastWriteTime = dirInfo.LastWriteTime,
                            Name = dirInfo.Name,
                        };

                        // 过滤文件夹
                        var cpath = loDir.Key.ToUrlPath(backupRootInfo.Name).TrimPath();
                        if (!string.IsNullOrWhiteSpace(cpath))
                        {
                            if (ShouldFilter($"/{cpath}/"))
                            {
                                return;
                            }
                        }

                        _localFolders.AddOrUpdate(loDir.Key, loDir, (k, v) => loDir);

                        var files = Directory.EnumerateFiles(dir);
                        foreach (var file in files)
                        {
                            var fileInfo = new FileInfo(file);
                            if (!fileInfo.Exists)
                            {
                                continue;
                            }

                            // 所有本地文件
                            var lf = new LocalFileInfo()
                            {
                                IsFile = true,
                                FullPath = fileInfo.FullName,
                                Key = GetFileKey(backupRootPath, fileInfo),
                                KeyPath = GetFileKeyPath(backupRootPath, fileInfo),
                                CreationTime = fileInfo.CreationTime,
                                LastWriteTime = fileInfo.LastWriteTime,
                                Length = fileInfo.Length,
                                Name = fileInfo.Name,
                            };

                            // 过滤文件
                            var cfile = lf.Key.ToUrlPath(backupRootInfo.Name).TrimPath();
                            if (!string.IsNullOrWhiteSpace(cfile))
                            {
                                if (ShouldFilter($"/{cfile}"))
                                {
                                    continue;
                                }
                            }

                            // 计算 hash
                            lf.Hash = HashHelper.ComputeFileHash(file, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm);

                            // 如果没有获取到，从本地缓存中对比获取 sha1
                            if (oldLocalFiles.TryGetValue(lf.Key, out var cacheFile) && cacheFile != null)
                            {
                                // 如果本地缓存 hash 和 当前文件一致，则使用缓存中的 sha1
                                if (cacheFile.Hash == lf.Hash
                                    && !string.IsNullOrWhiteSpace(cacheFile.Hash)
                                    && !string.IsNullOrWhiteSpace(cacheFile.Sha1)
                                    && lf.LastWriteTime == cacheFile.LastWriteTime
                                    && lf.Length == cacheFile.Length
                                    && lf.CreationTime == cacheFile.CreationTime)
                                {
                                    lf.Sha1 = cacheFile.Sha1;
                                }
                            }

                            _localFiles.AddOrUpdate(lf.Key, lf, (k, v) =>
                            {
                                // 如果旧的 hash = 当前 hash，说明文件没有变
                                // 如果扫描过的文件包含 sha1 则使用曾经扫描过的

                                // 且时间、大小无变化
                                if (lf.Hash == v.Hash && !string.IsNullOrWhiteSpace(v.Hash) && !string.IsNullOrWhiteSpace(v.Sha1)
                                && lf.LastWriteTime == v.LastWriteTime && lf.Length == v.Length && lf.CreationTime == v.CreationTime)
                                {
                                    // 如果之前内存中有文件的 sha1
                                    lf.Sha1 = v.Sha1;
                                }

                                return lf;
                            });
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _log.LogInformation("Access Denied: " + ex.Message);
                    }
                    catch (Exception ex)
                    {
                        _log.LogInformation(ex.Message);
                    }
                }
            }

            // 持久化本地文件
            _isLoadLocalFiles = true;
            PersistentDoWork(null);

            _log.LogInformation($"开始备份 {_localFiles.Count}, 扫描文件用时: {(DateTime.Now - now).TotalMilliseconds}ms");

            var process = 0;
            var total = _localFiles.Count;

            // 并行上传
            await Parallel.ForEachAsync(_localFiles, options, async (item, cancellationToken) =>
            {
                // 上传文件到阿里云盘
                await UploadFileToAliyunDisk(item.Value);

                Interlocked.Increment(ref process);

                _log.LogInformation($"上传中 {process}/{total}，用时：{(DateTime.Now - now).TotalMilliseconds}ms，{item.Key}");
            });

            _log.LogInformation($"备份完成 {_localFiles.Count}，用时：{(DateTime.Now - now).TotalMilliseconds}ms");
        }

        public async Task Restore()
        {
            var now = DateTime.Now;

            // 自动线程梳理
            var processorCount = Environment.ProcessorCount;
            if (processorCount <= 0)
            {
                processorCount = 1;
            }
            else if (processorCount >= 12)
            {
                processorCount = 4;
            }
            else if (processorCount >= 24)
            {
                processorCount = 8;
            }

#if DEBUG
            processorCount = 1;
#endif

            var options = new ParallelOptions() { MaxDegreeOfParallelism = processorCount };
            var dirs = Directory.EnumerateDirectories(_localRestorePath, "*", SearchOption.AllDirectories);

            // 加载文件
            Load(_localRestorePath);
            Parallel.ForEach(dirs, options, Load);

            void Load(string dir)
            {
                try
                {
                    var dirInfo = new DirectoryInfo(dir);
                    if (!dirInfo.Exists)
                    {
                        return;
                    }

                    // 所有本地文件夹
                    var loDir = new LocalFileInfo()
                    {
                        FullPath = dirInfo.FullName,
                        Key = GetDirectoryKey(_localRestorePath, dirInfo),
                        CreationTime = dirInfo.CreationTime,
                        LastWriteTime = dirInfo.LastWriteTime,
                        Name = dirInfo.Name,
                    };
                    _localRestoreFolders.AddOrUpdate(loDir.Key, loDir, (k, v) => loDir);

                    var files = Directory.EnumerateFiles(dir);
                    foreach (var file in files)
                    {
                        var fileInfo = new FileInfo(file);
                        if (!fileInfo.Exists)
                        {
                            continue;
                        }

                        // 所有本地文件
                        var lf = new LocalFileInfo()
                        {
                            IsFile = true,
                            FullPath = fileInfo.FullName,
                            Key = GetFileKey(_localRestorePath, fileInfo),
                            KeyPath = GetFileKeyPath(_localRestorePath, fileInfo),
                            CreationTime = fileInfo.CreationTime,
                            LastWriteTime = fileInfo.LastWriteTime,
                            Length = fileInfo.Length,
                            Name = fileInfo.Name,
                            Hash = HashHelper.ComputeFileHash(file, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm)
                        };

                        _localRestoreFiles.AddOrUpdate(lf.Key, lf, (k, v) => lf);
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    _log.LogInformation("Access Denied: " + ex.Message);
                }
                catch (Exception ex)
                {
                    _log.LogInformation(ex.Message);
                }
            }

            _log.LogInformation($"开始还原 {_localRestoreFiles.Count}, time: {(DateTime.Now - now).TotalMilliseconds}ms");

            var process = 0;
            var total = _driveFiles.Count;

            // 先处理文件夹
            foreach (var item in _driveFolders)
            {
                var subPaths = item.Key.ToUrlPath(_driveSavePath)
                              .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var savePath = Path.Combine(_localRestorePath, Path.Combine(subPaths));

                var tmpPath = Path.GetDirectoryName(savePath);
                lock (_lock)
                {
                    if (!Directory.Exists(tmpPath))
                    {
                        Directory.CreateDirectory(tmpPath);
                    }
                }
            }

            // 开启并行下载
            await Parallel.ForEachAsync(_driveFiles, options, async (item, cancellationToken) =>
            {
                try
                {
                    // 阿里云盘文件到本地
                    var savePath = _localRestorePath;

                    // 根目录
                    if (item.Value.ParentFileId == "root")
                    {
                    }
                    else
                    {
                        // 子目录
                        var parent = _driveFolders.First(x => x.Value.IsFolder && x.Value.FileId == item.Value.ParentFileId)!;

                        // 移除云盘前缀
                        var subPaths = parent.Key.ToUrlPath(_driveSavePath)
                            .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        savePath = Path.Combine(_localRestorePath, Path.Combine(subPaths));
                    }

                    var finalFilePath = Path.Combine(savePath, item.Value.Name);
                    if (File.Exists(finalFilePath))
                    {
                        // 验证本地是否已存在文件，并比较 sha1 值
                        var hash = HashHelper.ComputeFileHash(finalFilePath, "sha1");
                        if (hash == item.Value.ContentHash)
                        {
                            _log.LogInformation($"文件已存在，跳过 {finalFilePath}");
                            return;
                        }
                    }

                    // 获取详情 url
                    var request = new RestRequest("/adrive/v1.0/openFile/get", Method.Post);
                    request.AddHeader("Content-Type", "application/json");
                    request.AddHeader("Authorization", $"Bearer {AccessToken}");
                    object body = new
                    {
                        drive_id = _driveId,
                        file_id = item.Value.FileId
                    };
                    request.AddBody(body);
                    var response = await _apiClient.ExecuteAsync<AliyunDriveFileItem>(request);
                    if (response.StatusCode == HttpStatusCode.OK && !string.IsNullOrWhiteSpace(response.Data.Url))
                    {
                        await DownloadFileAsync(response.Data.Url, item.Value.Name, item.Value.ContentHash, savePath);
                    }
                    else
                    {
                        throw response.ErrorException;
                    }

                    // 如果是大文件 TODO 则通过下载链接下载文件
                }
                catch (Exception)
                {
                    throw;
                }
                finally
                {
                    process++;
                    _log.LogInformation($"下载中 {process}/{total}, time: {(DateTime.Now - now).TotalSeconds}s, {item.Key},{item.Value.Name}");
                }
            });

            //foreach (var item in _driveFiles)
            //{
            //}

            // 清理临时文件
            var tempPath = Path.Combine(_localRestorePath, ".duplicaticache");
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }

            var tmpFiles = Directory.GetFiles(_localRestorePath, "*.duplicatidownload", SearchOption.AllDirectories);
            foreach (var file in tmpFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
            }
        }

        private async Task DownloadFileAsync(string url, string fileName, string fileSha1, string savePath)
        {
            var tempFilePath = Path.Combine(_localRestorePath, ".duplicaticache", $"{fileName}.{Guid.NewGuid():N}.duplicatidownload");
            var finalFilePath = Path.Combine(savePath, fileName);

            using (var httpClient = new HttpClient())
            {
                try
                {
                    // 设置 45 分钟超时
                    httpClient.Timeout = TimeSpan.FromMinutes(45);

                    var tmpPath = Path.GetDirectoryName(tempFilePath);
                    var path = Path.GetDirectoryName(finalFilePath);

                    lock (_lock)
                    {
                        if (!Directory.Exists(tmpPath))
                        {
                            Directory.CreateDirectory(tmpPath);
                        }
                        if (!Directory.Exists(path))
                        {
                            Directory.CreateDirectory(path);
                        }
                    }

                    using (var response = await httpClient.GetAsync(url, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();

                        // 读取响应流
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        {
                            // 创建一个新文件，并将响应流中的内容写入文件
                            using (var fileStream = new FileStream(tempFilePath, FileMode.Create, FileAccess.Write, FileShare.None, 8192, true))
                            {
                                await stream.CopyToAsync(fileStream);
                            }
                        }
                    }

                    // 校验 hash 值
                    var sha1 = HashHelper.ComputeFileHash(tempFilePath, "sha1");
                    if (!string.IsNullOrWhiteSpace(fileSha1) && sha1 != fileSha1)
                    {
                        throw new Exception("文件内容不一致");
                    }

                    // 重命名临时文件为最终文件
                    // 强制覆盖本地
                    File.Move(tempFilePath, finalFilePath, true);
                    _log.LogInformation($"文件下载完成: {fileName}");
                }
                catch (Exception ex)
                {
                    _log.LogInformation($"下载文件时出错: {ex.Message}");

                    // 如果存在临时文件，删除它
                    if (File.Exists(tempFilePath))
                    {
                        File.Delete(tempFilePath);
                    }
                }
            }
        }

        private async Task UploadFileToAliyunDisk(LocalFileInfo localFileInfo, bool needPreHash = true)
        {
            try
            {
                var filePath = localFileInfo.FullPath;
                var fileInfo = new FileInfo(filePath);
                if (!fileInfo.Exists)
                {
                    throw new ArgumentException("文件不存在");
                }

                // 文件名
                var name = AliyunDriveHelper.EncodeFileName(Path.GetFileName(filePath));

                // 存储目录 ID
                var saveParentFileId = "root";

                // 计算保存存储目录
                var saveParentPath = $"{_driveSavePath}/{localFileInfo.KeyPath}".TrimPath();

                // 计算文件存储路径
                var saveFilePath = $"{saveParentPath}/{name}".TrimPath();

                // 判断云盘是否存在路径，不存在则创建
                if (!string.IsNullOrWhiteSpace(saveParentPath))
                {
                    if (!_driveFolders.ContainsKey(saveParentPath))
                    {
                        var savePaths = saveParentPath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        var savePathsParentFileId = "root";
                        foreach (var subPath in savePaths)
                        {
                            savePathsParentFileId = await CreateFolder(subPath, savePathsParentFileId);
                        }
                    }

                    if (!_driveFolders.ContainsKey(saveParentPath))
                    {
                        throw new Exception("文件夹创建失败");
                    }

                    saveParentFileId = _driveFolders[saveParentPath].FileId;
                }

                // 本地文件没有 sha1 时，计算本地文件的 sha1
                if (string.IsNullOrWhiteSpace(localFileInfo.Sha1))
                {
                    localFileInfo.Sha1 = HashHelper.ComputeFileHash(filePath, "sha1");
                }

                // 如果文件已上传则跳过
                // 需要对比文件差异 sha1
                if (_driveFiles.TryGetValue(saveFilePath, out var driveItem) && driveItem != null && driveItem.ContentHash == localFileInfo.Sha1)
                {
                    //_log.LogInformation($"本地与远程文件一致，不需要上传 {driveItem.Name}");
                    return;
                }

                var request = new RestRequest("/adrive/v1.0/openFile/create", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", $"Bearer {AccessToken}");

                var fileSize = fileInfo.Length;

                object body = new
                {
                    drive_id = _driveId,
                    parent_file_id = saveParentFileId,
                    name = name,
                    type = "file",
                    check_name_mode = "ignore", // 覆盖文件模式
                    size = fileSize
                };

                // 是否进行秒传处理
                var isRapidUpload = false;
                if (_jobConfig.RapidUpload)
                {
                    // 开启秒传
                    // 如果文件 > 10kb 则进行秒传计算，否则不进行
                    if (fileSize > 1024 * 10)
                    {
                        isRapidUpload = true;
                    }
                }

                // 如果需要计算秒传
                if (isRapidUpload)
                {
                    if (fileSize > 1024 * 1024 && needPreHash)
                    {
                        // 如果文件超过 1mb 则进行预处理，判断是否可以进行妙传
                        var preHash = AliyunDriveHelper.GenerateStartSHA1(filePath);
                        body = new
                        {
                            drive_id = _driveId,
                            parent_file_id = saveParentFileId,
                            name = name,
                            type = "file",
                            check_name_mode = "ignore",
                            size = fileInfo.Length,
                            pre_hash = preHash
                        };
                    }
                    else
                    {
                        // > 10kb 且 < 1mb 的文件直接计算 sha1
                        var proofCode = AliyunDriveHelper.GenerateProofCode(filePath, fileSize, AccessToken);
                        var contentHash = AliyunDriveHelper.GenerateSHA1(filePath);

                        body = new
                        {
                            drive_id = _driveId,
                            parent_file_id = saveParentFileId,
                            name = name,
                            type = "file",
                            check_name_mode = "ignore",
                            size = fileInfo.Length,
                            content_hash = contentHash,
                            content_hash_name = "sha1",
                            proof_version = "v1",
                            proof_code = proofCode
                        };
                    }
                }
                request.AddBody(body);
                var response = await _apiClient.ExecuteAsync(request);

                // 如果需要秒传，并且需要预处理时
                // System.Net.HttpStatusCode.Conflict 注意可能不是 409
                if (isRapidUpload && needPreHash
                    && (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict)
                    && response.Content.Contains("PreHashMatched"))
                {
                    using (var doc = JsonDocument.Parse(response.Content))
                    {
                        // 尝试获取code属性的值
                        if (doc.RootElement.TryGetProperty("code", out JsonElement codeElement))
                        {
                            var code = codeElement.GetString();
                            if (code == "PreHashMatched")
                            {
                                // 匹配成功，进行完整的秒传，不需要预处理
                                await UploadFileToAliyunDisk(localFileInfo, false);
                                return;
                            }
                        }
                    }
                }

                if (response.StatusCode == HttpStatusCode.OK)
                {
                    using var doc = JsonDocument.Parse(response.Content!);
                    var root = doc.RootElement;

                    var drive_id = root.GetProperty("drive_id").GetString();
                    var file_id = root.GetProperty("file_id").GetString();
                    var upload_id = root.GetProperty("upload_id").GetString();

                    var rapid_upload = root.GetProperty("rapid_upload").GetBoolean();
                    if (rapid_upload)
                    {
                        _log.LogInformation($"秒传 {name}");
                        return;
                    }

                    var upload_url = root.GetProperty("part_info_list").EnumerateArray().FirstOrDefault().GetProperty("upload_url").GetString();

                    //using (HttpClient httpClient = new HttpClient())
                    //{
                    // 读取文件作为字节流
                    byte[] fileData = await File.ReadAllBytesAsync(filePath);

                    // 创建HttpContent
                    var content = new ByteArrayContent(fileData);

                    // 发送PUT请求
                    HttpResponseMessage uploadRes = null; //  = await httpClient.PutAsync(upload_url, content);

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
                        uploadRes = await _uploadHttpClient.PutAsync(upload_url, content);

                        if (!uploadRes.IsSuccessStatusCode)
                        {
                            throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
                        }

                        _log.LogInformation("上传成功");
                    });

                    // 检查请求是否成功
                    if (uploadRes.IsSuccessStatusCode)
                    {
                        var request3 = new RestRequest("/adrive/v1.0/openFile/complete", Method.Post);
                        request3.AddHeader("Content-Type", "application/json");
                        request3.AddHeader("Authorization", $"Bearer {AccessToken}");
                        var body3 = new
                        {
                            drive_id = _driveId,
                            file_id = file_id,
                            upload_id = upload_id,
                        };
                        request3.AddBody(body3);
                        var response3 = await _apiClient.ExecuteAsync(request3);

                        // TODO
                        if (response3.StatusCode != HttpStatusCode.OK)
                            throw new Exception(response3.Content);

                        _log.LogInformation("上传标记完成 " + localFileInfo.Key);

                        // 将文件添加到上传列表
                        var data = JsonSerializer.Deserialize<AliyunDriveFileItem>(response3.Content);
                        if (data.ParentFileId == "root")
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
                    else
                    {
                        _log.LogInformation($"Failed to upload the file. Status Code: {response.StatusCode}");
                    }
                    //}
                }
                else
                {
                    throw response.ErrorException;
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 创建文件夹（同名不覆盖）
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="parentId"></param>
        /// <returns></returns>
        public async Task<string> CreateFolder(string filePath, string parentId)
        {
            var name = AliyunDriveHelper.EncodeFileName(filePath);

            try
            {
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
                    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == parentId).First()!;
                    var path = $"{parent.Key}/{name}".TrimPath();
                    if (_driveFolders.ContainsKey(path))
                    {
                        return _driveFolders[path].FileId;
                    }
                }

                // v1 https://openapi.alipan.com/adrive/v1.0/openFile/create
                // v2 https://api.aliyundrive.com/adrive/v2/file/createWithFolders
                var request = new RestRequest("/adrive/v1.0/openFile/create", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", $"Bearer {AccessToken}");

                var body = new
                {
                    drive_id = _driveId,
                    parent_file_id = parentId,
                    name = name,
                    type = "folder",
                    check_name_mode = "refuse", // 同名不创建
                };

                request.AddBody(body);
                var response = await _apiClient.ExecuteAsync(request);

                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    //using var doc = JsonDocument.Parse(response.Content!);
                    //var root = doc.RootElement;
                    //return root.GetProperty("file_id").GetString()!;

                    var data = JsonSerializer.Deserialize<AliyunDriveFileItem>(response.Content);
                    data.Name = data.FileName;

                    if (parentId == "root")
                    {
                        // 当前目录在根路径
                        // /{当前路径}/
                        _driveFolders.TryAdd($"{data.Name}".TrimPath(), data);
                    }
                    else
                    {
                        // 计算父级路径
                        var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == parentId).First()!;
                        var path = $"{parent.Key}/{data.Name}".TrimPath();

                        // /{父级路径}/{当前路径}/
                        _driveFolders.TryAdd(path, data);
                    }

                    return data.FileId;
                }

                throw response.ErrorException;
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        /// <summary>
        /// 获取文件列表（限流 4 QPS）
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="parentFileId"></param>
        /// <param name="limit"></param>
        /// <param name="orderBy"></param>
        /// <param name="orderDirection"></param>
        /// <param name="category"></param>
        /// <param name="type"></param>
        /// <param name="saveRootPath">备份保存的目录，如果匹配到则立即返回</param>
        /// <returns></returns>
        public async Task FetchAllFilesAsync(
            string driveId,
            string parentFileId,
            int limit = 100,
            string orderBy = null,
            string orderDirection = null,
            string category = null,
            string type = "all")
        {
            try
            {
                var allItems = new List<AliyunDriveFileItem>();
                string marker = null;
                do
                {
                    var sw = new Stopwatch();
                    sw.Start();

                    var response = await FetchFileListAsync(driveId, parentFileId, limit, marker, orderBy, orderDirection, category, type);
                    var responseData = JsonSerializer.Deserialize<AliyunFileList>(response.Content);
                    if (responseData.Items.Count > 0)
                    {
                        allItems.AddRange(responseData.Items.ToList());
                    }
                    marker = responseData.NextMarker;

                    sw.Stop();

                    // 等待 250ms 以遵守限流策略
                    if (sw.ElapsedMilliseconds < _listRequestInterval)
                        await Task.Delay((int)(_listRequestInterval - sw.ElapsedMilliseconds));
                } while (!string.IsNullOrEmpty(marker));

                foreach (var item in allItems)
                {
                    // 如果是文件夹，则递归获取子文件列表
                    if (item.Type == "folder")
                    {
                        // 如果是根目录
                        if (item.ParentFileId == "root")
                        {
                            _driveFolders.TryAdd($"{item.Name}".TrimPath(), item);
                        }
                        else
                        {
                            var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                            _driveFolders.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        }

                        await FetchAllFilesAsync(driveId, item.FileId, limit, orderBy, orderDirection, category, type);
                    }
                    else
                    {
                        // 如果是根目录的文件
                        if (item.ParentFileId == "root")
                        {
                            _driveFiles.TryAdd($"{item.Name}".TrimPath(), item);
                        }
                        else
                        {
                            // 构建文件路径作为字典的键
                            var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                            _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        }
                    }

                    _log.LogInformation($"云盘文件加载中，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹，{item.Name}");
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }
        }

        public async Task SearchAllFilesAsync(string driveId, int limit = 100)
        {
            try
            {
                var allItems = new List<AliyunDriveFileItem>();
                var marker = "";
                do
                {
                    //var sw = new Stopwatch();
                    //sw.Start();

                    var request = new RestRequest("/adrive/v1.0/openFile/search", Method.Post);
                    request.AddHeader("Content-Type", "application/json");
                    request.AddHeader("Authorization", $"Bearer {AccessToken}");
                    var body = new
                    {
                        drive_id = driveId,
                        limit = limit,
                        marker = marker,
                        query = ""
                    };
                    request.AddJsonBody(body);
                    var response = await _apiClient.ExecuteAsync<AliyunFileList>(request);
                    if (response.StatusCode == System.Net.HttpStatusCode.OK)
                    {
                        if (response.Data?.Items.Count > 0)
                        {
                            allItems.AddRange(response.Data?.Items);
                        }
                        marker = response.Data.NextMarker;
                    }
                    else
                    {
                        throw response.ErrorException;
                    }

                    //sw.Stop();

                    //// 等待 250ms 以遵守限流策略
                    //if (sw.ElapsedMilliseconds < _listRequestInterval)
                    //    await Task.Delay((int)(_listRequestInterval - sw.ElapsedMilliseconds));
                } while (!string.IsNullOrEmpty(marker));

                // 先加载文件夹
                LoadPath();
                void LoadPath(string parentFileId = "root")
                {
                    foreach (var item in allItems.Where(c => c.IsFolder).Where(c => c.ParentFileId == parentFileId))
                    {
                        // 如果是文件夹，则递归获取子文件列表
                        if (item.Type == "folder")
                        {
                            var keyPath = "";

                            // 如果是根目录
                            if (item.ParentFileId == "root")
                            {
                                keyPath = $"{item.Name}".TrimPath();
                            }
                            else
                            {
                                var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                                keyPath = $"{parent.Key}/{item.Name}".TrimPath();
                            }

                            if (string.IsNullOrWhiteSpace(keyPath))
                            {
                                throw new Exception("路径异常");
                            }

                            // 路径必须符合根路径，否则跳过
                            if (keyPath == _driveSavePath || keyPath.StartsWith($"{_driveSavePath}/"))
                            //if (keyPath.StartsWith(_driveSavePath) || _driveSavePath.StartsWith(keyPath))
                            {
                                _driveFolders.TryAdd(keyPath, item);
                                LoadPath(item.FileId);

                                //_log.LogInformation($"云盘文件加载中，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹，{item.Name}");
                            }
                        }
                    }
                }

                // 再加载列表
                foreach (var item in allItems.Where(c => c.IsFile))
                {
                    // 如果是文件夹，则递归获取子文件列表
                    if (item.Type == "folder")
                    {
                        //// 如果是根目录
                        //if (item.ParentFileId == "root")
                        //{
                        //    _driveFolders.TryAdd($"{item.Name}".TrimPath(), item);
                        //}
                        //else
                        //{
                        //    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                        //    _driveFolders.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        //}
                    }
                    else
                    {
                        // 文件必须在备份路径中
                        var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(parent.Key))
                        {
                            _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);

                            //_log.LogInformation($"云盘文件加载中，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹，{item.Name}");
                        }
                        else
                        {
                        }

                        //// 如果是根目录的文件
                        //if (item.ParentFileId == "root")
                        //{
                        //    _driveFiles.TryAdd($"{item.Name}".TrimPath(), item);
                        //}
                        //else
                        //{
                        //    // 构建文件路径作为字典的键
                        //    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                        //    _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
                        //}
                    }
                }
            }
            catch (Exception ex)
            {
                throw ex;
            }

            _log.LogInformation($"云盘文件加载完成，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹。");
        }

        private async Task<RestResponse> FetchFileListAsync(string driveId, string parentFileId, int limit, string marker, string orderBy, string orderDirection, string category, string type)
        {
            var request = new RestRequest("/adrive/v1.0/openFile/list", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");
            var body = new
            {
                drive_id = driveId,
                parent_file_id = parentFileId,
                limit,
                marker,
                order_by = orderBy,
                order_direction = orderDirection,
                category,
                type
            };
            request.AddJsonBody(body);

            return await ExecuteWithRetryAsync(request);
        }

        private async Task<RestResponse> ExecuteWithRetryAsync(RestRequest request)
        {
            const int maxRetries = 5;
            int retries = 0;
            while (true)
            {
                var response = await _apiClient.ExecuteAsync(request);
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    return response;
                }
                else if (response.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
                {
                    if (retries >= maxRetries)
                    {
                        throw new Exception("请求次数过多，已达到最大重试次数");
                    }

                    _log.LogInformation("请求次数过多");

                    await Task.Delay(_listRequestInterval);
                    retries++;
                }
                else
                {
                    throw new Exception($"请求失败: {response.StatusCode}");
                }
            }
        }

        /// <summary>
        /// 开始备份
        /// </summary>
        /// <returns></returns>
        public async Task StartBackup()
        {
            if (State == JobState.BackingUp)
            {
                _log.LogInformation("正在执行中，跳过");
                return;
            }

            // 备份中
            ChangeState(JobState.BackingUp);

            try
            {
                var sw = new Stopwatch();
                sw.Restart();
                _log.LogInformation("计算云盘存储根目录...");

                await InitBackupPath();

                sw.Stop();
                _log.LogInformation($"计算云盘存储根目录完成. 用时 {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // 验证并获取备份根目录
                // 加载备份文件夹下的所有文件夹、文件列表
                var saveParentFileId = "root";
                if (!string.IsNullOrWhiteSpace(_driveSavePath))
                {
                    if (_driveFolders.TryGetValue(_driveSavePath, out AliyunDriveFileItem item) && item != null)
                    {
                        saveParentFileId = item.FileId;
                    }
                    else
                    {
                        throw new Exception("云盘存储根目录不存在，请重新启动");
                    }
                }

                //// 加载备份文件夹下的所有文件夹
                //await FetchAllFilesAsync(_driveId, saveParentFileId, 100, type: "folder");

                _log.LogInformation("加载云盘存储文件列表...");

                // 所有文件列表
                await SearchAllFilesAsync(_driveId);

                //await FetchAllFilesAsync(_driveId, saveParentFileId, 100);

                sw.Stop();
                _log.LogInformation($"加载云盘存储文件列表完成. 用时 {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // 开始备份
                _log.LogInformation("开始备份本地文件...");

                await Search();

                sw.Stop();
                _log.LogInformation($"end. {_driveFiles.Count}, {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _log.LogInformation("执行异常" + ex.Message);
            }

            // 开始校验
            ChangeState(JobState.Verifying);

            await Maintenance();
        }

        /// <summary>
        /// 初始化备份目录
        /// </summary>
        /// <returns></returns>
        public async Task InitBackupPath()
        {
            // 首先加载根目录结构
            // 并计算需要保存的目录
            // 计算/创建备份文件夹
            // 如果备份文件夹不存在
            var saveRootSubPaths = _driveSavePath.Split('/').Select(c => c.Trim().Trim('/')).Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
            var searchParentFileId = "root";
            foreach (var subPath in saveRootSubPaths)
            {
                var request = new RestRequest("/adrive/v1.0/openFile/search", Method.Post);
                request.AddHeader("Content-Type", "application/json");
                request.AddHeader("Authorization", $"Bearer {AccessToken}");

                object body = new
                {
                    drive_id = _driveId,
                    query = $"parent_file_id='{searchParentFileId}' and type = 'folder' and name = '{subPath}'"
                };
                request.AddBody(body);
                var response = await _apiClient.ExecuteAsync<AliyunFileList>(request);
                if (response.StatusCode == System.Net.HttpStatusCode.OK)
                {
                    if (response.Data != null)
                    {
                        var okPath = response.Data.Items.FirstOrDefault(x => x.Name == subPath && x.Type == "folder" && x.ParentFileId == searchParentFileId);
                        if (okPath == null)
                        {
                            // 未找到目录
                            searchParentFileId = await CreateFolder(subPath, searchParentFileId);
                        }
                        else
                        {
                            if (searchParentFileId == "root")
                            {
                                // 当前目录在根路径
                                // /{当前路径}/
                                _driveFolders.TryAdd($"{okPath.Name}".TrimPath(), okPath);
                            }
                            else
                            {
                                // 计算父级路径
                                var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == searchParentFileId).First()!;
                                var path = $"{parent.Key}/{okPath.Name}".TrimPath();

                                // /{父级路径}/{当前路径}/
                                _driveFolders.TryAdd(path, okPath);
                            }

                            searchParentFileId = okPath.FileId;
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 开始还原
        /// </summary>
        /// <returns></returns>
        public async Task StartRestore()
        {
            try
            {
                // 验证还原目录
                if (Directory.Exists(_localRestorePath))
                {
                    Directory.CreateDirectory(_localRestorePath);
                }

                var sw = new Stopwatch();
                sw.Restart();
                _log.LogInformation("计算云盘存储根目录...");
                await InitBackupPath();
                sw.Stop();
                _log.LogInformation($"计算云盘存储根目录完成. 用时 {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // 加载备份文件夹下的所有文件夹、文件列表
                var saveParentFileId = "root";
                if (!string.IsNullOrWhiteSpace(_driveSavePath))
                {
                    if (_driveFolders.TryGetValue(_driveSavePath, out AliyunDriveFileItem item) && item != null)
                    {
                        saveParentFileId = item.FileId;
                    }
                    else
                    {
                        throw new Exception("云盘存储根目录不存在，请重新启动");
                    }
                }

                //// 加载备份文件夹下的所有文件夹
                //await FetchAllFilesAsync(_driveId, saveParentFileId, 100, type: "folder");

                _log.LogInformation("加载云盘存储文件列表...");

                // 所有文件列表
                await SearchAllFilesAsync(_driveId);

                //await FetchAllFilesAsync(_driveId, saveParentFileId, 100);

                sw.Stop();
                _log.LogInformation($"加载云盘存储文件列表完成. 用时 {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // 开始还原
                _log.LogInformation("开始拉取远程文件到本地...");

                await Restore(); // 替换为你的起始路径

                sw.Stop();
                _log.LogInformation($"end. {_driveFiles.Count}, {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                throw;
            }
        }

        public void Dispose()
        {
            // TODO

            foreach (var watcher in _localWatchers)
            {
                watcher.Dispose();
            }
        }
    }
}