using MDriveSync.Core.DB;
using MDriveSync.Core.Models;
using MDriveSync.Core.Services;
using MDriveSync.Core.ViewModels;
using MDriveSync.Security;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Polly;
using RestSharp;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Net;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace MDriveSync.Core
{
    /// <summary>
    /// 作业管理
    ///
    /// TODO
    /// 文件监听变换时，移除 sha1 缓存，下次同步时重新计算
    /// 分块上传 50~100mb
    /// 快速对比本地文件与服务器文件差异
    /// 开启文件监听，如果开启文件监听，如果文件发生变化时则重新计算完整 hash 并同步
    /// 排除文件/夹
    /// 增加存储到备份盘/资源库
    /// 首次备份时，计算全文 hash
    /// 由于加载列表限流，加载列表时，同时计算本地 hash
    ///
    ///
    /// </summary>
    public class AliyunJob : IDisposable
    {
        /// <summary>
        /// 本地文件锁
        /// </summary>
        private static readonly object _localLock = new();

        private readonly ILogger _log;

        /// <summary>
        /// 本地文件列表缓存
        /// </summary>
        private readonly SqliteRepository<LocalFileInfo, string> _localFileCache;

        /// <summary>
        /// 例行检查锁
        /// </summary>
        private readonly SemaphoreSlim _maintenanceLock = new(1, 1);

        /// <summary>
        /// 异步锁/资源锁
        /// </summary>
        private AsyncLockV2 _lock = new();

        /// <summary>
        /// 用于控制任务暂停和继续的对象
        /// </summary>
        private ManualResetEventSlim _pauseEvent = new ManualResetEventSlim(true);

        /// <summary>
        /// 暂停时的作业状态
        /// </summary>
        private JobState _pauseJobState;

        /// <summary>
        /// 本地缓存
        /// 令牌缓存、下载链接缓存等
        /// </summary>
        private readonly IMemoryCache _cache;

        /// <summary>
        /// 阿里云盘接口
        /// </summary>
        private readonly AliyunDriveApi _driveApi;

        /// <summary>
        /// 文件上传请求
        /// </summary>
        private readonly HttpClient _uploadHttpClient;

        /// <summary>
        /// 所有本地文件列表
        /// </summary>
        public ConcurrentDictionary<string, LocalFileInfo> _localFiles = new();

        /// <summary>
        /// 本地所有文件路径，true: 路径, false: 文件
        /// </summary>
        public ConcurrentDictionary<string, bool> _localPaths = new();

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
        private AliyunStorageConfig _currentStorageConfig;

        /// <summary>
        /// 阿里云盘备份盘/资源盘 ID
        /// </summary>
        public string AliyunDriveId => _driveId;

        /// <summary>
        /// 当前云盘
        /// </summary>
        public AliyunStorageConfig CurrrentStorageConfig => _currentStorageConfig;

        /// <summary>
        /// 云盘所有文件夹
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> DriveFolders => _driveFolders;

        /// <summary>
        /// 云盘所有文件
        /// </summary>
        public ConcurrentDictionary<string, AliyunDriveFileItem> DriveFiles => _driveFiles;

        // 作业配置
        private AliyunJobConfig _jobConfig;

        // 远程备份还原到本地目录
        private string _localRestorePath;

        // 远程备份保存的目录
        private string _driveSavePath;

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
        /// 本地文件监听
        /// </summary>
        private List<FileSystemWatcher> _localWatchers = [];

        /// <summary>
        /// 一次性任务是否已执行
        /// </summary>
        public bool isTemporaryIsEnd = false;

        /// <summary>
        /// 作业状态
        /// </summary>
        public JobState CurrentState { get; private set; }

        /// <summary>
        /// 当前作业
        /// </summary>
        public AliyunJobConfig CurrrentJob => _jobConfig;

        /// <summary>
        /// 进度总数
        /// </summary>
        public int ProcessCount = 0;

        /// <summary>
        /// 当前进度
        /// </summary>
        public int ProcessCurrent = 0;

        /// <summary>
        /// 当前进度消息
        /// </summary>
        public string ProcessMessage = string.Empty;

        /// <summary>
        /// 挂载云盘
        /// </summary>
        private AliyunDriveMounter _mountDrive;

        public AliyunJob(AliyunStorageConfig driveConfig, AliyunJobConfig jobConfig, ILogger log)
        {
            _localFileCache = new($"{jobConfig.Id}.d", "cache", true);
            _driveApi = new AliyunDriveApi();
            _log = log;

            // 本地缓存
            _cache = new MemoryCache(new MemoryCacheOptions());

            _currentStorageConfig = driveConfig;
            _jobConfig = jobConfig;

            // 上传请求
            // 上传链接最大有效 1 小时
            // 设置 45 分钟超时
            // 在 HttpClient 中，一旦发送了第一个请求，就不能再更改其配置属性，如超时时间 (Timeout)。
            // 这是因为 HttpClient 被设计为可重用的，它的属性设置在第一个请求发出之后就被固定下来。
            _uploadHttpClient = new HttpClient
            {
                Timeout = TimeSpan.FromMinutes(45)
            };

            // 非禁用状态时，创建默认为 none 状态
            if (jobConfig.State != JobState.Disabled)
            {
                jobConfig.State = JobState.None;
                CurrentState = JobState.None;
            }
            else
            {
                CurrentState = JobState.Disabled;
            }
        }

        /// <summary>
        /// 定期检查
        /// </summary>
        /// <returns></returns>
        public async Task Maintenance()
        {
            if (_maintenanceLock.CurrentCount == 0)
            {
                // 如果检查执行中，则跳过，保证执行中的只有 1 个
                return;
            }

            await _maintenanceLock.WaitAsync();

            try
            {
                switch (CurrentState)
                {
                    case JobState.None:
                        {
                            // 初始化
                            StartJob();
                        }
                        break;

                    case JobState.Starting:
                        {
                            // 启动中
                            StartJob();
                        }
                        break;

                    case JobState.Idle:
                        {
                            // 初始化作业调度
                            // 检查作业调度
                            InitJobScheduling();
                        }
                        break;

                    case JobState.Scanning:
                        {
                            // 扫描中
                        }
                        break;

                    case JobState.BackingUp:
                        {
                            // 备份中
                        }
                        break;

                    case JobState.Restoring:
                        {
                            // 还原中
                        }
                        break;

                    case JobState.Verifying:
                        {
                            // 校验中
                        }
                        break;

                    case JobState.Queued:
                        {
                        }
                        break;

                    case JobState.Completed:
                        {
                            // 完成的作业恢复到空闲
                            ChangeState(JobState.Idle);
                        }
                        break;

                    case JobState.Paused:
                        {
                        }
                        break;

                    case JobState.Error:
                        {
                            // 错误的作业恢复到空闲
                            ChangeState(JobState.Idle);
                        }
                        break;

                    case JobState.Cancelling:
                        {
                        }
                        break;

                    case JobState.Cancelled:
                        {
                            // 取消的作业恢复到空闲
                            ChangeState(JobState.Idle);
                        }
                        break;

                    case JobState.Disabled:
                        {
                        }
                        break;

                    default:
                        break;
                }
            }
            finally
            {
                GC.Collect();

                _maintenanceLock.Release();
            }
        }

        /// <summary>
        /// 初始化作业
        /// </summary>
        public void InitJobScheduling()
        {
            // 开始计算业务
            // 计算下一次执行备份等计划作业
            if (_jobConfig.Schedules.Count > 0)
            {
                foreach (var cron in _jobConfig.Schedules)
                {
                    var exp = cron;

                    // 常用表达式
                    if (QuartzCronScheduler.CommonExpressions.ContainsKey(exp))
                    {
                        exp = QuartzCronScheduler.CommonExpressions[exp];
                    }

                    if (!_schedulers.TryGetValue(exp, out var sch) || sch == null)
                    {
                        // 创建备份计划
                        var scheduler = new QuartzCronScheduler(exp, () =>
                        {
                            StartSync();
                        });
                        scheduler.Start();
                        _schedulers[exp] = scheduler;

                        // 如果立即执行的
                        if (_jobConfig.IsTemporary)
                        {
                            StartSync();
                        }
                    }
                }
            }
            else
            {
                // 如果是一次性任务
                if (_jobConfig.IsTemporary)
                {
                    if (!isTemporaryIsEnd)
                    {
                        StartSync();
                        isTemporaryIsEnd = true;
                    }
                }
            }
        }

        /// <summary>
        /// 启动后台作业、启动缓存、启动监听等
        /// </summary>
        /// <returns></returns>
        public void StartJob()
        {
            AliyunDriveInitialize();

            ChangeState(JobState.Starting);

            _log.LogInformation("作业启动中");

            var sw = new Stopwatch();
            sw.Start();

            // 每 5 分钟持久化本地缓存
            _localPersistentTimer = new Timer(PersistentDoWork, null, TimeSpan.Zero, TimeSpan.FromMinutes(5));

            // 监听函数
            // 如果文件发生变化，则重新计算相关逻辑
            if (_jobConfig.FileWatcher)
            {
                foreach (var localBackupPath in _jobConfig.Sources)
                {
                    var watcher = new FileSystemWatcher(localBackupPath);
                    watcher.NotifyFilter = NotifyFilters.FileName |
                            NotifyFilters.DirectoryName |
                            NotifyFilters.LastWrite |
                            NotifyFilters.Size;

                    // 监控所有文件
                    watcher.Filter = "*.*";

                    // 包括子目录
                    watcher.IncludeSubdirectories = true;

                    watcher.Created += (o, e) =>
                    {
                        OnCreated(o, e, localBackupPath);
                    };

                    watcher.Deleted += (o, e) =>
                    {
                        OnDeleted(o, e, localBackupPath);
                    };
                    watcher.Renamed += (o, e) =>
                    {
                        OnRenamed(o, e, localBackupPath);
                    };
                    watcher.Changed += OnChanged;
                    watcher.Error += OnError;

                    watcher.EnableRaisingEvents = true;
                    _localWatchers.Add(watcher);

                    //// 文件夹监听
                    //var dirFsWatcher = new FileSystemWatcher(localBackupPath)
                    //{
                    //    IncludeSubdirectories = true,
                    //    NotifyFilter = NotifyFilters.DirectoryName
                    //};
                    //dirFsWatcher.Deleted += OnCommonFileSystemWatcherDirectoryDeleted;
                    //dirFsWatcher.EnableRaisingEvents = true;
                }
            }

            sw.Stop();
            _log.LogInformation($"作业启动完成，用时：{sw.ElapsedMilliseconds}ms");

            // 启动完成，处于空闲
            ChangeState(JobState.Idle);

            InitJobScheduling();
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

            var caches = _localFileCache.GetAll();
            var cacheKeys = caches.Select(c => c.Key).ToList();

            // 比较文件，变化的则更新到数据库
            var fs = _localFiles.Values.ToList();

            var updatedList = new ConcurrentBag<LocalFileInfo>();
            var addList = new ConcurrentBag<LocalFileInfo>();

            // 不需要并行比较
            // 单线程，每秒可处理 3600万+
            foreach (var file in fs)
            {
                var f = caches.FirstOrDefault(c => c.Key == file.Key);
                if (f == null)
                {
                    addList.Add(file);
                }
                else
                {
                    // 更新前判断每个字段是否一致，如果一致则不需要更新
                    if (!_localFileCache.FastAreObjectsEqual(f, file))
                    {
                        updatedList.Add(file);
                    }
                }

                // 移除
                cacheKeys.Remove(file.Key);
            }

            if (addList.Count > 0)
            {
                _localFileCache.AddRange(addList);
            }

            if (updatedList.Count > 0)
            {
                _localFileCache.UpdateRange(updatedList);
            }

            if (cacheKeys.Count > 0)
            {
                _localFileCache.DeleteRange(cacheKeys);
            }

            _log.LogInformation("持久化本地文件缓存，新增：{@0}，更新：{@1}，删除：{@2}", addList.Count, updatedList.Count, cacheKeys.Count);
        }

        private void ChangeState(JobState newState)
        {
            CurrentState = newState;

            // 触发状态改变事件

            ProcessMessage = string.Empty;
            ProcessCount = 0;
            ProcessCurrent = 0;
        }

        /// <summary>
        /// 获取当前有效的访问令牌
        /// </summary>
        private string AccessToken
        {
            get
            {
                return AliyunDriveToken.Instance.GetAccessToken(_currentStorageConfig.Id);
            }
        }

        /// <summary>
        /// 开始同步
        /// </summary>
        /// <returns></returns>
        public void StartSync()
        {
            // 如果不是处于空闲状态，则终止
            if (CurrentState != JobState.Idle
                && CurrentState != JobState.Queued
                && CurrentState != JobState.Cancelled
                && CurrentState != JobState.Error)
            {
                return;
            }

            // 加入队列
            _log.LogInformation("任务 {@0} 加入队列", _jobConfig.Name);

            ChangeState(JobState.Queued);

            // 添加到全局队列
            GlobalJob.Instance.AddOrRestartJob(_jobConfig.Id, async (cancellationToken) =>
            {
                try
                {
                    _log.LogInformation("开始同步 {@0}", _jobConfig.Name);

                    //ChangeState(JobState.BackingUp);

                    //await Task.Delay(20000, cancellationToken);

                    // 实现文件作业逻辑
                    await StartSyncJob(cancellationToken);

                    //ChangeState(JobState.Idle);

                    _log.LogInformation("完成同步 {@0}", _jobConfig.Name);
                }
                catch (OperationCanceledException)
                {
                    _log.LogInformation("任务 {@0} 取消", _jobConfig.Name);

                    ChangeState(JobState.Cancelled);
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, "任务 {@0} 错误", _jobConfig.Name);

                    ChangeState(JobState.Error);
                }
            });
        }

        public async Task StartSyncJob(CancellationToken cancellationToken)
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }

            // 备份中 | 校验中 跳过
            if (CurrentState == JobState.BackingUp || CurrentState == JobState.Verifying)
            {
                return;
            }

            // 如果不是处于空闲状态，则终止
            if (CurrentState != JobState.Idle
                && CurrentState != JobState.Queued
                && CurrentState != JobState.Cancelled
                && CurrentState != JobState.Error)
            {
                return;
            }

            var swAll = new Stopwatch();
            swAll.Start();

            _log.LogInformation($"同步作业开始：{DateTime.Now:G}");

            // 备份中
            ChangeState(JobState.BackingUp);

            // 恢复继续
            _pauseEvent.Set();

            var sw = new Stopwatch();
            sw.Start();

            try
            {
                AliyunDriveInitBackupPath();

                sw.Stop();
                _log.LogInformation($"云盘存储根目录初始化完成，用时：{sw.ElapsedMilliseconds}ms");
                sw.Restart();
                _log.LogInformation("开始加载云盘存储文件列表");

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

                // 加载所有文件列表
                AliyunDriveSearchFiles(_driveId);

                // 加载备份文件夹下的所有文件夹
                //await FetchAllFilesAsync(_driveId, saveParentFileId, 100);

                sw.Stop();
                _log.LogInformation($"加载云盘存储文件列表完成，用时：{sw.ElapsedMilliseconds}ms");
                sw.Restart();
                _log.LogInformation("开始执行同步");

                await SyncFiles(cancellationToken);

                sw.Stop();
                _log.LogInformation($"同步作业完成，用时：{sw.ElapsedMilliseconds}ms");
            }
            catch (Exception ex)
            {
                _log.LogError(ex, "同步作业完成执行异常");
            }

            // 开始校验
            sw.Restart();
            _log.LogInformation($"同步作业结束：{DateTime.Now:G}");
            ChangeState(JobState.Verifying);

            await AliyunDriveVerify();

            sw.Stop();
            _log.LogInformation($"同步作业校验完成，用时：{sw.ElapsedMilliseconds}ms");

            swAll.Stop();

            ProcessMessage = $"执行完成，总用时 {swAll.ElapsedMilliseconds / 1000} 秒";
        }

        /// <summary>
        /// 同步本地文件到云盘
        /// </summary>
        /// <returns></returns>
        private async Task SyncFiles(CancellationToken token)
        {
            ScanLocalFiles();

            var now = DateTime.Now;

            var processCount = GetUploadThreadCount();
            var options = new ParallelOptions() { MaxDegreeOfParallelism = processCount };

            var process = 0;
            var total = _localFolders.Count;

            ProcessCurrent = 0;
            processCount = total;
            ProcessMessage = string.Empty;

            // 并行创建文件夹
            Parallel.ForEach(_localFolders, options, (item) =>
            {
                try
                {
                    // 在关键点添加暂停点
                    _pauseEvent.Wait();
                    token.ThrowIfCancellationRequested();

                    // 计算存储目录
                    var saveParentPath = $"{_driveSavePath}/{item.Key}".TrimPath();

                    // 存储目录 ID
                    var saveParentFileId = "root";

                    // 判断云盘是否存在路径，不存在则创建
                    if (!string.IsNullOrWhiteSpace(saveParentPath))
                    {
                        if (!_driveFolders.ContainsKey(saveParentPath))
                        {
                            var savePaths = saveParentPath.Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                            var savePathsParentFileId = "root";
                            foreach (var subPath in savePaths)
                            {
                                savePathsParentFileId = AliyunDriveCreateFolder(subPath, savePathsParentFileId);
                            }
                        }

                        if (!_driveFolders.ContainsKey(saveParentPath))
                        {
                            throw new Exception("文件夹创建失败");
                        }

                        saveParentFileId = _driveFolders[saveParentPath].FileId;
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"文件上传处理异常 {item.Value.FullPath}");
                }
                finally
                {
                    Interlocked.Increment(ref process);

                    ProcessCurrent = process;
                    ProcessMessage = $"同步文件夹中 {process}/{total}";

                    _log.LogInformation($"同步文件夹中 {process}/{total}，用时：{(DateTime.Now - now).TotalMilliseconds}ms，{item.Key}");
                }
            });
            _log.LogInformation($"同步文件夹完成，总文件夹数：{_localFolders.Count}，用时：{(DateTime.Now - now).TotalMilliseconds}ms");

            now = DateTime.Now;
            process = 0;
            total = _localFiles.Count;

            ProcessCurrent = 0;
            processCount = total;

            // 并行上传
            await Parallel.ForEachAsync(_localFiles, options, async (item, cancellationToken) =>
            {
                try
                {
                    // 在关键点添加暂停点
                    _pauseEvent.Wait();
                    token.ThrowIfCancellationRequested();

                    await AliyunDriveUploadFile(item.Value);
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"文件上传处理异常 {item.Value.FullPath}");
                }
                finally
                {
                    Interlocked.Increment(ref process);
                    ProcessCurrent = process;
                    ProcessMessage = $"同步文件中 {process}/{total}";
                    _log.LogInformation($"同步文件中 {process}/{total}，用时：{(DateTime.Now - now).TotalMilliseconds}ms，{item.Key}");
                }
            });

            ProcessMessage = string.Empty;

            _log.LogInformation($"同步文件完成，总文件数：{_localFiles.Count}，用时：{(DateTime.Now - now).TotalMilliseconds}ms");
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
                AliyunDriveInitBackupPath();
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
                AliyunDriveSearchFiles(_driveId);

                //await FetchAllFilesAsync(_driveId, saveParentFileId, 100);

                sw.Stop();
                _log.LogInformation($"加载云盘存储文件列表完成. 用时 {sw.ElapsedMilliseconds}ms");
                sw.Restart();

                // 开始还原
                _log.LogInformation("开始拉取远程文件到本地...");

                await RestoreFiles(); // 替换为你的起始路径

                sw.Stop();
                _log.LogInformation($"end. {_driveFiles.Count}, {sw.ElapsedMilliseconds}ms");
            }
            catch (Exception)
            {
                throw;
            }
        }

        /// <summary>
        /// 还原云盘文件到本地
        /// </summary>
        /// <returns></returns>
        private async Task RestoreFiles()
        {
            var now = DateTime.Now;

            var processorCount = GetDownloadThreadCount();
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
                    var ld = new LocalFileInfo()
                    {
                        FullPath = dirInfo.FullName,
                        Key = GetDirectoryKey(_localRestorePath, dirInfo),
                        CreationTime = dirInfo.CreationTime,
                        LastWriteTime = dirInfo.LastWriteTime,
                        LocalFileName = dirInfo.Name
                    };
                    _localRestoreFolders.AddOrUpdate(ld.Key, ld, (k, v) => ld);

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
                            IsEncrypt = _jobConfig.IsEncrypt,
                            IsEncryptName = _jobConfig.IsEncryptName,
                            IsFile = true,
                            FullPath = fileInfo.FullName,
                            Key = GetFileKey(_localRestorePath, fileInfo.FullName),
                            KeyPath = GetFileKeyPath(_localRestorePath, fileInfo),
                            CreationTime = fileInfo.CreationTime,
                            LastWriteTime = fileInfo.LastWriteTime,
                            Length = fileInfo.Length,
                            LocalFileName = fileInfo.Name,
                            Hash = ShaHashHelper.ComputeFileHash(file, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, file.Length)
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
                var subPaths = item.Key.TrimPrefix(_driveSavePath)
                    .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                var savePath = Path.Combine(_localRestorePath, Path.Combine(subPaths));

                var tmpPath = Path.GetDirectoryName(savePath);
                lock (_localLock)
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
                        throw new Exception("不支持根目录文件下载");
                    }
                    else
                    {
                        // 子目录
                        var parent = _driveFolders.First(x => x.Value.IsFolder && x.Value.FileId == item.Value.ParentFileId)!;

                        // 移除云盘前缀
                        var subPaths = parent.Key.TrimPrefix(_driveSavePath)
                            .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
                        savePath = Path.Combine(_localRestorePath, Path.Combine(subPaths));
                    }

                    var finalFilePath = Path.Combine(savePath, item.Value.Name);
                    if (File.Exists(finalFilePath))
                    {
                        // 验证本地是否已存在文件，并比较 sha1 值
                        var hash = ShaHashHelper.ComputeFileHash(finalFilePath, "sha1");
                        if (hash.Equals(item.Value.ContentHash, StringComparison.OrdinalIgnoreCase))
                        {
                            _log.LogInformation($"文件已存在，跳过 {finalFilePath}");
                            return;
                        }
                    }

                    // 获取详情 url
                    var data = AliyunDriveGetDetail<AliyunDriveFileItem>(item.Value.FileId);
                    await AliyunDriveDownload(data.Url,
                                   item.Value.Name,
                                   item.Value.ContentHash,
                                   savePath,
                                   _localRestorePath);

                    // TODO > 100MB
                    // 如果是大文件，则通过下载链接下载文件
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

            // 清理下载缓存
            ClearDownloadCache(_localRestorePath);
        }

        public void Dispose()
        {
            try
            {
                foreach (var watcher in _localWatchers)
                {
                    watcher.Dispose();
                }

                GC.SuppressFinalize(this);
            }
            catch
            {
            }
        }

        /// <summary>
        /// 获取云盘文件文件夹
        /// </summary>
        /// <param name="parentId"></param>
        public async Task<List<AliyunDriveFileItem>> GetDrivleFiles(string parentId = "")
        {
            if (string.IsNullOrWhiteSpace(parentId))
            {
                if (_driveFolders.TryGetValue(_driveSavePath, out var p))
                {
                    parentId = p.FileId;
                }

                // 如果为空则初始化备份目录
                if (string.IsNullOrWhiteSpace(parentId))
                {
                    AliyunDriveInitBackupPath();

                    if (_driveFolders.TryGetValue(_driveSavePath, out var p2))
                    {
                        parentId = p2.FileId;
                    }
                }
            }

            var list = new List<AliyunDriveFileItem>();

            // 拉取云盘文件
            await AliyunDriveFetchAllFiles(_driveId, parentId, isDeepLoop: false);

            // 目录下的所有文件文件夹
            var fdirs = _driveFolders.Values.Where(c => c.ParentFileId == parentId)
                .OrderBy(c => c.Name)
                .ToList();

            var fs = _driveFiles.Values.Where(c => c.ParentFileId == parentId)
                .OrderBy(c => c.Name)
                .ToList();

            // 当云盘存在文件，但是没有本地文件时
            if (fs.Count > 0 && _localFiles.Count <= 0)
            {
                // 重新扫描本地文件
                ScanLocalFiles();
            }

            // 如果是加密文件
            if (_jobConfig.IsEncrypt)
            {
                if (_jobConfig.IsEncryptName)
                {
                    // 解析加密文件对应的本地文件名称
                    foreach (var item in fs)
                    {
                        var localEntryptKey = item.Key.TrimPrefix(_driveSavePath);
                        var f = _localFiles.Values.FirstOrDefault(x => x.EncryptKey == localEntryptKey);
                        if (f != null)
                        {
                            item.LocalFileName = f.LocalFileName;
                        }
                    }
                }
                else
                {
                    foreach (var item in fs)
                    {
                        item.LocalFileName = item.Name.TrimSuffix(".e");
                    }
                }
            }

            list.AddRange(fdirs);
            list.AddRange(fs);

            return list;
        }

        /// <summary>
        /// 阿里云盘 - 获取文件详情
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        public FilePathKeyResult GetFileDetail(string fileId)
        {
            var info = AliyunDriveGetDetail<FilePathKeyResult>(fileId);
            if (info.IsFolder)
            {
                var f = _driveFolders.Where(c => c.Value.FileId == fileId).FirstOrDefault();

                if (!string.IsNullOrWhiteSpace(f.Key))
                {
                    info.Key = f.Key;
                }
            }
            else
            {
                var f = _driveFiles.Where(c => c.Value.FileId == fileId).FirstOrDefault();
                if (!string.IsNullOrWhiteSpace(f.Key))
                {
                    info.Key = f.Key;
                }
            }
            return info;
        }

        /// <summary>
        /// 更新作业配置（只有空闲、错误、取消、禁用、完成状态才可以更新）
        /// </summary>
        /// <param name="cfg"></param>
        public void JobUpdate(AliyunJobConfig cfg)
        {
            if (cfg == null)
            {
                throw new LogicException("参数错误，请填写必填选项，且符合规范");
            }

            var allowJobStates = new[] { JobState.Idle, JobState.Error, JobState.Cancelled, JobState.Disabled, JobState.Completed };
            if (!allowJobStates.Contains(CurrentState))
            {
                throw new LogicException($"当前作业处于 {CurrentState.GetDescription()} 状态，不能修改作业");
            }

            if (cfg.Id != _jobConfig.Id)
            {
                throw new LogicException("作业标识错误");
            }

            var drive = AliyunStorageDb.Instance.DB.GetAll().Where(c => c.Id == _currentStorageConfig.Id).FirstOrDefault();
            if (drive == null)
            {
                throw new LogicException("配置配置错误，请重启程序");
            }

            // 禁止作业指向同一目标
            if (!string.IsNullOrWhiteSpace(cfg.Target) && drive.Jobs.Any(x => x.Target == cfg.Target && x.Id != cfg.Id))
            {
                throw new LogicException("多个作业禁止指向云盘同一个目标目录");
            }

            // 清除表达式所有作业
            _schedulers.Clear();

            _jobConfig.Filters = cfg.Filters;
            _jobConfig.Name = cfg.Name;
            _jobConfig.Description = cfg.Description;
            _jobConfig.CheckLevel = cfg.CheckLevel;
            _jobConfig.CheckAlgorithm = cfg.CheckAlgorithm;
            _jobConfig.Sources = cfg.Sources;
            _jobConfig.DefaultDrive = cfg.DefaultDrive;
            _jobConfig.DownloadThread = cfg.DownloadThread;
            _jobConfig.UploadThread = cfg.UploadThread;
            _jobConfig.Target = cfg.Target;
            _jobConfig.Schedules = cfg.Schedules;
            _jobConfig.FileWatcher = cfg.FileWatcher;
            _jobConfig.IsRecycleBin = cfg.IsRecycleBin;
            _jobConfig.IsTemporary = cfg.IsTemporary;
            _jobConfig.Order = cfg.Order;
            _jobConfig.RapidUpload = cfg.RapidUpload;
            _jobConfig.Mode = cfg.Mode;
            _jobConfig.Restore = cfg.Restore;

            _jobConfig.MountConfig ??= new AliyunDriveMountConfig();
            _jobConfig.MountConfig.IsRecycleBin = cfg.IsRecycleBin;
            _jobConfig.MountConfig.RapidUpload = cfg.RapidUpload;
            _jobConfig.MountConfig.MountDrive = cfg.DefaultDrive;
            _jobConfig.MountConfig.MountPath = cfg.Target;

            // 作业挂载、只可编辑 3 项
            _jobConfig.MountConfig.MountOnStartup = cfg.MountConfig?.MountOnStartup ?? false;
            _jobConfig.MountConfig.MountReadOnly = cfg.MountConfig?.MountReadOnly ?? false;
            _jobConfig.MountConfig.MountPoint = cfg?.MountConfig?.MountPoint;

            _currentStorageConfig.SaveJob(_jobConfig);
        }

        /// <summary>
        /// 作业状态修改
        /// </summary>
        /// <param name="state"></param>
        public void JobStateChange(JobState state)
        {
            if (state == JobState.Initializing)
            {
                // 如果作业未启动，则可以初始化
                var allowJobStates = new[] { JobState.Disabled, JobState.None, JobState.Completed, JobState.Cancelled, JobState.Error };
                if (!allowJobStates.Contains(CurrentState))
                {
                    throw new LogicException($"当前作业处于 {CurrentState.GetDescription()} 状态，不能初始化作业");
                }

                AliyunDriveInitialize();
            }
            else if (state == JobState.Cancelled)
            {
                // 取消作业
                var allowJobStates = new[] { JobState.Queued, JobState.Scanning, JobState.BackingUp, JobState.Restoring, JobState.Paused };
                if (!allowJobStates.Contains(CurrentState))
                {
                    throw new LogicException($"当前作业处于 {CurrentState.GetDescription()} 状态，无法取消作业");
                }

                if (CurrentState == JobState.Queued)
                {
                    // 如果处于队列中
                    GlobalJob.Instance.CancelJob(_jobConfig.Id);

                    // 变更为空闲
                    ChangeState(JobState.Idle);
                }
                else
                {
                    // 如果处于备份中、暂停中、还原中、扫描中取消，则调用取消任务
                    GlobalJob.Instance.CancelJob(_jobConfig.Id);

                    // 需要先恢复作业
                    _pauseEvent.Set();

                    // 切换任务处于取消中
                    ChangeState(JobState.Cancelling);
                }
            }
            else if (state == JobState.BackingUp)
            {
                // 执行作业，加入到队列
                var allowJobStates = new[] { JobState.Idle, JobState.Error, JobState.Cancelled };
                if (!allowJobStates.Contains(CurrentState))
                {
                    throw new LogicException($"当前作业处于 {CurrentState.GetDescription()} 状态，无法暂停作业");
                }

                // 开始执行，加入到队列
                StartSync();
            }
            else if (state == JobState.Paused)
            {
                // 暂停作业
                var allowJobStates = new[] { JobState.BackingUp, JobState.Restoring };
                if (!allowJobStates.Contains(CurrentState))
                {
                    throw new LogicException($"当前作业处于 {CurrentState.GetDescription()} 状态，无法暂停作业");
                }

                // 暂停执行
                _pauseEvent.Reset();
                _pauseJobState = CurrentState;

                // 切换状态
                ChangeState(JobState.Paused);
            }
            else if (state == JobState.Continue)
            {
                // Resume
                // 作业恢复
                // 仅用于暂停
                if (CurrentState == JobState.Paused)
                {
                    // 恢复继续
                    _pauseEvent.Set();

                    // 切换原有状态
                    ChangeState(_pauseJobState);
                }
            }
            else if (state == JobState.Disabled)
            {
                // 禁用作业
                var allowJobStates = new[] { JobState.Idle, JobState.Error, JobState.Cancelled, JobState.Disabled, JobState.Completed };
                if (!allowJobStates.Contains(CurrentState))
                {
                    throw new LogicException($"当前作业处于 {CurrentState.GetDescription()} 状态，不能禁用作业");
                }
                CurrentState = state;
                _jobConfig.State = state;
                _currentStorageConfig.SaveJob(_jobConfig);
            }
            else if (state == JobState.Deleted)
            {
                // 删除作业
                var allowJobStates = new[] { JobState.Idle, JobState.Error, JobState.Cancelled, JobState.Disabled, JobState.Completed };
                if (!allowJobStates.Contains(CurrentState))
                {
                    throw new LogicException($"当前作业处于 {CurrentState.GetDescription()} 状态，不能删除作业");
                }
                _currentStorageConfig.SaveJob(_jobConfig, true);
            }
            else if (state == JobState.None)
            {
                // 启用作业，恢复默认状态
                var allowJobStates = new[] { JobState.Disabled };
                if (!allowJobStates.Contains(CurrentState))
                {
                    throw new LogicException($"当前作业处于 {CurrentState.GetDescription()} 状态，不能启用作业");
                }
                CurrentState = state;
                _jobConfig.State = state;
                _currentStorageConfig.SaveJob(_jobConfig);
            }
            else
            {
                throw new LogicException("操作不支持");
            }
        }

        #region 私有方法

        /// <summary>
        /// 写日志
        /// </summary>
        /// <param name="msg"></param>
        private void LogInfo(string msg)
        {
            _log.LogInformation(msg);
            ProcessMessage = msg;
        }

        /// <summary>
        /// 写错误日志
        /// </summary>
        /// <param name="ex"></param>
        /// <param name="msg"></param>
        private void LogError(Exception ex, string msg)
        {
            _log.LogError(ex, msg);
            ProcessMessage = msg;
        }

        /// <summary>
        /// 扫描本地文件
        /// </summary>
        private void ScanLocalFiles()
        {
            ProcessMessage = "正在扫描本地文件";

            var now = DateTime.Now;

            // 读取本地缓存文件
            var oldLocalFiles = new Dictionary<string, LocalFileInfo>();
            var oldLocalFileList = _localFileCache.GetAll();
            if (oldLocalFileList.Count > 0)
            {
                oldLocalFiles = oldLocalFileList.ToDictionary(c => c.Key, c => c);
            }

            var processorCount = GetUploadThreadCount();
            var options = new ParallelOptions() { MaxDegreeOfParallelism = processorCount };

            // 当前本地文件 key
            var localFileKeys = new ConcurrentDictionary<string, bool>(_localFiles.Keys.ToDictionary(c => c, v => true));

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
                        var ld = GetLocalDirectory(dir, backupRootPath);
                        if (ld == null)
                        {
                            return;
                        }
                        _localFolders.AddOrUpdate(ld.Key, ld, (k, v) => ld);

                        var files = Directory.EnumerateFiles(dir);
                        foreach (var fileFullPath in files)
                        {
                            var lf = GetLocalFile(fileFullPath, backupRootPath);
                            if (lf == null)
                            {
                                continue;
                            }

                            // 计算 hash
                            lf.Hash = ShaHashHelper.ComputeFileHash(fileFullPath, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, lf.Length);

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

                                if (lf.IsEncrypt)
                                {
                                    if (string.IsNullOrWhiteSpace(lf.EncryptFileName))
                                    {
                                        lf.EncryptFileName = cacheFile.EncryptFileName;
                                    }
                                }
                            }


                            // 计算加密文件名称
                            if (lf.IsEncrypt)
                            {
                                if (string.IsNullOrWhiteSpace(lf.EncryptFileName))
                                {
                                    var name = lf.LocalFileName;
                                    if (lf.IsEncryptName)
                                    {
                                        name = HashHelper.ComputeHash(Encoding.UTF8.GetBytes(name), "MD5").ToHex() + ".e";
                                    }
                                    else
                                    {
                                        name += ".e";
                                    }

                                    lf.EncryptFileName = name;
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

                            localFileKeys.TryRemove(lf.Key, out _);

                            ProcessMessage = $"正在扫描本地文件 {_localFiles.Count}";
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        _log.LogWarning(ex, $"加载本地目录文件没有权限 {dir}");
                    }
                    catch (Exception ex)
                    {
                        _log.LogError(ex, $"加载本地目录文件异常 {dir}");
                        throw;
                    }
                }
            }

            // 如果本地文件不存在了
            foreach (var item in localFileKeys)
            {
                _localFiles.TryRemove(item.Key, out _);
            }

            // 持久化本地文件
            _isLoadLocalFiles = true;

            PersistentDoWork(null);

            _log.LogInformation($"扫描本地文件，总文件数：{_localFiles.Count}, 扫描文件用时: {(DateTime.Now - now).TotalMilliseconds}ms");
        }

        /// <summary>
        /// 获取上传线程数
        /// </summary>
        /// <returns></returns>
        private int GetUploadThreadCount()
        {
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

            if (_jobConfig.UploadThread > 0)
            {
                processorCount = _jobConfig.UploadThread;
            }

            return processorCount;
        }

        /// <summary>
        /// 获取下载线程数
        /// </summary>
        /// <returns></returns>
        private int GetDownloadThreadCount()
        {
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

            if (_jobConfig.DownloadThread > 0)
            {
                processorCount = _jobConfig.DownloadThread;
            }

            return processorCount;
        }

        /// <summary>
        /// 清理下载缓存
        /// </summary>
        private void ClearDownloadCache(string saveRootPath)
        {
            // 清理临时文件
            var tempPath = Path.Combine(saveRootPath, ".duplicaticache");
            if (Directory.Exists(tempPath))
            {
                Directory.Delete(tempPath, true);
            }

            var tmpFiles = Directory.GetFiles(saveRootPath, "*.duplicatidownload", SearchOption.AllDirectories);
            foreach (var file in tmpFiles)
            {
                if (File.Exists(file))
                {
                    File.Delete(file);
                }
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
            var subPath = directoryInfo.FullName.TrimPrefix(localRootPath);
            return $"{rootPathName}/{subPath}".TrimPath();
        }

        /// <summary>
        /// 获取文件路径 key
        /// </summary>
        /// <param name="localRootPath"></param>
        /// <param name="fileInfo"></param>
        /// <returns></returns>
        private string GetFileKey(string localRootPath, string fileFullPath)
        {
            var localRootInfo = new DirectoryInfo(localRootPath);
            var rootPathName = localRootInfo.Name;
            var subPath = fileFullPath.TrimPrefix(localRootInfo.FullName);
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
            var subPath = Path.GetDirectoryName(fileInfo.FullName).TrimPrefix(localRootInfo.FullName);
            return $"{rootPathName}/{subPath}".TrimPath();
        }

        /// <summary>
        /// 检查一个给定的路径是否应该被过滤
        /// </summary>
        /// <param name="path"></param>
        /// <returns></returns>
        private bool ShouldFilter(string path)
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
                    if (item.StartsWith("#"))
                        continue;

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

        /// <summary>
        /// 将 Kopia 规则转换为正则表达式
        /// </summary>
        /// <param name="kopiaPattern"></param>
        /// <returns></returns>
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
        /// 尝试获取本地文件，如果本地文件不存在或不符合则返回 NULL
        /// </summary>
        /// <param name="fileFullPath"></param>
        /// <param name="localBackupFullPath"></param>
        /// <returns></returns>
        private LocalFileInfo GetLocalFile(string fileFullPath, string localBackupFullPath)
        {
            var backupDirInfo = new DirectoryInfo(localBackupFullPath);
            if (!backupDirInfo.Exists)
            {
                return null;
            }

            var fileInfo = new FileInfo(fileFullPath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            // 所有本地文件
            var lf = new LocalFileInfo()
            {
                IsFile = true,
                FullPath = fileInfo.FullName,
                Key = GetFileKey(localBackupFullPath, fileInfo.FullName),
                KeyPath = GetFileKeyPath(localBackupFullPath, fileInfo),
                CreationTime = fileInfo.CreationTime,
                LastWriteTime = fileInfo.LastWriteTime,
                Length = fileInfo.Length,
                LocalFileName = fileInfo.Name,
                IsEncrypt = _jobConfig.IsEncrypt,
                IsEncryptName = _jobConfig.IsEncryptName
            };

            // 过滤文件
            var cfile = lf.Key.TrimPrefix(backupDirInfo.Name).TrimPath();
            if (!string.IsNullOrWhiteSpace(cfile))
            {
                if (ShouldFilter($"/{cfile}"))
                {
                    return null;
                }
            }

            // 文件添加到本地路径
            _localPaths.TryAdd(lf.FullPath, false);

            return lf;
        }

        /// <summary>
        /// 尝试获取本地文件夹，如果本地文件夹不存在或不符合则返回 NULL
        /// </summary>
        /// <param name="dirFullPath"></param>
        /// <param name="localBackupFullPath"></param>
        /// <returns></returns>
        private LocalFileInfo GetLocalDirectory(string dirFullPath, string localBackupFullPath)
        {
            var dirInfo = new DirectoryInfo(dirFullPath);
            if (!dirInfo.Exists)
            {
                return null;
            }

            var backupDirInfo = new DirectoryInfo(localBackupFullPath);
            if (!backupDirInfo.Exists)
            {
                return null;
            }

            // 所有本地文件夹
            var ld = new LocalFileInfo()
            {
                FullPath = dirInfo.FullName,
                Key = GetDirectoryKey(localBackupFullPath, dirInfo),
                CreationTime = dirInfo.CreationTime,
                LastWriteTime = dirInfo.LastWriteTime,
                LocalFileName = dirInfo.Name
            };

            // 过滤文件夹
            var cpath = ld.Key.TrimPrefix(backupDirInfo.Name).TrimPath();
            if (!string.IsNullOrWhiteSpace(cpath))
            {
                if (ShouldFilter($"/{cpath}/"))
                {
                    return null;
                }
            }

            // 文件夹
            _localPaths.TryAdd(ld.FullPath, true);

            return ld;
        }

        #endregion 私有方法

        #region 阿里云盘

        /// <summary>
        /// 阿里云盘 - 初始化作业（路径、云盘信息等）
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitialize()
        {
            var isLock = LocalLock.TryLock("init_job_lock", TimeSpan.FromSeconds(60), () =>
            {
                var oldState = CurrentState;

                try
                {
                    LogInfo("作业初始化中");

                    ChangeState(JobState.Initializing);

                    var sw = new Stopwatch();
                    sw.Start();

                    var isLinux = RuntimeInformation.IsOSPlatform(OSPlatform.Linux);

                    _log.LogInformation($"Linux: {isLinux}");

                    // 格式化路径
                    _localRestorePath = _jobConfig.Restore.TrimPath();
                    if (isLinux && !string.IsNullOrWhiteSpace(_localRestorePath))
                    {
                        _localRestorePath = $"/{_localRestorePath}";
                    }

                    _driveSavePath = _jobConfig.Target.TrimPrefix();

                    // 格式化备份目录
                    var sources = _jobConfig.Sources.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.TrimPath()).Distinct().ToList();
                    _jobConfig.Sources.Clear();
                    foreach (var item in sources)
                    {
                        var path = item.TrimPath();
                        if (isLinux && !string.IsNullOrWhiteSpace(path))
                        {
                            // Linux
                            path = $"/{path}";
                            var dir = new DirectoryInfo(path);
                            if (!dir.Exists)
                            {
                                dir.Create();
                            }
                            _jobConfig.Sources.Add($"/{dir.FullName.TrimPath()}");
                        }
                        else
                        {
                            // Windows
                            var dir = new DirectoryInfo(path);
                            if (!dir.Exists)
                            {
                                dir.Create();
                            }
                            _jobConfig.Sources.Add(dir.FullName);
                        }
                    }

                    // 获取云盘信息
                    AliyunDriveInitInfo();

                    // 空间信息
                    AliyunDriveInitSpaceInfo();

                    // VIP 信息
                    AliyunDriveInitVipInfo();

                    // 保存配置
                    _currentStorageConfig.Save();

                    sw.Stop();
                    _log.LogInformation($"作业初始化完成，用时：{sw.ElapsedMilliseconds}ms");

                    ProcessMessage = "作业初始化完成";
                }
                finally
                {
                    // 初始化结束，还原状态
                    ChangeState(oldState);
                }
            });

            //// 如果获取到锁则开始作业
            //if (isLock)
            //{
            //    // 开始作业
            //    StartJob();
            //}

            if (!isLock)
            {
                throw new LogicException("其他作业正在初始化中，请稍后重试");
            }
        }

        /// <summary>
        /// 阿里云盘 - 文件校验
        /// </summary>
        /// <returns></returns>
        private async Task AliyunDriveVerify()
        {
            if (CurrentState != JobState.Verifying)
                return;

            // 根据同步方式，单向、双向、镜像，对文件进行删除、移动、重命名、下载等处理
            switch (_jobConfig.Mode)
            {
                // 镜像同步
                case JobMode.Mirror:
                    {
                        // 计算需要删除的远程文件
                        var localFileKeys = _localFiles.Values.Select(c => c.EncryptKey).Select(c => $"{_driveSavePath}/{c}".TrimPath()).ToList();
                        var removeFileKeys = _driveFiles.Keys.Except(localFileKeys).ToList();
                        if (removeFileKeys.Count > 0)
                        {
                            foreach (var k in removeFileKeys)
                            {
                                if (_driveFiles.TryRemove(k, out var v))
                                {
                                    _driveApi.FileDelete(_driveId, v.FileId, AccessToken, _jobConfig.IsRecycleBin);
                                }
                            }
                        }

                        // 计算需要删除的远程文件夹
                        // 注意需要排除云盘根目录
                        var localFolderKeys = _localFolders.Keys.Select(c => $"{_driveSavePath}/{c}".TrimPath()).ToList();
                        var removeFolderKeys = _driveFolders.Keys
                            .Where(c => !$"{_driveSavePath}/".StartsWith(c))
                            .Except(localFolderKeys).ToList();

                        while (removeFolderKeys.Count > 0)
                        {
                            var k = removeFolderKeys.First();
                            if (_driveFolders.TryRemove(k, out var v))
                            {
                                _driveApi.FileDelete(_driveId, v.FileId, AccessToken, _jobConfig.IsRecycleBin);

                                // 如果删除父文件夹时，则移除所有的子文件夹
                                removeFolderKeys.RemoveAll(c => c.StartsWith(k));
                            }
                            removeFolderKeys.Remove(k);
                        }
                    }
                    break;

                // 冗余同步
                case JobMode.Redundancy:
                    {
                        // 以本地为主，将本地备份到远程，不删除远程文件
                    }
                    break;

                // 双向同步
                case JobMode.TwoWaySync:
                    {
                        // 计算需要同步的远程文件
                        var localFileKeys = _localFiles.Keys.Select(c => $"{_driveSavePath}/{c}".TrimPath()).ToList();
                        var addFileKeys = _driveFiles.Keys.Except(localFileKeys).ToList();
                        if (addFileKeys.Count > 0)
                        {
                            // 验证本地文件是否存在
                            // 验证本地文件是否和远程文件一致
                            // 多线程下载处理

                            var processorCount = GetDownloadThreadCount();
                            var options = new ParallelOptions() { MaxDegreeOfParallelism = processorCount };

                            // 开启并行下载
                            await Parallel.ForEachAsync(addFileKeys, options, async (item, cancellationToken) =>
                            {
                                if (!_driveFiles.TryGetValue(item, out var dinfo) || dinfo == null)
                                {
                                    return;
                                }

                                // 根目录
                                if (dinfo.ParentFileId == "root")
                                {
                                    throw new Exception("不支持根目录文件下载");
                                }

                                // 子目录
                                var parent = _driveFolders.First(x => x.Value.IsFolder && x.Value.FileId == dinfo.ParentFileId)!;

                                // 移除云盘前缀
                                var subPaths = parent.Key.TrimPrefix(_driveSavePath)
                                    .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                                // 判断是哪个备份的根目录
                                var saveRootPath = _jobConfig.Sources
                                .Where(c => $"{string.Join('/', subPaths).TrimPath()}/".StartsWith(new DirectoryInfo(c).Name + "/"))
                                .FirstOrDefault();

                                if (saveRootPath == null)
                                {
                                    throw new Exception("未找到匹配的根目录");
                                }

                                // 找到备份的根目录
                                // 同时移除同步目录的根目录名称
                                var saveRootInfo = new DirectoryInfo(saveRootPath);
                                var savePath = Path.Combine(saveRootPath, Path.Combine(subPaths).TrimPath().TrimPrefix(saveRootInfo.Name));

                                try
                                {
                                    var finalFilePath = Path.Combine(savePath, dinfo.Name);
                                    if (File.Exists(finalFilePath))
                                    {
                                        // 验证本地是否已存在文件，并比较 sha1 值
                                        var hash = ShaHashHelper.ComputeFileHash(finalFilePath, "sha1");
                                        if (hash.Equals(dinfo.ContentHash, StringComparison.OrdinalIgnoreCase))
                                        {
                                            _log.LogInformation($"文件已存在，跳过 {finalFilePath}");
                                            return;
                                        }
                                        else
                                        {
                                            // 文件重命名
                                            // 下载远程文件到本地，如果有冲突，则将本地和远程文件同时重命名
                                            // 如果不一致，则重命名远程文件并下载
                                            var fi = 0;
                                            do
                                            {
                                                var fname = Path.GetFileNameWithoutExtension(dinfo.Name);
                                                var fext = Path.GetExtension(dinfo.Name);
                                                var suffix = $"";
                                                if (fi > 0)
                                                {
                                                    suffix = $" ({fi})";
                                                }
                                                var newName = $"{fname} - 副本{suffix}{fext}";
                                                finalFilePath = Path.Combine(savePath, newName);

                                                // 如果本地和远程都不存在，说明可以重命名
                                                var anyFile = AliyunDriveFileExist(dinfo.ParentFileId, newName);
                                                if (!File.Exists(finalFilePath) && !anyFile)
                                                {
                                                    // 远程文件重命名
                                                    var upData = _driveApi.FileUpdate(_driveId, dinfo.FileId, newName, AccessToken);
                                                    if (upData == null)
                                                    {
                                                        throw new Exception("文件重命名失败");
                                                    }

                                                    // 添加新的文件
                                                    _driveFiles.TryAdd($"{parent.Key}/{newName}".TrimPath(), upData);

                                                    // 删除旧的文件
                                                    _driveFiles.TryRemove(item, out _);
                                                    break;
                                                }
                                                fi++;
                                            } while (true);
                                        }
                                    }

                                    // 获取详情 url
                                    var detail = _driveApi.GetDetail<AliyunDriveFileItem>(_driveId, dinfo.FileId, AccessToken);
                                    await AliyunDriveDownload(detail.Url, dinfo.Name, dinfo.ContentHash, savePath, saveRootPath);

                                    // TODO
                                    // 如果是大文件，则通过下载链接下载文件
                                }
                                catch (Exception ex)
                                {
                                    _log.LogError(ex, "双向同步执行异常");
                                }
                            });

                            foreach (var path in _jobConfig.Sources)
                            {
                                // 清理下载缓存
                                ClearDownloadCache(path);
                            }
                        }
                    }
                    break;

                default:
                    break;
            }

            // 计算云盘文件
            // 计算变动的文件
            // TODO
            // 计算新增、更新、删除、下载的文件
            _jobConfig.Metadata = new JobMetadata()
            {
                FileCount = _driveFiles.Count,
                FolderCount = _driveFolders.Count,
                TotalSize = _driveFiles.Values.Sum(c => c.Size ?? 0)
            };

            _currentStorageConfig.SaveJob(_jobConfig);

            // 校验通过 -> 空闲
            ChangeState(JobState.Idle);
        }

        /// <summary>
        /// 阿里云盘 - 搜索文件
        /// </summary>
        /// <param name="driveId"></param>
        /// <param name="limit"></param>
        /// <returns></returns>
        private void AliyunDriveSearchFiles(string driveId, int limit = 100)
        {
            try
            {
                ProcessMessage = "正在加载云盘文件";

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

                            // 路径必须符合，否则跳过
                            // 如果备份目录以当前目录开头，说明是备份目录的父级
                            if ($"{_driveSavePath}/".StartsWith(keyPath)
                                // 如果相等
                                || keyPath == _driveSavePath
                                // 如果当前目录是以备份目录开头，说明是备份目录的子目录
                                || keyPath.StartsWith($"{_driveSavePath}/"))
                            {
                                _driveFolders.TryAdd(keyPath, item);
                                LoadPath(item.FileId);
                            }
                        }
                    }
                }

                // 再加载列表
                foreach (var item in allItems.Where(c => c.IsFile))
                {
                    if (item.IsFile)
                    {
                        // 文件必须在备份路径中
                        var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).FirstOrDefault();
                        if (!string.IsNullOrWhiteSpace(parent.Key))
                        {
                            _driveFiles.TryAdd($"{parent.Key}/{item.Name}".TrimPath(), item);
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
                    else
                    {
                        // 如果是文件夹，则递归获取子文件列表
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
                }
            }
            catch (Exception)
            {
                throw;
            }

            _log.LogInformation($"云盘文件加载完成，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹。");
        }

        /// <summary>
        /// 阿里云盘 - 下载文件
        /// </summary>
        /// <param name="url"></param>
        /// <param name="fileName"></param>
        /// <param name="fileSha1"></param>
        /// <param name="savePath"></param>
        /// <returns></returns>
        private async Task AliyunDriveDownload(string url, string fileName, string fileSha1, string savePath, string saveRootPath)
        {
            var tempFilePath = Path.Combine(saveRootPath, ".duplicaticache", $"{fileName}.{Guid.NewGuid():N}.duplicatidownload");
            var finalFilePath = Path.Combine(savePath, fileName);

            using (var httpClient = new HttpClient())
            {
                try
                {
                    // 设置 45 分钟超时
                    httpClient.Timeout = TimeSpan.FromMinutes(45);

                    var tmpPath = Path.GetDirectoryName(tempFilePath);
                    var path = Path.GetDirectoryName(finalFilePath);

                    lock (_localLock)
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
                    var sha1 = ShaHashHelper.ComputeFileHash(tempFilePath, "sha1");
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

        /// <summary>
        /// 阿里云盘 - 创建文件夹（同名不创建）
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="parentId"></param>
        /// <returns></returns>
        private string AliyunDriveCreateFolder(string filePath, string parentId)
        {
            // 同一级文件夹共用一个锁
            using (_lock.Lock($"create_folder_{parentId}"))
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
                    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == parentId).First()!;
                    var path = $"{parent.Key}/{name}".TrimPath();
                    if (_driveFolders.ContainsKey(path))
                    {
                        return _driveFolders[path].FileId;
                    }
                }

                var data = _driveApi.CreateFolder(_driveId, parentId, name, AccessToken);
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
        }


        /// <summary>
        /// 阿里云盘 - 上传文件
        /// </summary>
        /// <param name="localFileInfo"></param>
        /// <param name="needPreHash"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        /// <exception cref="HttpRequestException"></exception>
        private async Task AliyunDriveUploadFile(LocalFileInfo localFileInfo, bool needPreHash = true)
        {
            var fileFullPath = localFileInfo.FullPath;

            var fileInfo = new FileInfo(fileFullPath);
            if (!fileInfo.Exists)
            {
                // 本地文件不存在
                _localFiles.TryRemove(localFileInfo.Key, out _);
                return;
            }

            // 文件名
            var name = AliyunDriveHelper.EncodeFileName(Path.GetFileName(fileInfo.Name));

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
                        savePathsParentFileId = AliyunDriveCreateFolder(subPath, savePathsParentFileId);
                    }
                }

                if (!_driveFolders.ContainsKey(saveParentPath))
                {
                    throw new Exception("文件夹创建失败");
                }

                saveParentFileId = _driveFolders[saveParentPath].FileId;
            }

            localFileInfo.IsEncrypt = _jobConfig.IsEncrypt;
            localFileInfo.IsEncryptName = _jobConfig.IsEncryptName;

            // 加密处理
            if (_jobConfig.IsEncrypt)
            {
                if (_jobConfig.IsEncryptName)
                {
                    name = HashHelper.ComputeHash(Encoding.UTF8.GetBytes(name), "MD5").ToHex() + ".e";
                }
                else
                {
                    name += ".e";
                }

                localFileInfo.EncryptFileName = name;
                saveFilePath = $"{saveParentPath}/{name}".TrimPath();

                // 如果文件已上传则跳过
                // 对比文件差异 sha1
                if (_driveFiles.TryGetValue(saveFilePath, out var driveItem) && driveItem != null)
                {
                    // 如果存在同名文件，且内容相同则跳过
                    if (driveItem.ContentHash.Equals(localFileInfo.Sha1, StringComparison.OrdinalIgnoreCase))
                    {
                        return;
                    }
                }

                // 根据加密算法生成加密文件
                var encryptCachePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "cache");
                Directory.CreateDirectory(encryptCachePath);

                var encryptCacheFile = Path.Combine(encryptCachePath, $"{Guid.NewGuid():N}.c");
                var encryptFile = Path.Combine(encryptCachePath, name);

                using FileStream inputFileStream = new FileStream(fileFullPath, FileMode.Open, FileAccess.Read);
                using FileStream outputFileStream = new FileStream(encryptCacheFile, FileMode.Create, FileAccess.Write);
                CompressionHelper.CompressStream(inputFileStream, outputFileStream,
                    _jobConfig.CompressAlgorithm, _jobConfig.EncryptAlgorithm, _jobConfig.EncryptKey, _jobConfig.HashAlgorithm, _jobConfig.IsEncryptName,
                    localFileInfo.LocalFileName);

                // 关闭文件流
                inputFileStream.Close();
                outputFileStream.Close();

                //// 解密测试
                //using FileStream inputFileStream1 = new FileStream(encryptCacheFile, FileMode.Open, FileAccess.Read);
                //using FileStream outputFileStream1 = new FileStream(Path.Combine(encryptCachePath, $"{Guid.NewGuid():N}.c"), FileMode.Create, FileAccess.Write);
                //CompressionHelper.DecompressStream(inputFileStream1, outputFileStream1, _jobConfig.CompressAlgorithm, _jobConfig.EncryptAlgorithm, _jobConfig.EncryptKey, _jobConfig.HashAlgorithm,
                //    _jobConfig.IsEncryptName, out var decryptFileName);
                //inputFileStream1.Close();
                //outputFileStream1.Close();

                try
                {
                    // 更新上传文件信息
                    // 将文件重命名为 hash.e
                    File.Move(encryptCacheFile, encryptFile, true);
                    var encryptFileInfo = new FileInfo(encryptFile);

                    fileFullPath = encryptFile;
                    fileInfo = encryptFileInfo;

                    // 上传文件
                    await AliyunDriveUploadFileDoWork(localFileInfo, fileFullPath, fileInfo, saveParentFileId, saveFilePath, name, needPreHash);
                }
                finally
                {
                    // 删除加密文件和缓存文件，如果存在
                    if (File.Exists(encryptCacheFile))
                    {
                        File.Delete(encryptCacheFile);
                    }
                    if (File.Exists(encryptFile))
                    {
                        File.Delete(encryptFile);
                    }
                }
            }
            else
            {
                // 上传文件
                await AliyunDriveUploadFileDoWork(localFileInfo, fileFullPath, fileInfo, saveParentFileId, saveFilePath, name, needPreHash);
            }
        }

        /// <summary>
        /// 阿里云盘 - 上传文件
        /// </summary>
        /// <param name="localFileInfo"></param>
        /// <param name="fileFullPath"></param>
        /// <param name="fileInfo"></param>
        /// <param name="saveParentFileId"></param>
        /// <param name="saveFilePath"></param>
        /// <param name="name"></param>
        /// <param name="needPreHash"></param>
        /// <returns></returns>
        private async Task AliyunDriveUploadFileDoWork(LocalFileInfo localFileInfo, string fileFullPath, FileInfo fileInfo, string saveParentFileId, string saveFilePath, string name, bool needPreHash)
        {
            // 分块上传 buffer 大小
            var partBuffSize = 1024 * 1024 * 16;

            // 本地文件没有 hash 时，计算本地文件的 hash
            if (string.IsNullOrWhiteSpace(localFileInfo.Hash))
            {
                // 计算 hash
                localFileInfo.Hash = ShaHashHelper.ComputeFileHash(localFileInfo.FullPath, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, localFileInfo.Length);
            }

            // 如果是加密文件
            if (localFileInfo.IsEncrypt)
            {
                // 本地文件没有 sha1 时，计算本地文件的 sha1
                if (string.IsNullOrWhiteSpace(localFileInfo.Sha1))
                {
                    localFileInfo.Sha1 = ShaHashHelper.ComputeFileHash(fileFullPath, "sha1");
                }

            }
            else
            {
                // 本地文件没有 sha1 时，计算本地文件的 sha1
                if (string.IsNullOrWhiteSpace(localFileInfo.Sha1))
                {
                    localFileInfo.Sha1 = ShaHashHelper.ComputeFileHash(localFileInfo.FullPath, "sha1");
                }
            }



            // 如果文件已上传则跳过
            // 对比文件差异 sha1
            if (_driveFiles.TryGetValue(saveFilePath, out var driveItem) && driveItem != null)
            {
                // 如果存在同名文件，且内容相同则跳过
                if (driveItem.ContentHash.Equals(localFileInfo.Sha1, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }
                else
                {
                    // 删除同名文件
                    _driveApi.FileDelete(_driveId, driveItem.FileId, AccessToken, _jobConfig.IsRecycleBin);
                    _driveFiles.TryRemove(saveFilePath, out _);

                    // 再次搜索确认是否有同名文件，有则删除
                    do
                    {
                        var delData = _driveApi.Exist(_driveId, saveParentFileId, name, AccessToken);
                        if (delData?.Items?.Count > 0)
                        {
                            foreach (var f in delData.Items)
                            {
                                var delRes = _driveApi.FileDelete(_driveId, f.FileId, AccessToken, _jobConfig.IsRecycleBin);
                                if (delRes == null)
                                {
                                    _log.LogInformation($"远程文件已删除 {localFileInfo.Key}");
                                }
                            }
                        }
                        else
                        {
                            break;
                        }
                    } while (true);
                }
            }

            _log.LogInformation($"正在上传文件 {localFileInfo.Key}");

            var request = new RestRequest("/adrive/v1.0/openFile/create", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");

            var fileSize = fileInfo.Length;

            // 分块计算
            var partCount = (int)Math.Ceiling((double)fileSize / partBuffSize);

            var partInfoList = Enumerable.Range(1, partCount).Select(i => new
            {
                part_number = i
            }).ToArray();

            object body = new
            {
                drive_id = _driveId,
                parent_file_id = saveParentFileId,
                name = name,
                type = "file",

                // refuse 同名不创建
                // ignore 同名文件可创建

                check_name_mode = "refuse", // 覆盖文件模式
                size = fileSize,

                part_info_list = fileSize <= partBuffSize ? null : partInfoList // 分块计算
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
                    var preHash = AliyunDriveHelper.GenerateStartSHA1(fileFullPath);
                    body = new
                    {
                        drive_id = _driveId,
                        parent_file_id = saveParentFileId,
                        name = name,
                        type = "file",

                        // refuse 同名不创建
                        // ignore 同名文件可创建
                        check_name_mode = "refuse",
                        size = fileInfo.Length,
                        pre_hash = preHash,

                        part_info_list = fileSize <= partBuffSize ? null : partInfoList // 分块计算
                    };
                }
                else
                {
                    // > 10kb 且 < 1mb 的文件直接计算 sha1
                    var proofCode = AliyunDriveHelper.GenerateProofCode(fileFullPath, fileSize, AccessToken);
                    var contentHash = AliyunDriveHelper.GenerateSHA1(fileFullPath);

                    body = new
                    {
                        drive_id = _driveId,
                        parent_file_id = saveParentFileId,
                        name = name,
                        type = "file",

                        // refuse 同名不创建
                        // ignore 同名文件可创建
                        check_name_mode = "refuse",
                        size = fileInfo.Length,
                        content_hash = contentHash,
                        content_hash_name = "sha1",
                        proof_version = "v1",
                        proof_code = proofCode,

                        part_info_list = fileSize <= partBuffSize ? null : partInfoList
                    };
                }
            }
            request.AddBody(body);
            var response = _driveApi.WithRetry<dynamic>(request);

            // 如果需要秒传，并且需要预处理时
            // System.Net.HttpStatusCode.Conflict 注意可能不是 409
            if (isRapidUpload && needPreHash
                && (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict)
                && response.Content.Contains("PreHashMatched"))
            {
                using (var mcDoc = JsonDocument.Parse(response.Content))
                {
                    // 尝试获取code属性的值
                    if (mcDoc.RootElement.TryGetProperty("code", out JsonElement codeElement))
                    {
                        var code = codeElement.GetString();
                        if (code == "PreHashMatched")
                        {
                            // 匹配成功，进行完整的秒传，不需要预处理
                            needPreHash = false;
                            await AliyunDriveUploadFileDoWork(localFileInfo, fileFullPath, fileInfo, saveParentFileId, saveFilePath, name, needPreHash);
                            return;
                        }
                    }
                }
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                _log.LogError(response.ErrorException, $"文件上传失败 {localFileInfo.Key} {response.Content}");

                throw response.ErrorException ?? new Exception($"文件上传失败 {localFileInfo.Key}");
            }

            using var doc = JsonDocument.Parse(response.Content!);
            var root = doc.RootElement;

            var drive_id = root.GetProperty("drive_id").GetString();
            var file_id = root.GetProperty("file_id").GetString();
            var upload_id = root.GetProperty("upload_id").GetString();

            var rapid_upload = root.GetProperty("rapid_upload").GetBoolean();
            if (rapid_upload)
            {
                _log.LogInformation($"文件秒传成功 {localFileInfo.Key}");
                return;
            }

            var partList = root.GetProperty("part_info_list").EnumerateArray().Select(p => new
            {
                part_number = p.GetProperty("part_number").GetInt32(),
                upload_url = p.GetProperty("upload_url").GetString()
            }).ToArray();

            if (fileSize > partBuffSize)
            {
                using var fs = new FileStream(fileFullPath, FileMode.Open, FileAccess.Read);
                var partNumber = 1;
                var buffer = new byte[partBuffSize];
                int bytesRead;

                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    var partUploadUrl = partList.First(p => p.part_number == partNumber).upload_url;
                    var content = new ByteArrayContent(buffer, 0, bytesRead);
                    await UploadPart(partUploadUrl, content);
                    partNumber++;
                }
            }
            else
            {
                var partUploadUrl = partList.First().upload_url;
                byte[] fileData = await File.ReadAllBytesAsync(fileFullPath);
                var content = new ByteArrayContent(fileData);
                await UploadPart(partUploadUrl, content);
            }

            var completeResponse = _driveApi.UploadComplete(_driveId, file_id, upload_id, AccessToken);
            if (completeResponse.ParentFileId == "root")
            {
                _driveFiles.TryAdd($"{completeResponse.Name}".TrimPath(), completeResponse);
            }
            else
            {
                var parent = _driveFolders.First(c => c.Value.Type == "folder" && c.Value.FileId == completeResponse.ParentFileId);
                var path = $"{parent.Key}/{completeResponse.Name}".TrimPath();
                _driveFiles.TryAdd(path, completeResponse);
            }

            //var upload_url = root.GetProperty("part_info_list").EnumerateArray().FirstOrDefault().GetProperty("upload_url").GetString();

            //// 读取文件作为字节流
            //byte[] fileData = await File.ReadAllBytesAsync(fileFullPath);

            //// 创建HttpContent
            //var content = new ByteArrayContent(fileData);

            //// 发送PUT请求
            //HttpResponseMessage uploadRes = null;

            //// 定义重试策略 3 次
            //var retryPolicy = Policy
            //    .Handle<HttpRequestException>()
            //    .WaitAndRetryAsync(3, retryAttempt =>
            //    {
            //        // 5s 25s 125s 后重试
            //        return TimeSpan.FromSeconds(Math.Pow(5, retryAttempt));
            //    });

            //// 执行带有重试策略的请求
            //await retryPolicy.ExecuteAsync(async () =>
            //{
            //    uploadRes = await _uploadHttpClient.PutAsync(upload_url, content);

            //    if (!uploadRes.IsSuccessStatusCode)
            //    {
            //        throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
            //    }
            //});

            //// 检查请求是否成功
            //if (!uploadRes.IsSuccessStatusCode)
            //{
            //    throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
            //}

            //// 将文件添加到上传列表
            //var data = _driveApi.UploadComplete(_driveId, file_id, upload_id, AccessToken);
            //if (data.ParentFileId == "root")
            //{
            //    // 当前目录在根路径
            //    // /{当前路径}/
            //    _driveFiles.TryAdd($"{data.Name}".TrimPath(), data);
            //}
            //else
            //{
            //    // 计算父级路径
            //    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == data.ParentFileId).First()!;
            //    var path = $"{parent.Key}/{data.Name}".TrimPath();

            //    // /{父级路径}/{当前路径}/
            //    _driveFiles.TryAdd(path, data);
            //}

            _log.LogInformation($"文件上传成功 {localFileInfo.Key}");
        }

        private async Task UploadPart(string uploadUrl, ByteArrayContent content)
        {
            HttpResponseMessage uploadRes = null;
            var retryPolicy = Policy
                .Handle<HttpRequestException>()
                .WaitAndRetryAsync(3, retryAttempt => TimeSpan.FromSeconds(Math.Pow(5, retryAttempt)));

            await retryPolicy.ExecuteAsync(async () =>
            {
                uploadRes = await _uploadHttpClient.PutAsync(uploadUrl, content);
                if (!uploadRes.IsSuccessStatusCode)
                {
                    throw new HttpRequestException($"Failed to upload file part. Status code: {uploadRes.StatusCode}");
                }
            });

            if (!uploadRes.IsSuccessStatusCode)
            {
                throw new HttpRequestException($"Failed to upload file part. Status code: {uploadRes.StatusCode}");
            }
        }

        /// <summary>
        /// 阿里云盘 - 获取文件列表（限流 4 QPS）
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
        private async Task AliyunDriveFetchAllFiles(string driveId,
            string parentFileId,
            int limit = 100,
            string orderBy = null,
            string orderDirection = null,
            string category = null,
            string type = "all",

            // 是否递归获取子文件夹
            bool isDeepLoop = true)
        {
            try
            {
                var allItems = new List<AliyunDriveFileItem>();
                string marker = null;
                do
                {
                    var sw = new Stopwatch();
                    sw.Start();

                    var responseData = _driveApi.FileList(driveId, parentFileId, limit, marker, orderBy, orderDirection, category, type, AccessToken);
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
                    // 如果是文件夹，则递归获取子文件列表
                    if (item.Type == "folder")
                    {
                        // 如果是根目录
                        if (item.ParentFileId == "root")
                        {
                            item.Key = $"{item.Name}".TrimPath();
                            _driveFolders.TryAdd(item.Key, item);
                        }
                        else
                        {
                            var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                            item.Key = $"{parent.Key}/{item.Name}".TrimPath();
                            _driveFolders.TryAdd(item.Key, item);
                        }

                        if (isDeepLoop)
                        {
                            await AliyunDriveFetchAllFiles(driveId, item.FileId, limit, orderBy, orderDirection, category, type);
                        }
                    }
                    else
                    {
                        // 如果是根目录的文件
                        if (item.ParentFileId == "root")
                        {
                            item.Key = $"{item.Name}".TrimPath();
                            _driveFiles.TryAdd(item.Key, item);
                        }
                        else
                        {
                            // 构建文件路径作为字典的键
                            var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == item.ParentFileId).First()!;
                            item.Key = $"{parent.Key}/{item.Name}".TrimPath();
                            _driveFiles.TryAdd(item.Key, item);
                        }
                    }

                    _log.LogInformation($"云盘文件加载中，包含 {_driveFiles.Count} 个文件，{_driveFolders.Count} 个文件夹，{item.Name}");
                }
            }
            catch (Exception)
            {
                throw;
            }
        }


        /// <summary>
        /// 阿里云盘 - 获取文件列表（限流 4 QPS）
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
        public async Task AliyunDriveFetchAllSubFiles(string parentFileId, int limit = 100, string orderBy = null, string orderDirection = null, string category = null, string type = "all")
        {
            await AliyunDriveFetchAllFiles(_driveId, parentFileId, limit, orderBy, orderDirection, category, type);
        }

        /// <summary>
        /// 阿里云盘 - 获取用户 drive 信息
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitInfo()
        {
            var data = _driveApi.DriveInfo(AccessToken);

            _driveId = data.DefaultDriveId;

            if (_jobConfig.DefaultDrive == "backup" && string.IsNullOrWhiteSpace(data.BackupDriveId))
            {
                _driveId = data.BackupDriveId;
            }
            else if (_jobConfig.DefaultDrive == "resource" && !string.IsNullOrWhiteSpace(data.ResourceDriveId))
            {
                _driveId = data.ResourceDriveId;
            }

            _currentStorageConfig.Name = !string.IsNullOrWhiteSpace(data.NickName) ? data.NickName : data.Name;
        }

        /// <summary>
        /// 阿里云盘 - 获取用户空间信息
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitSpaceInfo()
        {
            var data = _driveApi.SpaceInfo(AccessToken);

            _currentStorageConfig.Metadata ??= new();
            _currentStorageConfig.Metadata.UsedSize = data?.PersonalSpaceInfo?.UsedSize;
            _currentStorageConfig.Metadata.TotalSize = data?.PersonalSpaceInfo?.TotalSize;
        }

        /// <summary>
        /// 阿里云盘 - 获取用户 VIP 信息
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitVipInfo()
        {
            var data = _driveApi.VipInfo(AccessToken);
            _currentStorageConfig.Metadata ??= new();
            _currentStorageConfig.Metadata.Identity = data?.Identity;
            _currentStorageConfig.Metadata.Level = data?.Level;
            _currentStorageConfig.Metadata.Expire = data?.ExpireDateTime;
        }

        /// <summary>
        /// 阿里云盘 - 初始化备份目录
        /// </summary>
        /// <returns></returns>
        private void AliyunDriveInitBackupPath()
        {
            // 首先加载根目录结构
            // 并计算需要保存的目录
            // 计算/创建备份文件夹
            // 如果备份文件夹不存在
            var saveRootSubPaths = _driveSavePath.Split('/').Select(c => c.Trim().Trim('/')).Where(c => !string.IsNullOrWhiteSpace(c)).ToArray();
            var searchParentFileId = "root";
            foreach (var subPath in saveRootSubPaths)
            {
                var subItem = _driveApi.GetSubFolders(_driveId, searchParentFileId, subPath, AccessToken);
                var okPath = subItem.Items.FirstOrDefault(x => x.Name == subPath && x.Type == "folder" && x.ParentFileId == searchParentFileId);
                if (okPath == null)
                {
                    // 未找到目录
                    searchParentFileId = AliyunDriveCreateFolder(subPath, searchParentFileId);
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

        /// <summary>
        /// 阿里云盘 - 判断文件是否存在
        /// </summary>
        /// <param name="parentFileId"></param>
        /// <param name="name"></param>
        /// <param name="type">folder | file</param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        private bool AliyunDriveFileExist(string parentFileId, string name, string type = "file")
        {
            return _driveApi.Exist(_driveId, parentFileId, name, type)?.Items?.Count > 0;
        }

        /// <summary>
        /// 阿里云盘 - 获取文件详情
        /// </summary>
        /// <param name="fileId"></param>
        /// <returns></returns>
        public T AliyunDriveGetDetail<T>(string fileId) where T : AliyunDriveFileItem
        {
            return _driveApi.GetDetail<T>(_driveId, fileId, AccessToken);
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

        /*
/// <summary>
/// 阿里云盘 - 上传文件
/// </summary>
/// <param name="localFileInfo"></param>
/// <param name="needPreHash"></param>
/// <returns></returns>
/// <exception cref="Exception"></exception>
/// <exception cref="HttpRequestException"></exception>
private async Task AliyunDriveUploadFile(LocalFileInfo localFileInfo, bool needPreHash = true)
{
    var fileFullPath = localFileInfo.FullPath;

    var fileInfo = new FileInfo(fileFullPath);
    if (!fileInfo.Exists)
    {
        // 本地文件不存在
        _localFiles.TryRemove(localFileInfo.Key, out _);
        return;
    }

    // 文件名
    var name = AliyunDriveHelper.EncodeFileName(Path.GetFileName(fileInfo.Name));

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
                savePathsParentFileId = AliyunDriveCreateFolder(subPath, savePathsParentFileId);
            }
        }

        if (!_driveFolders.ContainsKey(saveParentPath))
        {
            throw new Exception("文件夹创建失败");
        }

        saveParentFileId = _driveFolders[saveParentPath].FileId;
    }

    localFileInfo.IsEncrypt = _jobConfig.IsEncrypt;
    localFileInfo.IsEncryptName = _jobConfig.IsEncryptName;

    // 加密处理
    if (_jobConfig.IsEncrypt)
    {
        // 根据加密算法生成加密文件
        var encryptCachePath = Path.Combine(Directory.GetCurrentDirectory(), "caches");
        Directory.CreateDirectory(encryptCachePath);
        var encryptCacheFile = Path.Combine(encryptCachePath, $"{Guid.NewGuid():N}.e.cache");

        using FileStream inputFileStream = new FileStream(fileFullPath, FileMode.Open, FileAccess.Read);
        using FileStream outputFileStream = new FileStream(encryptCacheFile, FileMode.Create, FileAccess.Write);
        CompressionHelper.CompressStream(inputFileStream, outputFileStream,
            _jobConfig.CompressAlgorithm, _jobConfig.EncryptAlgorithm, _jobConfig.EncryptKey, _jobConfig.HashAlgorithm, _jobConfig.IsEncryptName, name, out var encryptFileHash);

        // 关闭文件流
        inputFileStream.Close();
        outputFileStream.Close();

        //// 解密测试
        //using FileStream inputFileStream1 = new FileStream(encryptCacheFile, FileMode.Open, FileAccess.Read);
        //using FileStream outputFileStream1 = new FileStream(Path.Combine(encryptCachePath, $"{Guid.NewGuid():N}.d.cache"), FileMode.Create, FileAccess.Write);
        //CompressionHelper.DecompressStream(inputFileStream1, outputFileStream1, _jobConfig.CompressAlgorithm, _jobConfig.EncryptAlgorithm, _jobConfig.EncryptKey, _jobConfig.HashAlgorithm,
        //    _jobConfig.IsEncryptName, out var decryptFileName);
        //inputFileStream1.Close();
        //outputFileStream1.Close();

        // 将文件重命名为 hash.e
        var encryptFileName = $"{encryptFileHash}.e";
        var encryptFilePath = Path.Combine(encryptCachePath, encryptFileName);

        try
        {
            // 更新上传文件信息
            File.Move(encryptCacheFile, encryptFilePath, true);
            var encryptFileInfo = new FileInfo(encryptFilePath);

            name = encryptFileName;
            fileFullPath = encryptFilePath;
            fileInfo = encryptFileInfo;
            saveFilePath = $"{saveParentPath}/{name}".TrimPath();

            localFileInfo.EncryptFileName = encryptFileName;

            // ---------------------- 上传文件 ---------------------

            // 分块上传 buffer 大小
            var partBuffSize = 1024 * 1024 * 16;

            if (string.IsNullOrWhiteSpace(localFileInfo.Hash))
            {
                // 计算 hash
                localFileInfo.Hash = ShaHashHelper.ComputeFileHash(fileFullPath, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, localFileInfo.Length);
            }

            // 本地文件没有 sha1 时，计算本地文件的 sha1
            if (string.IsNullOrWhiteSpace(localFileInfo.Sha1))
            {
                localFileInfo.Sha1 = ShaHashHelper.ComputeFileHash(fileFullPath, "sha1");
            }

            // 如果文件已上传则跳过
            // 对比文件差异 sha1
            if (_driveFiles.TryGetValue(saveFilePath, out var driveItem) && driveItem != null)
            {
                // 如果存在同名文件，且内容相同则跳过
                if (driveItem.ContentHash == localFileInfo.Sha1)
                {
                    return;
                }
                else
                {
                    // 删除同名文件
                    _driveApi.FileDelete(_driveId, driveItem.FileId, AccessToken, _jobConfig.IsRecycleBin);
                    _driveFiles.TryRemove(saveFilePath, out _);

                    // 再次搜索确认是否有同名文件，有则删除
                    do
                    {
                        var delData = _driveApi.Exist(_driveId, saveParentFileId, name, AccessToken);
                        if (delData?.Items?.Count > 0)
                        {
                            foreach (var f in delData.Items)
                            {
                                var delRes = _driveApi.FileDelete(_driveId, f.FileId, AccessToken, _jobConfig.IsRecycleBin);
                                if (delRes == null)
                                {
                                    _log.LogInformation($"远程文件已删除 {localFileInfo.Key}");
                                }
                            }
                        }
                        else
                        {
                            break;
                        }
                    } while (true);
                }
            }

            _log.LogInformation($"正在上传文件 {localFileInfo.Key}");

            var request = new RestRequest("/adrive/v1.0/openFile/create", Method.Post);
            request.AddHeader("Content-Type", "application/json");
            request.AddHeader("Authorization", $"Bearer {AccessToken}");

            var fileSize = fileInfo.Length;

            // 分块计算
            var partCount = (int)Math.Ceiling((double)fileSize / partBuffSize);

            var partInfoList = Enumerable.Range(1, partCount).Select(i => new
            {
                part_number = i
            }).ToArray();

            object body = new
            {
                drive_id = _driveId,
                parent_file_id = saveParentFileId,
                name = name,
                type = "file",

                // refuse 同名不创建
                // ignore 同名文件可创建

                check_name_mode = "refuse", // 覆盖文件模式
                size = fileSize,

                part_info_list = fileSize <= partBuffSize ? null : partInfoList // 分块计算
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
                    var preHash = AliyunDriveHelper.GenerateStartSHA1(fileFullPath);
                    body = new
                    {
                        drive_id = _driveId,
                        parent_file_id = saveParentFileId,
                        name = name,
                        type = "file",

                        // refuse 同名不创建
                        // ignore 同名文件可创建
                        check_name_mode = "refuse",
                        size = fileInfo.Length,
                        pre_hash = preHash,

                        part_info_list = fileSize <= partBuffSize ? null : partInfoList // 分块计算
                    };
                }
                else
                {
                    // > 10kb 且 < 1mb 的文件直接计算 sha1
                    var proofCode = AliyunDriveHelper.GenerateProofCode(fileFullPath, fileSize, AccessToken);
                    var contentHash = AliyunDriveHelper.GenerateSHA1(fileFullPath);

                    body = new
                    {
                        drive_id = _driveId,
                        parent_file_id = saveParentFileId,
                        name = name,
                        type = "file",

                        // refuse 同名不创建
                        // ignore 同名文件可创建
                        check_name_mode = "refuse",
                        size = fileInfo.Length,
                        content_hash = contentHash,
                        content_hash_name = "sha1",
                        proof_version = "v1",
                        proof_code = proofCode,

                        part_info_list = fileSize <= partBuffSize ? null : partInfoList
                    };
                }
            }
            request.AddBody(body);
            var response = _driveApi.WithRetry<dynamic>(request);

            // 如果需要秒传，并且需要预处理时
            // System.Net.HttpStatusCode.Conflict 注意可能不是 409
            if (isRapidUpload && needPreHash
                && (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict)
                && response.Content.Contains("PreHashMatched"))
            {
                using (var mcDoc = JsonDocument.Parse(response.Content))
                {
                    // 尝试获取code属性的值
                    if (mcDoc.RootElement.TryGetProperty("code", out JsonElement codeElement))
                    {
                        var code = codeElement.GetString();
                        if (code == "PreHashMatched")
                        {
                            // 匹配成功，进行完整的秒传，不需要预处理
                            await AliyunDriveUploadFile(localFileInfo, false);
                            return;
                        }
                    }
                }
            }

            if (response.StatusCode != HttpStatusCode.OK)
            {
                _log.LogError(response.ErrorException, $"文件上传失败 {localFileInfo.Key} {response.Content}");

                throw response.ErrorException ?? new Exception($"文件上传失败 {localFileInfo.Key}");
            }

            using var doc = JsonDocument.Parse(response.Content!);
            var root = doc.RootElement;

            var drive_id = root.GetProperty("drive_id").GetString();
            var file_id = root.GetProperty("file_id").GetString();
            var upload_id = root.GetProperty("upload_id").GetString();

            var rapid_upload = root.GetProperty("rapid_upload").GetBoolean();
            if (rapid_upload)
            {
                _log.LogInformation($"文件秒传成功 {localFileInfo.Key}");
                return;
            }

            var partList = root.GetProperty("part_info_list").EnumerateArray().Select(p => new
            {
                part_number = p.GetProperty("part_number").GetInt32(),
                upload_url = p.GetProperty("upload_url").GetString()
            }).ToArray();

            if (fileSize > partBuffSize)
            {
                using var fs = new FileStream(fileFullPath, FileMode.Open, FileAccess.Read);
                var partNumber = 1;
                var buffer = new byte[partBuffSize];
                int bytesRead;

                while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
                {
                    var partUploadUrl = partList.First(p => p.part_number == partNumber).upload_url;
                    var content = new ByteArrayContent(buffer, 0, bytesRead);
                    await UploadPart(partUploadUrl, content);
                    partNumber++;
                }
            }
            else
            {
                var partUploadUrl = partList.First().upload_url;
                byte[] fileData = await File.ReadAllBytesAsync(fileFullPath);
                var content = new ByteArrayContent(fileData);
                await UploadPart(partUploadUrl, content);
            }

            var completeResponse = _driveApi.UploadComplete(_driveId, file_id, upload_id, AccessToken);
            if (completeResponse.ParentFileId == "root")
            {
                _driveFiles.TryAdd($"{completeResponse.Name}".TrimPath(), completeResponse);
            }
            else
            {
                var parent = _driveFolders.First(c => c.Value.Type == "folder" && c.Value.FileId == completeResponse.ParentFileId);
                var path = $"{parent.Key}/{completeResponse.Name}".TrimPath();
                _driveFiles.TryAdd(path, completeResponse);
            }

            _log.LogInformation($"文件上传成功 {localFileInfo.Key}");
        }
        finally
        {
            // 删除加密文件和缓存文件，如果存在
            if (File.Exists(encryptCacheFile))
            {
                File.Delete(encryptCacheFile);
            }
            if (File.Exists(encryptFilePath))
            {
                File.Delete(encryptFilePath);
            }
        }
    }
    else
    {
        // 分块上传 buffer 大小
        var partBuffSize = 1024 * 1024 * 16;

        if (string.IsNullOrWhiteSpace(localFileInfo.Hash))
        {
            // 计算 hash
            localFileInfo.Hash = ShaHashHelper.ComputeFileHash(fileFullPath, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, localFileInfo.Length);
        }

        // 本地文件没有 sha1 时，计算本地文件的 sha1
        if (string.IsNullOrWhiteSpace(localFileInfo.Sha1))
        {
            localFileInfo.Sha1 = ShaHashHelper.ComputeFileHash(fileFullPath, "sha1");
        }

        // 如果文件已上传则跳过
        // 对比文件差异 sha1
        if (_driveFiles.TryGetValue(saveFilePath, out var driveItem) && driveItem != null)
        {
            // 如果存在同名文件，且内容相同则跳过
            if (driveItem.ContentHash == localFileInfo.Sha1)
            {
                return;
            }
            else
            {
                // 删除同名文件
                _driveApi.FileDelete(_driveId, driveItem.FileId, AccessToken, _jobConfig.IsRecycleBin);
                _driveFiles.TryRemove(saveFilePath, out _);

                // 再次搜索确认是否有同名文件，有则删除
                do
                {
                    var delData = _driveApi.Exist(_driveId, saveParentFileId, name, AccessToken);
                    if (delData?.Items?.Count > 0)
                    {
                        foreach (var f in delData.Items)
                        {
                            var delRes = _driveApi.FileDelete(_driveId, f.FileId, AccessToken, _jobConfig.IsRecycleBin);
                            if (delRes == null)
                            {
                                _log.LogInformation($"远程文件已删除 {localFileInfo.Key}");
                            }
                        }
                    }
                    else
                    {
                        break;
                    }
                } while (true);
            }
        }

        _log.LogInformation($"正在上传文件 {localFileInfo.Key}");

        var request = new RestRequest("/adrive/v1.0/openFile/create", Method.Post);
        request.AddHeader("Content-Type", "application/json");
        request.AddHeader("Authorization", $"Bearer {AccessToken}");

        var fileSize = fileInfo.Length;

        // 分块计算
        var partCount = (int)Math.Ceiling((double)fileSize / partBuffSize);

        var partInfoList = Enumerable.Range(1, partCount).Select(i => new
        {
            part_number = i
        }).ToArray();

        object body = new
        {
            drive_id = _driveId,
            parent_file_id = saveParentFileId,
            name = name,
            type = "file",

            // refuse 同名不创建
            // ignore 同名文件可创建

            check_name_mode = "refuse", // 覆盖文件模式
            size = fileSize,

            part_info_list = fileSize <= partBuffSize ? null : partInfoList // 分块计算
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
                var preHash = AliyunDriveHelper.GenerateStartSHA1(fileFullPath);
                body = new
                {
                    drive_id = _driveId,
                    parent_file_id = saveParentFileId,
                    name = name,
                    type = "file",

                    // refuse 同名不创建
                    // ignore 同名文件可创建
                    check_name_mode = "refuse",
                    size = fileInfo.Length,
                    pre_hash = preHash,

                    part_info_list = fileSize <= partBuffSize ? null : partInfoList // 分块计算
                };
            }
            else
            {
                // > 10kb 且 < 1mb 的文件直接计算 sha1
                var proofCode = AliyunDriveHelper.GenerateProofCode(fileFullPath, fileSize, AccessToken);
                var contentHash = AliyunDriveHelper.GenerateSHA1(fileFullPath);

                body = new
                {
                    drive_id = _driveId,
                    parent_file_id = saveParentFileId,
                    name = name,
                    type = "file",

                    // refuse 同名不创建
                    // ignore 同名文件可创建
                    check_name_mode = "refuse",
                    size = fileInfo.Length,
                    content_hash = contentHash,
                    content_hash_name = "sha1",
                    proof_version = "v1",
                    proof_code = proofCode,

                    part_info_list = fileSize <= partBuffSize ? null : partInfoList
                };
            }
        }
        request.AddBody(body);
        var response = _driveApi.WithRetry<dynamic>(request);

        // 如果需要秒传，并且需要预处理时
        // System.Net.HttpStatusCode.Conflict 注意可能不是 409
        if (isRapidUpload && needPreHash
            && (response.StatusCode == HttpStatusCode.BadRequest || response.StatusCode == HttpStatusCode.Conflict)
            && response.Content.Contains("PreHashMatched"))
        {
            using (var mcDoc = JsonDocument.Parse(response.Content))
            {
                // 尝试获取code属性的值
                if (mcDoc.RootElement.TryGetProperty("code", out JsonElement codeElement))
                {
                    var code = codeElement.GetString();
                    if (code == "PreHashMatched")
                    {
                        // 匹配成功，进行完整的秒传，不需要预处理
                        await AliyunDriveUploadFile(localFileInfo, false);
                        return;
                    }
                }
            }
        }

        if (response.StatusCode != HttpStatusCode.OK)
        {
            _log.LogError(response.ErrorException, $"文件上传失败 {localFileInfo.Key} {response.Content}");

            throw response.ErrorException ?? new Exception($"文件上传失败 {localFileInfo.Key}");
        }

        using var doc = JsonDocument.Parse(response.Content!);
        var root = doc.RootElement;

        var drive_id = root.GetProperty("drive_id").GetString();
        var file_id = root.GetProperty("file_id").GetString();
        var upload_id = root.GetProperty("upload_id").GetString();

        var rapid_upload = root.GetProperty("rapid_upload").GetBoolean();
        if (rapid_upload)
        {
            _log.LogInformation($"文件秒传成功 {localFileInfo.Key}");
            return;
        }

        var partList = root.GetProperty("part_info_list").EnumerateArray().Select(p => new
        {
            part_number = p.GetProperty("part_number").GetInt32(),
            upload_url = p.GetProperty("upload_url").GetString()
        }).ToArray();

        if (fileSize > partBuffSize)
        {
            using var fs = new FileStream(fileFullPath, FileMode.Open, FileAccess.Read);
            var partNumber = 1;
            var buffer = new byte[partBuffSize];
            int bytesRead;

            while ((bytesRead = await fs.ReadAsync(buffer, 0, buffer.Length)) > 0)
            {
                var partUploadUrl = partList.First(p => p.part_number == partNumber).upload_url;
                var content = new ByteArrayContent(buffer, 0, bytesRead);
                await UploadPart(partUploadUrl, content);
                partNumber++;
            }
        }
        else
        {
            var partUploadUrl = partList.First().upload_url;
            byte[] fileData = await File.ReadAllBytesAsync(fileFullPath);
            var content = new ByteArrayContent(fileData);
            await UploadPart(partUploadUrl, content);
        }

        var completeResponse = _driveApi.UploadComplete(_driveId, file_id, upload_id, AccessToken);
        if (completeResponse.ParentFileId == "root")
        {
            _driveFiles.TryAdd($"{completeResponse.Name}".TrimPath(), completeResponse);
        }
        else
        {
            var parent = _driveFolders.First(c => c.Value.Type == "folder" && c.Value.FileId == completeResponse.ParentFileId);
            var path = $"{parent.Key}/{completeResponse.Name}".TrimPath();
            _driveFiles.TryAdd(path, completeResponse);
        }

        //var upload_url = root.GetProperty("part_info_list").EnumerateArray().FirstOrDefault().GetProperty("upload_url").GetString();

        //// 读取文件作为字节流
        //byte[] fileData = await File.ReadAllBytesAsync(fileFullPath);

        //// 创建HttpContent
        //var content = new ByteArrayContent(fileData);

        //// 发送PUT请求
        //HttpResponseMessage uploadRes = null;

        //// 定义重试策略 3 次
        //var retryPolicy = Policy
        //    .Handle<HttpRequestException>()
        //    .WaitAndRetryAsync(3, retryAttempt =>
        //    {
        //        // 5s 25s 125s 后重试
        //        return TimeSpan.FromSeconds(Math.Pow(5, retryAttempt));
        //    });

        //// 执行带有重试策略的请求
        //await retryPolicy.ExecuteAsync(async () =>
        //{
        //    uploadRes = await _uploadHttpClient.PutAsync(upload_url, content);

        //    if (!uploadRes.IsSuccessStatusCode)
        //    {
        //        throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
        //    }
        //});

        //// 检查请求是否成功
        //if (!uploadRes.IsSuccessStatusCode)
        //{
        //    throw new HttpRequestException($"Failed to upload file. Status code: {uploadRes.StatusCode}");
        //}

        //// 将文件添加到上传列表
        //var data = _driveApi.UploadComplete(_driveId, file_id, upload_id, AccessToken);
        //if (data.ParentFileId == "root")
        //{
        //    // 当前目录在根路径
        //    // /{当前路径}/
        //    _driveFiles.TryAdd($"{data.Name}".TrimPath(), data);
        //}
        //else
        //{
        //    // 计算父级路径
        //    var parent = _driveFolders.Where(c => c.Value.Type == "folder" && c.Value.FileId == data.ParentFileId).First()!;
        //    var path = $"{parent.Key}/{data.Name}".TrimPath();

        //    // /{父级路径}/{当前路径}/
        //    _driveFiles.TryAdd(path, data);
        //}

        _log.LogInformation($"文件上传成功 {localFileInfo.Key}");
    }
}
 */


        #endregion 阿里云盘

        #region 文件监听事件

        //private string AlterPathToMountPath(string path)
        //{
        //    var relativeMirrorPath = path.Substring(_sourcePath.Length).TrimStart('\\');

        //    return Path.Combine(_targetPath, relativeMirrorPath);
        //}

        //private void OnCommonFileSystemWatcherDirectoryDeleted(object sender, FileSystemEventArgs e)
        //{
        //    if (_dokanInstance.IsDisposed) return;
        //    var fullPath = AlterPathToMountPath(e.FullPath);

        //    Dokan.Notify.Delete(_dokanInstance, fullPath, true);
        //}

        /// <summary>
        /// 文件/文件夹删除事件
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// <param name="localBackupFullPath"></param>
        private void OnDeleted(object source, FileSystemEventArgs e, string localBackupFullPath)
        {
            if (_localPaths.TryGetValue(e.FullPath, out var isPath))
            {
                if (isPath)
                {
                    // 是删除路径
                    _log.LogInformation($"文件夹删除: {e.FullPath}, 类型: {e.ChangeType}");
                }
                else
                {
                    // 是删除文件
                    _log.LogInformation($"文件删除: {e.FullPath}, 类型: {e.ChangeType}");

                    var oldFileKey = GetFileKey(localBackupFullPath, e.FullPath);
                    _localFiles.TryRemove(oldFileKey, out _);
                    _localPaths.TryRemove(e.FullPath, out _);
                }
            }
            else
            {
                // 未知
                _log.LogInformation($"文件夹/文件删除: {e.FullPath}, 类型: {e.ChangeType}");
            }

            //Dokan.Notify.Delete(_dokanInstance, fullPath, false);
        }

        /// <summary>
        /// 文件/文件夹创建事件
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// <param name="localBackupFullPath"></param>
        private void OnCreated(object source, FileSystemEventArgs e, string localBackupFullPath)
        {
            if (File.Exists(e.FullPath))
            {
                _log.LogInformation($"文件创建: {e.FullPath}, 类型: {e.ChangeType}");

                var lf = GetLocalFile(e.FullPath, localBackupFullPath);
                if (lf != null)
                {
                    _localFiles.TryAdd(lf.Key, lf);
                }
            }
            else if (Directory.Exists(e.FullPath))
            {
                // 不处理
                _log.LogInformation($"文件夹创建: {e.FullPath}, 类型: {e.ChangeType}");
            }
            else
            {
                // 文件或文件夹，不处理
                _log.LogInformation($"文件/文件夹创建: {e.FullPath}, 类型: {e.ChangeType}");
            }

            //var fullPath = AlterPathToMountPath(e.FullPath);
            //var isDirectory = Directory.Exists(fullPath);
            //Dokan.Notify.Create(_dokanInstance, fullPath, isDirectory);
        }

        /// <summary>
        /// 文件/文件夹变更事件
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void OnChanged(object source, FileSystemEventArgs e)
        {
            _log.LogInformation($"文件/文件夹更改: {e.FullPath}, 类型: {e.ChangeType}");

            //Dokan.Notify.Update(_dokanInstance, fullPath);
        }

        /// <summary>
        /// 文件/文件夹重命名事件
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        /// <param name="localBackupFullPath"></param>
        private void OnRenamed(object source, RenamedEventArgs e, string localBackupFullPath)
        {
            if (File.Exists(e.FullPath))
            {
                _log.LogInformation($"文件重命名: {e.OldFullPath} 更改为 {e.FullPath}, 类型: {e.ChangeType}");

                var lf = GetLocalFile(e.FullPath, localBackupFullPath);
                if (lf != null)
                {
                    _localFiles.TryAdd(lf.Key, lf);
                }

                var oldFileKey = GetFileKey(localBackupFullPath, e.OldFullPath);
                _localFiles.TryRemove(oldFileKey, out _);
            }
            else if (Directory.Exists(e.FullPath))
            {
                // 文件夹重命名
                _log.LogInformation($"文件夹重命名: {e.OldFullPath} 更改为 {e.FullPath}, 类型: {e.ChangeType}");
            }
            else
            {
                // 不处理
                _log.LogInformation($"文件/文件夹夹重命名: {e.OldFullPath} 更改为 {e.FullPath}, 类型: {e.ChangeType}");
            }

            //var oldFullPath = AlterPathToMountPath(e.OldFullPath);
            //var oldDirectoryName = Path.GetDirectoryName(e.OldFullPath);

            //var fullPath = AlterPathToMountPath(e.FullPath);
            //var directoryName = Path.GetDirectoryName(e.FullPath);

            //var isDirectory = Directory.Exists(e.FullPath);
            //var isInSameDirectory = String.Equals(oldDirectoryName, directoryName);

            //Dokan.Notify.Rename(_dokanInstance, oldFullPath, fullPath, isDirectory, isInSameDirectory);
        }

        /// <summary>
        /// 文件监听出错了
        /// </summary>
        /// <param name="source"></param>
        /// <param name="e"></param>
        private void OnError(object source, ErrorEventArgs e)
        {
            _log.LogError(e.GetException(), $"文件系统监听发生错误");
        }

        #endregion 文件监听事件

        #region 磁盘挂载

        /// <summary>
        /// 挂载磁盘
        /// </summary>
        /// <param name="mountPoint"></param>
        public void DriveMount()
        {
            //// 先释放
            //_mountDrive?.Unmount();

            // 重新创建
            //_mountDrive = new AliyunDriveMounterByJob(mountPoint, this, _driveFolders, _driveFiles);
            //_mountDrive.Mount();

            _mountDrive = new AliyunDriveMounter(_currentStorageConfig, _jobConfig.MountConfig, _jobConfig.Name);
            _mountDrive.Mount();
        }

        /// <summary>
        /// 卸载磁盘
        /// </summary>
        /// <param name="mountPoint"></param>
        public void DriveUnmount()
        {
            _mountDrive?.Unmount();
            _mountDrive = null;
        }

        /// <summary>
        /// 是否已挂载
        /// </summary>
        /// <returns></returns>
        public bool DriveIsMount()
        {
            return _mountDrive != null;
        }

        #endregion 磁盘挂载
    }
}