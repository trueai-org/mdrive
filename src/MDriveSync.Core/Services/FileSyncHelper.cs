using MDriveSync.Security;
using MDriveSync.Security.Models;
using Serilog;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 文件同步助手，支持单向、镜像、双向等多种同步模式，高性能文件系统扫描及比较
    /// </summary>
    public class FileSyncHelper
    {
        private readonly object _lockObject = new object();
        private bool _isRunning;

        private readonly SyncOptions _options;
        private readonly IProgress<SyncProgress> _progress;
        private readonly CancellationToken _cancellationToken;

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private int _processedItems = 0;
        private int _totalItems = 0;
        private SyncStatistics _statistics = new SyncStatistics();

        /// <summary>
        /// Cron 调度器
        /// </summary>
        private readonly QuartzCronScheduler _quartzCronScheduler;

        /// <summary>
        /// 定时器调度器
        /// </summary>
        private readonly IntervalScheduler _intervalScheduler;

        /// <summary>
        /// 初始化文件同步助手
        /// </summary>
        /// <param name="options">同步选项</param>
        /// <param name="progress">进度报告回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        public FileSyncHelper(SyncOptions options, IProgress<SyncProgress> progress = null, CancellationToken cancellationToken = default)
        {
            _options = options ?? throw new ArgumentNullException(nameof(options));
            _progress = progress;
            _cancellationToken = cancellationToken;

            // 默认忽略临时文件
            if (!string.IsNullOrWhiteSpace(_options.TempFileSuffix) &&
                !_options.IgnorePatterns.Contains(_options.TempFileSuffix))
            {
                _options.IgnorePatterns.Add(_options.TempFileSuffix);
            }

            // 如果配置了 cron
            if (!string.IsNullOrEmpty(_options.CronExpression))
            {
                // 创建计划
                _quartzCronScheduler = new QuartzCronScheduler(_options.CronExpression, async () =>
                {
                    await SyncAsync();
                });
                _quartzCronScheduler.Start();
            }
            // 如果配置了定时器
            else if (_options.Interval > 0)
            {
                // 启动定时器
                _intervalScheduler = new IntervalScheduler(_options.Interval, async () =>
                {
                    await SyncAsync();
                });
                _intervalScheduler.Start();
            }
        }

        /// <summary>
        /// 执行同步操作
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncAsync()
        {
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                SourcePath = _options.SourcePath,
                TargetPath = _options.TargetPath,
                Mode = _options.SyncMode,
                Status = ESyncStatus.Started
            };

            // 如果配置了定时
            if (!string.IsNullOrWhiteSpace(_options.CronExpression) || _options.Interval > 0)
            {
                // 未开启立即执行
                if (!_options.ExecuteImmediately)
                {
                    // 直接返回
                    result.Status = ESyncStatus.NotStarted;
                    return result;
                }
            }

            lock (_lockObject)
            {
                if (_isRunning)
                {
                    Log.Warning("同步操作正在进行中，请稍后再试。");
                    result.Status = ESyncStatus.Running;
                    return result;
                }

                _isRunning = true;
                _stopwatch.Restart();
                _statistics = new SyncStatistics();
            }

            try
            {
                Log.Information($"开始同步操作...");
                Log.Information($"源目录: {_options.SourcePath}");
                Log.Information($"目标目录: {_options.TargetPath}");
                Log.Information($"同步模式: {_options.SyncMode}");
                Log.Information($"比较方法: {_options.CompareMethod}");

                // 初始化源目录和目标目录
                ValidatePaths();
                ReportProgress("正在初始化同步操作...", 0);

                // 扫描源目录和目标目录的文件列表
                var (sourceFiles, sourceDirs) = await ScanDirectoryAsync(_options.SourcePath, "源目录");
                var (targetFiles, targetDirs) = await ScanDirectoryAsync(_options.TargetPath, "目标目录");

                _totalItems = sourceFiles.Count + targetFiles.Count;
                ReportProgress($"开始比较文件差异，共 {_totalItems} 个文件需要处理", 0);

                // 根据同步模式执行不同的同步策略
                await ExecuteSyncByMode(sourceFiles, sourceDirs, targetFiles, targetDirs, result);

                result.Status = ESyncStatus.Completed;
                result.ElapsedTime = _stopwatch.Elapsed;
                result.Statistics = _statistics;

                ReportProgress($"同步完成，耗时: {_stopwatch.Elapsed.TotalSeconds:F2}秒", 100);

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Status = ESyncStatus.Canceled;
                result.ElapsedTime = _stopwatch.Elapsed;
                result.Statistics = _statistics;
                ReportProgress("同步操作已取消", -1);
                return result;
            }
            catch (Exception ex)
            {
                result.Status = ESyncStatus.Failed;
                result.ErrorMessage = ex.Message;
                result.ElapsedTime = _stopwatch.Elapsed;
                result.Statistics = _statistics;
                ReportProgress($"同步操作失败: {ex.Message}", -1);
                return result;
            }
            finally
            {
                lock (_lockObject)
                {
                    _stopwatch.Stop();
                    result.EndTime = DateTime.Now;

                    _isRunning = false;

                    // 显示结果
                    Log.Information($"同步操作完成，状态: {result.Status}");
                    Log.Information($"总耗时: {result.ElapsedTime.TotalSeconds:F2} 秒");

                    if (result.Statistics != null)
                    {
                        Log.Information($"文件复制: {result.Statistics.FilesCopied} 个");
                        Log.Information($"文件更新: {result.Statistics.FilesUpdated} 个");
                        Log.Information($"文件删除: {result.Statistics.FilesDeleted} 个");
                        Log.Information($"文件跳过: {result.Statistics.FilesSkipped} 个");
                        Log.Information($"目录创建: {result.Statistics.DirectoriesCreated} 个");
                        Log.Information($"目录删除: {result.Statistics.DirectoriesDeleted} 个");
                        Log.Information($"错误数量: {result.Statistics.Errors} 个");
                        Log.Information($"处理总量: {result.Statistics.BytesProcessed.FormatSize()}");
                    }

                    // 如果配置了 cron 或定时器，则不停止，并显示下次执行时间
                    if (_quartzCronScheduler != null)
                    {
                        Log.Information($"下次执行时间: {_quartzCronScheduler.GetNextRunTime()}");
                    }
                    else if (_intervalScheduler != null)
                    {
                        Log.Information($"下次执行时间: {_intervalScheduler.GetNextRunTime()}");
                    }

                    // 任务结束了，显示终止分割信息
                    Log.Information(new string('-', 50));
                }
            }
        }

        /// <summary>
        /// 根据同步模式执行相应的同步策略
        /// </summary>
        private async Task ExecuteSyncByMode(
            Dictionary<string, FileInfo> sourceFiles,
            HashSet<string> sourceDirs,
            Dictionary<string, FileInfo> targetFiles,
            HashSet<string> targetDirs,
            SyncResult result)
        {
            List<SyncAction> actions = new List<SyncAction>();

            switch (_options.SyncMode)
            {
                case ESyncMode.OneWay:
                    actions = CreateOneWaySyncActionsOptimized(sourceFiles, sourceDirs, targetFiles, targetDirs);
                    break;

                case ESyncMode.Mirror:
                    actions = CreateMirrorSyncActionsOptimized(sourceFiles, sourceDirs, targetFiles, targetDirs);
                    break;

                case ESyncMode.TwoWay:
                    actions = CreateTwoWaySyncActionsOptimized(sourceFiles, sourceDirs, targetFiles, targetDirs);
                    break;

                default:
                    throw new NotSupportedException($"不支持的同步模式: {_options.SyncMode}");
            }

            // 记录行动计划
            result.Actions = actions;

            // 如果是预览模式，则不执行实际操作
            if (_options.PreviewOnly)
            {
                ReportProgress("预览模式 - 不执行实际操作", -1);
                return;
            }

            // 执行同步操作
            await ExecuteSyncActionsAsyncOptimized(actions);
        }

        /// <summary>
        /// 优化的单向同步创建方法，使用并行处理提高性能
        /// </summary>
        private List<SyncAction> CreateOneWaySyncActionsOptimized(
            Dictionary<string, FileInfo> sourceFiles,
            HashSet<string> sourceDirs,
            Dictionary<string, FileInfo> targetFiles,
            HashSet<string> targetDirs)
        {
            // 预估总共需要处理的项目数
            var totalItems = sourceDirs.Count + sourceFiles.Count;
            var processedItems = 0;
            var lastProgressReport = DateTime.MinValue;

            ReportProgress($"正在分析同步计划：共 {sourceDirs.Count} 个目录及 {sourceFiles.Count} 个文件需要处理", 0);

            // 使用ConcurrentBag来安全地收集并行处理的结果
            var actions = new ConcurrentBag<SyncAction>();

            // 使用线程安全的计数器跟踪统计信息
            int filesToCopy = 0;
            int filesToUpdate = 0;
            int filesSkipped = 0;
            long bytesToProcess = 0;

            // 使用读写锁保护进度更新
            var progressLock = new ReaderWriterLockSlim();

            // 1. 批量处理目录创建 - 可以并行执行但通常不是瓶颈
            ReportProgress("正在分析目录结构...", 0);
            var dirStartTime = DateTime.Now;
            var targetDirSet = new HashSet<string>(targetDirs, StringComparer.OrdinalIgnoreCase);

            // 处理目录创建（目录操作通常比较少，可以顺序处理）
            foreach (var sourceDir in sourceDirs)
            {
                var relativePath = GetRelativePath(sourceDir, _options.SourcePath);
                var targetDirPath = Path.Combine(_options.TargetPath, relativePath);

                if (!targetDirSet.Contains(targetDirPath))
                {
                    actions.Add(new SyncAction
                    {
                        ActionType = ESyncActionType.CreateDirectory,
                        SourcePath = sourceDir,
                        TargetPath = targetDirPath,
                        RelativePath = relativePath
                    });
                }

                // 更新进度
                processedItems++;
                if (ShouldReportProgress(processedItems, totalItems, ref lastProgressReport))
                {
                    double progressPercent = (double)processedItems / totalItems * 100;
                    ReportProgress($"正在分析目录结构: {processedItems}/{totalItems} ({progressPercent:F1}%)", (int)progressPercent);
                }
            }

            var dirTime = DateTime.Now - dirStartTime;
            ReportProgress($"目录分析完成，用时: {dirTime.TotalSeconds:F2}秒", (int)((double)processedItems / totalItems * 100));

            // 2. 批量处理文件 - 使用并行处理提高性能
            ReportProgress($"正在分析文件: 共 {sourceFiles.Count} 个", (int)((double)processedItems / totalItems * 100));
            var fileStartTime = DateTime.Now;

            // 准备目标文件查找表
            var targetPathLookup = PrepareTargetFileLookup(targetFiles);

            // 并行处理的参数配置
            int parallelism = _options.EnableParallelFileOperations
                ? Math.Min(_options.MaxParallelOperations, Environment.ProcessorCount)
                : 1;

            // 每批处理的文件数 - 大文件集合时使用分批处理避免内存压力
            const int batchSize = 1000;
            var sourceFilesList = sourceFiles.ToList();
            int batchCount = (int)Math.Ceiling(sourceFilesList.Count / (double)batchSize);

            // 跟踪已处理的文件计数
            int currentFileIndex = 0;
            object lockObject = new object();

            // 批量处理文件
            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                var currentBatch = sourceFilesList
                    .Skip(batchIndex * batchSize)
                    .Take(batchSize)
                    .ToList();

                // 并行处理当前批次
                Parallel.ForEach(
                    currentBatch,
                    new ParallelOptions { MaxDegreeOfParallelism = parallelism },
                    sourceEntry =>
                    {
                        var sourceFilePath = sourceEntry.Key;
                        var sourceFile = sourceEntry.Value;
                        var relativePath = GetRelativePath(sourceFilePath, _options.SourcePath);
                        var targetFilePath = Path.Combine(_options.TargetPath, relativePath);
                        bool needsAction = false;

                        // 检查目标文件是否存在
                        if (!targetPathLookup.TryGetValue(targetFilePath, out var targetFile))
                        {
                            // 目标不存在文件，需要复制
                            actions.Add(new SyncAction
                            {
                                ActionType = ESyncActionType.CopyFile,
                                SourcePath = sourceFilePath,
                                TargetPath = targetFilePath,
                                RelativePath = relativePath,
                                Size = sourceFile.Length
                            });
                            Interlocked.Increment(ref filesToCopy);
                            Interlocked.Add(ref bytesToProcess, sourceFile.Length);
                            needsAction = true;
                        }
                        else if (NeedsUpdate(sourceFile, targetFile))
                        {
                            // 文件需要更新
                            actions.Add(new SyncAction
                            {
                                ActionType = ESyncActionType.UpdateFile,
                                SourcePath = sourceFilePath,
                                TargetPath = targetFilePath,
                                RelativePath = relativePath,
                                Size = sourceFile.Length
                            });
                            Interlocked.Increment(ref filesToUpdate);
                            Interlocked.Add(ref bytesToProcess, sourceFile.Length);
                            needsAction = true;
                        }
                        else
                        {
                            Interlocked.Increment(ref filesSkipped);
                        }

                        // 更新处理进度 (使用锁以避免并发更新冲突)
                        lock (lockObject)
                        {
                            processedItems++;
                            currentFileIndex++;

                            if (ShouldReportProgress(processedItems, totalItems, ref lastProgressReport))
                            {
                                double progressPercent = (double)processedItems / totalItems * 100;

                                // 计算处理速度 (文件/秒)
                                var elapsed = DateTime.Now - fileStartTime;
                                double filesPerSecond = elapsed.TotalSeconds > 0
                                    ? currentFileIndex / elapsed.TotalSeconds
                                    : 0;

                                // 预估剩余时间
                                var remainingFiles = sourceFiles.Count - currentFileIndex;
                                string remainingTime = filesPerSecond > 0
                                    ? $", 剩余时间: {TimeSpan.FromSeconds(remainingFiles / filesPerSecond):mm\\:ss}"
                                    : "";

                                ReportProgress($"正在分析文件: {processedItems}/{totalItems} " +
                                              $"[复制:{filesToCopy}, 更新:{filesToUpdate}, 跳过:{filesSkipped}], " +
                                              $"速度: {filesPerSecond:F1}文件/秒{remainingTime}",
                                              (int)progressPercent);
                            }
                        }
                    });
            }

            var fileTime = DateTime.Now - fileStartTime;

            // 更新同步统计信息
            _statistics.FilesToCopy = filesToCopy;
            _statistics.FilesToUpdate = filesToUpdate;
            _statistics.FilesSkipped = filesSkipped;
            _statistics.BytesToProcess = bytesToProcess;

            ReportProgress($"文件分析完成，用时: {fileTime.TotalSeconds:F2}秒。需要复制: {filesToCopy}个, " +
                          $"需要更新: {filesToUpdate}个, 可跳过: {filesSkipped}个, 总数据量: {bytesToProcess.FormatSize()}", 100);

            // 将ConcurrentBag转换为List并按照操作类型排序返回
            return actions.OrderBy(a => GetActionPriority(a.ActionType)).ToList();
        }

        /// <summary>
        /// 优化的镜像同步创建方法，使用并行处理提高性能
        /// </summary>
        private List<SyncAction> CreateMirrorSyncActionsOptimized(
            Dictionary<string, FileInfo> sourceFiles,
            HashSet<string> sourceDirs,
            Dictionary<string, FileInfo> targetFiles,
            HashSet<string> targetDirs)
        {
            // 预估操作数并报告进度
            ReportProgress($"正在分析镜像同步计划：{sourceFiles.Count}个源文件，{targetFiles.Count}个目标文件", 0);
            var startTime = DateTime.Now;

            // 使用ConcurrentBag收集所有的操作，线程安全
            var actions = new ConcurrentBag<SyncAction>();

            // 创建单向同步操作（已经并行优化）
            var oneWayActions = CreateOneWaySyncActionsOptimized(sourceFiles, sourceDirs, targetFiles, targetDirs);
            foreach (var action in oneWayActions)
            {
                actions.Add(action);
            }

            // 使用线程安全的计数器记录统计信息
            int filesToDelete = 0;
            int dirsToDelete = 0;

            // 预计算所有的相对路径并使用高效的HashSet存储，提高查找效率
            var sourceRelativePaths = new ConcurrentDictionary<string, byte>();
            var sourceRelativeDirs = new ConcurrentDictionary<string, byte>();

            // 并行填充源路径HashSet
            Parallel.ForEach(sourceFiles.Keys,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                sourcePath =>
                {
                    var relativePath = GetRelativePath(sourcePath, _options.SourcePath);
                    sourceRelativePaths[relativePath] = 0;
                });

            Parallel.ForEach(sourceDirs,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                sourceDir =>
                {
                    var relativePath = GetRelativePath(sourceDir, _options.SourcePath);
                    sourceRelativeDirs[relativePath] = 0;
                });

            ReportProgress("预处理完成，开始查找需要删除的文件...", 20);

            // 查找需要删除的文件（并行处理）
            Parallel.ForEach(targetFiles,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                targetFile =>
                {
                    var relativePath = GetRelativePath(targetFile.Key, _options.TargetPath);
                    if (!sourceRelativePaths.ContainsKey(relativePath))
                    {
                        // 目标文件在源中不存在，需要删除
                        actions.Add(new SyncAction
                        {
                            ActionType = ESyncActionType.DeleteFile,
                            TargetPath = targetFile.Key,
                            RelativePath = relativePath,
                            Size = targetFile.Value.Length
                        });
                        Interlocked.Increment(ref filesToDelete);
                    }
                });

            // 为了保证删除目录的顺序正确（子目录先删除），这部分不能完全并行化
            // 但可以先并行筛选出需要删除的目录，然后按顺序添加到操作列表
            var dirsToDeleteList = new ConcurrentBag<(string Path, string RelativePath, int Depth)>();

            Parallel.ForEach(targetDirs.Where(d => d != _options.TargetPath),
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                targetDir =>
                {
                    var relativePath = GetRelativePath(targetDir, _options.TargetPath);
                    if (!sourceRelativeDirs.ContainsKey(relativePath))
                    {
                        // 计算目录深度，用于后续排序
                        int depth = relativePath.Count(c => c == Path.DirectorySeparatorChar || c == Path.AltDirectorySeparatorChar);
                        dirsToDeleteList.Add((targetDir, relativePath, depth));
                        Interlocked.Increment(ref dirsToDelete);
                    }
                });

            // 按目录深度降序排序（确保先删除深层目录）
            var orderedDirsToDelete = dirsToDeleteList
                .ToList()
                .OrderByDescending(d => d.Depth)
                .ToList();

            // 添加目录删除操作
            foreach (var dir in orderedDirsToDelete)
            {
                actions.Add(new SyncAction
                {
                    ActionType = ESyncActionType.DeleteDirectory,
                    TargetPath = dir.Path,
                    RelativePath = dir.RelativePath
                });
            }

            _statistics.FilesToDelete = filesToDelete;
            _statistics.DirectoriesToDelete = dirsToDelete;

            var timeElapsed = DateTime.Now - startTime;
            ReportProgress($"镜像同步分析完成，耗时: {timeElapsed.TotalSeconds:F2}秒，需要删除: {filesToDelete}个文件, {dirsToDelete}个目录", 100);

            // 将ConcurrentBag转换为List并按操作优先级排序
            return actions.OrderBy(a => GetActionPriority(a.ActionType)).ToList();
        }

        /// <summary>
        /// 创建优化的双向同步操作列表（源 <-> 目标，高效解决冲突），使用并行处理提高性能
        /// </summary>
        private List<SyncAction> CreateTwoWaySyncActionsOptimized(
            Dictionary<string, FileInfo> sourceFiles,
            HashSet<string> sourceDirs,
            Dictionary<string, FileInfo> targetFiles,
            HashSet<string> targetDirs)
        {
            var startTime = DateTime.Now;
            ReportProgress($"正在分析双向同步计划：{sourceFiles.Count}个源文件，{targetFiles.Count}个目标文件", 0);

            // 使用线程安全集合
            var actions = new ConcurrentBag<SyncAction>();
            var processedPaths = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            // 线程安全的统计计数器
            int filesToCopy = 0;
            int filesToUpdate = 0;
            int filesSkipped = 0;
            long bytesToProcess = 0;

            // 并行预计算所有目录的相对路径
            var allSourceRelativeDirs = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);
            var allTargetRelativeDirs = new ConcurrentDictionary<string, byte>(StringComparer.OrdinalIgnoreCase);

            Parallel.ForEach(sourceDirs,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                sourceDir =>
                {
                    var relativePath = GetRelativePath(sourceDir, _options.SourcePath);
                    allSourceRelativeDirs.TryAdd(relativePath, 0);
                });

            Parallel.ForEach(targetDirs.Where(d => d != _options.TargetPath),
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                targetDir =>
                {
                    var relativePath = GetRelativePath(targetDir, _options.TargetPath);
                    allTargetRelativeDirs.TryAdd(relativePath, 0);
                });

            Log.Information($"准备双向同步: 源目录 {allSourceRelativeDirs.Count} 个, 目标目录 {allTargetRelativeDirs.Count} 个");
            Log.Information($"源文件 {sourceFiles.Count} 个, 目标文件 {targetFiles.Count} 个");

            ReportProgress("开始处理目录创建操作...", 10);

            // 1. 目录创建操作（虽然目录数量通常不多，但仍使用并行可提高性能）

            // 1.1 在目标中创建源中存在的目录
            Parallel.ForEach(allSourceRelativeDirs.Keys,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations / 2 : 1 },
                sourceRelativeDir =>
                {
                    var targetDirPath = Path.Combine(_options.TargetPath, sourceRelativeDir);
                    if (!targetDirs.Contains(targetDirPath))
                    {
                        actions.Add(new SyncAction
                        {
                            ActionType = ESyncActionType.CreateDirectory,
                            SourcePath = Path.Combine(_options.SourcePath, sourceRelativeDir),
                            TargetPath = targetDirPath,
                            RelativePath = sourceRelativeDir
                        });
                    }
                });

            // 1.2 在源中创建目标中存在的目录
            Parallel.ForEach(allTargetRelativeDirs.Keys,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations / 2 : 1 },
                targetRelativeDir =>
                {
                    var sourceDirPath = Path.Combine(_options.SourcePath, targetRelativeDir);
                    if (!sourceDirs.Contains(sourceDirPath))
                    {
                        actions.Add(new SyncAction
                        {
                            ActionType = ESyncActionType.CreateDirectory,
                            SourcePath = Path.Combine(_options.TargetPath, targetRelativeDir),
                            TargetPath = sourceDirPath,
                            RelativePath = targetRelativeDir,
                            Direction = ESyncDirection.TargetToSource
                        });
                    }
                });

            ReportProgress("目录创建操作分析完成，开始预处理文件...", 20);

            // 预计算并缓存所有相对路径，使用线程安全的字典
            var sourceRelativePathMap = new ConcurrentDictionary<string, (string FullPath, FileInfo Info)>(StringComparer.OrdinalIgnoreCase);
            var targetRelativePathMap = new ConcurrentDictionary<string, (string FullPath, FileInfo Info)>(StringComparer.OrdinalIgnoreCase);

            // 并行填充源路径映射
            Parallel.ForEach(sourceFiles,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                sourceEntry =>
                {
                    var relativePath = GetRelativePath(sourceEntry.Key, _options.SourcePath);
                    sourceRelativePathMap.TryAdd(relativePath, (sourceEntry.Key, sourceEntry.Value));
                });

            // 并行填充目标路径映射
            Parallel.ForEach(targetFiles,
                new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                targetEntry =>
                {
                    var relativePath = GetRelativePath(targetEntry.Key, _options.TargetPath);
                    targetRelativePathMap.TryAdd(relativePath, (targetEntry.Key, targetEntry.Value));
                });

            ReportProgress("文件路径映射完成，开始分析源->目标文件...", 30);

            // 2.1 处理源到目标的文件（将所有源文件分成多个批次并行处理）
            const int batchSize = 1000;
            var sourceRelativePathsList = sourceRelativePathMap.ToList();
            int batchCount = (int)Math.Ceiling(sourceRelativePathsList.Count / (double)batchSize);

            for (int batchIndex = 0; batchIndex < batchCount; batchIndex++)
            {
                var currentBatch = sourceRelativePathsList
                    .Skip(batchIndex * batchSize)
                    .Take(batchSize)
                    .ToList();

                Parallel.ForEach(currentBatch,
                    new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                    sourceEntry =>
                    {
                        var relativePath = sourceEntry.Key;
                        var sourceFilePath = sourceEntry.Value.FullPath;
                        var sourceFile = sourceEntry.Value.Info;
                        var targetFilePath = Path.Combine(_options.TargetPath, relativePath);

                        // 标记路径已处理
                        processedPaths.TryAdd(relativePath, 0);

                        if (!targetRelativePathMap.TryGetValue(relativePath, out var targetEntry))
                        {
                            // 目标不存在，复制到目标
                            actions.Add(new SyncAction
                            {
                                ActionType = ESyncActionType.CopyFile,
                                SourcePath = sourceFilePath,
                                TargetPath = targetFilePath,
                                RelativePath = relativePath,
                                Size = sourceFile.Length
                            });
                            Interlocked.Increment(ref filesToCopy);
                            Interlocked.Add(ref bytesToProcess, sourceFile.Length);
                        }
                        else
                        {
                            var targetFile = targetEntry.Info;

                            // 两边都存在，需要解决冲突
                            var conflictResult = ResolveConflict(sourceFile, targetFile);

                            switch (conflictResult)
                            {
                                case ESyncConflictResolution.SourceWins:
                                    // 只有当文件实际需要更新时才添加操作
                                    if (NeedsUpdate(sourceFile, targetFile))
                                    {
                                        actions.Add(new SyncAction
                                        {
                                            ActionType = ESyncActionType.UpdateFile,
                                            SourcePath = sourceFilePath,
                                            TargetPath = targetFilePath,
                                            RelativePath = relativePath,
                                            Size = sourceFile.Length,
                                            ConflictResolution = conflictResult
                                        });
                                        Interlocked.Increment(ref filesToUpdate);
                                        Interlocked.Add(ref bytesToProcess, sourceFile.Length);
                                    }
                                    else
                                    {
                                        Interlocked.Increment(ref filesSkipped);
                                    }
                                    break;

                                case ESyncConflictResolution.TargetWins:
                                    // 只有当文件实际需要更新时才添加操作
                                    if (NeedsUpdate(targetFile, sourceFile))
                                    {
                                        actions.Add(new SyncAction
                                        {
                                            ActionType = ESyncActionType.UpdateFile,
                                            SourcePath = targetFilePath,
                                            TargetPath = sourceFilePath,
                                            RelativePath = relativePath,
                                            Size = targetFile.Length,
                                            Direction = ESyncDirection.TargetToSource,
                                            ConflictResolution = conflictResult
                                        });
                                        Interlocked.Increment(ref filesToUpdate);
                                        Interlocked.Add(ref bytesToProcess, targetFile.Length);
                                    }
                                    else
                                    {
                                        Interlocked.Increment(ref filesSkipped);
                                    }
                                    break;

                                case ESyncConflictResolution.KeepBoth:
                                    // 生成冲突文件名是线程安全的
                                    string targetNewName = GetConflictFileName(targetFilePath);

                                    // 首先重命名目标文件
                                    actions.Add(new SyncAction
                                    {
                                        ActionType = ESyncActionType.RenameFile,
                                        SourcePath = targetFilePath,
                                        TargetPath = targetNewName,
                                        RelativePath = GetRelativePath(targetNewName, _options.TargetPath),
                                        ConflictResolution = conflictResult
                                    });

                                    // 然后复制源文件到目标
                                    actions.Add(new SyncAction
                                    {
                                        ActionType = ESyncActionType.CopyFile,
                                        SourcePath = sourceFilePath,
                                        TargetPath = targetFilePath,
                                        RelativePath = relativePath,
                                        Size = sourceFile.Length
                                    });

                                    Interlocked.Increment(ref filesToCopy);
                                    Interlocked.Add(ref bytesToProcess, sourceFile.Length);
                                    break;

                                case ESyncConflictResolution.Skip:
                                    Interlocked.Increment(ref filesSkipped);
                                    break;
                            }
                        }
                    });

                // 报告每个批次的进度
                int progress = 30 + (int)(40 * (batchIndex + 1) / batchCount);
                ReportProgress($"处理源->目标文件 批次 {batchIndex + 1}/{batchCount}, 已处理: {filesToCopy + filesToUpdate + filesSkipped}个文件", progress);
            }

            // 2.2 处理目标到源的文件（只处理尚未处理的文件）
            if (targetRelativePathMap.Count > 0)
            {
                ReportProgress("开始分析目标->源文件...", 70);

                // 筛选未处理的目标文件
                var unprocessedTargetFiles = targetRelativePathMap
                    .Where(entry => !processedPaths.ContainsKey(entry.Key))
                    .ToList();

                // 对于大量文件使用并行处理
                if (unprocessedTargetFiles.Count > 0)
                {
                    int unprocessedBatchCount = (int)Math.Ceiling(unprocessedTargetFiles.Count / (double)batchSize);

                    for (int batchIndex = 0; batchIndex < unprocessedBatchCount; batchIndex++)
                    {
                        var currentBatch = unprocessedTargetFiles
                            .Skip(batchIndex * batchSize)
                            .Take(batchSize)
                            .ToList();

                        Parallel.ForEach(currentBatch,
                            new ParallelOptions { MaxDegreeOfParallelism = _options.EnableParallelFileOperations ? _options.MaxParallelOperations : 1 },
                            entry =>
                            {
                                var relativePath = entry.Key;
                                var targetFilePath = entry.Value.FullPath;
                                var targetFile = entry.Value.Info;
                                var sourceFilePath = Path.Combine(_options.SourcePath, relativePath);

                                actions.Add(new SyncAction
                                {
                                    ActionType = ESyncActionType.CopyFile,
                                    SourcePath = targetFilePath,
                                    TargetPath = sourceFilePath,
                                    RelativePath = relativePath,
                                    Size = targetFile.Length,
                                    Direction = ESyncDirection.TargetToSource
                                });

                                Interlocked.Increment(ref filesToCopy);
                                Interlocked.Add(ref bytesToProcess, targetFile.Length);
                            });

                        // 报告每个批次的进度
                        int progress = 70 + (int)(30 * (batchIndex + 1) / unprocessedBatchCount);
                        ReportProgress($"处理目标->源文件 批次 {batchIndex + 1}/{unprocessedBatchCount}, 剩余: {unprocessedTargetFiles.Count - (batchIndex + 1) * batchSize}个文件", progress);
                    }
                }
            }

            // 更新统计信息
            _statistics.FilesToCopy = filesToCopy;
            _statistics.FilesToUpdate = filesToUpdate;
            _statistics.FilesSkipped = filesSkipped;
            _statistics.BytesToProcess = bytesToProcess;

            var timeElapsed = DateTime.Now - startTime;
            ReportProgress($"双向同步分析完成，耗时: {timeElapsed.TotalSeconds:F2}秒，需要复制: {filesToCopy}个, 更新: {filesToUpdate}个, 跳过: {filesSkipped}个, 总数据量: {bytesToProcess.FormatSize()}", 100);

            // 将ConcurrentBag转换为List并按操作优先级排序，确保目录先创建
            return actions.OrderBy(a => GetActionPriority(a.ActionType)).ToList();
        }

        /// <summary>
        /// 优化的执行同步操作方法
        /// </summary>
        private async Task ExecuteSyncActionsAsyncOptimized(List<SyncAction> actions)
        {
            if (actions.Count == 0)
            {
                ReportProgress("没有需要执行的操作", 100);
                return;
            }

            // 按操作类型对操作进行分组和排序
            var actionGroups = actions
                .GroupBy(a => GetActionPriority(a.ActionType))
                .OrderBy(g => g.Key)
                .ToList();

            _totalItems = actions.Count;
            _processedItems = 0;

            // 优化：为大型操作列表预热文件系统缓存（创建目录操作）
            var createDirActions = actionGroups
                .FirstOrDefault(g => g.First().ActionType == ESyncActionType.CreateDirectory)
                ?.ToList() ?? new List<SyncAction>();

            if (createDirActions.Count > 100)
            {
                ReportProgress($"预热文件系统：准备创建 {createDirActions.Count} 个目录...", 0);
                // 批量创建所有目录（通常很快且减少文件系统冲突）
                await Task.Run(() =>
                {
                    foreach (var action in createDirActions)
                    {
                        try
                        {
                            if (!Directory.Exists(action.TargetPath))
                            {
                                Directory.CreateDirectory(action.TargetPath);
                                action.Status = ESyncActionStatus.Completed;
                                _statistics.DirectoriesCreated++;
                            }
                        }
                        catch (Exception ex)
                        {
                            action.Status = ESyncActionStatus.Failed;
                            action.ErrorMessage = ex.Message;
                            _statistics.Errors++;

                            if (!_options.ContinueOnError)
                                throw;
                        }
                        _processedItems++;
                    }
                });

                // 报告进度
                ReportProgress($"目录创建完成，处理完成 {createDirActions.Count} 个目录",
                    CalculateProgress(_processedItems, _totalItems));
            }

            // 处理剩余操作组
            foreach (var group in actionGroups)
            {
                // 跳过已处理的目录创建组
                if (group.First().ActionType == ESyncActionType.CreateDirectory &&
                    createDirActions.Count > 100)
                    continue;

                var groupActions = group.ToList();
                var actionType = groupActions.First().ActionType;

                ReportProgress($"处理 {GetActionTypeDescription(actionType)} 操作，共 {groupActions.Count} 项",
                    CalculateProgress(_processedItems, _totalItems));

                // 设置并行度，文件操作使用配置的并行度，目录操作单线程执行
                int parallelism = IsFileOperation(actionType) && _options.EnableParallelFileOperations
                    ? _options.MaxParallelOperations
                    : 1;

                // 批处理：每次处理一批操作以平衡内存使用和并行效率
                const int batchSize = 500;

                for (int i = 0; i < groupActions.Count; i += batchSize)
                {
                    var batch = groupActions.Skip(i).Take(batchSize).ToList();

                    // 使用SemaphoreSlim控制并行度
                    using var semaphore = new SemaphoreSlim(parallelism);
                    var tasks = new List<Task>(batch.Count);

                    foreach (var action in batch)
                    {
                        // 获取信号量
                        await semaphore.WaitAsync(_cancellationToken);

                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await ExecuteSingleActionAsync(action);
                                Interlocked.Increment(ref _processedItems);
                            }
                            finally
                            {
                                // 释放信号量
                                semaphore.Release();
                            }
                        }, _cancellationToken));
                    }

                    // 等待当前批次完成
                    await Task.WhenAll(tasks);

                    // 每批次后报告进度
                    ReportProgress(
                        $"正在处理 {GetActionTypeDescription(actionType)} 操作 ({_processedItems}/{_totalItems})",
                        CalculateProgress(_processedItems, _totalItems)
                    );
                }
            }
        }

        /// <summary>
        /// 准备目标文件路径查找表以加速查找
        /// </summary>
        private Dictionary<string, FileInfo> PrepareTargetFileLookup(Dictionary<string, FileInfo> targetFiles)
        {
            // 对于小规模文件集合，直接返回原字典
            if (targetFiles.Count < 1000)
                return targetFiles;

            // 对于大规模文件集合，创建一个更高效的查找字典
            // 使用相同的字符串比较器确保查找时不区分大小写
            return new Dictionary<string, FileInfo>(targetFiles, StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 判断是否应该报告进度（控制报告频率）
        /// </summary>
        private bool ShouldReportProgress(int processedItems, int totalItems, ref DateTime lastReport)
        {
            var now = DateTime.Now;

            // 在以下情况报告进度:
            // 1. 处理了100个项目
            // 2. 已经过去了200毫秒
            // 3. 是首个项目或最后一个项目
            bool shouldReport = processedItems % 100 == 0 ||
                               (now - lastReport).TotalMilliseconds > 200 ||
                               processedItems == 1 ||
                               processedItems == totalItems;

            if (shouldReport)
                lastReport = now;

            return shouldReport;
        }

        /// <summary>
        /// 执行单个同步操作
        /// </summary>
        private async Task ExecuteSingleActionAsync(SyncAction action)
        {
            try
            {
                _cancellationToken.ThrowIfCancellationRequested();

                // 确定源和目标路径（考虑同步方向）
                string actualSource = action.Direction == ESyncDirection.SourceToTarget
                    ? action.SourcePath
                    : action.TargetPath;

                string actualTarget = action.Direction == ESyncDirection.SourceToTarget
                    ? action.TargetPath
                    : action.SourcePath;

                switch (action.ActionType)
                {
                    case ESyncActionType.CreateDirectory:
                        if (!Directory.Exists(actualTarget))
                        {
                            Directory.CreateDirectory(actualTarget);
                            _statistics.DirectoriesCreated++;
                        }
                        break;

                    case ESyncActionType.CopyFile:
                        await EnsureDirectoryExistsAsync(Path.GetDirectoryName(actualTarget));
                        await CopyFileWithRetryAsync(actualSource, actualTarget);
                        _statistics.FilesCopied++;
                        _statistics.BytesProcessed += action.Size;
                        break;

                    case ESyncActionType.UpdateFile:
                        await EnsureDirectoryExistsAsync(Path.GetDirectoryName(actualTarget));
                        await CopyFileWithRetryAsync(actualSource, actualTarget);
                        _statistics.FilesUpdated++;
                        _statistics.BytesProcessed += action.Size;
                        break;

                    case ESyncActionType.DeleteFile:
                        if (File.Exists(actualTarget))
                        {
                            if (_options.UseRecycleBin)
                            {
                                // 使用回收站删除文件
                                FileOperationAPIWrapper.MoveToRecycleBin(actualTarget);
                            }
                            else
                            {
                                File.Delete(actualTarget);
                            }
                            _statistics.FilesDeleted++;
                        }
                        break;

                    case ESyncActionType.DeleteDirectory:
                        if (Directory.Exists(actualTarget))
                        {
                            if (_options.UseRecycleBin)
                            {
                                // 使用回收站删除目录
                                FileOperationAPIWrapper.MoveToRecycleBin(actualTarget);
                            }
                            else
                            {
                                Directory.Delete(actualTarget, true);
                            }
                            _statistics.DirectoriesDeleted++;
                        }
                        break;

                    case ESyncActionType.RenameFile:
                        if (File.Exists(actualSource) && !File.Exists(actualTarget))
                        {
                            await EnsureDirectoryExistsAsync(Path.GetDirectoryName(actualTarget));
                            File.Move(actualSource, actualTarget);
                            _statistics.FilesRenamed++;
                        }
                        break;
                }

                action.Status = ESyncActionStatus.Completed;
            }
            catch (Exception ex)
            {
                action.Status = ESyncActionStatus.Failed;
                action.ErrorMessage = ex.Message;
                _statistics.Errors++;

                if (_options.ContinueOnError)
                {
                    // 记录错误但继续执行
                    ReportProgress($"错误: {ex.Message}", -1);
                }
                else
                {
                    // 出错时中断操作
                    throw;
                }
            }
        }

        /// <summary>
        /// 检查两个文件是否需要更新
        /// </summary>
        private bool NeedsUpdate(FileInfo sourceFile, FileInfo targetFile)
        {
            // 检查文件大小
            if (sourceFile.Length != targetFile.Length)
                return true;

            // 检查修改时间
            if (_options.CompareMethod == ESyncCompareMethod.DateTime ||
                _options.CompareMethod == ESyncCompareMethod.DateTimeAndSize)
            {
                // 使用阈值比较时间，避免因时区等问题导致的微小差异
                var timeDiff = Math.Abs((sourceFile.LastWriteTimeUtc - targetFile.LastWriteTimeUtc).TotalSeconds);
                if (timeDiff > _options.DateTimeThresholdSeconds)
                    return true;
            }

            // 检查文件内容
            if (_options.CompareMethod == ESyncCompareMethod.Content ||
                _options.CompareMethod == ESyncCompareMethod.Hash)
            {
                try
                {
                    return !FilesAreEqual(sourceFile, targetFile);
                }
                catch (Exception ex)
                {
                    Log.Error(ex, "文件比较失败: {SourceFile} vs {TargetFile}", sourceFile.FullName, targetFile.FullName);

                    // 文件比较出错，安全起见认为需要更新
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// 比较两个文件内容是否相同
        /// </summary>
        private bool FilesAreEqual(FileInfo sourceFile, FileInfo targetFile)
        {
            if (_options.CompareMethod == ESyncCompareMethod.Hash)
            {
                return CompareFileHash(sourceFile, targetFile);
            }
            else // 内容比较
            {
                return CompareFileContent(sourceFile, targetFile);
            }
        }

        /// <summary>
        /// 使用哈希算法比较文件
        /// </summary>
        private bool CompareFileHash(FileInfo sourceFile, FileInfo targetFile)
        {
            if (_options.SamplingRate > 0 && _options.SamplingRate < 1.0)
            {
                return CompareFileHashWithSampling(sourceFile, targetFile);
            }

            // 全文件哈希比较
            var hashAlgorithm = GetHashAlgorithm().ToString();

            // 计算源文件哈希
            byte[] sourceHash;
            using (var stream = sourceFile.OpenRead())
            {
                sourceHash = HashHelper.ComputeHash(stream, hashAlgorithm);
            }

            // 计算目标文件哈希
            byte[] targetHash;
            using (var stream = targetFile.OpenRead())
            {
                targetHash = HashHelper.ComputeHash(stream, hashAlgorithm);
            }

            // 比较哈希值
            return CompareHash(sourceHash, targetHash);
        }

        /// <summary>
        /// 使用抽样哈希比较文件
        /// </summary>
        private bool CompareFileHashWithSampling(FileInfo sourceFile, FileInfo targetFile)
        {
            const int headerSize = 8192; // 头部始终比较的大小
            const int randomSampleSize = 8192; // 随机抽样的块大小

            // 如果文件较小，直接全文比较
            if (sourceFile.Length < headerSize * 2)
                return CompareFileHash(sourceFile, targetFile);

            var hashAlgorithm = GetHashAlgorithm().ToString();

            using var sourceStream = sourceFile.OpenRead();
            using var targetStream = targetFile.OpenRead();

            // 比较文件头部
            byte[] sourceHeader = new byte[headerSize];
            byte[] targetHeader = new byte[headerSize];

            sourceStream.Read(sourceHeader, 0, headerSize);
            targetStream.Read(targetHeader, 0, headerSize);

            if (!CompareBytes(sourceHeader, targetHeader))
                return false;

            // 比较文件尾部
            byte[] sourceFooter = new byte[headerSize];
            byte[] targetFooter = new byte[headerSize];

            sourceStream.Seek(-headerSize, SeekOrigin.End);
            targetStream.Seek(-headerSize, SeekOrigin.End);

            sourceStream.Read(sourceFooter, 0, headerSize);
            targetStream.Read(targetFooter, 0, headerSize);

            if (!CompareBytes(sourceFooter, targetFooter))
                return false;

            // 生成随机抽样点
            Random random = new Random(sourceFile.FullName.GetHashCode());
            int samplesCount = (int)Math.Max(1, (sourceFile.Length - headerSize * 2) * _options.SamplingRate / randomSampleSize);
            long range = sourceFile.Length - headerSize * 2 - randomSampleSize;

            for (int i = 0; i < samplesCount; i++)
            {
                // 生成随机位置（避开头尾已比较的部分）
                long position = headerSize + (long)(random.NextDouble() * range);

                byte[] sourceSample = new byte[randomSampleSize];
                byte[] targetSample = new byte[randomSampleSize];

                sourceStream.Seek(position, SeekOrigin.Begin);
                targetStream.Seek(position, SeekOrigin.Begin);

                sourceStream.Read(sourceSample, 0, randomSampleSize);
                targetStream.Read(targetSample, 0, randomSampleSize);

                if (!CompareBytes(sourceSample, targetSample))
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 比较文件内容
        /// </summary>
        private bool CompareFileContent(FileInfo sourceFile, FileInfo targetFile)
        {
            const int bufferSize = 4096;

            using var sourceStream = sourceFile.OpenRead();
            using var targetStream = targetFile.OpenRead();

            byte[] sourceBuffer = new byte[bufferSize];
            byte[] targetBuffer = new byte[bufferSize];

            while (true)
            {
                int sourceBytesRead = sourceStream.Read(sourceBuffer, 0, bufferSize);
                int targetBytesRead = targetStream.Read(targetBuffer, 0, bufferSize);

                if (sourceBytesRead != targetBytesRead)
                    return false;

                if (sourceBytesRead == 0)
                    break;

                for (int i = 0; i < sourceBytesRead; i++)
                {
                    if (sourceBuffer[i] != targetBuffer[i])
                        return false;
                }
            }

            return true;
        }

        /// <summary>
        /// 扫描目录并获取文件和子目录列表
        /// </summary>
        private async Task<(Dictionary<string, FileInfo> Files, HashSet<string> Directories)> ScanDirectoryAsync(string path, string description)
        {
            ReportProgress($"正在扫描{description}...", -1);

            try
            {
                // 尝试使用 FileUltraScanner（高性能扫描器）
                var ultraScanResult = await ScanWithFileUltraScannerAsync(path);
                return ultraScanResult;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "高性能扫描器失败，切换到备用扫描器");

                ReportProgress($"高性能扫描器失败，切换到备用扫描器", -1);

                // 退回到 FileFastScanner（备用扫描器）
                var fastScanResult = await ScanWithFileFastScannerAsync(path);
                return fastScanResult;
            }
        }

        /// <summary>
        /// 使用 FileUltraScanner 扫描目录
        /// </summary>
        private async Task<(Dictionary<string, FileInfo>, HashSet<string>)> ScanWithFileUltraScannerAsync(string path)
        {
            FileUltraScanner scanner = new FileUltraScanner(
                path,
                _options.MaxParallelOperations,
                8192,
                _options.FollowSymlinks,
                _options.IgnorePatterns?.ToList()
            );

            var result = await scanner.ScanAsync(
                reportProgress: _options.Verbose,
                cancellationToken: _cancellationToken
            );

            var files = result.Files
                .ToDictionary(
                    f => f,
                    f => new FileInfo(f),
                    StringComparer.OrdinalIgnoreCase
                );

            var directories = new HashSet<string>(result.Directories, StringComparer.OrdinalIgnoreCase);

            return (files, directories);
        }

        /// <summary>
        /// 使用 FileFastScanner 扫描目录
        /// </summary>
        private async Task<(Dictionary<string, FileInfo>, HashSet<string>)> ScanWithFileFastScannerAsync(string path)
        {
            var scanner = new FileFastScanner();
            var result = scanner.ScanAsync(
                path,
                "*",
                _options.IgnorePatterns,
                _options.MaxParallelOperations,
                reportProgress: _options.Verbose,
                cancellationToken: _cancellationToken
            );

            var files = result.Files
                .ToDictionary(
                    f => f,
                    f => new FileInfo(f),
                    StringComparer.OrdinalIgnoreCase
                );

            var directories = new HashSet<string>(result.Directories, StringComparer.OrdinalIgnoreCase);

            return (files, directories);
        }

        /// <summary>
        /// 解析冲突
        /// </summary>
        private ESyncConflictResolution ResolveConflict(FileInfo sourceFile, FileInfo targetFile)
        {
            switch (_options.ConflictResolution)
            {
                case ESyncConflictResolution.SourceWins:
                    return ESyncConflictResolution.SourceWins;

                case ESyncConflictResolution.TargetWins:
                    return ESyncConflictResolution.TargetWins;

                case ESyncConflictResolution.KeepBoth:
                    return ESyncConflictResolution.KeepBoth;

                case ESyncConflictResolution.Skip:
                    return ESyncConflictResolution.Skip;

                case ESyncConflictResolution.Newer:
                    return sourceFile.LastWriteTimeUtc > targetFile.LastWriteTimeUtc
                        ? ESyncConflictResolution.SourceWins
                        : ESyncConflictResolution.TargetWins;

                case ESyncConflictResolution.Older:
                    return sourceFile.LastWriteTimeUtc < targetFile.LastWriteTimeUtc
                        ? ESyncConflictResolution.SourceWins
                        : ESyncConflictResolution.TargetWins;

                case ESyncConflictResolution.Larger:
                    return sourceFile.Length > targetFile.Length
                        ? ESyncConflictResolution.SourceWins
                        : ESyncConflictResolution.TargetWins;

                default:
                    return ESyncConflictResolution.Skip;
            }
        }

        /// <summary>
        /// 获取用于解决冲突的新文件名
        /// </summary>
        private string GetConflictFileName(string originalPath)
        {
            string dir = Path.GetDirectoryName(originalPath);
            string fileName = Path.GetFileNameWithoutExtension(originalPath);
            string ext = Path.GetExtension(originalPath);
            string timestamp = DateTime.Now.ToString("yyyyMMdd_HHmmss");

            return Path.Combine(dir, $"{fileName} ({timestamp}){ext}");
        }

        /// <summary>
        /// 获取操作优先级（用于排序）
        /// </summary>
        private int GetActionPriority(ESyncActionType actionType)
        {
            switch (actionType)
            {
                case ESyncActionType.CreateDirectory:
                    return 1;

                case ESyncActionType.CopyFile:
                case ESyncActionType.UpdateFile:
                    return 2;

                case ESyncActionType.RenameFile:
                    return 3;

                case ESyncActionType.DeleteFile:
                    return 4;

                case ESyncActionType.DeleteDirectory:
                    return 5;

                default:
                    return 99;
            }
        }

        /// <summary>
        /// 获取操作类型描述
        /// </summary>
        private string GetActionTypeDescription(ESyncActionType actionType)
        {
            switch (actionType)
            {
                case ESyncActionType.CreateDirectory:
                    return "创建目录";

                case ESyncActionType.CopyFile:
                    return "复制文件";

                case ESyncActionType.UpdateFile:
                    return "更新文件";

                case ESyncActionType.DeleteFile:
                    return "删除文件";

                case ESyncActionType.DeleteDirectory:
                    return "删除目录";

                case ESyncActionType.RenameFile:
                    return "重命名文件";

                default:
                    return "未知操作";
            }
        }

        /// <summary>
        /// 获取文件的相对路径
        /// </summary>
        private string GetRelativePath(string fullPath, string basePath)
        {
            // 确保路径以分隔符结尾以正确处理相对路径
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
                basePath += Path.DirectorySeparatorChar;

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                var relativePath = fullPath.Substring(basePath.Length);
                return relativePath;
            }

            return fullPath;
        }

        /// <summary>
        /// 确保目录存在
        /// </summary>
        private Task EnsureDirectoryExistsAsync(string directory)
        {
            if (!Directory.Exists(directory))
            {
                Directory.CreateDirectory(directory);
            }
            return Task.CompletedTask;
        }

        /// <summary>
        /// 创建哈希算法实例
        /// </summary>
        private EHashType? GetHashAlgorithm()
        {
            return _options.HashAlgorithm;
        }

        /// <summary>
        /// 比较两个哈希值是否相同
        /// </summary>
        private bool CompareHash(byte[] hash1, byte[] hash2)
        {
            if (hash1.Length != hash2.Length)
                return false;

            for (int i = 0; i < hash1.Length; i++)
            {
                if (hash1[i] != hash2[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 比较两个字节数组是否相同
        /// </summary>
        private bool CompareBytes(byte[] buffer1, byte[] buffer2)
        {
            if (buffer1.Length != buffer2.Length)
                return false;

            for (int i = 0; i < buffer1.Length; i++)
            {
                if (buffer1[i] != buffer2[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 带重试的文件复制操作，支持分块传输和校验
        /// </summary>
        private async Task CopyFileWithRetryAsync(string sourcePath, string targetPath)
        {
            int maxRetries = _options.MaxRetries;
            int currentRetry = 0;
            bool success = false;
            bool useChunkedTransfer = _options.ChunkSizeMB > 0;

            // 确保目标目录存在
            string targetDir = Path.GetDirectoryName(targetPath);
            if (!Directory.Exists(targetDir))
            {
                Directory.CreateDirectory(targetDir);
            }

            // 临时文件路径
            string tempTargetPath = targetPath + _options.TempFileSuffix;

            while (!success && currentRetry <= maxRetries)
            {
                try
                {
                    if (currentRetry > 0)
                    {
                        // 添加延迟重试
                        await Task.Delay(TimeSpan.FromSeconds(Math.Pow(2, currentRetry - 1)), _cancellationToken);
                        ReportProgress($"重试复制文件 {Path.GetFileName(sourcePath)} (尝试 {currentRetry}/{maxRetries})", -1);
                    }

                    if (useChunkedTransfer)
                    {
                        await CopyFileWithChunksAsync(sourcePath, tempTargetPath, targetPath);
                    }
                    else
                    {
                        await CopyFileStandardAsync(sourcePath, tempTargetPath, targetPath);
                    }

                    // 验证复制的文件完整性
                    if (_options.VerifyAfterCopy)
                    {
                        if (!await VerifyFileIntegrityAsync(sourcePath, targetPath))
                        {
                            throw new IOException($"文件完整性验证失败: {sourcePath} -> {targetPath}");
                        }
                    }

                    // 同步文件的最后修改时间
                    if (_options.SyncLastModifiedTime)
                    {
                        File.SetCreationTimeUtc(targetPath, File.GetCreationTimeUtc(sourcePath));
                        File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));

                        // 不同步访问时间
                        //File.SetLastAccessTimeUtc(targetPath, File.GetLastAccessTimeUtc(sourcePath));
                    }

                    success = true;
                }
                catch (IOException iex) when (currentRetry < maxRetries)
                {
                    Log.Warning(iex, "复制文件时出现IO异常: {Message}, 将重试操作", iex.Message);

                    currentRetry++;

                    // 清理可能损坏的临时文件
                    if (File.Exists(tempTargetPath))
                    {
                        try { File.Delete(tempTargetPath); } catch { }
                    }
                }
                catch (Exception ex)
                {
                    // 清理临时文件
                    if (File.Exists(tempTargetPath))
                    {
                        try { File.Delete(tempTargetPath); } catch { }
                    }

                    // 记录错误并重新抛出
                    Log.Error(ex, $"复制文件失败: {sourcePath} -> {targetPath}");
                    throw;
                }
            }

            if (!success)
            {
                throw new IOException($"复制文件 {sourcePath} 到 {targetPath} 失败，已重试 {maxRetries} 次");
            }
        }

        /// <summary>
        /// 标准文件复制方法
        /// </summary>
        private async Task CopyFileStandardAsync(string sourcePath, string tempTargetPath, string targetPath)
        {
            if (_options.PreserveFileTime)
            {
                using (FileStream sourceStream = new FileStream(sourcePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                using (FileStream targetStream = new FileStream(tempTargetPath, FileMode.Create, System.IO.FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
                {
                    await sourceStream.CopyToAsync(targetStream, 81920, _cancellationToken);
                }

                // 完成后，将临时文件重命名为目标文件
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }
                File.Move(tempTargetPath, targetPath);
            }
            else
            {
                // 简单复制
                File.Copy(sourcePath, targetPath, true);
            }
        }

        /// <summary>
        /// 分块文件复制方法（支持断点续传和分块校验）
        /// </summary>
        private async Task CopyFileWithChunksAsync(string sourcePath, string tempTargetPath, string targetPath)
        {
            // 获取源文件信息
            var sourceFileInfo = new FileInfo(sourcePath);
            long fileSize = sourceFileInfo.Length;

            // 计算分块大小和分块数量
            int chunkSizeBytes = _options.ChunkSizeMB * 1024 * 1024;
            int chunksCount = (int)Math.Ceiling((double)fileSize / chunkSizeBytes);

            // 设置使用的哈希算法 - 优先使用用户配置的算法，否则默认使用SHA256
            string hashAlgorithm = _options.HashAlgorithm?.ToString() ?? "SHA256";
            Log.Debug($"使用 {hashAlgorithm} 算法进行分块校验");

            // 记录分块状态
            bool[] chunkCompleted = new bool[chunksCount];
            var chunkTempFiles = new ConcurrentDictionary<int, string>();

            try
            {
                // 检查之前的块是否已经存在并且有效
                if (chunksCount > 1)
                {
                    Log.Debug($"检查分块文件是否可复用，共 {chunksCount} 个分块");

                    int reusedChunks = 0;

                    for (int i = 0; i < chunksCount; i++)
                    {
                        string chunkPath = $"{targetPath}.part{i}{_options.TempFileSuffix}";
                        if (File.Exists(chunkPath))
                        {
                            // 验证块的完整性
                            ReportProgress($"验证分块 {i + 1}/{chunksCount} 的完整性", -1);
                            bool chunkValid = await VerifyChunkIntegrityAsync(sourcePath, chunkPath, i, chunkSizeBytes, hashAlgorithm);
                            chunkCompleted[i] = chunkValid;

                            if (chunkValid)
                            {
                                // 如果块有效，记录到字典中
                                chunkTempFiles[i] = chunkPath;
                                reusedChunks++;
                                ReportProgress($"分块 {i + 1}/{chunksCount} 校验通过，可复用", -1);
                            }
                            else if (File.Exists(chunkPath))
                            {
                                // 如果块无效，删除它
                                try
                                {
                                    File.Delete(chunkPath);
                                    ReportProgress($"分块 {i + 1}/{chunksCount} 校验失败，已删除", -1);
                                }
                                catch (Exception ex)
                                {
                                    Log.Warning(ex, $"删除无效分块文件失败: {chunkPath}");
                                }
                            }
                        }
                    }

                    if (reusedChunks > 0)
                    {
                        Log.Information($"成功复用 {reusedChunks}/{chunksCount} 个分块文件，断点续传");
                    }
                }

                // 如果是小文件或者只有一个块，使用标准复制方法
                if (chunksCount <= 1 || fileSize <= chunkSizeBytes)
                {
                    await CopyFileStandardAsync(sourcePath, tempTargetPath, targetPath);
                    return;
                }

                // 创建目标临时文件
                using (var targetStream = new FileStream(tempTargetPath, FileMode.Create, System.IO.FileAccess.Write))
                {
                    // 调整文件大小以预分配空间
                    targetStream.SetLength(fileSize);
                }

                // 创建任务列表
                var tasks = new List<Task>();

                // 使用内存中的信号量控制并行度
                int maxParallelChunks = Math.Min(_options.MaxParallelOperations, 4);
                using (var semaphore = new SemaphoreSlim(maxParallelChunks))
                {
                    // 处理每个块
                    for (int i = 0; i < chunksCount; i++)
                    {
                        int chunkIndex = i;

                        // 如果这个块已经完成，跳过它
                        if (chunkCompleted[chunkIndex])
                        {
                            ReportProgress($"跳过已完成的块 {chunkIndex + 1}/{chunksCount} - {Path.GetFileName(sourcePath)}", -1);
                            continue;
                        }

                        // 等待信号量
                        await semaphore.WaitAsync(_cancellationToken);

                        // 为每个块创建唯一的临时文件路径
                        string chunkTempPath = $"{targetPath}.part{chunkIndex}{_options.TempFileSuffix}";

                        tasks.Add(Task.Run(async () =>
                        {
                            try
                            {
                                await ProcessChunkAsync(sourcePath, chunkTempPath, chunkIndex, chunkSizeBytes, fileSize);
                                chunkTempFiles[chunkIndex] = chunkTempPath;
                            }
                            finally
                            {
                                semaphore.Release();
                            }
                        }, _cancellationToken));
                    }

                    // 等待所有任务完成
                    await Task.WhenAll(tasks);
                }

                // 所有块处理完成后，将块合并到临时文件
                ReportProgress($"所有分块处理完成，开始合并文件", -1);

                using (var targetStream = new FileStream(tempTargetPath, FileMode.Open, FileAccess.Write, FileShare.None))
                {
                    for (int i = 0; i < chunksCount; i++)
                    {
                        if (chunkTempFiles.TryGetValue(i, out string chunkPath) && File.Exists(chunkPath))
                        {
                            long startPosition = (long)i * chunkSizeBytes;
                            targetStream.Position = startPosition;

                            using (var chunkStream = new FileStream(chunkPath, FileMode.Open, FileAccess.Read, FileShare.Read))
                            {
                                await chunkStream.CopyToAsync(targetStream, 81920, _cancellationToken);
                            }
                        }
                        else
                        {
                            // 如果缺少块，抛出异常
                            throw new IOException($"缺少块 {i}/{chunksCount} 用于文件 {targetPath}");
                        }
                    }
                }

                // 最后进行完整性验证
                if (_options.VerifyAfterCopy)
                {
                    ReportProgress($"正在验证合并后的文件完整性: {Path.GetFileName(targetPath)}", -1);
                    if (!await VerifyFileIntegrityAsync(sourcePath, tempTargetPath))
                    {
                        throw new IOException($"文件完整性验证失败: {sourcePath} -> {tempTargetPath}");
                    }
                }

                // 最后将临时文件重命名为目标文件
                if (File.Exists(targetPath))
                {
                    File.Delete(targetPath);
                }

                File.Move(tempTargetPath, targetPath);
                ReportProgress($"文件 {Path.GetFileName(targetPath)} 传输完成", -1);
            }
            finally
            {
                // 清理临时块文件 - 如成功则清理，否则保留以便下次续传
                if (File.Exists(targetPath))
                {
                    foreach (var chunkPath in chunkTempFiles.Values)
                    {
                        try
                        {
                            if (File.Exists(chunkPath))
                            {
                                File.Delete(chunkPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            Log.Warning(ex, $"删除临时分块文件失败: {chunkPath}");
                        }
                    }
                }
            }
        }

        /// <summary>
        /// 验证文件块的完整性 - 支持自定义哈希算法
        /// </summary>
        private async Task<bool> VerifyChunkIntegrityAsync(string sourcePath, string chunkPath, int chunkIndex, int chunkSizeBytes, string hashAlgorithm = "SHA256")
        {
            if (!File.Exists(chunkPath))
                return false;

            var chunkInfo = new FileInfo(chunkPath);
            var sourceInfo = new FileInfo(sourcePath);

            long startPosition = (long)chunkIndex * chunkSizeBytes;
            long endPosition = Math.Min(startPosition + chunkSizeBytes, sourceInfo.Length);
            int expectedChunkSize = (int)(endPosition - startPosition);

            // 首先检查大小
            if (chunkInfo.Length != expectedChunkSize)
            {
                Log.Debug($"分块大小不匹配: 预期 {expectedChunkSize} 字节, 实际 {chunkInfo.Length} 字节");
                return false;
            }

            try
            {
                // 使用指定的哈希算法计算源文件分块的哈希值
                byte[] sourceChunkHash;
                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
                {
                    sourceStream.Position = startPosition;
                    byte[] buffer = new byte[expectedChunkSize];
                    await sourceStream.ReadAsync(buffer, 0, expectedChunkSize, _cancellationToken);
                    sourceChunkHash = HashHelper.ComputeHash(buffer, hashAlgorithm);
                }

                // 计算目标分块文件的哈希值
                byte[] targetChunkHash;
                using (var chunkStream = new FileStream(chunkPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
                {
                    byte[] buffer = new byte[expectedChunkSize];
                    await chunkStream.ReadAsync(buffer, 0, expectedChunkSize, _cancellationToken);
                    targetChunkHash = HashHelper.ComputeHash(buffer, hashAlgorithm);
                }

                // 比较哈希值
                return CompareHash(sourceChunkHash, targetChunkHash);
            }
            catch (Exception ex)
            {
                Log.Warning(ex, $"验证分块时发生错误: {chunkPath}");
                return false;
            }
        }

        /// <summary>
        /// 处理单个文件块
        /// </summary>
        private async Task ProcessChunkAsync(string sourcePath, string chunkTempPath, int chunkIndex, int chunkSizeBytes, long fileSize)
        {
            long startPosition = (long)chunkIndex * chunkSizeBytes;
            long endPosition = Math.Min(startPosition + chunkSizeBytes, fileSize);
            int currentChunkSize = (int)(endPosition - startPosition);

            try
            {
                // 从源文件读取指定块的数据到内存
                byte[] buffer = new byte[currentChunkSize];

                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read))
                {
                    sourceStream.Position = startPosition;
                    await sourceStream.ReadAsync(buffer, 0, currentChunkSize, _cancellationToken);
                }

                // 确保目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(chunkTempPath));

                // 将数据直接写入块临时文件
                using (var chunkStream = new FileStream(chunkTempPath, FileMode.Create, FileAccess.Write, FileShare.None))
                {
                    await chunkStream.WriteAsync(buffer, 0, currentChunkSize, _cancellationToken);
                    await chunkStream.FlushAsync(_cancellationToken);
                }

                // 报告进度
                ReportProgress($"处理块 {chunkIndex + 1}/{Math.Ceiling((double)fileSize / chunkSizeBytes)} - " +
                              $"{Path.GetFileName(sourcePath)} - 100%", -1);
            }
            catch (Exception ex)
            {
                Log.Error(ex, $"处理文件块失败: {sourcePath}, 块 {chunkIndex}");
                throw;
            }
        }

        /// <summary>
        /// 验证文件完整性
        /// </summary>
        private async Task<bool> VerifyFileIntegrityAsync(string sourcePath, string targetPath)
        {
            // 如果使用哈希比较方法，我们可以利用现有哈希计算函数
            if (_options.CompareMethod == ESyncCompareMethod.Hash && _options.HashAlgorithm.HasValue)
            {
                using (var sourceStream = new FileStream(sourcePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
                using (var targetStream = new FileStream(targetPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
                {
                    string algorithm = _options.HashAlgorithm.ToString();
                    byte[] sourceHash = HashHelper.ComputeHash(sourceStream, algorithm);
                    byte[] targetHash = HashHelper.ComputeHash(targetStream, algorithm);

                    return CompareHash(sourceHash, targetHash);
                }
            }
            else
            {
                // 对于其他比较方法，执行标准比较
                var sourceInfo = new FileInfo(sourcePath);
                var targetInfo = new FileInfo(targetPath);

                // 首先比较大小
                if (sourceInfo.Length != targetInfo.Length)
                    return false;

                // 如果文件很小，直接比较内容
                if (sourceInfo.Length < 1024 * 1024) // 小于1MB
                {
                    return await CompareFileContentCompletely(sourcePath, targetPath);
                }

                // 对于大文件，使用抽样比较
                return await CompareFileContentSampling(sourcePath, targetPath);
            }
        }

        /// <summary>
        /// 完全比较两个文件的内容
        /// </summary>
        private async Task<bool> CompareFileContentCompletely(string file1, string file2)
        {
            const int bufferSize = 4096;

            using (var stream1 = new FileStream(file1, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
            using (var stream2 = new FileStream(file2, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read, bufferSize, FileOptions.SequentialScan))
            {
                byte[] buffer1 = new byte[bufferSize];
                byte[] buffer2 = new byte[bufferSize];

                while (true)
                {
                    int count1 = await stream1.ReadAsync(buffer1, 0, bufferSize);
                    int count2 = await stream2.ReadAsync(buffer2, 0, bufferSize);

                    if (count1 != count2)
                        return false;

                    if (count1 == 0)
                        return true;

                    for (int i = 0; i < count1; i++)
                    {
                        if (buffer1[i] != buffer2[i])
                            return false;
                    }
                }
            }
        }

        /// <summary>
        /// 使用抽样比较两个文件的内容
        /// </summary>
        private async Task<bool> CompareFileContentSampling(string file1, string file2)
        {
            const int sampleSize = 8192;
            const double samplingRate = 0.01; // 抽样比例1%

            using (var stream1 = new FileStream(file1, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
            using (var stream2 = new FileStream(file2, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
            {
                long fileSize = stream1.Length;

                // 始终比较文件头部和尾部
                if (!await CompareFileRegion(stream1, stream2, 0, (int)Math.Min(sampleSize, fileSize)))
                    return false;

                if (fileSize > sampleSize)
                {
                    if (!await CompareFileRegion(stream1, stream2, fileSize - sampleSize, sampleSize))
                        return false;

                    // 随机抽样比较中间部分
                    Random random = new Random(fileSize.GetHashCode());
                    int numSamples = (int)Math.Max(1, (fileSize * samplingRate) / sampleSize);

                    for (int i = 0; i < numSamples; i++)
                    {
                        long position = sampleSize + (long)(random.NextDouble() * (fileSize - sampleSize * 2));
                        if (!await CompareFileRegion(stream1, stream2, position, sampleSize))
                            return false;
                    }
                }

                return true;
            }
        }

        /// <summary>
        /// 比较两个文件流指定区域的内容
        /// </summary>
        private async Task<bool> CompareFileRegion(Stream stream1, Stream stream2, long position, int length)
        {
            byte[] buffer1 = new byte[length];
            byte[] buffer2 = new byte[length];

            stream1.Position = position;
            stream2.Position = position;

            await stream1.ReadAsync(buffer1, 0, length);
            await stream2.ReadAsync(buffer2, 0, length);

            for (int i = 0; i < length; i++)
            {
                if (buffer1[i] != buffer2[i])
                    return false;
            }

            return true;
        }

        /// <summary>
        /// 验证文件块的完整性
        /// </summary>
        private async Task<bool> VerifyChunkIntegrityAsync(string sourcePath, string chunkPath, int chunkIndex, int chunkSizeBytes)
        {
            if (!File.Exists(chunkPath))
                return false;

            var chunkInfo = new FileInfo(chunkPath);
            var sourceInfo = new FileInfo(sourcePath);

            long startPosition = (long)chunkIndex * chunkSizeBytes;
            long endPosition = Math.Min(startPosition + chunkSizeBytes, sourceInfo.Length);
            int expectedChunkSize = (int)(endPosition - startPosition);

            // 首先检查大小
            if (chunkInfo.Length != expectedChunkSize)
                return false;

            // 然后比较内容抽样
            using (var sourceStream = new FileStream(sourcePath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
            using (var chunkStream = new FileStream(chunkPath, FileMode.Open, System.IO.FileAccess.Read, FileShare.Read))
            {
                sourceStream.Position = startPosition;

                // 对于小块，直接完整比较
                if (expectedChunkSize < 1024 * 1024) // 小于1MB
                {
                    byte[] sourceBuffer = new byte[expectedChunkSize];
                    byte[] chunkBuffer = new byte[expectedChunkSize];

                    await sourceStream.ReadAsync(sourceBuffer, 0, expectedChunkSize);
                    await chunkStream.ReadAsync(chunkBuffer, 0, expectedChunkSize);

                    return CompareBytes(sourceBuffer, chunkBuffer);
                }

                // 对于大块，使用抽样比较
                const int sampleSize = 8192;

                // 比较开始、中间和结束位置
                if (!await CompareStreamRegion(sourceStream, chunkStream, 0, sampleSize))
                    return false;

                if (!await CompareStreamRegion(sourceStream, chunkStream, expectedChunkSize / 2, sampleSize))
                    return false;

                if (!await CompareStreamRegion(sourceStream, chunkStream, expectedChunkSize - sampleSize, sampleSize))
                    return false;

                return true;
            }
        }

        /// <summary>
        /// 比较两个流指定区域的内容
        /// </summary>
        private async Task<bool> CompareStreamRegion(Stream stream1, Stream stream2, long relativePosition, int length)
        {
            long originalPosition1 = stream1.Position;
            long originalPosition2 = stream2.Position;

            try
            {
                stream1.Position = originalPosition1 + relativePosition;
                stream2.Position = relativePosition; // 第二个流从0开始

                byte[] buffer1 = new byte[length];
                byte[] buffer2 = new byte[length];

                int read1 = await stream1.ReadAsync(buffer1, 0, length);
                int read2 = await stream2.ReadAsync(buffer2, 0, length);

                if (read1 != read2)
                    return false;

                for (int i = 0; i < read1; i++)
                {
                    if (buffer1[i] != buffer2[i])
                        return false;
                }

                return true;
            }
            finally
            {
                // 恢复原始位置
                stream1.Position = originalPosition1;
                stream2.Position = originalPosition2;
            }
        }

        /// <summary>
        /// 计算进度百分比
        /// </summary>
        private int CalculateProgress(int current, int total)
        {
            if (total <= 0) return 0;
            return (int)((current / (double)total) * 100);
        }

        /// <summary>
        /// 报告进度
        /// </summary>
        private void ReportProgress(string message, int progressPercentage)
        {
            if (_progress == null)
            {
                return;
            }

            // 控制进度更新频率
            var now = DateTime.Now;
            if ((now - _lastProgressUpdate).TotalMilliseconds < 100 && progressPercentage >= 0 && progressPercentage < 100)
                return;

            _lastProgressUpdate = now;

            double bytesPerSecond = _stopwatch.Elapsed.TotalSeconds > 0
                ? _statistics.BytesProcessed / _stopwatch.Elapsed.TotalSeconds
                : 0;

            double itemsPerSecond = _stopwatch.Elapsed.TotalSeconds > 0
                ? _processedItems / _stopwatch.Elapsed.TotalSeconds
                : 0;

            _progress.Report(new SyncProgress
            {
                Message = message,
                ProgressPercentage = progressPercentage,
                ElapsedTime = _stopwatch.Elapsed,
                ProcessedItems = _processedItems,
                TotalItems = _totalItems,
                BytesProcessed = _statistics.BytesProcessed,
                BytesToProcess = _statistics.BytesToProcess,
                BytesPerSecond = bytesPerSecond,
                ItemsPerSecond = itemsPerSecond,
                Statistics = _statistics
            });
        }

        /// <summary>
        /// 验证同步路径
        /// </summary>
        private void ValidatePaths()
        {
            // 验证源路径
            if (string.IsNullOrEmpty(_options.SourcePath))
                throw new ArgumentException("源路径不能为空");

            if (!Directory.Exists(_options.SourcePath))
                throw new DirectoryNotFoundException($"源目录不存在: {_options.SourcePath}");

            // 验证目标路径
            if (string.IsNullOrEmpty(_options.TargetPath))
                throw new ArgumentException("目标路径不能为空");

            // 目标路径可能不存在，需要创建
            if (!Directory.Exists(_options.TargetPath) && !_options.PreviewOnly)
            {
                Directory.CreateDirectory(_options.TargetPath);
            }

            // 检查路径不能互相包含
            string normalizedSource = Path.GetFullPath(_options.SourcePath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
            string normalizedTarget = Path.GetFullPath(_options.TargetPath).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;

            if (normalizedSource.StartsWith(normalizedTarget, StringComparison.OrdinalIgnoreCase) ||
                normalizedTarget.StartsWith(normalizedSource, StringComparison.OrdinalIgnoreCase))
            {
                throw new ArgumentException("源路径和目标路径不能互相包含");
            }
        }

        /// <summary>
        /// 判断操作是否为文件操作
        /// </summary>
        private bool IsFileOperation(ESyncActionType actionType)
        {
            return actionType == ESyncActionType.CopyFile ||
                   actionType == ESyncActionType.UpdateFile ||
                   actionType == ESyncActionType.RenameFile ||
                   actionType == ESyncActionType.DeleteFile;
        }

        /// <summary>
        /// 从JSON配置文件加载同步选项
        /// </summary>
        public static SyncOptions LoadFromJsonFile(string configFilePath)
        {
            if (!File.Exists(configFilePath))
                throw new FileNotFoundException($"配置文件不存在: {configFilePath}");

            string json = File.ReadAllText(configFilePath);
            var options = JsonSerializer.Deserialize<SyncOptions>(json, new JsonSerializerOptions
            {
                PropertyNameCaseInsensitive = true,
                Converters = { new JsonStringEnumConverter() }
            });

            return options;
        }

        /// <summary>
        /// 保存同步选项到JSON配置文件
        /// </summary>
        public static void SaveToJsonFile(SyncOptions options, string configFilePath)
        {
            string json = JsonSerializer.Serialize(options, new JsonSerializerOptions
            {
                WriteIndented = true,
                Converters = { new JsonStringEnumConverter() }
            });

            File.WriteAllText(configFilePath, json);
        }
    }

    /// <summary>
    /// 同步选项类
    /// </summary>
    public class SyncOptions
    {
        /// <summary>
        /// 源目录路径
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// 目标目录路径
        /// </summary>
        public string TargetPath { get; set; }

        /// <summary>
        /// 同步模式
        /// </summary>
        public ESyncMode SyncMode { get; set; } = ESyncMode.OneWay;

        /// <summary>
        /// 文件比较方法
        /// </summary>
        public ESyncCompareMethod CompareMethod { get; set; } = ESyncCompareMethod.DateTimeAndSize;

        /// <summary>
        /// 哈希算法类型
        /// </summary>
        public EHashType? HashAlgorithm { get; set; }

        /// <summary>
        /// 哈希抽样率（0.0-1.0）
        /// </summary>
        public double SamplingRate { get; set; } = 0.1;

        /// <summary>
        /// 最大并行操作数
        /// </summary>
        public int MaxParallelOperations { get; set; } = Math.Max(1, Environment.ProcessorCount);

        /// <summary>
        /// 是否启用并行文件操作
        /// </summary>
        public bool EnableParallelFileOperations { get; set; } = true;

        /// <summary>
        /// 日期时间比较阈值（秒）
        /// </summary>
        public int DateTimeThresholdSeconds { get; set; } = 0;

        /// <summary>
        /// 是否保留原始文件时间
        /// </summary>
        public bool PreserveFileTime { get; set; } = true;

        /// <summary>
        /// 是否使用回收站删除文件
        /// </summary>
        public bool UseRecycleBin { get; set; } = true;

        /// <summary>
        /// 冲突解决策略
        /// </summary>
        public ESyncConflictResolution ConflictResolution { get; set; } = ESyncConflictResolution.Newer;

        /// <summary>
        /// 是否跟踪符号链接
        /// </summary>
        public bool FollowSymlinks { get; set; } = false;

        /// <summary>
        /// 是否仅预览，不执行实际操作
        /// </summary>
        public bool PreviewOnly { get; set; } = false;

        /// <summary>
        /// 是否在出错时继续
        /// </summary>
        public bool ContinueOnError { get; set; } = true;

        /// <summary>
        /// 最大重试次数
        /// </summary>
        public int MaxRetries { get; set; } = 3;

        /// <summary>
        /// 是否启用详细输出
        /// </summary>
        public bool Verbose { get; set; } = false;

        /// <summary>
        /// 要忽略的文件/目录模式
        /// </summary>
        public List<string> IgnorePatterns { get; set; } = new List<string>
        {
            "**/System Volume Information/**",
            "**/$RECYCLE.BIN/**",
            "**/Thumbs.db",
            "**/*.tmp",
            "**/*.temp",
            "**/*.bak",
            "**/@Recycle/**",
            "**/@Recently-Snapshot/**",
            "**/.@__thumb/**",
            "**/@Transcode/**",
            "**/.obsidian/**",
            "**/.git/**",
            "**/.svn/**",
            "**/node_modules/**"
        };

        /// <summary>
        /// 定时同步周期，单位秒
        /// </summary>
        public int Interval { get; set; }

        /// <summary>
        /// Cron 表达式，设置后将优先使用 Cron 表达式进行调度
        /// </summary>
        public string CronExpression { get; set; }

        /// <summary>
        /// 定时任务时，是否立即执行一次
        /// </summary>
        public bool ExecuteImmediately { get; set; } = true;

        /// <summary>
        /// 文件同步分块大小（MB），当值大于0时启用分块传输
        /// 分块传输支持断点续传，适用于大文件传输，对大文件采用固定分割，并对比 hash 的方式进行完整性校验，如果某个块一致，则不再传输
        /// 当需要传输的分块完成后，最后再合并到临时文件 .mdrivetmp 中
        /// 分块默认后缀名格式为 {文件名}.part{分块序号}.mdrivetmp
        /// </summary>
        public int ChunkSizeMB { get; set; } = 0;

        /// <summary>
        /// 同步完成后是否同步文件的最后修改时间
        /// </summary>
        public bool SyncLastModifiedTime { get; set; } = true;

        /// <summary>
        /// 临时文件后缀，用于标识传输中的文件。默认为.mdrivetmp
        /// </summary>
        public string TempFileSuffix { get; set; } = ".mdrivetmp";

        /// <summary>
        /// 文件传输完成后验证文件完整性
        /// </summary>
        public bool VerifyAfterCopy { get; set; } = true;

        /// <summary>
        /// 源存储提供者类型（用于跨服务同步）
        /// </summary>
        public StorageProviderType SourceProviderType { get; set; } = StorageProviderType.Local;

        /// <summary>
        /// 源存储提供者配置（用于跨服务同步）
        /// </summary>
        public StorageProviderOptions SourceProviderOptions { get; set; }

        /// <summary>
        /// 目标存储提供者类型（用于跨服务同步）
        /// </summary>
        public StorageProviderType TargetProviderType { get; set; } = StorageProviderType.Local;

        /// <summary>
        /// 目标存储提供者配置（用于跨服务同步）
        /// </summary>
        public StorageProviderOptions TargetProviderOptions { get; set; }
    }

    /// <summary>
    /// 存储提供者类型
    /// </summary>
    public enum StorageProviderType
    {
        /// <summary>
        /// 本地文件系统
        /// </summary>
        Local,

        /// <summary>
        /// FTP服务器
        /// </summary>
        Ftp,

        /// <summary>
        /// SFTP服务器
        /// </summary>
        Sftp,

        /// <summary>
        /// WebDAV服务器
        /// </summary>
        WebDav,

        /// <summary>
        /// 阿里云盘
        /// </summary>
        AliyunDrive,

        /// <summary>
        /// 阿里云OSS
        /// </summary>
        AliyunOSS,

        /// <summary>
        /// 腾讯云COS
        /// </summary>
        TencentCOS,

        /// <summary>
        /// AWS S3
        /// </summary>
        S3,

        /// <summary>
        /// SMB/CIFS共享
        /// </summary>
        SMB,

        /// <summary>
        /// Google Drive
        /// </summary>
        GoogleDrive,

        /// <summary>
        /// OneDrive
        /// </summary>
        OneDrive,

        /// <summary>
        /// Dropbox
        /// </summary>
        Dropbox,

        /// <summary>
        /// 百度网盘
        /// </summary>
        BaiduPan,

        /// <summary>
        /// 自定义HTTP API
        /// </summary>
        CustomApi
    }

    /// <summary>
    /// 存储提供者选项基类
    /// </summary>
    [JsonDerivedType(typeof(FtpProviderOptions), typeDiscriminator: "Ftp")]
    [JsonDerivedType(typeof(SftpProviderOptions), typeDiscriminator: "Sftp")]
    [JsonDerivedType(typeof(WebDavProviderOptions), typeDiscriminator: "WebDav")]
    [JsonDerivedType(typeof(AliyunDriveProviderOptions), typeDiscriminator: "AliyunDrive")]
    [JsonDerivedType(typeof(S3ProviderOptions), typeDiscriminator: "S3")]
    [JsonDerivedType(typeof(SmbProviderOptions), typeDiscriminator: "SMB")]
    public abstract class StorageProviderOptions
    {
        /// <summary>
        /// 连接超时（秒）
        /// </summary>
        public int ConnectionTimeout { get; set; } = 30;

        /// <summary>
        /// 操作超时（秒）
        /// </summary>
        public int OperationTimeout { get; set; } = 300;

        /// <summary>
        /// 重试次数
        /// </summary>
        public int RetryCount { get; set; } = 3;

        /// <summary>
        /// 重试间隔（秒）
        /// </summary>
        public int RetryInterval { get; set; } = 5;

        /// <summary>
        /// 初始重试延迟（秒）
        /// </summary>
        public int InitialRetryDelay { get; set; } = 1;

        /// <summary>
        /// 最大重试延迟（秒）
        /// </summary>
        public int MaxRetryDelay { get; set; } = 60;

        /// <summary>
        /// 代理服务器地址
        /// </summary>
        public string ProxyAddress { get; set; }

        /// <summary>
        /// 使用的代理类型
        /// </summary>
        public ProxyType? ProxyType { get; set; }
    }

    /// <summary>
    /// FTP提供者选项
    /// </summary>
    public class FtpProviderOptions : StorageProviderOptions
    {
        /// <summary>
        /// 服务器地址
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; } = 21;

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 是否使用被动模式
        /// </summary>
        public bool UsePassive { get; set; } = true;

        /// <summary>
        /// 是否使用FTPS（加密）
        /// </summary>
        public bool UseFTPS { get; set; } = false;

        /// <summary>
        /// 是否使用显式SSL
        /// </summary>
        public bool UseExplicitSSL { get; set; } = true;

        /// <summary>
        /// 是否使用UTF8编码
        /// </summary>
        public bool UseUTF8 { get; set; } = true;

        /// <summary>
        /// 路径前缀
        /// </summary>
        public string PathPrefix { get; set; } = "/";
    }

    /// <summary>
    /// SFTP提供者选项
    /// </summary>
    public class SftpProviderOptions : StorageProviderOptions
    {
        /// <summary>
        /// 服务器地址
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 端口号
        /// </summary>
        public int Port { get; set; } = 22;

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// 密码（和私钥至少需要一个）
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 私钥文件路径（和密码至少需要一个）
        /// </summary>
        public string PrivateKeyFile { get; set; }

        /// <summary>
        /// 私钥文件密码（如果私钥有密码保护）
        /// </summary>
        public string PrivateKeyPassword { get; set; }

        /// <summary>
        /// 是否验证主机密钥
        /// </summary>
        public bool ValidateHostKey { get; set; } = true;

        /// <summary>
        /// 路径前缀
        /// </summary>
        public string PathPrefix { get; set; } = "/";
    }

    /// <summary>
    /// WebDAV提供者选项
    /// </summary>
    public class WebDavProviderOptions : StorageProviderOptions
    {
        /// <summary>
        /// WebDAV服务URL
        /// </summary>
        public string Url { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 是否验证SSL证书
        /// </summary>
        public bool ValidateSSL { get; set; } = true;

        /// <summary>
        /// 路径前缀
        /// </summary>
        public string PathPrefix { get; set; } = "/";
    }

    /// <summary>
    /// 阿里云盘提供者选项
    /// </summary>
    public class AliyunDriveProviderOptions : StorageProviderOptions
    {
        /// <summary>
        /// 刷新令牌
        /// </summary>
        public string RefreshToken { get; set; }

        /// <summary>
        /// 访问令牌
        /// </summary>
        public string AccessToken { get; set; }

        /// <summary>
        /// 令牌类型
        /// </summary>
        public string TokenType { get; set; } = "Bearer";

        /// <summary>
        /// 过期时间(秒)
        /// </summary>
        public int ExpiresIn { get; set; } = 7200;

        /// <summary>
        /// 路径前缀
        /// </summary>
        public string PathPrefix { get; set; } = "/";

        /// <summary>
        /// 驱动类型(资源盘/备份盘)
        /// </summary>
        public string DriveType { get; set; } = "backup";

        /// <summary>
        /// 是否启用秒传功能
        /// </summary>
        public bool EnableRapidUpload { get; set; } = true;

        /// <summary>
        /// 上传线程数
        /// </summary>
        public int UploadThreads { get; set; } = 4;

        /// <summary>
        /// 下载线程数
        /// </summary>
        public int DownloadThreads { get; set; } = 4;
    }

    /// <summary>
    /// S3兼容存储提供者选项
    /// </summary>
    public class S3ProviderOptions : StorageProviderOptions
    {
        /// <summary>
        /// 终端节点URL
        /// </summary>
        public string Endpoint { get; set; }

        /// <summary>
        /// 访问密钥ID
        /// </summary>
        public string AccessKeyId { get; set; }

        /// <summary>
        /// 访问密钥Secret
        /// </summary>
        public string AccessKeySecret { get; set; }

        /// <summary>
        /// 区域
        /// </summary>
        public string Region { get; set; }

        /// <summary>
        /// 存储桶名称
        /// </summary>
        public string BucketName { get; set; }

        /// <summary>
        /// 对象前缀
        /// </summary>
        public string ObjectPrefix { get; set; }

        /// <summary>
        /// 是否使用HTTPS
        /// </summary>
        public bool UseHttps { get; set; } = true;

        /// <summary>
        /// 是否使用路径样式访问
        /// </summary>
        public bool UsePathStyle { get; set; } = false;
    }

    /// <summary>
    /// SMB/CIFS提供者选项
    /// </summary>
    public class SmbProviderOptions : StorageProviderOptions
    {
        /// <summary>
        /// 服务器地址
        /// </summary>
        public string Host { get; set; }

        /// <summary>
        /// 共享名称
        /// </summary>
        public string ShareName { get; set; }

        /// <summary>
        /// 域名
        /// </summary>
        public string Domain { get; set; }

        /// <summary>
        /// 用户名
        /// </summary>
        public string Username { get; set; }

        /// <summary>
        /// 密码
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 路径前缀
        /// </summary>
        public string PathPrefix { get; set; } = "\\";

        /// <summary>
        /// SMB协议版本
        /// </summary>
        public string SmbVersion { get; set; } = "3.0";
    }

    /// <summary>
    /// 代理类型
    /// </summary>
    public enum ProxyType
    {
        /// <summary>
        /// HTTP代理
        /// </summary>
        Http,

        /// <summary>
        /// SOCKS4代理
        /// </summary>
        Socks4,

        /// <summary>
        /// SOCKS5代理
        /// </summary>
        Socks5
    }

    /// <summary>
    /// 加密选项
    /// </summary>
    public class EncryptionOptions
    {
        /// <summary>
        /// 是否启用加密
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 加密算法
        /// </summary>
        public EEncryptionAlgorithm Algorithm { get; set; } = EEncryptionAlgorithm.AES256GCM;

        /// <summary>
        /// 加密密码
        /// </summary>
        public string Password { get; set; }

        /// <summary>
        /// 加密密钥
        /// </summary>
        public string Key { get; set; }

        /// <summary>
        /// 加密盐值
        /// </summary>
        public string Salt { get; set; }

        /// <summary>
        /// 键派生迭代次数
        /// </summary>
        public int Iterations { get; set; } = 100000;

        /// <summary>
        /// 是否加密文件名
        /// </summary>
        public bool EncryptFilenames { get; set; } = false;
    }

    /// <summary>
    /// 加密算法
    /// </summary>
    public enum EEncryptionAlgorithm
    {
        /// <summary>
        /// AES-256-GCM
        /// </summary>
        AES256GCM,

        /// <summary>
        /// ChaCha20-Poly1305
        /// </summary>
        ChaCha20Poly1305
    }

    /// <summary>
    /// 压缩选项
    /// </summary>
    public class ECompressionOptions
    {
        /// <summary>
        /// 是否启用压缩
        /// </summary>
        public bool Enabled { get; set; } = false;

        /// <summary>
        /// 压缩算法
        /// </summary>
        public ECompressionAlgorithm Algorithm { get; set; } = ECompressionAlgorithm.Zstd;

        /// <summary>
        /// 压缩级别
        /// </summary>
        public int Level { get; set; } = 3;

        /// <summary>
        /// 最小压缩文件大小(字节)
        /// </summary>
        public long MinimumSize { get; set; } = 4096;

        /// <summary>
        /// 不压缩的文件扩展名列表
        /// </summary>
        public List<string> ExcludeExtensions { get; set; } = new List<string>
        {
            ".jpg", ".jpeg", ".png", ".gif", ".webp", ".mp4", ".avi", ".mov",
            ".mp3", ".m4a", ".zip", ".rar", ".7z", ".gz", ".xz", ".bz2"
        };
    }

    /// <summary>
    /// 压缩算法
    /// </summary>
    public enum ECompressionAlgorithm
    {
        /// <summary>
        /// Zstandard算法
        /// </summary>
        Zstd,

        /// <summary>
        /// LZ4算法
        /// </summary>
        LZ4,

        /// <summary>
        /// Snappy算法
        /// </summary>
        Snappy
    }

    /// <summary>
    /// 同步模式
    /// </summary>
    public enum ESyncMode
    {
        /// <summary>
        /// 单向同步：源 -> 目标
        /// </summary>
        OneWay,

        /// <summary>
        /// 镜像同步：源 -> 目标，删除目标中多余内容
        /// </summary>
        Mirror,

        /// <summary>
        /// 双向同步：源 <-> 目标
        /// </summary>
        TwoWay
    }

    /// <summary>
    /// 文件比较方法
    /// </summary>
    public enum ESyncCompareMethod
    {
        /// <summary>
        /// 仅比较文件大小
        /// </summary>
        Size,

        /// <summary>
        /// 仅比较修改时间
        /// </summary>
        DateTime,

        /// <summary>
        /// 比较修改时间和文件大小
        /// </summary>
        DateTimeAndSize,

        /// <summary>
        /// 比较文件内容（读取文件）
        /// </summary>
        Content,

        /// <summary>
        /// 使用哈希算法比较
        /// </summary>
        Hash
    }

    /// <summary>
    /// 文件同步冲突解决策略
    /// </summary>
    public enum ESyncConflictResolution
    {
        /// <summary>
        /// 源文件优先
        /// </summary>
        SourceWins,

        /// <summary>
        /// 目标文件优先
        /// </summary>
        TargetWins,

        /// <summary>
        /// 保留两者
        /// </summary>
        KeepBoth,

        /// <summary>
        /// 跳过冲突文件
        /// </summary>
        Skip,

        /// <summary>
        /// 更新的文件优先
        /// </summary>
        Newer,

        /// <summary>
        /// 较早的文件优先
        /// </summary>
        Older,

        /// <summary>
        /// 较大的文件优先
        /// </summary>
        Larger
    }

    /// <summary>
    /// 同步操作类型
    /// </summary>
    public enum ESyncActionType
    {
        CreateDirectory,
        CopyFile,
        UpdateFile,
        DeleteFile,
        DeleteDirectory,
        RenameFile
    }

    /// <summary>
    /// 同步方向
    /// </summary>
    public enum ESyncDirection
    {
        SourceToTarget,
        TargetToSource
    }

    /// <summary>
    /// 同步操作状态
    /// </summary>
    public enum ESyncActionStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }

    /// <summary>
    /// 同步状态
    /// </summary>
    public enum ESyncStatus
    {
        NotStarted,
        Started,
        Running,
        Completed,
        Failed,
        Canceled
    }

    /// <summary>
    /// 同步操作
    /// </summary>
    public class SyncAction
    {
        /// <summary>
        /// 操作类型
        /// </summary>
        public ESyncActionType ActionType { get; set; }

        /// <summary>
        /// 源路径
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// 目标路径
        /// </summary>
        public string TargetPath { get; set; }

        /// <summary>
        /// 相对路径
        /// </summary>
        public string RelativePath { get; set; }

        /// <summary>
        /// 文件大小
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 同步方向
        /// </summary>
        public ESyncDirection Direction { get; set; } = ESyncDirection.SourceToTarget;

        /// <summary>
        /// 操作状态
        /// </summary>
        public ESyncActionStatus Status { get; set; } = ESyncActionStatus.Pending;

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 冲突解决策略
        /// </summary>
        public ESyncConflictResolution? ConflictResolution { get; set; }
    }

    /// <summary>
    /// 同步统计信息
    /// </summary>
    public class SyncStatistics
    {
        /// <summary>
        /// 待复制文件数
        /// </summary>
        public int FilesToCopy { get; set; }

        /// <summary>
        /// 待更新文件数
        /// </summary>
        public int FilesToUpdate { get; set; }

        /// <summary>
        /// 待删除文件数
        /// </summary>
        public int FilesToDelete { get; set; }

        /// <summary>
        /// 已创建目录数
        /// </summary>
        public int DirectoriesCreated { get; set; }

        /// <summary>
        /// 待删除目录数
        /// </summary>
        public int DirectoriesToDelete { get; set; }

        /// <summary>
        /// 已复制文件数
        /// </summary>
        public int FilesCopied { get; set; }

        /// <summary>
        /// 已更新文件数
        /// </summary>
        public int FilesUpdated { get; set; }

        /// <summary>
        /// 已删除文件数
        /// </summary>
        public int FilesDeleted { get; set; }

        /// <summary>
        /// 已重命名文件数
        /// </summary>
        public int FilesRenamed { get; set; }

        /// <summary>
        /// 已删除目录数
        /// </summary>
        public int DirectoriesDeleted { get; set; }

        /// <summary>
        /// 已跳过文件数
        /// </summary>
        public int FilesSkipped { get; set; }

        /// <summary>
        /// 错误数
        /// </summary>
        public int Errors { get; set; }

        /// <summary>
        /// 总处理字节数
        /// </summary>
        public long BytesProcessed { get; set; }

        /// <summary>
        /// 待处理字节数
        /// </summary>
        public long BytesToProcess { get; set; }
    }

    /// <summary>
    /// 同步进度信息
    /// </summary>
    public class SyncProgress
    {
        /// <summary>
        /// 进度消息
        /// </summary>
        public string Message { get; set; }

        /// <summary>
        /// 进度百分比（-1表示不确定）
        /// </summary>
        public int ProgressPercentage { get; set; }

        /// <summary>
        /// 已用时间
        /// </summary>
        public TimeSpan ElapsedTime { get; set; }

        /// <summary>
        /// 已处理项目数
        /// </summary>
        public int ProcessedItems { get; set; }

        /// <summary>
        /// 总项目数
        /// </summary>
        public int TotalItems { get; set; }

        /// <summary>
        /// 已处理字节数
        /// </summary>
        public long BytesProcessed { get; set; }

        /// <summary>
        /// 待处理字节数
        /// </summary>
        public long BytesToProcess { get; set; }

        /// <summary>
        /// 每秒处理字节数
        /// </summary>
        public double BytesPerSecond { get; set; }

        /// <summary>
        /// 每秒处理项目数
        /// </summary>
        public double ItemsPerSecond { get; set; }

        /// <summary>
        /// 统计信息
        /// </summary>
        public SyncStatistics Statistics { get; set; }

        /// <summary>
        /// 获取格式化的处理速度
        /// </summary>
        public string FormattedSpeed => FormatSize(BytesPerSecond) + "/s";

        /// <summary>
        /// 获取格式化的已处理大小
        /// </summary>
        public string FormattedBytesProcessed => FormatSize(BytesProcessed);

        /// <summary>
        /// 获取格式化的总大小
        /// </summary>
        public string FormattedBytesToProcess => FormatSize(BytesToProcess);

        /// <summary>
        /// 格式化大小显示
        /// </summary>
        private string FormatSize(double bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            int i = 0;
            while (bytes >= 1024 && i < suffixes.Length - 1)
            {
                bytes /= 1024;
                i++;
            }
            return $"{bytes:0.##} {suffixes[i]}";
        }

        /// <summary>
        /// 估计剩余时间
        /// </summary>
        public TimeSpan EstimatedTimeRemaining
        {
            get
            {
                if (BytesProcessed <= 0 || BytesToProcess <= 0 || BytesPerSecond <= 0)
                    return TimeSpan.Zero;

                double remainingBytes = BytesToProcess - BytesProcessed;
                double remainingSeconds = remainingBytes / BytesPerSecond;
                return TimeSpan.FromSeconds(remainingSeconds);
            }
        }

        /// <summary>
        /// 获取格式化的估计剩余时间
        /// </summary>
        public string FormattedTimeRemaining
        {
            get
            {
                var time = EstimatedTimeRemaining;
                if (time.TotalSeconds < 1)
                    return "完成";

                if (time.TotalHours >= 1)
                    return $"{(int)time.TotalHours}小时 {time.Minutes}分钟";
                else if (time.TotalMinutes >= 1)
                    return $"{time.Minutes}分钟 {time.Seconds}秒";
                else
                    return $"{time.Seconds}秒";
            }
        }
    }

    /// <summary>
    /// 同步结果
    /// </summary>
    public class SyncResult
    {
        /// <summary>
        /// 源路径
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// 目标路径
        /// </summary>
        public string TargetPath { get; set; }

        /// <summary>
        /// 同步模式
        /// </summary>
        public ESyncMode Mode { get; set; }

        /// <summary>
        /// 同步状态
        /// </summary>
        public ESyncStatus Status { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 耗时
        /// </summary>
        public TimeSpan ElapsedTime { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 同步操作列表
        /// </summary>
        public List<SyncAction> Actions { get; set; } = new List<SyncAction>();

        /// <summary>
        /// 同步统计信息
        /// </summary>
        public SyncStatistics Statistics { get; set; }

        /// <summary>
        /// 是否成功完成同步
        /// </summary>
        public bool IsSuccessful => Status == ESyncStatus.Completed;

        /// <summary>
        /// 已处理的总文件数
        /// </summary>
        public int TotalFilesProcessed => Statistics?.FilesCopied + Statistics?.FilesUpdated + Statistics?.FilesDeleted + Statistics?.FilesSkipped ?? 0;

        /// <summary>
        /// 已处理的总目录数
        /// </summary>
        public int TotalDirectoriesProcessed => Statistics?.DirectoriesCreated + Statistics?.DirectoriesDeleted ?? 0;

        /// <summary>
        /// 同步总项目数
        /// </summary>
        public int TotalItemsProcessed => TotalFilesProcessed + TotalDirectoriesProcessed;

        /// <summary>
        /// 每秒处理的平均项目数
        /// </summary>
        public double ItemsPerSecond => ElapsedTime.TotalSeconds > 0
            ? TotalItemsProcessed / ElapsedTime.TotalSeconds
            : 0;

        /// <summary>
        /// 每秒处理的平均字节数
        /// </summary>
        public double BytesPerSecond => ElapsedTime.TotalSeconds > 0 && Statistics != null
            ? Statistics.BytesProcessed / ElapsedTime.TotalSeconds
            : 0;

        /// <summary>
        /// 获取同步操作的简要摘要
        /// </summary>
        /// <returns>摘要文本</returns>
        public string GetSummary()
        {
            if (Status == ESyncStatus.NotStarted)
                return "同步尚未开始";

            if (Status == ESyncStatus.Failed)
                return $"同步失败: {ErrorMessage}";

            if (Status == ESyncStatus.Canceled)
                return "同步被取消";

            if (Statistics == null)
                return "没有同步统计信息";

            var summary = new StringBuilder();
            summary.AppendLine($"同步模式: {GetSyncModeDescription(Mode)}");
            summary.AppendLine($"源路径: {SourcePath}");
            summary.AppendLine($"目标路径: {TargetPath}");
            summary.AppendLine($"状态: {GetStatusDescription(Status)}");
            summary.AppendLine($"开始时间: {StartTime:yyyy-MM-dd HH:mm:ss}");
            summary.AppendLine($"结束时间: {EndTime:yyyy-MM-dd HH:mm:ss}");
            summary.AppendLine($"总耗时: {FormatTimeSpan(ElapsedTime)}");
            summary.AppendLine($"处理速度: {FormatBytesPerSecond(BytesPerSecond)}");

            summary.AppendLine("文件统计:");
            summary.AppendLine($"  - 复制: {Statistics.FilesCopied} 个文件");
            summary.AppendLine($"  - 更新: {Statistics.FilesUpdated} 个文件");
            summary.AppendLine($"  - 删除: {Statistics.FilesDeleted} 个文件");
            summary.AppendLine($"  - 跳过: {Statistics.FilesSkipped} 个文件");
            summary.AppendLine($"  - 重命名: {Statistics.FilesRenamed} 个文件");

            summary.AppendLine("目录统计:");
            summary.AppendLine($"  - 创建: {Statistics.DirectoriesCreated} 个目录");
            summary.AppendLine($"  - 删除: {Statistics.DirectoriesDeleted} 个目录");

            summary.AppendLine($"错误数量: {Statistics.Errors}");

            if (Statistics.Errors > 0 && !string.IsNullOrEmpty(ErrorMessage))
            {
                summary.AppendLine($"最后错误: {ErrorMessage}");
            }

            summary.AppendLine($"总处理字节: {FormatBytes(Statistics.BytesProcessed)}");

            return summary.ToString();
        }

        /// <summary>
        /// 获取同步模式的描述
        /// </summary>
        private string GetSyncModeDescription(ESyncMode mode)
        {
            return mode switch
            {
                ESyncMode.OneWay => "单向同步",
                ESyncMode.Mirror => "镜像同步",
                ESyncMode.TwoWay => "双向同步",
                _ => mode.ToString()
            };
        }

        /// <summary>
        /// 获取同步状态的描述
        /// </summary>
        private string GetStatusDescription(ESyncStatus status)
        {
            return status switch
            {
                ESyncStatus.NotStarted => "未开始",
                ESyncStatus.Started => "已开始",
                ESyncStatus.Running => "运行中",
                ESyncStatus.Completed => "已完成",
                ESyncStatus.Failed => "失败",
                ESyncStatus.Canceled => "已取消",
                _ => status.ToString()
            };
        }

        /// <summary>
        /// 格式化时间间隔
        /// </summary>
        private string FormatTimeSpan(TimeSpan timeSpan)
        {
            if (timeSpan.TotalHours >= 1)
            {
                return $"{(int)timeSpan.TotalHours}小时 {timeSpan.Minutes}分钟 {timeSpan.Seconds}秒";
            }
            else if (timeSpan.TotalMinutes >= 1)
            {
                return $"{timeSpan.Minutes}分钟 {timeSpan.Seconds}秒";
            }
            else
            {
                return $"{timeSpan.Seconds}.{timeSpan.Milliseconds}秒";
            }
        }

        /// <summary>
        /// 格式化字节大小
        /// </summary>
        private string FormatBytes(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:0.##} {suffixes[i]}";
        }

        /// <summary>
        /// 格式化每秒字节数
        /// </summary>
        private string FormatBytesPerSecond(double bytesPerSecond)
        {
            return $"{FormatBytes((long)bytesPerSecond)}/秒";
        }

        /// <summary>
        /// 生成完整的同步报告
        /// </summary>
        /// <returns>详细报告文本</returns>
        public string GenerateReport()
        {
            var report = new StringBuilder();
            report.AppendLine("==================================");
            report.AppendLine("          同步操作报告           ");
            report.AppendLine("==================================");
            report.AppendLine();
            report.Append(GetSummary());
            report.AppendLine();

            if (Actions.Count > 0)
            {
                report.AppendLine("==================================");
                report.AppendLine("          详细操作记录           ");
                report.AppendLine("==================================");

                // 按操作类型分组显示
                var actionsByType = Actions.GroupBy(a => a.ActionType);
                foreach (var group in actionsByType.OrderBy(g => (int)g.Key))
                {
                    report.AppendLine();
                    report.AppendLine($"【{GetActionTypeDescription(group.Key)}】操作记录 - 共 {group.Count()} 项");
                    report.AppendLine("----------------------------------");

                    int count = 0;
                    foreach (var action in group.OrderBy(a => a.RelativePath).Take(100)) // 限制每类显示最多100条记录
                    {
                        count++;
                        var status = action.Status == ESyncActionStatus.Completed ? "✓" :
                                     action.Status == ESyncActionStatus.Failed ? "✗" : "?";

                        var path = action.RelativePath?.Length > 80
                            ? "..." + action.RelativePath.Substring(action.RelativePath.Length - 77)
                            : action.RelativePath;

                        report.AppendLine($"{status} {path ?? "-"}");

                        if (action.Status == ESyncActionStatus.Failed && !string.IsNullOrEmpty(action.ErrorMessage))
                        {
                            report.AppendLine($"   错误: {action.ErrorMessage}");
                        }
                    }

                    if (group.Count() > 100)
                    {
                        report.AppendLine($"... 还有 {group.Count() - 100} 项未显示 ...");
                    }
                }
            }

            report.AppendLine();
            report.AppendLine("==================================");
            report.AppendLine("          报告生成完毕           ");
            report.AppendLine("==================================");

            return report.ToString();
        }

        /// <summary>
        /// 获取操作类型的描述
        /// </summary>
        private string GetActionTypeDescription(ESyncActionType actionType)
        {
            return actionType switch
            {
                ESyncActionType.CreateDirectory => "创建目录",
                ESyncActionType.CopyFile => "复制文件",
                ESyncActionType.UpdateFile => "更新文件",
                ESyncActionType.DeleteFile => "删除文件",
                ESyncActionType.DeleteDirectory => "删除目录",
                ESyncActionType.RenameFile => "重命名文件",
                _ => actionType.ToString()
            };
        }

        /// <summary>
        /// 创建一个基于项目同步API返回的标准Result对象
        /// </summary>
        /// <returns>包含同步结果的Result对象</returns>
        public Result<SyncResultDto> ToApiResult()
        {
            var dto = new SyncResultDto
            {
                SourcePath = this.SourcePath,
                TargetPath = this.TargetPath,
                SyncMode = this.Mode.ToString(),
                Status = this.Status.ToString(),
                StartTime = this.StartTime,
                EndTime = this.EndTime,
                ElapsedTimeSeconds = this.ElapsedTime.TotalSeconds,
                IsSuccessful = this.IsSuccessful,
                ErrorMessage = this.ErrorMessage,
                TotalFilesProcessed = this.TotalFilesProcessed,
                TotalDirectoriesProcessed = this.TotalDirectoriesProcessed,
                BytesProcessed = this.Statistics?.BytesProcessed ?? 0,
                Summary = this.GetSummary()
            };

            if (this.IsSuccessful)
            {
                return Result.Ok(dto, "同步操作已成功完成");
            }
            else if (this.Status == ESyncStatus.Canceled)
            {
                return Result.Ok(dto, "同步操作被用户取消");
            }
            else
            {
                return Result.Fail(dto, this.ErrorMessage ?? "同步操作失败");
            }
        }

        /// <summary>
        /// 创建一个失败的同步结果
        /// </summary>
        /// <param name="sourcePath">源路径</param>
        /// <param name="targetPath">目标路径</param>
        /// <param name="errorMessage">错误消息</param>
        /// <returns>失败的同步结果</returns>
        public static SyncResult Failed(string sourcePath, string targetPath, string errorMessage)
        {
            return new SyncResult
            {
                SourcePath = sourcePath,
                TargetPath = targetPath,
                Status = ESyncStatus.Failed,
                ErrorMessage = errorMessage,
                StartTime = DateTime.Now,
                EndTime = DateTime.Now,
                ElapsedTime = TimeSpan.Zero,
                Statistics = new SyncStatistics()
            };
        }

        /// <summary>
        /// 创建一个取消的同步结果
        /// </summary>
        /// <param name="sourcePath">源路径</param>
        /// <param name="targetPath">目标路径</param>
        /// <param name="elapsedTime">已耗时间</param>
        /// <param name="statistics">已有的统计信息</param>
        /// <returns>取消的同步结果</returns>
        public static SyncResult Canceled(string sourcePath, string targetPath, TimeSpan elapsedTime, SyncStatistics statistics)
        {
            return new SyncResult
            {
                SourcePath = sourcePath,
                TargetPath = targetPath,
                Status = ESyncStatus.Canceled,
                StartTime = DateTime.Now - elapsedTime,
                EndTime = DateTime.Now,
                ElapsedTime = elapsedTime,
                Statistics = statistics ?? new SyncStatistics(),
                ErrorMessage = "同步操作被用户取消"
            };
        }
    }

    /// <summary>
    /// 同步结果数据传输对象（用于API返回）
    /// </summary>
    public class SyncResultDto
    {
        /// <summary>
        /// 源路径
        /// </summary>
        public string SourcePath { get; set; }

        /// <summary>
        /// 目标路径
        /// </summary>
        public string TargetPath { get; set; }

        /// <summary>
        /// 同步模式
        /// </summary>
        public string SyncMode { get; set; }

        /// <summary>
        /// 同步状态
        /// </summary>
        public string Status { get; set; }

        /// <summary>
        /// 开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 耗时（秒）
        /// </summary>
        public double ElapsedTimeSeconds { get; set; }

        /// <summary>
        /// 是否成功
        /// </summary>
        public bool IsSuccessful { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 处理的总文件数
        /// </summary>
        public int TotalFilesProcessed { get; set; }

        /// <summary>
        /// 处理的总目录数
        /// </summary>
        public int TotalDirectoriesProcessed { get; set; }

        /// <summary>
        /// 处理的总字节数
        /// </summary>
        public long BytesProcessed { get; set; }

        /// <summary>
        /// 同步操作摘要
        /// </summary>
        public string Summary { get; set; }
    }
}