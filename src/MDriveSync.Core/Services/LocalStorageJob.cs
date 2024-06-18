using MDriveSync.Core.DB;
using MDriveSync.Core.Models;
using MDriveSync.Core.Services;
using MDriveSync.Security;
using Microsoft.Extensions.Logging;
using Microsoft.VisualBasic.FileIO;
using ServiceStack;
using System.Collections.Concurrent;
using System.Data;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Text.RegularExpressions;
using SearchOption = System.IO.SearchOption;

namespace MDriveSync.Core
{
    /// <summary>
    /// 本地存储作业管理
    ///
    /// 注意处理全局函数，避免多线程冲突，例如：全局锁、全局队列、等
    ///
    /// </summary>
    public class LocalStorageJob : IDisposable
    {
        /// <summary>
        /// 本地文件锁
        /// </summary>
        private static readonly object _localLock = new();

        /// <summary>
        /// Log
        /// </summary>
        private readonly ILogger _log;

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
        /// 所有本地文件列表
        /// </summary>
        public ConcurrentDictionary<string, LocalStorageFileInfo> _localFiles = new();

        /// <summary>
        /// 本地所有文件路径，true: 路径, false: 文件
        /// 用于监听文件夹/文件处理
        /// </summary>
        public ConcurrentDictionary<string, bool> _localListenPaths = new();

        /// <summary>
        /// 所有本地文件夹
        /// </summary>
        public ConcurrentDictionary<string, LocalStorageFileInfo> _localFolders = new();

        /// <summary>
        /// 所有本地还原文件列表
        /// </summary>
        public ConcurrentDictionary<string, LocalStorageFileInfo> _localRestoreFiles = new();

        /// <summary>
        /// 所有本地还原文件夹
        /// </summary>
        public ConcurrentDictionary<string, LocalStorageFileInfo> _localRestoreFolders = new();

        /// <summary>
        /// 所有目标文件
        /// </summary>
        public ConcurrentDictionary<string, LocalStorageFileInfo> _targetFiles = new();

        /// <summary>
        /// 所有目标文件夹
        /// </summary>
        public ConcurrentDictionary<string, LocalStorageFileInfo> _targetFolders = new();

        /// <summary>
        /// 备份计划任务
        /// </summary>
        public ConcurrentDictionary<string, QuartzCronScheduler> _schedulers = new();

        /// <summary>
        /// 本地存储配置
        /// </summary>
        private LocalStorageConfig _currentStorageConfig;

        /// <summary>
        /// 本地存储配置
        /// </summary>
        public LocalStorageConfig CurrrentLocalStorage => _currentStorageConfig;

        /// <summary>
        /// 云盘所有文件夹
        /// </summary>
        public ConcurrentDictionary<string, LocalStorageFileInfo> TargetFolders => _targetFolders;

        /// <summary>
        /// 云盘所有文件
        /// </summary>
        public ConcurrentDictionary<string, LocalStorageFileInfo> TargetFiles => _targetFiles;

        /// <summary>
        /// 目标文件数据库（记录目标文件信息、加密信息等）
        /// </summary>
        private readonly SqliteRepository<LocalStorageTargetFileInfo, string> _targetDb;

        // 作业配置
        private LocalJobConfig _jobConfig;

        // 远程备份还原到本地目录
        private string _tartgetRestoreRootPath;

        // 远程备份保存的目录
        private string _targetSaveRootPath;

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
        public LocalJobConfig CurrrentJob => _jobConfig;

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

        public LocalStorageJob(LocalStorageConfig driveConfig, LocalJobConfig jobConfig, ILogger log)
        {
            _log = log;

            _targetDb = new SqliteRepository<LocalStorageTargetFileInfo, string>($"job_local_{jobConfig.Id}.db", "", false);
            _currentStorageConfig = driveConfig;
            _jobConfig = jobConfig;

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
            Initialize();

            ChangeState(JobState.Starting);

            _log.LogInformation("作业启动中");

            var sw = new Stopwatch();
            sw.Start();

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
                // 初始化目标根目录
                InitTargetRootPath();

                // 加载目标文件列表
                ScanTargetFiles();

                sw.Stop();
                _log.LogInformation($"加载目标文件完成，用时：{sw.ElapsedMilliseconds}ms");
                sw.Restart();

                _log.LogInformation("开始执行同步");

                SyncFiles(cancellationToken);

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

            SyncVerify();

            sw.Stop();
            _log.LogInformation($"同步作业校验完成，用时：{sw.ElapsedMilliseconds}ms");

            swAll.Stop();

            ProcessMessage = $"执行完成，总用时 {swAll.ElapsedMilliseconds / 1000} 秒";
        }

        /// <summary>
        /// 同步本地文件到目标
        /// </summary>
        /// <returns></returns>
        private void SyncFiles(CancellationToken token)
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

            // 文件夹同步
            Parallel.ForEach(_localFolders, options, (item) =>
            {
                try
                {
                    // 在关键点添加暂停点
                    _pauseEvent.Wait();
                    token.ThrowIfCancellationRequested();

                    // 如果目标目录不存在，则创建
                    if (!_targetFolders.ContainsKey(item.Key))
                    {
                        var saveParentPath = $"{_targetSaveRootPath}/{item.Key.TrimPath()}";
                        var dirInfo = new DirectoryInfo(saveParentPath);
                        if (!dirInfo.Exists)
                        {
                            // 创建文件夹
                            dirInfo.Create();
                        }

                        // 处理时间
                        dirInfo.CreationTime = item.Value.CreationTime;
                        dirInfo.LastWriteTime = item.Value.LastWriteTime;

                        // 处理隐藏
                        if (item.Value.IsHidden)
                        {
                            dirInfo.Attributes |= FileAttributes.Hidden;
                        }
                        else
                        {
                            dirInfo.Attributes &= ~FileAttributes.Hidden;
                        }

                        // 处理只读
                        if (item.Value.IsReadOnly)
                        {
                            dirInfo.Attributes |= FileAttributes.ReadOnly;
                        }
                        else
                        {
                            dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                        }

                        var ld = new LocalStorageFileInfo()
                        {
                            IsFile = false,
                            Length = 0,
                            Key = item.Key,
                            Hash = string.Empty,
                            FullName = dirInfo.FullName,
                            Name = dirInfo.Name,
                            IsExists = dirInfo.Exists,
                            IsHidden = dirInfo.Attributes.HasFlag(FileAttributes.Hidden),
                            IsReadOnly = dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly),
                            CreationTime = dirInfo.CreationTime,
                            LastWriteTime = dirInfo.LastWriteTime,
                        };

                        _targetFolders.TryAdd(ld.Key, ld);
                    }
                    else
                    {
                        // 处理时间、隐藏、只读，如果不一致
                        if (!LocalStorageFileInfo.FastAreObjectsEqual(item.Value, _targetFolders[item.Key]))
                        {
                            var saveParentPath = $"{_targetSaveRootPath}/{item.Key.TrimPath()}";
                            var dirInfo = new DirectoryInfo(saveParentPath);
                            if(!dirInfo.Exists)
                            {
                                dirInfo.Create();
                            }

                            // 处理时间
                            dirInfo.CreationTime = item.Value.CreationTime;
                            dirInfo.LastWriteTime = item.Value.LastWriteTime;

                            // 处理隐藏
                            if (item.Value.IsHidden)
                            {
                                dirInfo.Attributes |= FileAttributes.Hidden;
                            }
                            else
                            {
                                dirInfo.Attributes &= ~FileAttributes.Hidden;
                            }

                            // 处理只读
                            if (item.Value.IsReadOnly)
                            {
                                dirInfo.Attributes |= FileAttributes.ReadOnly;
                            }
                            else
                            {
                                dirInfo.Attributes &= ~FileAttributes.ReadOnly;
                            }

                            _targetFolders[item.Key].CreationTime = dirInfo.CreationTime;
                            _targetFolders[item.Key].LastWriteTime = dirInfo.LastWriteTime;
                            _targetFolders[item.Key].IsHidden = dirInfo.Attributes.HasFlag(FileAttributes.Hidden);
                            _targetFolders[item.Key].IsReadOnly = dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"文件上传处理异常 {item.Value.FullName}");
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
            Parallel.ForEach(_localFiles, options, (item, cancellationToken) =>
            {
                try
                {
                    // 在关键点添加暂停点
                    _pauseEvent.Wait();
                    token.ThrowIfCancellationRequested();

                    if (_jobConfig.IsEncrypt && _jobConfig.IsPack)
                    {
                        // 如果是文件打包加密模式
                        // TODO
                    }
                    else
                    {
                        SyncFile(item.Value);
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"文件上传处理异常 {item.Value.FullName}");
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
                if (Directory.Exists(_tartgetRestoreRootPath))
                {
                    Directory.CreateDirectory(_tartgetRestoreRootPath);
                }

                var sw = new Stopwatch();
                sw.Restart();

                InitTargetRootPath();

                // 所有文件列表
                ScanTargetFiles();

                await RestoreFiles(); // 替换为你的起始路径

                sw.Stop();
                _log.LogInformation($"end. {_targetFiles.Count}, {sw.ElapsedMilliseconds}ms");
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
            //var now = DateTime.Now;

            //var processorCount = GetDownloadThreadCount();
            //var options = new ParallelOptions() { MaxDegreeOfParallelism = processorCount };
            //var dirs = Directory.EnumerateDirectories(_tartgetRestoreRootPath, "*", SearchOption.AllDirectories);

            //// 加载文件
            //Load(_tartgetRestoreRootPath);
            //Parallel.ForEach(dirs, options, Load);

            //void Load(string dir)
            //{
            //    try
            //    {
            //        var dirInfo = new DirectoryInfo(dir);
            //        if (!dirInfo.Exists)
            //        {
            //            return;
            //        }

            //        // 所有本地文件夹
            //        var ld = new LocalFileInfo()
            //        {
            //            FullPath = dirInfo.FullName,
            //            Key = GetDirectoryKey(_tartgetRestoreRootPath, dirInfo),
            //            CreationTime = dirInfo.CreationTime,
            //            LastWriteTime = dirInfo.LastWriteTime,
            //            LocalFileName = dirInfo.Name
            //        };
            //        _localRestoreFolders.AddOrUpdate(ld.Key, ld, (k, v) => ld);

            //        var files = Directory.EnumerateFiles(dir);
            //        foreach (var file in files)
            //        {
            //            var fileInfo = new FileInfo(file);
            //            if (!fileInfo.Exists)
            //            {
            //                continue;
            //            }

            //            // 所有本地文件
            //            var lf = new LocalFileInfo()
            //            {
            //                IsEncrypt = _jobConfig.IsEncrypt,
            //                IsEncryptName = _jobConfig.IsEncryptName,
            //                IsFile = true,
            //                FullPath = fileInfo.FullName,
            //                Key = GetFileKey(_tartgetRestoreRootPath, fileInfo.FullName),
            //                KeyPath = GetFileKeyPath(_tartgetRestoreRootPath, fileInfo),
            //                CreationTime = fileInfo.CreationTime,
            //                LastWriteTime = fileInfo.LastWriteTime,
            //                Length = fileInfo.Length,
            //                LocalFileName = fileInfo.Name,
            //                Hash = ShaHashHelper.ComputeFileHash(file, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, file.Length)
            //            };

            //            _localRestoreFiles.AddOrUpdate(lf.Key, lf, (k, v) => lf);
            //        }
            //    }
            //    catch (UnauthorizedAccessException ex)
            //    {
            //        _log.LogInformation("Access Denied: " + ex.Message);
            //    }
            //    catch (Exception ex)
            //    {
            //        _log.LogInformation(ex.Message);
            //    }
            //}

            //_log.LogInformation($"开始还原 {_localRestoreFiles.Count}, time: {(DateTime.Now - now).TotalMilliseconds}ms");

            //var process = 0;
            //var total = _targetFiles.Count;

            //// 先处理文件夹
            //foreach (var item in _targetFolders)
            //{
            //    var subPaths = item.Key.TrimPrefix(_targetSaveRootPath)
            //        .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            //    var savePath = Path.Combine(_tartgetRestoreRootPath, Path.Combine(subPaths));

            //    var tmpPath = Path.GetDirectoryName(savePath);
            //    lock (_localLock)
            //    {
            //        if (!Directory.Exists(tmpPath))
            //        {
            //            Directory.CreateDirectory(tmpPath);
            //        }
            //    }
            //}

            //// 开启并行下载
            //await Parallel.ForEachAsync(_targetFiles, options, async (item, cancellationToken) =>
            //{
            //    try
            //    {
            //        // 阿里云盘文件到本地
            //        var savePath = _tartgetRestoreRootPath;

            //        // 根目录
            //        if (item.Value.ParentFileId == "root")
            //        {
            //            throw new Exception("不支持根目录文件下载");
            //        }
            //        else
            //        {
            //            // 子目录
            //            var parent = _targetFolders.First(x => x.Value.IsFolder && x.Value.FileId == item.Value.ParentFileId)!;

            //            // 移除云盘前缀
            //            var subPaths = parent.Key.TrimPrefix(_targetSaveRootPath)
            //                .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            //            savePath = Path.Combine(_tartgetRestoreRootPath, Path.Combine(subPaths));
            //        }

            //        var finalFilePath = Path.Combine(savePath, item.Value.Name);
            //        if (File.Exists(finalFilePath))
            //        {
            //            // 验证本地是否已存在文件，并比较 sha1 值
            //            var hash = ShaHashHelper.ComputeFileHash(finalFilePath, "sha1");
            //            if (hash.Equals(item.Value.ContentHash, StringComparison.OrdinalIgnoreCase))
            //            {
            //                _log.LogInformation($"文件已存在，跳过 {finalFilePath}");
            //                return;
            //            }
            //        }

            //        // 获取详情 url
            //        var data = AliyunDriveGetDetail<AliyunDriveFileItem>(item.Value.FileId);
            //        await AliyunDriveDownload(data.Url,
            //                       item.Value.Name,
            //                       item.Value.ContentHash,
            //                       savePath,
            //                       _tartgetRestoreRootPath);

            //        // TODO > 100MB
            //        // 如果是大文件，则通过下载链接下载文件
            //    }
            //    catch (Exception)
            //    {
            //        throw;
            //    }
            //    finally
            //    {
            //        process++;
            //        _log.LogInformation($"下载中 {process}/{total}, time: {(DateTime.Now - now).TotalSeconds}s, {item.Key},{item.Value.Name}");
            //    }
            //});

            //// 清理下载缓存
            //ClearDownloadCache(_tartgetRestoreRootPath);
        }

        /// <summary>
        /// 更新作业配置（只有空闲、错误、取消、禁用、完成状态才可以更新）
        /// </summary>
        /// <param name="cfg"></param>
        public void JobUpdate(LocalJobConfig cfg)
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

            var drive = LocalStorageDb.Instance.DB.GetAll().Where(c => c.Id == _currentStorageConfig.Id).FirstOrDefault();
            if (drive == null)
            {
                throw new LogicException("配置配置错误，请重启程序");
            }

            // 禁止作业指向同一目标
            if (!string.IsNullOrWhiteSpace(cfg.Target) && drive.Jobs.Any(x => x.Target == cfg.Target && x.Id != cfg.Id))
            {
                throw new LogicException("多个作业禁止指向同一个目标目录");
            }

            // 目标目录不能为源目录或源目录的子目录
            if (cfg.Sources.Count > 0 && !string.IsNullOrWhiteSpace(cfg.Target))
            {
                var tarDir = new DirectoryInfo(cfg.Target);
                foreach (var source in cfg.Sources)
                {
                    var sourceDir = new DirectoryInfo(source);

                    if (sourceDir.FullName.Equals(tarDir.FullName, StringComparison.OrdinalIgnoreCase) || tarDir.FullName.StartsWith(sourceDir.FullName, StringComparison.OrdinalIgnoreCase))
                    {
                        throw new LogicException("目标目录不能为源目录或源目录的子目录");
                    }
                }
            }

            // 清除表达式所有作业
            _schedulers.Clear();

            _jobConfig.Filters = cfg.Filters;
            _jobConfig.Name = cfg.Name;
            _jobConfig.Description = cfg.Description;
            _jobConfig.CheckLevel = cfg.CheckLevel;
            _jobConfig.CheckAlgorithm = cfg.CheckAlgorithm;
            _jobConfig.Sources = cfg.Sources;
            _jobConfig.DownloadThread = cfg.DownloadThread;
            _jobConfig.UploadThread = cfg.UploadThread;
            _jobConfig.Target = cfg.Target;
            _jobConfig.Schedules = cfg.Schedules;
            _jobConfig.FileWatcher = cfg.FileWatcher;
            _jobConfig.IsRecycleBin = cfg.IsRecycleBin;
            _jobConfig.IsTemporary = cfg.IsTemporary;
            _jobConfig.Order = cfg.Order;
            _jobConfig.Mode = cfg.Mode;
            _jobConfig.Restore = cfg.Restore;

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

                Initialize();
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

            var processorCount = GetUploadThreadCount();
            var options = new ParallelOptions() { MaxDegreeOfParallelism = processorCount };

            // 当前本地文件 key
            var localFileKeys = new ConcurrentDictionary<string, bool>(_localFiles.Keys.ToDictionary(c => c, v => true));

            // 当前本地文件夹 key
            var localFolderKeys = new ConcurrentDictionary<string, bool>(_localFolders.Keys.ToDictionary(c => c, v => true));

            // 循环多个目录处理
            foreach (var sourceRootPath in _jobConfig.Sources)
            {
                var backupRootInfo = new DirectoryInfo(sourceRootPath);
                var backupDirs = Directory.EnumerateDirectories(sourceRootPath, "*", SearchOption.AllDirectories);

                // 加载文件
                LoadFiles(sourceRootPath);
                Parallel.ForEach(backupDirs, options, LoadFiles);

                void LoadFiles(string dir)
                {
                    try
                    {
                        var ld = GetLocalDirectory(dir, sourceRootPath);
                        if (ld == null)
                        {
                            return;
                        }

                        _localFolders.AddOrUpdate(ld.Key, ld, (k, v) => ld);
                        localFolderKeys.TryRemove(ld.Key, out _);

                        var files = Directory.EnumerateFiles(dir);
                        foreach (var fileFullPath in files)
                        {
                            var lf = GetLocalFile(fileFullPath, sourceRootPath);
                            if (lf == null)
                            {
                                continue;
                            }

                            _localFiles.AddOrUpdate(lf.Key, lf, (k, v) => lf);
                            localFileKeys.TryRemove(lf.Key, out _);
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
                    finally
                    {
                        ProcessMessage = $"正在扫描本地文件 {_localFiles.Count}";
                    }
                }
            }

            // 如果本地文件不存在了
            foreach (var item in localFileKeys)
            {
                _localFiles.TryRemove(item.Key, out _);
            }

            // 如果本地文件夹不存在了
            foreach (var item in localFolderKeys)
            {
                _localFolders.TryRemove(item.Key, out _);
            }

            _log.LogInformation($"扫描本地文件，总文件数：{_localFiles.Count}, 扫描文件用时: {(DateTime.Now - now).TotalMilliseconds}ms");
        }

        /// <summary>
        /// 扫描存储目标文件
        /// </summary>
        private void ScanTargetFiles()
        {
            ProcessMessage = "正在扫描存储目标文件";

            var now = DateTime.Now;

            var processorCount = GetUploadThreadCount();
            var options = new ParallelOptions() { MaxDegreeOfParallelism = processorCount };

            // 根目录
            var rootDir = _targetSaveRootPath;
            var rootInfo = new DirectoryInfo(rootDir);

            // 子目录
            var childrenDirs = Directory.EnumerateDirectories(rootDir, "*", SearchOption.AllDirectories);

            // 加载根目录文件
            LoadFiles(rootDir);

            // 加载子目录文件
            Parallel.ForEach(childrenDirs, options, LoadFiles);

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
                    var ld = new LocalStorageFileInfo()
                    {
                        IsFile = false,
                        Hash = string.Empty,
                        Key = dirInfo.FullName.TrimPath().TrimPrefix(rootInfo.FullName).TrimPath(),
                        Length = 0,
                        FullName = dirInfo.FullName,
                        Name = dirInfo.Name,
                        IsReadOnly = dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly),
                        IsHidden = dirInfo.Attributes.HasFlag(FileAttributes.Hidden),
                        IsExists = dirInfo.Exists,
                        CreationTime = dirInfo.CreationTime,
                        LastWriteTime = dirInfo.LastWriteTime,
                    };
                    _targetFolders.AddOrUpdate(ld.Key, ld, (k, v) => ld);

                    var files = Directory.EnumerateFiles(dir);
                    foreach (var fileFullPath in files)
                    {
                        var fileInfo = new FileInfo(fileFullPath);
                        if (!fileInfo.Exists)
                        {
                            continue;
                        }

                        // 所有本地文件
                        var lf = new LocalStorageFileInfo()
                        {
                            IsFile = true,
                            Hash = string.Empty,
                            Key = fileInfo.FullName.TrimPath().TrimPrefix(rootInfo.FullName).TrimPath(),
                            Length = fileInfo.Length,
                            FullName = fileInfo.FullName,
                            CreationTime = fileInfo.CreationTime,
                            LastWriteTime = fileInfo.LastWriteTime,
                            IsExists = fileInfo.Exists,
                            Name = fileInfo.Name,
                            IsHidden = fileInfo.Attributes.HasFlag(FileAttributes.Hidden),
                            IsReadOnly = fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly)
                        };

                        _targetFiles.AddOrUpdate(lf.Key, lf, (k, v) => lf);

                        ProcessMessage = $"正在扫描目标文件 {_targetFiles.Count}";
                    }
                }
                catch (UnauthorizedAccessException ex)
                {
                    _log.LogWarning(ex, $"加载目标目录文件没有权限 {dir}");
                }
                catch (Exception ex)
                {
                    _log.LogError(ex, $"加载目标目录文件异常 {dir}");
                    throw;
                }
            }

            _log.LogInformation($"扫描目标文件，总文件数：{_targetFiles.Count}, 扫描文件用时: {(DateTime.Now - now).TotalMilliseconds}ms");
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
        /// 将本地路径转为 {备份根目录}/{子目录}
        /// </summary>
        /// <param name="rootDirFullPath"></param>
        /// <param name="directoryInfo"></param>
        /// <returns></returns>
        private string GetDirectoryKey(string rootDirFullPath, DirectoryInfo directoryInfo)
        {
            var baseDirInfo = new DirectoryInfo(rootDirFullPath);
            var baseDirName = baseDirInfo.Name;

            var subPath = directoryInfo.FullName.TrimPrefix(rootDirFullPath);
            return $"{baseDirName}/{subPath.TrimPath()}";
        }

        /// <summary>
        /// 获取文件路径 key
        /// </summary>
        /// <param name="rootPath"></param>
        /// <param name="fileInfo"></param>
        /// <returns></returns>
        private string GetFileKey(string rootPath, string fileFullPath)
        {
            var rootInfo = new DirectoryInfo(rootPath);
            var rootPathName = rootInfo.Name;

            var subPath = fileFullPath.TrimPrefix(rootInfo.FullName);
            return $"{rootPathName}/{subPath.TrimPath()}";
        }

        /// <summary>
        /// 获取文件路径 key
        /// </summary>
        /// <param name="rootPath"></param>
        /// <param name="fileInfo"></param>
        /// <returns></returns>
        private string GetFileKeyPath(string rootPath, FileInfo fileInfo)
        {
            var rootInfo = new DirectoryInfo(rootPath);
            var rootName = rootInfo.Name;

            var subPath = Path.GetDirectoryName(fileInfo.FullName).TrimPrefix(rootInfo.FullName);
            return $"{rootName}/{subPath.TrimPath()}";
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
        /// <param name="rootDirFullPath"></param>
        /// <returns></returns>
        private LocalStorageFileInfo GetLocalFile(string fileFullPath, string rootDirFullPath)
        {
            var rootDirInfo = new DirectoryInfo(rootDirFullPath);
            if (!rootDirInfo.Exists)
            {
                return null;
            }

            var fileInfo = new FileInfo(fileFullPath);
            if (!fileInfo.Exists)
            {
                return null;
            }

            // 所有本地文件
            var lf = new LocalStorageFileInfo()
            {
                IsFile = true,
                FullName = fileInfo.FullName,
                Key = GetFileKey(rootDirFullPath, fileInfo.FullName),
                CreationTime = fileInfo.CreationTime,
                LastWriteTime = fileInfo.LastWriteTime,
                Length = fileInfo.Length,
                IsExists = fileInfo.Exists,
                Name = fileInfo.Name,
                IsReadOnly = fileInfo.Attributes.HasFlag(FileAttributes.ReadOnly),
                IsHidden = fileInfo.Attributes.HasFlag(FileAttributes.Hidden),
                Hash = string.Empty,
            };

            // 过滤文件
            var cfile = lf.Key.TrimPrefix(rootDirInfo.Name).TrimPath();
            if (!string.IsNullOrWhiteSpace(cfile))
            {
                if (ShouldFilter($"/{cfile}"))
                {
                    return null;
                }
            }

            // 文件添加到本地路径
            _localListenPaths.TryAdd(lf.FullName, false);

            return lf;
        }

        /// <summary>
        /// 尝试获取本地文件夹，如果本地文件夹不存在或不符合则返回 NULL
        /// </summary>
        /// <param name="dirFullPath"></param>
        /// <param name="rootDirFullPath"></param>
        /// <returns></returns>
        private LocalStorageFileInfo GetLocalDirectory(string dirFullPath, string rootDirFullPath)
        {
            var dirInfo = new DirectoryInfo(dirFullPath);
            if (!dirInfo.Exists)
            {
                return null;
            }

            var rootDirInfo = new DirectoryInfo(rootDirFullPath);
            if (!rootDirInfo.Exists)
            {
                return null;
            }

            // 所有本地文件夹
            var ld = new LocalStorageFileInfo()
            {
                FullName = dirInfo.FullName,
                Name = dirInfo.Name,
                IsExists = dirInfo.Exists,
                IsReadOnly = dirInfo.Attributes.HasFlag(FileAttributes.ReadOnly),
                IsHidden = dirInfo.Attributes.HasFlag(FileAttributes.Hidden),
                IsFile = false,
                Length = 0,
                Hash = string.Empty,
                Key = GetDirectoryKey(rootDirFullPath, dirInfo),
                CreationTime = dirInfo.CreationTime,
                LastWriteTime = dirInfo.LastWriteTime,
            };

            // 过滤文件夹
            var cpath = ld.Key.TrimPrefix(rootDirInfo.Name).TrimPath();
            if (!string.IsNullOrWhiteSpace(cpath))
            {
                if (ShouldFilter($"/{cpath}/"))
                {
                    return null;
                }
            }

            // 文件夹
            _localListenPaths.TryAdd(ld.FullName, true);

            return ld;
        }

        /// <summary>
        /// 判断是否是 Windows 系统
        /// </summary>
        /// <returns></returns>
        private static bool IsWindows()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Windows);
        }

        /// <summary>
        /// 判断是否是 Linux 系统
        /// </summary>
        /// <returns></returns>
        private static bool IsLinux()
        {
            return RuntimeInformation.IsOSPlatform(OSPlatform.Linux);
        }

        #endregion 私有方法

        #region 本地存储

        /// <summary>
        /// 初始化作业（路径、云盘信息等）
        /// </summary>
        /// <returns></returns>
        private void Initialize()
        {
            var isLock = LocalLock.TryLock("init_local_job_lock", TimeSpan.FromSeconds(60), () =>
            {
                var oldState = CurrentState;

                try
                {
                    LogInfo("作业初始化中");

                    ChangeState(JobState.Initializing);

                    var sw = new Stopwatch();
                    sw.Start();

                    var isLinux = IsLinux();

                    _log.LogInformation($"Linux: {isLinux}");

                    // 处理 RestoreRootPath
                    if (IsLinux() && (_jobConfig.Restore?.StartsWith("/") ?? false))
                    {
                        _tartgetRestoreRootPath = "/" + _jobConfig.Restore.TrimPath();
                    }
                    else
                    {
                        _tartgetRestoreRootPath = _jobConfig.Restore.TrimPath();
                    }

                    // 处理 TargetRootPath
                    if (IsLinux() && (_jobConfig.Target?.StartsWith("/") ?? false))
                    {
                        _targetSaveRootPath = "/" + _jobConfig.Target.TrimPath();
                    }
                    else
                    {
                        _targetSaveRootPath = _jobConfig.Target.TrimPath();
                    }

                    // 格式化备份目录
                    var sources = _jobConfig.Sources.Where(c => !string.IsNullOrWhiteSpace(c)).Select(c => c.TrimPath()).Distinct().ToList();
                    _jobConfig.Sources.Clear();
                    foreach (var item in sources)
                    {
                        if (isLinux && item.StartsWith('/'))
                        {
                            var dir = new DirectoryInfo(item);
                            if (!dir.Exists)
                            {
                                dir.Create();
                            }
                            _jobConfig.Sources.Add($"{dir.FullName.TrimPath()}");
                        }
                        else
                        {
                            var dir = new DirectoryInfo(item);
                            if (!dir.Exists)
                            {
                                dir.Create();
                            }
                            _jobConfig.Sources.Add(dir.FullName);
                        }
                    }

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
        /// 同步完成 - 文件校验
        /// </summary>
        /// <returns></returns>
        private void SyncVerify()
        {
            if (CurrentState != JobState.Verifying)
            {
                return;
            }

            // 根据同步方式，单向、双向、镜像，对文件进行删除、移动、重命名、下载等处理
            switch (_jobConfig.Mode)
            {
                // 镜像同步
                case JobMode.Mirror:
                    {
                        if (_jobConfig.IsEncrypt && _jobConfig.IsPack)
                        {
                            // 文件打包模式
                        }
                        else
                        {
                            // 计算需要删除的远程文件夹
                            // 注意需要排除根目录
                            // 优先删除短路径（父路径）
                            var localFolderKeys = _localFolders.Keys.ToList();
                            var removeFolderKeys = _targetFolders.Keys.Except(localFolderKeys).Where(c => !string.IsNullOrWhiteSpace(c)).OrderBy(c => c.Length).ToList();
                            if (removeFolderKeys.Count > 0)
                            {
                                foreach (var k in removeFolderKeys)
                                {
                                    if (_targetFolders.TryRemove(k, out var v))
                                    {
                                        if (Directory.Exists(v.FullName))
                                        {
                                            try
                                            {
                                                // 如果是 windows 平台并且启动回收站
                                                if (IsWindows() && _jobConfig.IsRecycleBin)
                                                {
                                                    // 删除文件夹到系统回收站
                                                    // 将文件移动到回收站
                                                    FileSystem.DeleteDirectory(v.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                                                }
                                                else
                                                {
                                                    // 彻底删除文件夹
                                                    Directory.Delete(v.FullName, true);
                                                }
                                            }
                                            catch
                                            {
                                                try
                                                {
                                                    Directory.Delete(v.FullName, true);
                                                }
                                                catch (Exception ex)
                                                {
                                                    _log.LogError(ex, "删除文件夹 {@0} 失败", v.FullName);
                                                }
                                            }
                                        }
                                    }
                                }
                            }

                            if (_jobConfig.IsEncrypt && _jobConfig.IsEncryptName)
                            {
                                // 文件加密模式，加密文件名

                                // 计算需要删除的目标文件
                                var localFileKeys = _localFiles.Keys.ToList();

                                var fs = _targetDb.GetAll(false).Where(c => c.IsFile).ToList();
                                var removeFileKeys = fs.Select(c => c.LocalFileKey).Except(localFileKeys).ToList();
                                if (removeFileKeys.Count > 0)
                                {
                                    foreach (var k in removeFileKeys)
                                    {
                                        var tf = fs.FirstOrDefault(x => x.LocalFileKey == k);
                                        if (tf != null)
                                        {
                                            if (_targetFiles.TryRemove(tf.Key, out var v))
                                            {
                                                if (File.Exists(v.FullName))
                                                {
                                                    try
                                                    {
                                                        if (IsWindows() && _jobConfig.IsRecycleBin)
                                                        {
                                                            // 删除文件到系统回收站
                                                            // 将文件移动到回收站
                                                            FileSystem.DeleteFile(v.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                                                        }
                                                        else
                                                        {
                                                            // 彻底删除文件
                                                            File.Delete(v.FullName);
                                                        }
                                                    }
                                                    catch
                                                    {
                                                        try
                                                        {
                                                            File.Delete(v.FullName);
                                                        }
                                                        catch (Exception ex)
                                                        {
                                                            _log.LogError(ex, "删除文件 {@0} 失败", v.FullName);
                                                        }
                                                    }
                                                }
                                            }

                                            // 删除加密文件
                                            _targetDb.Delete(tf.Key);
                                        }
                                    }
                                }
                            }
                            else if (_jobConfig.IsEncrypt)
                            {
                                // 文件加密模式，不加密文件名

                                // 计算需要删除的目标文件
                                var localFileKeys = _localFiles.Keys.Select(c => c + ".e").ToList();
                                var removeFileKeys = _targetFiles.Keys.Except(localFileKeys).ToList();
                                if (removeFileKeys.Count > 0)
                                {
                                    foreach (var k in removeFileKeys)
                                    {
                                        if (_targetFiles.TryRemove(k, out var v))
                                        {
                                            if (File.Exists(v.FullName))
                                            {
                                                try
                                                {
                                                    if (IsWindows() && _jobConfig.IsRecycleBin)
                                                    {
                                                        // 删除文件到系统回收站
                                                        // 将文件移动到回收站
                                                        FileSystem.DeleteFile(v.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                                                    }
                                                    else
                                                    {
                                                        // 彻底删除文件
                                                        File.Delete(v.FullName);
                                                    }
                                                }
                                                catch
                                                {
                                                    try
                                                    {
                                                        File.Delete(v.FullName);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _log.LogError(ex, "删除文件 {@0} 失败", v.FullName);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
                            else
                            {
                                // 未加密

                                // 计算需要删除的目标文件
                                var localFileKeys = _localFiles.Keys.ToList();
                                var removeFileKeys = _targetFiles.Keys.Except(localFileKeys).ToList();
                                if (removeFileKeys.Count > 0)
                                {
                                    foreach (var k in removeFileKeys)
                                    {
                                        if (_targetFiles.TryRemove(k, out var v))
                                        {
                                            if (File.Exists(v.FullName))
                                            {
                                                try
                                                {
                                                    if (IsWindows() && _jobConfig.IsRecycleBin)
                                                    {
                                                        // 删除文件到系统回收站
                                                        // 将文件移动到回收站
                                                        FileSystem.DeleteFile(v.FullName, UIOption.OnlyErrorDialogs, RecycleOption.SendToRecycleBin);
                                                    }
                                                    else
                                                    {
                                                        // 彻底删除文件
                                                        File.Delete(v.FullName);
                                                    }
                                                }
                                                catch
                                                {
                                                    try
                                                    {
                                                        File.Delete(v.FullName);
                                                    }
                                                    catch (Exception ex)
                                                    {
                                                        _log.LogError(ex, "删除文件 {@0} 失败", v.FullName);
                                                    }
                                                }
                                            }
                                        }
                                    }
                                }
                            }
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
                        //// 计算需要同步的远程文件
                        //var localFileKeys = _localFiles.Keys.Select(c => $"{_targetSaveRootPath}/{c}".TrimPath()).ToList();
                        //var addFileKeys = _targetFiles.Keys.Except(localFileKeys).ToList();
                        //if (addFileKeys.Count > 0)
                        //{
                        //    // 验证本地文件是否存在
                        //    // 验证本地文件是否和远程文件一致
                        //    // 多线程下载处理

                        //    var processorCount = GetDownloadThreadCount();
                        //    var options = new ParallelOptions() { MaxDegreeOfParallelism = processorCount };

                        //    // 开启并行下载
                        //    await Parallel.ForEachAsync(addFileKeys, options, async (item, cancellationToken) =>
                        //    {
                        //        if (!_targetFiles.TryGetValue(item, out var dinfo) || dinfo == null)
                        //        {
                        //            return;
                        //        }

                        //        // 根目录
                        //        if (dinfo.ParentFileId == "root")
                        //        {
                        //            throw new Exception("不支持根目录文件下载");
                        //        }

                        //        // 子目录
                        //        var parent = _targetFolders.First(x => x.Value.IsFolder && x.Value.FileId == dinfo.ParentFileId)!;

                        //        // 移除云盘前缀
                        //        var subPaths = parent.Key.TrimPrefix(_targetSaveRootPath)
                        //            .Split(new char[] { '/' }, StringSplitOptions.RemoveEmptyEntries);

                        //        // 判断是哪个备份的根目录
                        //        var saveRootPath = _jobConfig.Sources
                        //        .Where(c => $"{string.Join('/', subPaths).TrimPath()}/".StartsWith(new DirectoryInfo(c).Name + "/"))
                        //        .FirstOrDefault();

                        //        if (saveRootPath == null)
                        //        {
                        //            throw new Exception("未找到匹配的根目录");
                        //        }

                        //        // 找到备份的根目录
                        //        // 同时移除同步目录的根目录名称
                        //        var saveRootInfo = new DirectoryInfo(saveRootPath);
                        //        var savePath = Path.Combine(saveRootPath, Path.Combine(subPaths).TrimPath().TrimPrefix(saveRootInfo.Name));

                        //        try
                        //        {
                        //            var finalFilePath = Path.Combine(savePath, dinfo.Name);
                        //            if (File.Exists(finalFilePath))
                        //            {
                        //                // 验证本地是否已存在文件，并比较 sha1 值
                        //                var hash = ShaHashHelper.ComputeFileHash(finalFilePath, "sha1");
                        //                if (hash.Equals(dinfo.ContentHash, StringComparison.OrdinalIgnoreCase))
                        //                {
                        //                    _log.LogInformation($"文件已存在，跳过 {finalFilePath}");
                        //                    return;
                        //                }
                        //                else
                        //                {
                        //                    // 文件重命名
                        //                    // 下载远程文件到本地，如果有冲突，则将本地和远程文件同时重命名
                        //                    // 如果不一致，则重命名远程文件并下载
                        //                    var fi = 0;
                        //                    do
                        //                    {
                        //                        var fname = Path.GetFileNameWithoutExtension(dinfo.Name);
                        //                        var fext = Path.GetExtension(dinfo.Name);
                        //                        var suffix = $"";
                        //                        if (fi > 0)
                        //                        {
                        //                            suffix = $" ({fi})";
                        //                        }
                        //                        var newName = $"{fname} - 副本{suffix}{fext}";
                        //                        finalFilePath = Path.Combine(savePath, newName);

                        //                        // 如果本地和远程都不存在，说明可以重命名
                        //                        var anyFile = AliyunDriveFileExist(dinfo.ParentFileId, newName);
                        //                        if (!File.Exists(finalFilePath) && !anyFile)
                        //                        {
                        //                            // 远程文件重命名
                        //                            var upData = _driveApi.FileUpdate(_driveId, dinfo.FileId, newName, AccessToken);
                        //                            if (upData == null)
                        //                            {
                        //                                throw new Exception("文件重命名失败");
                        //                            }

                        //                            // 添加新的文件
                        //                            _targetFiles.TryAdd($"{parent.Key}/{newName}".TrimPath(), upData);

                        //                            // 删除旧的文件
                        //                            _targetFiles.TryRemove(item, out _);
                        //                            break;
                        //                        }
                        //                        fi++;
                        //                    } while (true);
                        //                }
                        //            }

                        //            // 获取详情 url
                        //            var detail = _driveApi.GetDetail<AliyunDriveFileItem>(_driveId, dinfo.FileId, AccessToken);
                        //            await AliyunDriveDownload(detail.Url, dinfo.Name, dinfo.ContentHash, savePath, saveRootPath);

                        //            // TODO
                        //            // 如果是大文件，则通过下载链接下载文件
                        //        }
                        //        catch (Exception ex)
                        //        {
                        //            _log.LogError(ex, "双向同步执行异常");
                        //        }
                        //    });

                        //    foreach (var path in _jobConfig.Sources)
                        //    {
                        //        // 清理下载缓存
                        //        ClearDownloadCache(path);
                        //    }
                        //}
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
                FileCount = _targetFiles.Count,
                FolderCount = _targetFolders.Count,
                TotalSize = _targetFiles.Values.Sum(c => c.Length)
            };

            _currentStorageConfig.SaveJob(_jobConfig);

            // 校验通过 -> 空闲
            ChangeState(JobState.Idle);
        }

        /// <summary>
        /// 本地存储 - 下载文件
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
        /// 同步文件处理
        /// </summary>
        /// <param name="localFileInfo"></param>
        /// <param name="needPreHash"></param>
        /// <returns></returns>
        /// <exception cref="Exception"></exception>
        /// <exception cref="HttpRequestException"></exception>
        private void SyncFile(LocalStorageFileInfo localFileInfo)
        {
            var fileInfo = new FileInfo(localFileInfo.FullName);
            if (!fileInfo.Exists)
            {
                // 本地文件不存在
                _localFiles.TryRemove(localFileInfo.Key, out _);
                return;
            }

            // 加密处理
            if (_jobConfig.IsEncrypt)
            {
                if (_jobConfig.IsPack)
                {
                    // 文件打包模式
                    return;
                }
                else
                {
                    var rootInfo = new DirectoryInfo(_targetSaveRootPath);

                    // 计算 hash
                    if (string.IsNullOrWhiteSpace(localFileInfo.Hash))
                    {
                        localFileInfo.Hash = ShaHashHelper.ComputeFileHash(localFileInfo.FullName, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, localFileInfo.Length);
                    }

                    // 比较文件 hash
                    var targetFile = _targetDb.Single(c => c.LocalFileKey == localFileInfo.Key);
                    if (targetFile != null && targetFile.LocalFileHash == localFileInfo.Hash)
                    {
                        //// 比较其他元信息
                        //// 加密之后 key, length, name, hash 不一致，比较时忽略
                        //if (LocalStorageFileInfo.FastAreObjectsEqual(targetFile, localFileInfo,
                        //    nameof(LocalStorageFileInfo.Key), nameof(LocalStorageFileInfo.Length), nameof(LocalStorageFileInfo.Name), nameof(LocalStorageFileInfo.Hash)))
                        //{
                        //    return;
                        //}

                        // 对于加密文件，简单一点只比较 hash 即可，其他信息不比较
                        return;
                    }

                    // 文件名
                    var name = fileInfo.Name;

                    // 文件加密模式，文件结构不变
                    if (_jobConfig.IsEncryptName)
                    {
                        name = HashHelper.ComputeHash(Encoding.UTF8.GetBytes(name), "MD5").ToHex() + ".e";
                    }
                    else
                    {
                        name += ".e";
                    }

                    // 计算保存存储目录
                    var saveParentPath = Path.GetDirectoryName($"{_targetSaveRootPath}/{localFileInfo.Key}");
                    Directory.CreateDirectory(saveParentPath);

                    // 根据加密算法生成加密文件
                    var encryptCachePath = Path.Combine(Directory.GetCurrentDirectory(), "data", "cache");
                    Directory.CreateDirectory(encryptCachePath);

                    var encryptCacheFile = Path.Combine(encryptCachePath, $"{Guid.NewGuid():N}.cache");

                    try
                    {
                        using FileStream inputFileStream = new FileStream(localFileInfo.FullName, FileMode.Open, FileAccess.Read);
                        using FileStream outputFileStream = new FileStream(encryptCacheFile, FileMode.Create, FileAccess.Write);
                        CompressionHelper.CompressStream(inputFileStream, outputFileStream,
                            _jobConfig.CompressAlgorithm, _jobConfig.EncryptAlgorithm, _jobConfig.EncryptKey, _jobConfig.HashAlgorithm, _jobConfig.IsEncryptName,
                            localFileInfo.FullName);

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

                        // 计算文件存储路径
                        var targetFileFullName = $"{saveParentPath}/{name}";

                        // 更新上传文件信息
                        // 将文件重命名为
                        File.Move(encryptCacheFile, targetFileFullName, true);

                        // 文件属性处理
                        var targetFileInfo = new FileInfo(targetFileFullName);

                        // 时间
                        targetFileInfo.CreationTime = localFileInfo.CreationTime;
                        targetFileInfo.LastWriteTime = localFileInfo.LastWriteTime;

                        // 只读
                        if (localFileInfo.IsReadOnly)
                        {
                            targetFileInfo.Attributes |= FileAttributes.ReadOnly;
                        }
                        else
                        {
                            targetFileInfo.Attributes &= ~FileAttributes.ReadOnly;
                        }

                        // 隐藏
                        if (localFileInfo.IsHidden)
                        {
                            targetFileInfo.Attributes |= FileAttributes.Hidden;
                        }
                        else
                        {
                            targetFileInfo.Attributes &= ~FileAttributes.Hidden;
                        }

                        var lf = new LocalStorageTargetFileInfo()
                        {
                            LocalFileKey = localFileInfo.Key,
                            LocalFileHash = localFileInfo.Hash,
                            LocalFileName = localFileInfo.Name,

                            IsFile = true,
                            Hash = string.Empty,
                            Length = targetFileInfo.Length,
                            Key = targetFileInfo.FullName.TrimPath().TrimPrefix(rootInfo.FullName).TrimPath(),
                            Name = targetFileInfo.Name,

                            IsExists = targetFileInfo.Exists,
                            CreationTime = targetFileInfo.CreationTime,
                            FullName = targetFileInfo.FullName,
                            IsHidden = targetFileInfo.Attributes.HasFlag(FileAttributes.Hidden),
                            IsReadOnly = targetFileInfo.Attributes.HasFlag(FileAttributes.ReadOnly),
                            LastWriteTime = targetFileInfo.LastWriteTime,
                        };
                        _targetFiles.AddOrUpdate(lf.Key, lf, (k, v) => lf);

                        // 比较文件 hash
                        targetFile = _targetDb.Single(c => c.LocalFileKey == localFileInfo.Key);
                        if (targetFile == null)
                        {
                            _targetDb.Add(lf);
                        }
                        else
                        {
                            _targetDb.Update(lf);
                        }

                        _log.LogInformation($"文件同步成功 {localFileInfo.FullName}");
                    }
                    finally
                    {
                        // 删除加密文件和缓存文件，如果存在
                        if (File.Exists(encryptCacheFile))
                        {
                            File.Delete(encryptCacheFile);
                        }
                    }
                }
            }
            else
            {
                // 上传文件
                SyncFileDoWork(localFileInfo);
            }
        }

        /// <summary>
        /// 非加密文件同步
        /// </summary>
        /// <param name="localFileInfo"></param>
        /// <param name="fileFullPath"></param>
        /// <param name="saveFilePath"></param>
        private void SyncFileDoWork(LocalStorageFileInfo localFileInfo)
        {
            if (_jobConfig.IsEncrypt)
            {
                return;
            }

            // 计算保存存储目录
            var saveParentPath = Path.GetDirectoryName($"{_targetSaveRootPath}/{localFileInfo.Key}");
            Directory.CreateDirectory(saveParentPath);

            // 计算文件存储路径
            var targetFileFullName = $"{saveParentPath}/{localFileInfo.Name.TrimPath()}";

            // 计算 hash
            if (string.IsNullOrWhiteSpace(localFileInfo.Hash))
            {
                localFileInfo.Hash = ShaHashHelper.ComputeFileHash(localFileInfo.FullName, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, localFileInfo.Length);
            }

            if (_targetFiles.TryGetValue(localFileInfo.Key, out var targetFile) && targetFile != null)
            {
                // 一致
                if (LocalStorageFileInfo.FastAreObjectsEqual(localFileInfo, targetFile))
                {
                    return;
                }

                // 如果目标文件没有 hash 值，则计算 hash 值
                if (string.IsNullOrWhiteSpace(targetFile.Hash))
                {
                    targetFile.Hash = ShaHashHelper.ComputeFileHash(targetFile.FullName, _jobConfig.CheckLevel, _jobConfig.CheckAlgorithm, targetFile.Length);

                    // 一致
                    if (LocalStorageFileInfo.FastAreObjectsEqual(localFileInfo, targetFile))
                    {
                        return;
                    }
                }
            }

            // 进行文件复制，如果存在则覆盖
            File.Copy(localFileInfo.FullName, targetFileFullName, true);

            // 文件属性处理
            var targetFileInfo = new FileInfo(targetFileFullName)
            {
                CreationTime = localFileInfo.CreationTime,
                LastAccessTime = localFileInfo.LastWriteTime,
            };

            // 只读
            if (localFileInfo.IsReadOnly)
            {
                targetFileInfo.Attributes |= FileAttributes.ReadOnly;
            }
            else
            {
                targetFileInfo.Attributes &= ~FileAttributes.ReadOnly;
            }

            // 隐藏
            if (localFileInfo.IsHidden)
            {
                targetFileInfo.Attributes |= FileAttributes.Hidden;
            }
            else
            {
                targetFileInfo.Attributes &= ~FileAttributes.Hidden;
            }

            var lf = new LocalStorageFileInfo()
            {
                Key = localFileInfo.Key,
                Hash = localFileInfo.Hash,
                IsFile = true,

                IsExists = targetFileInfo.Exists,
                CreationTime = targetFileInfo.CreationTime,
                FullName = targetFileInfo.FullName,
                IsHidden = targetFileInfo.Attributes.HasFlag(FileAttributes.Hidden),
                IsReadOnly = targetFileInfo.Attributes.HasFlag(FileAttributes.ReadOnly),
                LastWriteTime = targetFileInfo.LastWriteTime,
                Length = targetFileInfo.Length,
                Name = targetFileInfo.Name
            };
            _targetFiles.AddOrUpdate(lf.Key, lf, (k, v) => lf);

            _log.LogInformation($"文件同步成功 {localFileInfo.FullName}");
        }

        /// <summary>
        /// 初始化目标根目录
        /// </summary>
        /// <returns></returns>
        private void InitTargetRootPath()
        {
            // 目标根目录初始化
            var rootInfo = new DirectoryInfo(_targetSaveRootPath);
            if (!rootInfo.Exists)
            {
                rootInfo.Create();
            }

            var ld = new LocalStorageFileInfo()
            {
                Key = "",
                Hash = string.Empty,
                Length = 0,
                IsFile = false,
                FullName = rootInfo.FullName,
                CreationTime = rootInfo.CreationTime,
                LastWriteTime = rootInfo.LastWriteTime,
                Name = rootInfo.Name,
                IsExists = rootInfo.Exists,
                IsHidden = rootInfo.Attributes.HasFlag(FileAttributes.Hidden),
                IsReadOnly = rootInfo.Attributes.HasFlag(FileAttributes.ReadOnly)
            };

            _targetFolders.TryAdd(ld.Key, ld);
        }

        /// <summary>
        /// 获取文件/文件夹
        /// </summary>
        /// <param name="parentFullName"></param>
        public List<LocalStorageTargetFileInfo> GetLocalFiles(string parentFullName = "")
        {
            var list = new List<LocalStorageTargetFileInfo>();

            if (_jobConfig.IsEncrypt && _jobConfig.IsPack)
            {
            }
            else
            {
                // 如果没有目标文件，则重新扫描
                if (_targetFiles.Count <= 0)
                {
                    InitTargetRootPath();
                    ScanTargetFiles();
                }

                var p = _targetFolders
                    .WhereIf(string.IsNullOrWhiteSpace(parentFullName), c => c.Key == "")
                    .WhereIf(!string.IsNullOrWhiteSpace(parentFullName), c => c.Value.FullName == parentFullName)
                    .FirstOrDefault();

                parentFullName = p.Value.FullName;
                if (!string.IsNullOrWhiteSpace(parentFullName))
                {
                    _targetFolders.Values.Where(c => c.ParentFullName == parentFullName)
                        .OrderBy(c => c.Name)
                        .ToList()
                        .ForEach(c =>
                        {
                            list.Add(new LocalStorageTargetFileInfo()
                            {
                                Name = c.Name,
                                CreationTime = c.CreationTime,
                                LastWriteTime = c.LastWriteTime,
                                FullName = c.FullName,
                                Hash = c.Hash,
                                IsExists = c.IsExists,
                                IsFile = c.IsFile,
                                IsHidden = c.IsHidden,
                                IsReadOnly = c.IsReadOnly,
                                Key = c.Key,
                                Length = c.Length,

                                LocalFileHash = c.Hash,
                                LocalFileKey = c.Key,
                                LocalFileName = c.Name
                            });
                        });
                }

                if (_jobConfig.IsEncrypt && _jobConfig.IsEncryptName)
                {
                    var allValues = _targetDb.GetAll(false).Where(c => c.IsFile).ToList();
                    allValues.Where(c => c.ParentFullName == parentFullName)
                        .OrderBy(c => c.LocalFileName)
                        .ToList()
                        .ForEach(c =>
                        {
                            list.Add(c);
                        });
                }
                else if (_jobConfig.IsEncrypt)
                {
                    var allValues = _targetDb.GetAll(false).Where(c => c.IsFile).ToList();
                    allValues.Where(c => c.ParentFullName == parentFullName)
                        .OrderBy(c => c.LocalFileName)
                        .ToList()
                        .ForEach(c =>
                        {
                            list.Add(c);
                        });
                }
                else
                {
                    _targetFiles.Values.Where(c => c.ParentFullName == parentFullName)
                        .OrderBy(c => c.Name)
                        .ToList()
                        .ForEach(c =>
                        {
                            list.Add(new LocalStorageTargetFileInfo()
                            {
                                Name = c.Name,
                                CreationTime = c.CreationTime,
                                LastWriteTime = c.LastWriteTime,
                                FullName = c.FullName,
                                Hash = c.Hash,
                                IsExists = c.IsExists,
                                IsFile = c.IsFile,
                                IsHidden = c.IsHidden,
                                IsReadOnly = c.IsReadOnly,
                                Key = c.Key,
                                Length = c.Length,

                                LocalFileHash = c.Hash,
                                LocalFileKey = c.Key,
                                LocalFileName = c.Name
                            });
                        });
                }
            }

            return list;
        }

        /// <summary>
        /// 获取文件/文件夹详情
        /// </summary>
        /// <param name="fullName"></param>
        /// <returns></returns>
        public LocalStorageFileInfo GetLocalFileDetail(string fullName = "")
        {
            if (_jobConfig.IsEncrypt && _jobConfig.IsPack)
            {
            }
            else
            {
                // 如果没有目标文件，则重新扫描
                if (_targetFiles.Count <= 0)
                {
                    InitTargetRootPath();
                    ScanTargetFiles();
                }
                if (string.IsNullOrWhiteSpace(fullName))
                {
                    fullName = new DirectoryInfo(_targetSaveRootPath).FullName;
                }

                return _targetFolders.Values.Where(c => c.FullName == fullName).FirstOrDefault();

                //if (_targetFolders.TryGetValue(fullName, out var v) && v != null)
                //{
                //    return v;
                //}
            }

            return null;
        }

        /// <summary>
        /// 获取本地文件信息，通过 key
        /// </summary>
        /// <param name="key"></param>
        /// <returns></returns>
        public LocalStorageFileInfo GetLocalFileDetailByKey(string key = "")
        {
            if (_jobConfig.IsEncrypt && _jobConfig.IsPack)
            {
            }
            else
            {
                if (_targetFolders.ContainsKey(key) && _targetFolders.TryGetValue(key, out var v) && v != null)
                {
                    return v;
                }
                else if (!_jobConfig.IsEncrypt && _targetFiles.ContainsKey(key) && _targetFiles.TryGetValue(key, out var f) && f != null)
                {
                    return f;
                }
                else
                {
                    return _targetDb.Get(key);
                }
            }

            return null;
        }

        /// <summary>
        /// 获取文件夹下的所有文件，包含子文件
        /// </summary>
        /// <param name="parentKey"></param>
        /// <returns></returns>
        public List<LocalStorageFileInfo> GetLocalFilesByKey(string parentKey = "")
        {
            if (_jobConfig.IsEncrypt && _jobConfig.IsPack)
            {
            }
            else
            {
                if (_targetFolders.ContainsKey(parentKey) && _targetFolders.TryGetValue(parentKey, out var v) && v != null)
                {
                    if (!_jobConfig.IsEncrypt)
                    {
                        // 未加密
                        return _targetFiles.Values.Where(c => c.Key.StartsWith(parentKey)).ToList();
                    }
                    else
                    {
                        return _targetDb.GetAll(false).Where(c => c.IsFile && c.Key.StartsWith(parentKey)).Select(c => (LocalStorageFileInfo)c).ToList();
                    }
                }
            }

            return new List<LocalStorageFileInfo>();
        }

        #endregion 本地存储

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
            if (_localListenPaths.TryGetValue(e.FullPath, out var isPath))
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
                    _localListenPaths.TryRemove(e.FullPath, out _);
                }
            }
            else
            {
                // 未知
                _log.LogInformation($"文件夹/文件删除: {e.FullPath}, 类型: {e.ChangeType}");
            }
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

        #endregion 文件监听事件
    }
}