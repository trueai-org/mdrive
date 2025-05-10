using System.Collections.Concurrent;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 与 Kopia 算法保持一致的超高性能文件扫描器
    /// </summary>
    public class FileUltraScanner
    {
        // Windows API 相关常量
        private const int MAX_PATH = 260;

        private const int MAX_ALTERNATIVE_PATH = 32767;

        private const int FILE_ATTRIBUTE_DIRECTORY = 0x10;
        private const int FILE_ATTRIBUTE_REPARSE_POINT = 0x400;

        private const int ERROR_NO_MORE_FILES = 18;
        private const uint INVALID_FILE_ATTRIBUTES = 0xFFFFFFFF;

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Auto)]
        private struct WIN32_FIND_DATA
        {
            public FileAttributes dwFileAttributes;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftCreationTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastAccessTime;
            public System.Runtime.InteropServices.ComTypes.FILETIME ftLastWriteTime;
            public uint nFileSizeHigh;
            public uint nFileSizeLow;
            public uint dwReserved0;
            public uint dwReserved1;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = MAX_PATH)]
            public string cFileName;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 14)]
            public string cAlternateFileName;
        }

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern IntPtr FindFirstFile(string lpFileName, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern bool FindNextFile(IntPtr hFindFile, out WIN32_FIND_DATA lpFindFileData);

        [DllImport("kernel32.dll", SetLastError = true)]
        [return: MarshalAs(UnmanagedType.Bool)]
        private static extern bool FindClose(IntPtr hFindFile);

        [DllImport("kernel32.dll", SetLastError = true, CharSet = CharSet.Auto)]
        private static extern uint GetFileAttributes(string lpFileName);

        // Linux/macOS 相关结构和方法
        [DllImport("libc", SetLastError = true, EntryPoint = "opendir")]
        private static extern IntPtr OpenDir(string path);

        [DllImport("libc", SetLastError = true, EntryPoint = "readdir")]
        private static extern IntPtr ReadDir(IntPtr dir);

        [DllImport("libc", EntryPoint = "closedir")]
        private static extern int CloseDir(IntPtr dir);

        [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
        private struct Dirent
        {
            public ulong d_ino;
            public long d_off;
            public ushort d_reclen;
            public byte d_type;

            [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)]
            public string d_name;
        }

        private const byte DT_DIR = 4;  // 目录类型
        private const byte DT_REG = 8;  // 普通文件类型
        private const byte DT_LNK = 10; // 符号链接类型

        // 内部状态和设置
        private readonly int _maxConcurrency;

        private readonly int _batchSize;
        private readonly ConcurrentBag<string> _files;
        private readonly ConcurrentBag<string> _directories;
        private readonly ConcurrentBag<string> _errors;
        private readonly ConcurrentDictionary<string, bool> _visitedPaths;
        private long _processedItems;
        private readonly Stopwatch _stopwatch;
        private readonly bool _followSymlinks;
        private readonly FileIgnoreRuleSet _fileIgnoreRuleSet;
        private readonly string _rootPath;

        /// <summary>
        /// 创建超高性能文件扫描器
        /// </summary>
        /// <param name="maxConcurrency">最大并发数，默认处理器数的2倍</param>
        /// <param name="batchSize">批处理大小，默认4096</param>
        /// <param name="followSymlinks">是否跟踪符号链接，默认false</param>
        /// <param name="excludeDirs">排除的目录名列表</param>
        public FileUltraScanner(
            string rootPath,
            int? maxConcurrency = null,
            int batchSize = 4096,
            bool followSymlinks = false,
            IList<string> excludeDirs = null)
        {
            if (!Directory.Exists(rootPath))
                throw new DirectoryNotFoundException($"目录不存在: {rootPath}");

            // 确保路径标准化（使用全路径）
            _rootPath = Path.GetFullPath(rootPath);

            _maxConcurrency = maxConcurrency ?? Environment.ProcessorCount * 2;
            _batchSize = batchSize;
            _followSymlinks = followSymlinks;

            // 创建忽略规则集
            _fileIgnoreRuleSet = new FileIgnoreRuleSet(_rootPath, excludeDirs);

            _files = new ConcurrentBag<string>();
            _directories = new ConcurrentBag<string>();
            _errors = new ConcurrentBag<string>();
            _visitedPaths = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);
            _processedItems = 0;
            _stopwatch = new Stopwatch();
        }

        /// <summary>
        /// 扫描文件系统
        /// </summary>
        /// <param name="rootPath">根路径</param>
        /// <param name="reportProgress">是否报告进度</param>
        /// <param name="progressIntervalMs">进度报告间隔(毫秒)</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>扫描结果</returns>
        public async Task<ScanResult> ScanAsync(bool reportProgress = false, int progressIntervalMs = 100, CancellationToken cancellationToken = default)
        {
            // 初始化工作队列和信号量
            var workQueue = new ConcurrentQueue<WorkItem>();
            using var semaphore = new SemaphoreSlim(_maxConcurrency);

            // 将根目录加入队列
            workQueue.Enqueue(new WorkItem { Path = _rootPath, Depth = 0 });
            _visitedPaths.TryAdd(_rootPath, true);
            _directories.Add(_rootPath);

            // 启动计时器
            _stopwatch.Start();
            var startTime = DateTime.UtcNow;
            var lastProgressReport = DateTime.UtcNow;

            // 创建完成事件
            using var scanCompletionEvent = new ManualResetEventSlim(false);
            var activeWorkers = 0;

            // 开始处理队列
            while (!workQueue.IsEmpty || activeWorkers > 0)
            {
                if (cancellationToken.IsCancellationRequested)
                    break;

                // 尝试获取工作项
                if (workQueue.TryDequeue(out var workItem))
                {
                    // 等待信号量
                    await semaphore.WaitAsync(cancellationToken);

                    Interlocked.Increment(ref activeWorkers);

                    // 异步处理工作项
                    _ = Task.Run(() =>
                    {
                        try
                        {
                            // 使用底层API扫描目录
                            var (dirs, files, errors) = ScanDirectoryFast(workItem.Path, workItem.Depth);

                            // 记录结果
                            foreach (var error in errors)
                                _errors.Add(error);

                            foreach (var file in files)
                            {
                                _files.Add(file);
                                Interlocked.Increment(ref _processedItems);
                            }

                            foreach (var dir in dirs)
                            {
                                _directories.Add(dir);
                                Interlocked.Increment(ref _processedItems);

                                // 将新目录加入队列
                                if (_visitedPaths.TryAdd(dir, true))
                                {
                                    workQueue.Enqueue(new WorkItem { Path = dir, Depth = workItem.Depth + 1 });
                                }
                            }

                            // 更新进度
                            if (reportProgress && (DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= progressIntervalMs)
                            {
                                ReportProgress(workQueue.Count, activeWorkers);
                                lastProgressReport = DateTime.UtcNow;
                            }
                        }
                        finally
                        {
                            // 释放信号量
                            semaphore.Release();
                            Interlocked.Decrement(ref activeWorkers);

                            // 如果所有工作都完成，设置完成事件
                            if (workQueue.IsEmpty && activeWorkers == 0)
                            {
                                scanCompletionEvent.Set();
                            }
                        }
                    }, cancellationToken);
                }
                else if (activeWorkers > 0)
                {
                    // 队列暂时空了，但仍有活动工作，等待20毫秒
                    await Task.Delay(20, cancellationToken);
                }
                else
                {
                    // 所有工作都完成了
                    break;
                }
            }

            // 等待所有任务完成
            try
            {
                await Task.Run(() => scanCompletionEvent.Wait(cancellationToken), cancellationToken);
            }
            catch (OperationCanceledException)
            {
                // 扫描被取消
            }

            _stopwatch.Stop();

            if (reportProgress)
            {
                ReportProgress(0, 0, true);
            }

            // 创建结果
            return new ScanResult
            {
                RootPath = _rootPath,
                FileCount = _files.Count,
                DirectoryCount = _directories.Count,
                Files = new List<string>(_files),
                Directories = new List<string>(_directories),
                Errors = new List<string>(_errors),
                ElapsedTime = _stopwatch.Elapsed,
                StartTime = startTime,
                EndTime = DateTime.UtcNow
            };
        }

        private void ReportProgress(int queueCount, int activeWorkers, bool final = false)
        {
            double itemsPerSec = _stopwatch.Elapsed.TotalSeconds > 0
                ? _processedItems / _stopwatch.Elapsed.TotalSeconds
                : 0;

            Console.Write($"\r已处理: {_processedItems:N0} | 文件: {_files.Count:N0} | 目录: {_directories.Count:N0} | " +
                         $"队列: {queueCount:N0} | 工作线程: {activeWorkers} | {itemsPerSec:N0} 项/秒");

            if (final)
                Console.WriteLine();
        }

        private (List<string> Directories, List<string> Files, List<string> Errors) ScanDirectoryFast(string path, int depth)
        {
            var dirs = new List<string>(_batchSize);
            var files = new List<string>(_batchSize);
            var errors = new List<string>();

            // 根据平台选择不同的实现
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                ScanDirectoryWindows(path, dirs, files, errors);
            }
            else
            {
                ScanDirectoryUnix(path, dirs, files, errors);
            }

            return (dirs, files, errors);
        }

        private void ScanDirectoryWindows(string path, List<string> dirs, List<string> files, List<string> errors)
        {
            // 使用Windows API的FindFirstFile/FindNextFile快速遍历目录
            var searchPath = Path.Combine(path, "*");

            IntPtr findHandle = FindFirstFile(searchPath, out WIN32_FIND_DATA findData);
            if (findHandle == new IntPtr(-1)) // INVALID_HANDLE_VALUE
            {
                int error = Marshal.GetLastWin32Error();
                if (error != ERROR_NO_MORE_FILES)
                {
                    errors.Add($"无法读取目录 {path}: 错误代码 {error}");
                }
                return;
            }

            try
            {
                do
                {
                    string fileName = findData.cFileName;

                    // 跳过 "." 和 ".."
                    if (fileName == "." || fileName == "..")
                        continue;

                    // 构建完整路径
                    string fullPath = Path.Combine(path, fileName);

                    if (_fileIgnoreRuleSet.ShouldIgnore(fullPath))
                    {
                        continue;
                    }

                    // 检查是否是目录
                    bool isDirectory = (findData.dwFileAttributes & FileAttributes.Directory) != 0;
                    bool isReparsePoint = (findData.dwFileAttributes & FileAttributes.ReparsePoint) != 0;

                    if (isDirectory)
                    {
                        // 处理符号链接
                        if (isReparsePoint && !_followSymlinks)
                            continue;

                        dirs.Add(fullPath);
                    }
                    else
                    {
                        // 添加文件
                        files.Add(fullPath);
                    }
                } while (FindNextFile(findHandle, out findData));

                // 检查是否因为错误而退出循环
                int lastError = Marshal.GetLastWin32Error();
                if (lastError != ERROR_NO_MORE_FILES)
                {
                    errors.Add($"遍历目录时发生错误 {path}: 错误代码 {lastError}");
                }
            }
            finally
            {
                // 关闭查找句柄
                FindClose(findHandle);
            }
        }

        private void ScanDirectoryUnix(string path, List<string> dirs, List<string> files, List<string> errors)
        {
            // 使用libc的opendir/readdir快速遍历Unix/Linux/macOS目录
            IntPtr dirPtr = OpenDir(path);
            if (dirPtr == IntPtr.Zero)
            {
                errors.Add($"无法打开目录 {path}: {Marshal.GetLastWin32Error()}");
                return;
            }

            try
            {
                while (true)
                {
                    IntPtr entryPtr = ReadDir(dirPtr);
                    if (entryPtr == IntPtr.Zero)
                        break;

                    // 将指针转换为dirent结构
                    Dirent entry = Marshal.PtrToStructure<Dirent>(entryPtr);
                    string fileName = entry.d_name;

                    // 跳过 "." 和 ".."
                    if (fileName == "." || fileName == "..")
                        continue;

                    string fullPath = Path.Combine(path, fileName);

                    if (_fileIgnoreRuleSet.ShouldIgnore(fullPath))
                    {
                        continue;
                    }

                    // 根据d_type判断文件类型
                    if (entry.d_type == DT_DIR)
                    {
                        dirs.Add(fullPath);
                    }
                    else if (entry.d_type == DT_REG)
                    {
                        files.Add(fullPath);
                    }
                    else if (entry.d_type == DT_LNK && _followSymlinks)
                    {
                        // 处理符号链接
                        try
                        {
                            FileAttributes attr = File.GetAttributes(fullPath);
                            if ((attr & FileAttributes.Directory) == FileAttributes.Directory)
                            {
                                dirs.Add(fullPath);
                            }
                            else
                            {
                                files.Add(fullPath);
                            }
                        }
                        catch (Exception ex)
                        {
                            errors.Add($"解析符号链接失败 {fullPath}: {ex.Message}");
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                errors.Add($"处理目录时发生错误 {path}: {ex.Message}");
            }
            finally
            {
                // 关闭目录句柄
                CloseDir(dirPtr);
            }
        }

        private struct WorkItem
        {
            public string Path;
            public int Depth;
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
            /// 文件数量
            /// </summary>
            public int FileCount { get; set; }

            /// <summary>
            /// 目录数量
            /// </summary>
            public int DirectoryCount { get; set; }

            /// <summary>
            /// 文件列表
            /// </summary>
            public List<string> Files { get; set; }

            /// <summary>
            /// 目录列表
            /// </summary>
            public List<string> Directories { get; set; }

            /// <summary>
            /// 错误列表
            /// </summary>
            public List<string> Errors { get; set; }

            /// <summary>
            /// 扫描耗时
            /// </summary>
            public TimeSpan ElapsedTime { get; set; }

            /// <summary>
            /// 开始时间
            /// </summary>
            public DateTime StartTime { get; set; }

            /// <summary>
            /// 结束时间
            /// </summary>
            public DateTime EndTime { get; set; }

            /// <summary>
            /// 每秒处理项目数
            /// </summary>
            public double ItemsPerSecond => ElapsedTime.TotalSeconds > 0
                ? (FileCount + DirectoryCount) / ElapsedTime.TotalSeconds
                : 0;
        }

        /// <summary>
        /// 获取只包含文件路径的列表（无其他元数据）
        /// </summary>
        /// <param name="rootPath">根路径</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>文件路径列表</returns>
        public static async Task<List<string>> GetFilesOnlyAsync(string rootPath, CancellationToken cancellationToken = default)
        {
            var scanner = new FileUltraScanner(rootPath);
            var result = await scanner.ScanAsync(cancellationToken: cancellationToken);
            return result.Files;
        }
    }
}