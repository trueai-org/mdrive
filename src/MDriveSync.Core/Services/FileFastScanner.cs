using System.Collections.Concurrent;
using System.Diagnostics;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 高性能文件系统遍历工具
    /// </summary>
    public class FileFastScanner
    {
        /// <summary>
        /// 采用生产者-消费者模式遍历文件系统并返回文件路径集合
        /// </summary>
        /// <param name="rootPath">根路径</param>
        /// <param name="searchPattern">搜索模式，默认为"*"</param>
        /// <param name="ignorePatterns">忽略的模式列表</param>
        /// <param name="maxDegreeOfParallelism">最大并行度，默认为处理器数量</param>
        /// <param name="errorHandler">错误处理器</param>
        /// <param name="progress">进度报告回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>文件路径的集合</returns>
        public static IEnumerable<string> EnumerateFiles(
            string rootPath,
            string searchPattern = "*",
            IEnumerable<string> ignorePatterns = null,
            int? maxDegreeOfParallelism = null,
            Action<string, Exception> errorHandler = null,
            IProgress<ScanProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentException("根路径不能为空", nameof(rootPath));

            // 规范化根路径
            rootPath = Path.GetFullPath(rootPath);

            // 创建忽略规则集
            var ignoreRules = new FileIgnoreRuleSet(rootPath, ignorePatterns);

            // 并行度设置
            int parallelism = maxDegreeOfParallelism ?? Math.Max(1, Environment.ProcessorCount);

            // 创建生产者-消费者模式的数据结构
            var directoryQueue = new BlockingCollection<string>(new ConcurrentQueue<string>(), 10000);
            var fileResults = new BlockingCollection<string>(new ConcurrentQueue<string>(), 100000);

            // 进度跟踪
            var stopwatch = Stopwatch.StartNew();
            long processedItems = 0;
            int dirCount = 0;
            int fileCount = 0;
            DateTime lastProgressReport = DateTime.UtcNow;
            const int progressIntervalMs = 100; // 默认进度报告间隔毫秒数

            // 启动目录生产者任务
            var directoryProducerTask = Task.Run(() =>
            {
                try
                {
                    // 第一个目录是根目录（如果不应被忽略）
                    if (!ignoreRules.ShouldIgnore(rootPath))
                    {
                        directoryQueue.Add(rootPath);
                        Interlocked.Increment(ref dirCount);
                        Interlocked.Increment(ref processedItems);
                    }

                    // 使用广度优先遍历而不是预先收集所有目录
                    ProcessDirectories(rootPath, ignoreRules, directoryQueue, errorHandler,
                        ref processedItems, ref dirCount, cancellationToken);
                }
                finally
                {
                    // 标记目录队列已完成
                    directoryQueue.CompleteAdding();
                }
            }, cancellationToken);

            // 启动文件处理任务
            var fileProcessorTask = Task.Run(() =>
            {
                // 创建并行选项
                var options = new ParallelOptions
                {
                    MaxDegreeOfParallelism = parallelism,
                    CancellationToken = cancellationToken
                };

                // 从目录队列中并行处理目录
                try
                {
                    Parallel.ForEach(directoryQueue.GetConsumingEnumerable(), options, directory =>
                    {
                        // 检查是否取消
                        cancellationToken.ThrowIfCancellationRequested();

                        try
                        {
                            // 处理单个目录中的文件
                            foreach (var file in ProcessFiles(directory, searchPattern, ignoreRules, errorHandler, cancellationToken))
                            {
                                fileResults.Add(file);
                                Interlocked.Increment(ref fileCount);
                                Interlocked.Increment(ref processedItems);

                                // 检查是否需要报告进度
                                if (progress != null && (DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= progressIntervalMs)
                                {
                                    ReportProgress(progress, ref lastProgressReport, stopwatch, processedItems, dirCount, fileCount, directoryQueue.Count);
                                }
                            }
                        }
                        catch (OperationCanceledException)
                        {
                            throw; // 重新抛出取消异常
                        }
                        catch (Exception ex)
                        {
                            errorHandler?.Invoke(directory, ex);
                        }
                    });
                }
                catch (OperationCanceledException)
                {
                    // 任务被取消，无需处理
                }
                finally
                {
                    // 所有目录处理完成后，标记文件结果集已完成
                    fileResults.CompleteAdding();
                }
            }, cancellationToken);

            // 返回文件结果
            foreach (var file in fileResults.GetConsumingEnumerable())
            {
                // 如果请求取消，提前退出遍历
                if (cancellationToken.IsCancellationRequested)
                    break;

                yield return file;
            }

            try
            {
                // 确保所有任务完成
                Task.WaitAll(new[] { directoryProducerTask, fileProcessorTask }, cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 任务被取消，无需处理
            }
            finally
            {
                // 报告最终进度
                if (progress != null)
                {
                    stopwatch.Stop();
                    progress.Report(new ScanProgress
                    {
                        ProcessedItems = processedItems,
                        DirectoryCount = dirCount,
                        FileCount = fileCount,
                        QueueCount = 0,
                        ElapsedTime = stopwatch.Elapsed,
                        IsComplete = true
                    });
                }
            }
        }

        /// <summary>
        /// 异步扫描文件系统并返回包含更多信息的结果
        /// </summary>
        /// <param name="rootPath">根路径</param>
        /// <param name="searchPattern">搜索模式，默认为"*"</param>
        /// <param name="ignorePatterns">忽略的模式列表</param>
        /// <param name="maxDegreeOfParallelism">最大并行度，默认为处理器数量</param>
        /// <param name="reportProgress">是否报告进度</param>
        /// <param name="progress">进度报告回调</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>扫描结果</returns>
        public static FileScanResult ScanAsync(
            string rootPath,
            string searchPattern = "*",
            IEnumerable<string> ignorePatterns = null,
            int? maxDegreeOfParallelism = null,
            bool reportProgress = false,
            IProgress<ScanProgress> progress = null,
            CancellationToken cancellationToken = default)
        {
            var startTime = DateTime.UtcNow;
            var stopwatch = Stopwatch.StartNew();
            var errors = new ConcurrentBag<string>();

            var files = new List<string>();

            // 错误处理器
            Action<string, Exception> errorHandler = (path, ex) =>
            {
                errors.Add($"{path}: {ex.Message}");
            };

            try
            {
                // 使用枚举器收集文件
                files = EnumerateFiles(
                    rootPath,
                    searchPattern,
                    ignorePatterns,
                    maxDegreeOfParallelism,
                    errorHandler,
                    progress,
                    cancellationToken).ToList();

                stopwatch.Stop();

                return new FileScanResult
                {
                    RootPath = rootPath,
                    FileCount = files.Count,
                    DirectoryCount = 0, // 我们不保留目录列表，所以这里未填充
                    Files = files,
                    Directories = null, // 我们不保留目录列表
                    Errors = errors.ToList(),
                    ElapsedTime = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    WasCancelled = cancellationToken.IsCancellationRequested
                };
            }
            catch (OperationCanceledException)
            {
                // 扫描被取消
                stopwatch.Stop();

                return new FileScanResult
                {
                    RootPath = rootPath,
                    FileCount = files.Count,
                    DirectoryCount = 0,
                    Files = files,
                    Directories = null,
                    Errors = errors.ToList(),
                    ElapsedTime = stopwatch.Elapsed,
                    StartTime = startTime,
                    EndTime = DateTime.UtcNow,
                    WasCancelled = true
                };
            }
        }

        /// <summary>
        /// 处理目录（流式方式）
        /// </summary>
        private static void ProcessDirectories(
            string rootPath,
            FileIgnoreRuleSet ignoreRules,
            BlockingCollection<string> directoryQueue,
            Action<string, Exception> errorHandler,
            ref long processedItems,
            ref int dirCount,
            CancellationToken cancellationToken)
        {
            // 目录遍历设置
            var options = new EnumerationOptions
            {
                RecurseSubdirectories = false,
                AttributesToSkip = 0,
                IgnoreInaccessible = true
            };

            // 已处理目录集合，避免循环引用
            var processedDirs = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            processedDirs.Add(rootPath);

            // 使用队列进行广度优先遍历，但不预先加载所有目录
            var pendingDirs = new Queue<string>();
            pendingDirs.Enqueue(rootPath);

            while (pendingDirs.Count > 0 && !cancellationToken.IsCancellationRequested)
            {
                string currentDir = pendingDirs.Dequeue();

                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(currentDir, "*", options))
                    {
                        // 检查取消状态
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        // 检查目录是否应被忽略
                        if (!ignoreRules.ShouldIgnore(subDir) && processedDirs.Add(subDir))
                        {
                            directoryQueue.Add(subDir);
                            pendingDirs.Enqueue(subDir);
                            Interlocked.Increment(ref dirCount);
                            Interlocked.Increment(ref processedItems);
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw; // 重新抛出取消异常
                }
                catch (Exception ex) when (ex is UnauthorizedAccessException ||
                                          ex is DirectoryNotFoundException ||
                                          ex is IOException)
                {
                    errorHandler?.Invoke(currentDir, ex);
                }
            }
        }

        /// <summary>
        /// 处理单个目录中的文件
        /// </summary>
        private static IEnumerable<string> ProcessFiles(
            string directory,
            string searchPattern,
            FileIgnoreRuleSet ignoreRules,
            Action<string, Exception> errorHandler,
            CancellationToken cancellationToken)
        {
            if (string.IsNullOrEmpty(searchPattern))
            {
                searchPattern = "*";
            }

            // 避免在包含yield return的方法体中使用try-catch
            IEnumerable<string> files;
            try
            {
                var options = new EnumerationOptions
                {
                    RecurseSubdirectories = false, // 不递归子目录
                    //AttributesToSkip = 0, // 不跳过任何文件 // FileAttributes.System,
                    AttributesToSkip = FileAttributes.System,
                    IgnoreInaccessible = true // 忽略访问权限受限的文件
                };

                files = Directory.EnumerateFiles(directory, searchPattern, options);
            }
            catch (OperationCanceledException)
            {
                throw; // 重新抛出取消异常
            }
            catch (Exception ex) when (ex is UnauthorizedAccessException ||
                                      ex is DirectoryNotFoundException ||
                                      ex is IOException)
            {
                errorHandler?.Invoke(directory, ex);
                yield break;
            }

            // 处理找到的文件
            foreach (var file in files)
            {
                // 检查取消状态
                if (cancellationToken.IsCancellationRequested)
                    yield break;

                // 检查文件是否应被忽略
                if (!ignoreRules.ShouldIgnore(file))
                {
                    yield return file;
                }
            }
        }

        /// <summary>
        /// 报告扫描进度
        /// </summary>
        private static void ReportProgress(
            IProgress<ScanProgress> progress,
            ref DateTime lastReportTime,
            Stopwatch stopwatch,
            long processedItems,
            int dirCount,
            int fileCount,
            int queueCount)
        {
            lastReportTime = DateTime.UtcNow;

            progress.Report(new ScanProgress
            {
                ProcessedItems = processedItems,
                DirectoryCount = dirCount,
                FileCount = fileCount,
                QueueCount = queueCount,
                ElapsedTime = stopwatch.Elapsed,
                IsComplete = false
            });
        }

        /// <summary>
        /// 扫描进度信息类
        /// </summary>
        public class ScanProgress
        {
            /// <summary>
            /// 已处理的项目数（文件和目录）
            /// </summary>
            public long ProcessedItems { get; set; }

            /// <summary>
            /// 已扫描的文件数
            /// </summary>
            public int FileCount { get; set; }

            /// <summary>
            /// 已扫描的目录数
            /// </summary>
            public int DirectoryCount { get; set; }

            /// <summary>
            /// 待处理队列中的项目数
            /// </summary>
            public int QueueCount { get; set; }

            /// <summary>
            /// 已消耗时间
            /// </summary>
            public TimeSpan ElapsedTime { get; set; }

            /// <summary>
            /// 是否完成扫描
            /// </summary>
            public bool IsComplete { get; set; }

            /// <summary>
            /// 每秒处理项目数
            /// </summary>
            public double ItemsPerSecond => ElapsedTime.TotalSeconds > 0
                ? ProcessedItems / ElapsedTime.TotalSeconds
                : 0;
        }

        /// <summary>
        /// 获取只包含文件路径的列表（无其他元数据）
        /// </summary>
        /// <param name="rootPath">根路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>文件路径列表</returns>
        public static List<string> GetFilesOnlyAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            var result = ScanAsync(rootPath, cancellationToken: cancellationToken);
            return result.Files;
        }
    }
}
