using MDriveSync.Security;
using MDriveSync.Security.Models;
using System.Diagnostics;
using System.Security.Cryptography;
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
        private readonly SyncOptions _options;
        private readonly IProgress<SyncProgress> _progress;
        private readonly CancellationToken _cancellationToken;

        private readonly Stopwatch _stopwatch = new Stopwatch();
        private DateTime _lastProgressUpdate = DateTime.MinValue;
        private int _processedItems = 0;
        private int _totalItems = 0;
        private SyncStatistics _statistics = new SyncStatistics();

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
        }

        /// <summary>
        /// 执行同步操作
        /// </summary>
        /// <returns>同步结果</returns>
        public async Task<SyncResult> SyncAsync()
        {
            _stopwatch.Restart();
            _statistics = new SyncStatistics();
            var result = new SyncResult
            {
                StartTime = DateTime.Now,
                SourcePath = _options.SourcePath,
                TargetPath = _options.TargetPath,
                Mode = _options.SyncMode,
                Status = SyncStatus.Started
            };

            try
            {
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

                result.Status = SyncStatus.Completed;
                result.ElapsedTime = _stopwatch.Elapsed;
                result.Statistics = _statistics;
                ReportProgress($"同步完成，耗时: {_stopwatch.Elapsed.TotalSeconds:F2}秒", 100);

                return result;
            }
            catch (OperationCanceledException)
            {
                result.Status = SyncStatus.Canceled;
                result.ElapsedTime = _stopwatch.Elapsed;
                result.Statistics = _statistics;
                ReportProgress("同步操作已取消", -1);
                return result;
            }
            catch (Exception ex)
            {
                result.Status = SyncStatus.Failed;
                result.ErrorMessage = ex.Message;
                result.ElapsedTime = _stopwatch.Elapsed;
                result.Statistics = _statistics;
                ReportProgress($"同步操作失败: {ex.Message}", -1);
                return result;
            }
            finally
            {
                _stopwatch.Stop();
                result.EndTime = DateTime.Now;
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
                case SyncMode.OneWay:
                    actions = await CreateOneWaySyncActionsAsync(sourceFiles, sourceDirs, targetFiles, targetDirs);
                    break;

                case SyncMode.Mirror:
                    actions = await CreateMirrorSyncActionsAsync(sourceFiles, sourceDirs, targetFiles, targetDirs);
                    break;

                case SyncMode.TwoWay:
                    actions = await CreateTwoWaySyncActionsAsync(sourceFiles, sourceDirs, targetFiles, targetDirs);
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
            await ExecuteSyncActionsAsync(actions);
        }

        /// <summary>
        /// 创建单向同步操作列表（源 -> 目标）
        /// </summary>
        private async Task<List<SyncAction>> CreateOneWaySyncActionsAsync(
            Dictionary<string, FileInfo> sourceFiles,
            HashSet<string> sourceDirs,
            Dictionary<string, FileInfo> targetFiles,
            HashSet<string> targetDirs)
        {
            var actions = new List<SyncAction>();

            // 确保目标目录结构完整
            foreach (var sourceDir in sourceDirs)
            {
                var relativePath = GetRelativePath(sourceDir, _options.SourcePath);
                var targetDirPath = Path.Combine(_options.TargetPath, relativePath);

                if (!targetDirs.Contains(targetDirPath))
                {
                    actions.Add(new SyncAction
                    {
                        ActionType = SyncActionType.CreateDirectory,
                        SourcePath = sourceDir,
                        TargetPath = targetDirPath,
                        RelativePath = relativePath
                    });
                }
            }

            // 比较文件
            foreach (var sourceEntry in sourceFiles)
            {
                var sourceFilePath = sourceEntry.Key;
                var sourceFile = sourceEntry.Value;
                var relativePath = GetRelativePath(sourceFilePath, _options.SourcePath);
                var targetFilePath = Path.Combine(_options.TargetPath, relativePath);

                if (!targetFiles.TryGetValue(targetFilePath, out var targetFile))
                {
                    // 目标不存在文件，需要复制
                    actions.Add(new SyncAction
                    {
                        ActionType = SyncActionType.CopyFile,
                        SourcePath = sourceFilePath,
                        TargetPath = targetFilePath,
                        RelativePath = relativePath,
                        Size = sourceFile.Length
                    });
                    _statistics.FilesToCopy++;
                    _statistics.BytesToProcess += sourceFile.Length;
                }
                else if (NeedsUpdate(sourceFile, targetFile))
                {
                    // 文件需要更新
                    actions.Add(new SyncAction
                    {
                        ActionType = SyncActionType.UpdateFile,
                        SourcePath = sourceFilePath,
                        TargetPath = targetFilePath,
                        RelativePath = relativePath,
                        Size = sourceFile.Length
                    });
                    _statistics.FilesToUpdate++;
                    _statistics.BytesToProcess += sourceFile.Length;
                }
                else
                {
                    _statistics.FilesSkipped++;
                }
            }

            return actions;
        }

        /// <summary>
        /// 创建镜像同步操作列表（源 -> 目标，删除目标中多余内容）
        /// </summary>
        private async Task<List<SyncAction>> CreateMirrorSyncActionsAsync(
            Dictionary<string, FileInfo> sourceFiles,
            HashSet<string> sourceDirs,
            Dictionary<string, FileInfo> targetFiles,
            HashSet<string> targetDirs)
        {
            // 首先创建单向同步的操作
            var actions = await CreateOneWaySyncActionsAsync(sourceFiles, sourceDirs, targetFiles, targetDirs);

            // 查找目标中需要删除的文件和目录
            var sourceRelativePaths = sourceFiles.Keys
                .Select(path => GetRelativePath(path, _options.SourcePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var targetRelativePaths = targetFiles.Keys
                .Select(path => GetRelativePath(path, _options.TargetPath));

            foreach (var targetRelativePath in targetRelativePaths)
            {
                if (!sourceRelativePaths.Contains(targetRelativePath))
                {
                    var targetFilePath = Path.Combine(_options.TargetPath, targetRelativePath);
                    // 目标文件在源中不存在，需要删除
                    actions.Add(new SyncAction
                    {
                        ActionType = SyncActionType.DeleteFile,
                        TargetPath = targetFilePath,
                        RelativePath = targetRelativePath,
                        Size = targetFiles[targetFilePath].Length
                    });
                    _statistics.FilesToDelete++;
                }
            }

            // 查找目标中需要删除的目录（倒序处理，先删除子目录）
            var sourceRelativeDirs = sourceDirs
                .Select(path => GetRelativePath(path, _options.SourcePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var targetRelativeDirs = targetDirs
                .Where(d => d != _options.TargetPath) // 排除根目录
                .Select(path => GetRelativePath(path, _options.TargetPath))
                .OrderByDescending(p => p.Length); // 倒序排列，先删除深层目录

            foreach (var targetRelativeDir in targetRelativeDirs)
            {
                if (!sourceRelativeDirs.Contains(targetRelativeDir))
                {
                    var targetDirPath = Path.Combine(_options.TargetPath, targetRelativeDir);
                    // 目标目录在源中不存在，需要删除
                    actions.Add(new SyncAction
                    {
                        ActionType = SyncActionType.DeleteDirectory,
                        TargetPath = targetDirPath,
                        RelativePath = targetRelativeDir
                    });
                    _statistics.DirectoriesToDelete++;
                }
            }

            return actions;
        }

        /// <summary>
        /// 创建双向同步操作列表（源 <-> 目标，解决冲突）
        /// </summary>
        private async Task<List<SyncAction>> CreateTwoWaySyncActionsAsync(
            Dictionary<string, FileInfo> sourceFiles,
            HashSet<string> sourceDirs,
            Dictionary<string, FileInfo> targetFiles,
            HashSet<string> targetDirs)
        {
            var actions = new List<SyncAction>();
            var processedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

            // 第1步：同步目录结构
            var allSourceRelativeDirs = sourceDirs
                .Select(path => GetRelativePath(path, _options.SourcePath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            var allTargetRelativeDirs = targetDirs
                .Where(d => d != _options.TargetPath)
                .Select(path => GetRelativePath(path, _options.TargetPath))
                .ToHashSet(StringComparer.OrdinalIgnoreCase);

            // 在目标中创建源中存在的目录
            foreach (var sourceRelativeDir in allSourceRelativeDirs)
            {
                var targetDirPath = Path.Combine(_options.TargetPath, sourceRelativeDir);
                if (!targetDirs.Contains(targetDirPath))
                {
                    actions.Add(new SyncAction
                    {
                        ActionType = SyncActionType.CreateDirectory,
                        SourcePath = Path.Combine(_options.SourcePath, sourceRelativeDir),
                        TargetPath = targetDirPath,
                        RelativePath = sourceRelativeDir
                    });
                }
            }

            // 在源中创建目标中存在的目录
            foreach (var targetRelativeDir in allTargetRelativeDirs)
            {
                var sourceDirPath = Path.Combine(_options.SourcePath, targetRelativeDir);
                if (!sourceDirs.Contains(sourceDirPath))
                {
                    actions.Add(new SyncAction
                    {
                        ActionType = SyncActionType.CreateDirectory,
                        SourcePath = targetRelativeDir,
                        TargetPath = sourceDirPath,
                        RelativePath = targetRelativeDir,
                        Direction = SyncDirection.TargetToSource
                    });
                }
            }

            // 第2步：处理文件
            // 从源处理到目标
            foreach (var sourceEntry in sourceFiles)
            {
                var sourceFilePath = sourceEntry.Key;
                var sourceFile = sourceEntry.Value;
                var relativePath = GetRelativePath(sourceFilePath, _options.SourcePath);
                var targetFilePath = Path.Combine(_options.TargetPath, relativePath);

                processedPaths.Add(relativePath);

                if (!targetFiles.TryGetValue(targetFilePath, out var targetFile))
                {
                    // 目标不存在，复制到目标
                    actions.Add(new SyncAction
                    {
                        ActionType = SyncActionType.CopyFile,
                        SourcePath = sourceFilePath,
                        TargetPath = targetFilePath,
                        RelativePath = relativePath,
                        Size = sourceFile.Length
                    });
                    _statistics.FilesToCopy++;
                    _statistics.BytesToProcess += sourceFile.Length;
                }
                else
                {
                    // 两边都存在，需要解决冲突
                    var conflictResult = ResolveConflict(sourceFile, targetFile);
                    switch (conflictResult)
                    {
                        case ConflictResolution.SourceWins:
                            actions.Add(new SyncAction
                            {
                                ActionType = SyncActionType.UpdateFile,
                                SourcePath = sourceFilePath,
                                TargetPath = targetFilePath,
                                RelativePath = relativePath,
                                Size = sourceFile.Length,
                                ConflictResolution = conflictResult
                            });
                            _statistics.FilesToUpdate++;
                            _statistics.BytesToProcess += sourceFile.Length;
                            break;

                        case ConflictResolution.TargetWins:
                            actions.Add(new SyncAction
                            {
                                ActionType = SyncActionType.UpdateFile,
                                SourcePath = targetFilePath,
                                TargetPath = sourceFilePath,
                                RelativePath = relativePath,
                                Size = targetFile.Length,
                                Direction = SyncDirection.TargetToSource,
                                ConflictResolution = conflictResult
                            });
                            _statistics.FilesToUpdate++;
                            _statistics.BytesToProcess += targetFile.Length;
                            break;

                        case ConflictResolution.KeepBoth:
                            // 保留两个版本，重命名目标文件
                            string targetNewName = GetConflictFileName(targetFilePath);
                            actions.Add(new SyncAction
                            {
                                ActionType = SyncActionType.RenameFile,
                                SourcePath = targetFilePath,
                                TargetPath = targetNewName,
                                RelativePath = GetRelativePath(targetNewName, _options.TargetPath),
                                ConflictResolution = conflictResult
                            });
                            // 然后复制源文件到目标
                            actions.Add(new SyncAction
                            {
                                ActionType = SyncActionType.CopyFile,
                                SourcePath = sourceFilePath,
                                TargetPath = targetFilePath,
                                RelativePath = relativePath,
                                Size = sourceFile.Length
                            });
                            _statistics.FilesToCopy++;
                            _statistics.BytesToProcess += sourceFile.Length;
                            break;

                        case ConflictResolution.Skip:
                            _statistics.FilesSkipped++;
                            break;
                    }
                }
            }

            // 从目标处理到源
            foreach (var targetEntry in targetFiles)
            {
                var targetFilePath = targetEntry.Key;
                var targetFile = targetEntry.Value;
                var relativePath = GetRelativePath(targetFilePath, _options.TargetPath);

                // 跳过已处理的文件
                if (processedPaths.Contains(relativePath))
                    continue;

                var sourceFilePath = Path.Combine(_options.SourcePath, relativePath);

                // 源不存在，复制到源
                actions.Add(new SyncAction
                {
                    ActionType = SyncActionType.CopyFile,
                    SourcePath = targetFilePath,
                    TargetPath = sourceFilePath,
                    RelativePath = relativePath,
                    Size = targetFile.Length,
                    Direction = SyncDirection.TargetToSource
                });
                _statistics.FilesToCopy++;
                _statistics.BytesToProcess += targetFile.Length;
            }

            return actions;
        }

        /// <summary>
        /// 执行同步操作
        /// </summary>
        private async Task ExecuteSyncActionsAsync(List<SyncAction> actions)
        {
            // 按操作类型排序：先创建目录，然后复制/更新文件，最后删除文件和目录
            var orderedActions = actions
                .OrderBy(a => GetActionPriority(a.ActionType))
                .ToList();

            _totalItems = orderedActions.Count;
            _processedItems = 0;

            ReportProgress($"开始执行同步操作，共 {_totalItems} 个任务", 0);

            if (_options.MaxParallelOperations > 1 && _options.EnableParallelFileOperations)
            {
                // 并行处理
                await ProcessActionsInParallelAsync(orderedActions);
            }
            else
            {
                // 串行处理
                await ProcessActionsSequentiallyAsync(orderedActions);
            }
        }

        /// <summary>
        /// 并行处理同步操作
        /// </summary>
        private async Task ProcessActionsInParallelAsync(List<SyncAction> actions)
        {
            // 分组处理不同类型的操作
            var actionGroups = actions
                .GroupBy(a => GetActionPriority(a.ActionType))
                .OrderBy(g => g.Key);

            foreach (var group in actionGroups)
            {
                var groupActions = group.ToList();
                var actionType = groupActions.FirstOrDefault()?.ActionType;

                ReportProgress($"处理 {GetActionTypeDescription(actionType.Value)} 操作，共 {groupActions.Count} 项",
                    CalculateProgress(_processedItems, _totalItems));

                // 设置并行度，文件操作使用配置的并行度，目录操作单线程执行
                int parallelism = IsFileOperation(actionType.Value)
                    ? _options.MaxParallelOperations
                    : 1;

                await Parallel.ForEachAsync(
                    groupActions,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = parallelism,
                        CancellationToken = _cancellationToken
                    },
                    async (action, ct) =>
                    {
                        await ExecuteSingleActionAsync(action);
                        Interlocked.Increment(ref _processedItems);

                        if (_processedItems % 10 == 0 || _processedItems == _totalItems)
                        {
                            ReportProgress(
                                $"正在处理 {GetActionTypeDescription(action.ActionType)} 操作 ({_processedItems}/{_totalItems})",
                                CalculateProgress(_processedItems, _totalItems)
                            );
                        }
                    });
            }
        }

        /// <summary>
        /// 串行处理同步操作
        /// </summary>
        private async Task ProcessActionsSequentiallyAsync(List<SyncAction> actions)
        {
            for (int i = 0; i < actions.Count; i++)
            {
                _cancellationToken.ThrowIfCancellationRequested();

                var action = actions[i];
                await ExecuteSingleActionAsync(action);

                _processedItems++;

                if (i % 5 == 0 || i == actions.Count - 1)
                {
                    ReportProgress(
                        $"正在处理 {GetActionTypeDescription(action.ActionType)} 操作 ({_processedItems}/{_totalItems})",
                        CalculateProgress(_processedItems, _totalItems)
                    );
                }
            }
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
                string actualSource = action.Direction == SyncDirection.SourceToTarget
                    ? action.SourcePath
                    : action.TargetPath;

                string actualTarget = action.Direction == SyncDirection.SourceToTarget
                    ? action.TargetPath
                    : action.SourcePath;

                switch (action.ActionType)
                {
                    case SyncActionType.CreateDirectory:
                        if (!Directory.Exists(actualTarget))
                        {
                            Directory.CreateDirectory(actualTarget);
                            _statistics.DirectoriesCreated++;
                        }
                        break;

                    case SyncActionType.CopyFile:
                        await EnsureDirectoryExistsAsync(Path.GetDirectoryName(actualTarget));
                        await CopyFileWithRetryAsync(actualSource, actualTarget);
                        _statistics.FilesCopied++;
                        _statistics.BytesProcessed += action.Size;
                        break;

                    case SyncActionType.UpdateFile:
                        await EnsureDirectoryExistsAsync(Path.GetDirectoryName(actualTarget));
                        await CopyFileWithRetryAsync(actualSource, actualTarget);
                        _statistics.FilesUpdated++;
                        _statistics.BytesProcessed += action.Size;
                        break;

                    case SyncActionType.DeleteFile:
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

                    case SyncActionType.DeleteDirectory:
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

                    case SyncActionType.RenameFile:
                        if (File.Exists(actualSource) && !File.Exists(actualTarget))
                        {
                            await EnsureDirectoryExistsAsync(Path.GetDirectoryName(actualTarget));
                            File.Move(actualSource, actualTarget);
                            _statistics.FilesRenamed++;
                        }
                        break;
                }

                action.Status = SyncActionStatus.Completed;
            }
            catch (Exception ex)
            {
                action.Status = SyncActionStatus.Failed;
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
            if (_options.CompareMethod == CompareMethod.DateTime ||
                _options.CompareMethod == CompareMethod.DateTimeAndSize)
            {
                // 使用阈值比较时间，避免因时区等问题导致的微小差异
                var timeDiff = Math.Abs((sourceFile.LastWriteTimeUtc - targetFile.LastWriteTimeUtc).TotalSeconds);
                if (timeDiff > _options.DateTimeThresholdSeconds)
                    return true;
            }

            // 检查文件内容
            if (_options.CompareMethod == CompareMethod.Content ||
                _options.CompareMethod == CompareMethod.Hash)
            {
                try
                {
                    return !FilesAreEqual(sourceFile, targetFile);
                }
                catch (Exception)
                {
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
            if (_options.CompareMethod == CompareMethod.Hash)
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
            if (_options.SamplingRate < 1.0)
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
                ReportProgress($"高性能扫描器失败，切换到备用扫描器: {ex.Message}", -1);

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
        private ConflictResolution ResolveConflict(FileInfo sourceFile, FileInfo targetFile)
        {
            switch (_options.ConflictResolution)
            {
                case ConflictResolution.SourceWins:
                    return ConflictResolution.SourceWins;

                case ConflictResolution.TargetWins:
                    return ConflictResolution.TargetWins;

                case ConflictResolution.KeepBoth:
                    return ConflictResolution.KeepBoth;

                case ConflictResolution.Skip:
                    return ConflictResolution.Skip;

                case ConflictResolution.Newer:
                    return sourceFile.LastWriteTimeUtc > targetFile.LastWriteTimeUtc
                        ? ConflictResolution.SourceWins
                        : ConflictResolution.TargetWins;

                case ConflictResolution.Older:
                    return sourceFile.LastWriteTimeUtc < targetFile.LastWriteTimeUtc
                        ? ConflictResolution.SourceWins
                        : ConflictResolution.TargetWins;

                case ConflictResolution.Larger:
                    return sourceFile.Length > targetFile.Length
                        ? ConflictResolution.SourceWins
                        : ConflictResolution.TargetWins;

                default:
                    return ConflictResolution.Skip;
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
        private int GetActionPriority(SyncActionType actionType)
        {
            switch (actionType)
            {
                case SyncActionType.CreateDirectory:
                    return 1;

                case SyncActionType.CopyFile:
                case SyncActionType.UpdateFile:
                    return 2;

                case SyncActionType.RenameFile:
                    return 3;

                case SyncActionType.DeleteFile:
                    return 4;

                case SyncActionType.DeleteDirectory:
                    return 5;

                default:
                    return 99;
            }
        }

        /// <summary>
        /// 获取操作类型描述
        /// </summary>
        private string GetActionTypeDescription(SyncActionType actionType)
        {
            switch (actionType)
            {
                case SyncActionType.CreateDirectory:
                    return "创建目录";

                case SyncActionType.CopyFile:
                    return "复制文件";

                case SyncActionType.UpdateFile:
                    return "更新文件";

                case SyncActionType.DeleteFile:
                    return "删除文件";

                case SyncActionType.DeleteDirectory:
                    return "删除目录";

                case SyncActionType.RenameFile:
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
        /// 带重试的文件复制操作
        /// </summary>
        private async Task CopyFileWithRetryAsync(string sourcePath, string targetPath)
        {
            int maxRetries = _options.MaxRetries;
            int currentRetry = 0;
            bool success = false;

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

                    if (_options.PreserveFileTime)
                    {
                        // 保留原始时间戳
                        using (FileStream sourceStream = new FileStream(sourcePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, FileOptions.SequentialScan))
                        using (FileStream targetStream = new FileStream(targetPath, FileMode.Create, FileAccess.Write, FileShare.None, 4096, FileOptions.SequentialScan))
                        {
                            await sourceStream.CopyToAsync(targetStream, 81920, _cancellationToken);
                        }

                        // 设置目标文件的时间戳与源文件相同
                        File.SetCreationTimeUtc(targetPath, File.GetCreationTimeUtc(sourcePath));
                        File.SetLastWriteTimeUtc(targetPath, File.GetLastWriteTimeUtc(sourcePath));
                        File.SetLastAccessTimeUtc(targetPath, File.GetLastAccessTimeUtc(sourcePath));
                    }
                    else
                    {
                        // 简单复制，不保留时间戳
                        File.Copy(sourcePath, targetPath, true);
                    }

                    success = true;
                }
                catch (IOException) when (currentRetry < maxRetries)
                {
                    currentRetry++;
                }
                catch (Exception)
                {
                    // 其他异常直接抛出
                    throw;
                }
            }

            if (!success)
            {
                throw new IOException($"复制文件 {sourcePath} 到 {targetPath} 失败，已重试 {maxRetries} 次");
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
            if (_progress == null) return;

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
        private bool IsFileOperation(SyncActionType actionType)
        {
            return actionType == SyncActionType.CopyFile ||
                   actionType == SyncActionType.UpdateFile ||
                   actionType == SyncActionType.RenameFile ||
                   actionType == SyncActionType.DeleteFile;
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
        public SyncMode SyncMode { get; set; } = SyncMode.OneWay;

        /// <summary>
        /// 文件比较方法
        /// </summary>
        public CompareMethod CompareMethod { get; set; } = CompareMethod.DateTimeAndSize;

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
        public int DateTimeThresholdSeconds { get; set; } = 2;

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
        public ConflictResolution ConflictResolution { get; set; } = ConflictResolution.Newer;

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
        public IEnumerable<string> IgnorePatterns { get; set; } = new List<string>
        {
            "**/System Volume Information/**",
            "**/$RECYCLE.BIN/**",
            "**/Thumbs.db",
            "**/*.tmp",
            "**/*.temp",
            "**/*.bak"
        };
    }

    /// <summary>
    /// 同步模式
    /// </summary>
    public enum SyncMode
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
    public enum CompareMethod
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
    /// 哈希算法类型
    /// </summary>
    public enum HashAlgorithmType
    {
        MD5,
        SHA1,
        SHA256,
        SHA384,
        SHA512
    }

    /// <summary>
    /// 冲突解决策略
    /// </summary>
    public enum ConflictResolution
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
    public enum SyncActionType
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
    public enum SyncDirection
    {
        SourceToTarget,
        TargetToSource
    }

    /// <summary>
    /// 同步操作状态
    /// </summary>
    public enum SyncActionStatus
    {
        Pending,
        Running,
        Completed,
        Failed
    }

    /// <summary>
    /// 同步状态
    /// </summary>
    public enum SyncStatus
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
        public SyncActionType ActionType { get; set; }

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
        public SyncDirection Direction { get; set; } = SyncDirection.SourceToTarget;

        /// <summary>
        /// 操作状态
        /// </summary>
        public SyncActionStatus Status { get; set; } = SyncActionStatus.Pending;

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 冲突解决策略
        /// </summary>
        public ConflictResolution? ConflictResolution { get; set; }
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
        public SyncMode Mode { get; set; }

        /// <summary>
        /// 同步状态
        /// </summary>
        public SyncStatus Status { get; set; }

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
        public bool IsSuccessful => Status == SyncStatus.Completed;

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
            if (Status == SyncStatus.NotStarted)
                return "同步尚未开始";

            if (Status == SyncStatus.Failed)
                return $"同步失败: {ErrorMessage}";

            if (Status == SyncStatus.Canceled)
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
        private string GetSyncModeDescription(SyncMode mode)
        {
            return mode switch
            {
                SyncMode.OneWay => "单向同步",
                SyncMode.Mirror => "镜像同步",
                SyncMode.TwoWay => "双向同步",
                _ => mode.ToString()
            };
        }

        /// <summary>
        /// 获取同步状态的描述
        /// </summary>
        private string GetStatusDescription(SyncStatus status)
        {
            return status switch
            {
                SyncStatus.NotStarted => "未开始",
                SyncStatus.Started => "已开始",
                SyncStatus.Running => "运行中",
                SyncStatus.Completed => "已完成",
                SyncStatus.Failed => "失败",
                SyncStatus.Canceled => "已取消",
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
                        var status = action.Status == SyncActionStatus.Completed ? "✓" :
                                     action.Status == SyncActionStatus.Failed ? "✗" : "?";

                        var path = action.RelativePath?.Length > 80
                            ? "..." + action.RelativePath.Substring(action.RelativePath.Length - 77)
                            : action.RelativePath;

                        report.AppendLine($"{status} {path ?? "-"}");

                        if (action.Status == SyncActionStatus.Failed && !string.IsNullOrEmpty(action.ErrorMessage))
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
        private string GetActionTypeDescription(SyncActionType actionType)
        {
            return actionType switch
            {
                SyncActionType.CreateDirectory => "创建目录",
                SyncActionType.CopyFile => "复制文件",
                SyncActionType.UpdateFile => "更新文件",
                SyncActionType.DeleteFile => "删除文件",
                SyncActionType.DeleteDirectory => "删除目录",
                SyncActionType.RenameFile => "重命名文件",
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
            else if (this.Status == SyncStatus.Canceled)
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
                Status = SyncStatus.Failed,
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
                Status = SyncStatus.Canceled,
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