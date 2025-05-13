using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.RegularExpressions;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 高性能文件扫描器，基于 Kopia 风格设计，针对 .NET Core 优化
    /// </summary>
    public class FileFastScannerKopia
    {
        private readonly int _maxParallelism;
        private readonly int _queueCapacity;
        private readonly IProgress<ScanProgress> _progress;
        private readonly ScanOptions _options;
        private readonly ConcurrentDictionary<string, bool> _visitedSymlinks;
        private readonly ConcurrentDictionary<string, bool> _visitedDirectories;

        /// <summary>
        /// 创建高性能文件扫描器实例
        /// </summary>
        /// <param name="options">扫描选项</param>
        /// <param name="maxParallelism">最大并行度，默认为处理器核心数的两倍</param>
        /// <param name="queueCapacity">队列容量，默认为 100,000</param>
        /// <param name="progress">进度报告回调</param>
        public FileFastScannerKopia(
            ScanOptions options = null,
            int? maxParallelism = null,
            int queueCapacity = 100_000,
            IProgress<ScanProgress> progress = null)
        {
            _options = options ?? new ScanOptions();
            _maxParallelism = maxParallelism ?? Environment.ProcessorCount * 2;
            _queueCapacity = queueCapacity;
            _progress = progress;
            _visitedSymlinks = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _visitedDirectories = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
        }

        /// <summary>
        /// 异步扫描指定路径下的所有文件和目录
        /// </summary>
        /// <param name="rootPath">要扫描的根路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>扫描结果</returns>
        public async Task<ScanResult> ScanAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(rootPath))
                throw new ArgumentException("根路径不能为空", nameof(rootPath));

            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"目录不存在: {rootPath}");

            var result = new ScanResult
            {
                RootPath = rootPath,
                StartTime = DateTime.UtcNow
            };

            var stats = new ScanStatistics();
            var stopwatch = Stopwatch.StartNew();
            var lastProgressUpdate = DateTime.UtcNow;
            var progressUpdateInterval = TimeSpan.FromMilliseconds(200);

            // 创建容量有限的阻塞集合作为工作队列
            using var queue = new BlockingCollection<WorkItem>(_queueCapacity);
            var errors = new ConcurrentBag<ScanError>();

            // 添加根目录作为第一个工作项
            queue.Add(new WorkItem { Path = rootPath, Depth = 0 });
            _visitedDirectories.TryAdd(rootPath, true);

            // 创建并启动工作任务
            var tasks = new Task[_maxParallelism];
            for (int i = 0; i < _maxParallelism; i++)
            {
                tasks[i] = Task.Run(() => ProcessQueue(queue, stats, errors, cancellationToken), cancellationToken);
            }

            // 标记队列为完成添加
            var completionTask = Task.Run(async () =>
            {
                try
                {
                    // 等待所有工作项处理完毕
                    while (!queue.IsCompleted && !cancellationToken.IsCancellationRequested)
                    {
                        // 定期报告进度
                        if (_progress != null && (DateTime.UtcNow - lastProgressUpdate) > progressUpdateInterval)
                        {
                            ReportProgress(stats, stopwatch.Elapsed, result, queue.Count);
                            lastProgressUpdate = DateTime.UtcNow;
                        }
                        await Task.Delay(50, cancellationToken);

                        // 检查队列是否为空但仍有任务在处理 - 这是防止死锁的额外安全措施
                        if (queue.Count == 0 && tasks.All(t => t.Status == TaskStatus.WaitingForActivation))
                        {
                            break;
                        }
                    }
                }
                finally
                {
                    // 确保队列被标记为完成添加
                    if (!queue.IsAddingCompleted)
                        queue.CompleteAdding();
                }
            }, cancellationToken);

            // 等待所有任务完成
            try
            {
                await Task.WhenAll(tasks.Append(completionTask).ToArray());
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 扫描被取消，记录取消状态
                result.WasCancelled = true;
            }
            catch (Exception ex)
            {
                errors.Add(new ScanError
                {
                    Path = rootPath,
                    ErrorMessage = $"扫描过程中发生未处理异常: {ex.Message}",
                    Exception = ex
                });
            }

            stopwatch.Stop();
            result.ElapsedTime = stopwatch.Elapsed;
            result.TotalFiles = stats.FileCount;
            result.TotalDirectories = stats.DirectoryCount;
            result.TotalSize = stats.TotalSize;
            result.Errors = errors.ToList();
            result.EndTime = DateTime.UtcNow;

            // 最终进度更新
            if (_progress != null)
            {
                ReportProgress(stats, result.ElapsedTime, result, 0);
            }

            return result;
        }

        private void ReportProgress(ScanStatistics stats, TimeSpan elapsed, ScanResult result, int queueSize)
        {
            _progress?.Report(new ScanProgress
            {
                FilesProcessed = stats.FileCount,
                DirectoriesProcessed = stats.DirectoryCount,
                BytesProcessed = stats.TotalSize,
                ElapsedTime = elapsed,
                CurrentQueueSize = queueSize,
                ItemsPerSecond = elapsed.TotalSeconds > 0
                    ? (stats.FileCount + stats.DirectoryCount) / elapsed.TotalSeconds
                    : 0,
                CurrentResult = result
            });
        }

        private void ProcessQueue(
            BlockingCollection<WorkItem> queue,
            ScanStatistics stats,
            ConcurrentBag<ScanError> errors,
            CancellationToken cancellationToken)
        {
            try
            {
                foreach (var item in queue.GetConsumingEnumerable(cancellationToken))
                {
                    try
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        ProcessWorkItem(item, queue, stats, errors, cancellationToken);
                    }
                    catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
                    {
                        break;
                    }
                    catch (Exception ex)
                    {
                        errors.Add(new ScanError
                        {
                            Path = item.Path,
                            ErrorMessage = ex.Message,
                            Exception = ex
                        });
                    }
                }
            }
            catch (OperationCanceledException) when (cancellationToken.IsCancellationRequested)
            {
                // 任务取消，正常退出
            }
            catch (InvalidOperationException)
            {
                // 队列已关闭，正常退出
            }
        }

        private void ProcessWorkItem(
            WorkItem item,
            BlockingCollection<WorkItem> queue,
            ScanStatistics stats,
            ConcurrentBag<ScanError> errors,
            CancellationToken cancellationToken)
        {
            // 检查是否超过最大深度
            if (_options.MaxDepth > 0 && item.Depth > _options.MaxDepth)
                return;

            try
            {
                var dirInfo = new DirectoryInfo(item.Path);

                // 检查是否为符号链接
                bool isSymlink = IsSymbolicLink(dirInfo);
                if (isSymlink)
                {
                    if (!_options.FollowSymlinks)
                        return;

                    // 处理符号链接循环
                    string target = GetSymlinkTarget(dirInfo.FullName);
                    if (string.IsNullOrEmpty(target) || !_visitedSymlinks.TryAdd(target, true))
                        return;
                }

                // 目录计数递增
                Interlocked.Increment(ref stats.DirectoryCount);

                // 处理当前目录中的文件
                IEnumerable<FileInfo> files;
                try
                {
                    files = dirInfo.EnumerateFiles();
                }
                catch (Exception ex)
                {
                    errors.Add(new ScanError
                    {
                        Path = item.Path,
                        ErrorMessage = $"枚举文件失败: {ex.Message}",
                        Exception = ex
                    });
                    files = Enumerable.Empty<FileInfo>();
                }

                // 使用并行处理文件，但限制并行度以避免过度并行
                Parallel.ForEach(
                    files,
                    new ParallelOptions
                    {
                        MaxDegreeOfParallelism = Math.Max(1, _maxParallelism / 4),
                        CancellationToken = cancellationToken
                    },
                    file =>
                    {
                        if (cancellationToken.IsCancellationRequested)
                            return;

                        try
                        {
                            if (ShouldProcessFile(file))
                            {
                                // 文件计数递增
                                Interlocked.Increment(ref stats.FileCount);
                                // 总大小递增
                                Interlocked.Add(ref stats.TotalSize, file.Length);

                                // 如果有文件处理回调，则执行
                                _options.FileFoundCallback?.Invoke(new FileEntry
                                {
                                    Path = file.FullName,
                                    Size = file.Length,
                                    CreationTime = file.CreationTimeUtc,
                                    LastWriteTime = file.LastWriteTimeUtc,
                                    LastAccessTime = file.LastAccessTimeUtc,
                                    Attributes = file.Attributes
                                });
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add(new ScanError
                            {
                                Path = file.FullName,
                                ErrorMessage = $"处理文件失败: {ex.Message}",
                                Exception = ex
                            });
                        }
                    });

                // 处理子目录
                IEnumerable<DirectoryInfo> subdirs;
                try
                {
                    subdirs = dirInfo.EnumerateDirectories();
                }
                catch (Exception ex)
                {
                    errors.Add(new ScanError
                    {
                        Path = item.Path,
                        ErrorMessage = $"枚举子目录失败: {ex.Message}",
                        Exception = ex
                    });
                    subdirs = Enumerable.Empty<DirectoryInfo>();
                }

                foreach (var subdir in subdirs)
                {
                    if (cancellationToken.IsCancellationRequested)
                        break;

                    if (ShouldProcessDirectory(subdir))
                    {
                        string fullPath = subdir.FullName;

                        // 避免处理已访问过的目录（处理硬链接和交叉文件系统挂载点）
                        if (_visitedDirectories.TryAdd(fullPath, true))
                        {
                            // 添加子目录到队列
                            queue.Add(new WorkItem
                            {
                                Path = fullPath,
                                Depth = item.Depth + 1
                            });
                        }
                    }
                }
            }
            catch (DirectoryNotFoundException)
            {
                // 目录在扫描过程中被删除，忽略
            }
            catch (UnauthorizedAccessException ex)
            {
                errors.Add(new ScanError
                {
                    Path = item.Path,
                    ErrorMessage = $"访问被拒绝: {ex.Message}",
                    Exception = ex
                });
            }
            catch (Exception ex)
            {
                errors.Add(new ScanError
                {
                    Path = item.Path,
                    ErrorMessage = ex.Message,
                    Exception = ex
                });
            }
        }

        private bool ShouldProcessFile(FileInfo file)
        {
            try
            {
                if (_options.IgnoreHiddenFiles && (file.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                    return false;

                if (_options.IgnoreSystemFiles && (file.Attributes & FileAttributes.System) == FileAttributes.System)
                    return false;

                if (_options.ExcludePatterns != null && _options.ExcludePatterns.Any(pattern =>
                    MatchesPattern(file.Name, pattern)))
                    return false;

                if (_options.IncludePatterns != null && _options.IncludePatterns.Any() &&
                    !_options.IncludePatterns.Any(pattern =>
                        MatchesPattern(file.Name, pattern)))
                    return false;

                if (_options.MinFileSize.HasValue && file.Length < _options.MinFileSize.Value)
                    return false;

                if (_options.MaxFileSize.HasValue && file.Length > _options.MaxFileSize.Value)
                    return false;

                if (_options.MinAge.HasValue && file.LastWriteTimeUtc > DateTime.UtcNow.Subtract(_options.MinAge.Value))
                    return false;

                if (_options.MaxAge.HasValue && file.LastWriteTimeUtc < DateTime.UtcNow.Subtract(_options.MaxAge.Value))
                    return false;

                return true;
            }
            catch
            {
                // 文件访问错误，忽略此文件
                return false;
            }
        }

        private bool ShouldProcessDirectory(DirectoryInfo dir)
        {
            try
            {
                string dirName = dir.Name;

                if (_options.IgnoreHiddenDirectories && (dir.Attributes & FileAttributes.Hidden) == FileAttributes.Hidden)
                    return false;

                if (_options.IgnoreSystemDirectories && (dir.Attributes & FileAttributes.System) == FileAttributes.System)
                    return false;

                if (_options.ExcludeDirectories != null && _options.ExcludeDirectories.Contains(dirName, StringComparer.OrdinalIgnoreCase))
                    return false;

                if (_options.ExcludePatterns != null && _options.ExcludePatterns.Any(pattern =>
                    MatchesPattern(dirName, pattern)))
                    return false;

                return true;
            }
            catch
            {
                // 目录访问错误，忽略此目录
                return false;
            }
        }

        private bool MatchesPattern(string name, string pattern)
        {
            return Regex.IsMatch(name, WildcardToRegex(pattern), RegexOptions.IgnoreCase);
        }

        private static string WildcardToRegex(string pattern)
        {
            return "^" + Regex.Escape(pattern)
                      .Replace("\\*", ".*")
                      .Replace("\\?", ".") + "$";
        }

        private bool IsSymbolicLink(FileSystemInfo info)
        {
            return (info.Attributes & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        private string GetSymlinkTarget(string path)
        {
            try
            {
                if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
                {
                    return Path.GetFullPath(path);
                }
                else
                {
                    // Unix systems
                    return Path.GetFullPath(System.IO.File.ReadAllText(path));
                }
            }
            catch
            {
                return null;
            }
        }

        /// <summary>
        /// 工作项，表示待处理的目录
        /// </summary>
        private class WorkItem
        {
            public string Path { get; set; }
            public int Depth { get; set; }
        }

        /// <summary>
        /// 扫描统计信息
        /// </summary>
        public class ScanStatistics
        {
            public long FileCount;
            public long DirectoryCount;
            public long TotalSize;
        }
    }

    /// <summary>
    /// 扫描选项
    /// </summary>
    public class ScanOptions
    {
        /// <summary>
        /// 最大扫描深度，0 表示无限制
        /// </summary>
        public int MaxDepth { get; set; } = 0;

        /// <summary>
        /// 是否跟踪符号链接
        /// </summary>
        public bool FollowSymlinks { get; set; } = false;

        /// <summary>
        /// 是否忽略隐藏文件
        /// </summary>
        public bool IgnoreHiddenFiles { get; set; } = true;

        /// <summary>
        /// 是否忽略系统文件
        /// </summary>
        public bool IgnoreSystemFiles { get; set; } = true;

        /// <summary>
        /// 是否忽略隐藏目录
        /// </summary>
        public bool IgnoreHiddenDirectories { get; set; } = true;

        /// <summary>
        /// 是否忽略系统目录
        /// </summary>
        public bool IgnoreSystemDirectories { get; set; } = true;

        /// <summary>
        /// 要排除的目录名列表
        /// </summary>
        public HashSet<string> ExcludeDirectories { get; set; } = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "$RECYCLE.BIN",
            "System Volume Information",
            "RECYCLER",
            "RECYCLED",
            "lost+found",
            "node_modules",
            ".git",
            ".svn",
            "bin",
            "obj",
            "packages"
        };

        /// <summary>
        /// 排除的文件或目录通配符模式列表
        /// </summary>
        public List<string> ExcludePatterns { get; set; } = new List<string>
        {
            "*.tmp",
            "~*",
            "thumbs.db",
            "*.swp",
            "*.bak",
            "*.log",
            "*.cache"
        };

        /// <summary>
        /// 包含的文件通配符模式列表，如果为空，则包含所有文件
        /// </summary>
        public List<string> IncludePatterns { get; set; } = new List<string>();

        /// <summary>
        /// 最小文件大小（字节）
        /// </summary>
        public long? MinFileSize { get; set; } = null;

        /// <summary>
        /// 最大文件大小（字节）
        /// </summary>
        public long? MaxFileSize { get; set; } = null;

        /// <summary>
        /// 文件最小年龄（从现在起向后计算）
        /// </summary>
        public TimeSpan? MinAge { get; set; } = null;

        /// <summary>
        /// 文件最大年龄（从现在起向后计算）
        /// </summary>
        public TimeSpan? MaxAge { get; set; } = null;

        /// <summary>
        /// 找到文件时的回调函数
        /// </summary>
        public Action<FileEntry> FileFoundCallback { get; set; } = null;
    }

    /// <summary>
    /// 扫描结果
    /// </summary>
    public class ScanResult
    {
        /// <summary>
        /// 扫描的根路径
        /// </summary>
        public string RootPath { get; set; }

        /// <summary>
        /// 扫描开始时间
        /// </summary>
        public DateTime StartTime { get; set; }

        /// <summary>
        /// 扫描结束时间
        /// </summary>
        public DateTime EndTime { get; set; }

        /// <summary>
        /// 扫描耗时
        /// </summary>
        public TimeSpan ElapsedTime { get; set; }

        /// <summary>
        /// 扫描的文件总数
        /// </summary>
        public long TotalFiles { get; set; }

        /// <summary>
        /// 扫描的目录总数
        /// </summary>
        public long TotalDirectories { get; set; }

        /// <summary>
        /// 扫描的文件总大小（字节）
        /// </summary>
        public long TotalSize { get; set; }

        /// <summary>
        /// 扫描过程中的错误列表
        /// </summary>
        public List<ScanError> Errors { get; set; } = new List<ScanError>();

        /// <summary>
        /// 指示扫描是否被取消
        /// </summary>
        public bool WasCancelled { get; set; }

        /// <summary>
        /// 每秒处理的项目数
        /// </summary>
        public double ItemsPerSecond => ElapsedTime.TotalSeconds > 0
            ? (TotalFiles + TotalDirectories) / ElapsedTime.TotalSeconds
            : 0;

        /// <summary>
        /// 平均处理速度（字节/秒）
        /// </summary>
        public double BytesPerSecond => ElapsedTime.TotalSeconds > 0
            ? TotalSize / ElapsedTime.TotalSeconds
            : 0;

        /// <summary>
        /// 获取格式化的总大小（自动选择合适的单位）
        /// </summary>
        public string FormattedTotalSize => FormatSize(TotalSize);

        /// <summary>
        /// 获取格式化的处理速度（自动选择合适的单位）
        /// </summary>
        public string FormattedBytesPerSecond => $"{FormatSize(BytesPerSecond)}/s";

        private static string FormatSize(double bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB", "PB" };
            int unitIndex = 0;

            while (bytes >= 1024 && unitIndex < units.Length - 1)
            {
                bytes /= 1024;
                unitIndex++;
            }

            return $"{bytes:0.##} {units[unitIndex]}";
        }
    }

    /// <summary>
    /// 文件条目信息
    /// </summary>
    public class FileEntry
    {
        /// <summary>
        /// 文件完整路径
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 文件大小（字节）
        /// </summary>
        public long Size { get; set; }

        /// <summary>
        /// 文件创建时间（UTC）
        /// </summary>
        public DateTime CreationTime { get; set; }

        /// <summary>
        /// 文件最后修改时间（UTC）
        /// </summary>
        public DateTime LastWriteTime { get; set; }

        /// <summary>
        /// 文件最后访问时间（UTC）
        /// </summary>
        public DateTime LastAccessTime { get; set; }

        /// <summary>
        /// 文件属性
        /// </summary>
        public FileAttributes Attributes { get; set; }

        /// <summary>
        /// 获取格式化的文件大小
        /// </summary>
        public string FormattedSize
        {
            get
            {
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double size = Size;
                int unit = 0;

                while (size >= 1024 && unit < units.Length - 1)
                {
                    size /= 1024;
                    unit++;
                }

                return $"{size:0.##} {units[unit]}";
            }
        }
    }

    /// <summary>
    /// 扫描错误信息
    /// </summary>
    public class ScanError
    {
        /// <summary>
        /// 发生错误的路径
        /// </summary>
        public string Path { get; set; }

        /// <summary>
        /// 错误消息
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// 原始异常
        /// </summary>
        public Exception Exception { get; set; }
    }

    /// <summary>
    /// 扫描进度信息
    /// </summary>
    public class ScanProgress
    {
        /// <summary>
        /// 已处理的文件数
        /// </summary>
        public long FilesProcessed { get; set; }

        /// <summary>
        /// 已处理的目录数
        /// </summary>
        public long DirectoriesProcessed { get; set; }

        /// <summary>
        /// 已处理的字节数
        /// </summary>
        public long BytesProcessed { get; set; }

        /// <summary>
        /// 已用时间
        /// </summary>
        public TimeSpan ElapsedTime { get; set; }

        /// <summary>
        /// 当前队列大小
        /// </summary>
        public int CurrentQueueSize { get; set; }

        /// <summary>
        /// 每秒处理的项目数
        /// </summary>
        public double ItemsPerSecond { get; set; }

        /// <summary>
        /// 当前扫描结果（部分完成）
        /// </summary>
        public ScanResult CurrentResult { get; set; }

        /// <summary>
        /// 获取格式化的处理速度
        /// </summary>
        public string FormattedBytesPerSecond
        {
            get
            {
                string[] units = { "B", "KB", "MB", "GB", "TB" };
                double bytesPerSec = ElapsedTime.TotalSeconds > 0
                    ? BytesProcessed / ElapsedTime.TotalSeconds
                    : 0;
                int unit = 0;

                while (bytesPerSec >= 1024 && unit < units.Length - 1)
                {
                    bytesPerSec /= 1024;
                    unit++;
                }

                return $"{bytesPerSec:0.##} {units[unit]}/s";
            }
        }
    }
}