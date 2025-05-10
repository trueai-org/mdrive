using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text.RegularExpressions;

namespace MDriveSync.Core.Services
{
    public class FileSearcher
    {
        /// <summary>
        /// 文件搜索（少量文件） - 在处理小型目录或需要立即访问所有文件和目录的场景中更有优势。
        ///
        /// Directory.GetDirectories 和 Directory.GetFiles
        /// 行为: 这两个方法在返回结果之前，会检索并存储目标路径下的所有目录或文件。也就是说，它们会完全遍历指定路径下的所有目录或文件，并将它们作为一个数组返回。
        /// 适用场景: 当您需要立即访问所有结果，并且结果集不太大（不会导致显著的内存占用）时，这两个方法是合适的。
        /// 效率: 对于小型目录，这些方法通常效率很高，因为它们一次性加载所有数据。但对于包含大量文件或目录的路径，它们可能会导致显著的性能开销，因为需要等待整个目录树被遍历完毕。
        /// </summary>
        /// <param name="rootPath">根路径</param>
        /// <param name="processorCount">处理器数量</param>
        /// <param name="reportProgress">是否报告进度</param>
        /// <param name="progressIntervalMs">进度报告间隔(毫秒)</param>
        /// <param name="followSymlinks">是否跟踪符号链接</param>
        /// <param name="excludeDirs">排除的目录名列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>文件集合</returns>
        public static ConcurrentBag<string> SearchFiles(
            string rootPath,
            int processorCount = 4,
            bool reportProgress = false,
            int progressIntervalMs = 100,
            bool followSymlinks = false,
            IList<string> excludeDirs = null,
            CancellationToken cancellationToken = default)
        {
            var files = new ConcurrentBag<string>();
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = processorCount,
                CancellationToken = cancellationToken
            };
            var stopwatch = new Stopwatch();
            var lastProgressReport = DateTime.UtcNow;
            var processedItems = 0;
            var visitedPaths = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            var ignoreRule = new FileIgnoreRuleSet(rootPath, excludeDirs);

            stopwatch.Start();

            try
            {
                // 获取所有子目录（排除指定目录）
                var allDirectories = Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories);

                // 过滤排除的目录和符号链接
                var directories = allDirectories.Where(dir => !ignoreRule.ShouldIgnore(dir) &&
                    (followSymlinks || !IsSymbolicLink(dir)))
                    .ToArray();

                // 并行处理每个目录
                Parallel.ForEach(directories, options, directory =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    try
                    {
                        foreach (var file in Directory.GetFiles(directory))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            if (ignoreRule.ShouldIgnore(file))
                                continue;

                            files.Add(file);
                            Interlocked.Increment(ref processedItems);

                            // 更新进度
                            if (reportProgress && (DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= progressIntervalMs)
                            {
                                lock (stopwatch)
                                {
                                    if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= progressIntervalMs)
                                    {
                                        ReportProgress(processedItems, files.Count, stopwatch.Elapsed);
                                        lastProgressReport = DateTime.UtcNow;
                                    }
                                }
                            }
                        }
                    }
                    catch (UnauthorizedAccessException ex)
                    {
                        Console.WriteLine($"访问目录被拒绝: {directory}，错误: {ex.Message}");
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"处理目录发生错误: {directory}，错误: {ex.Message}");
                    }
                });

                // 处理根目录下的文件
                if (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var file in Directory.GetFiles(rootPath))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        files.Add(file);
                        Interlocked.Increment(ref processedItems);
                    }
                }

                // 最终进度报告
                if (reportProgress && !cancellationToken.IsCancellationRequested)
                {
                    ReportProgress(processedItems, files.Count, stopwatch.Elapsed, true);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("搜索操作已取消");
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("访问被拒绝: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("异常: " + ex.Message);
            }
            finally
            {
                stopwatch.Stop();
            }

            return files;
        }

        /// <summary>
        /// 文件搜索（大量文件）  - 在处理大型目录和需要逐个处理文件或目录的场景中更有效，尤其是在性能和内存使用方面。
        ///
        /// Directory.EnumerateDirectories 和 Directory.EnumerateFiles
        /// 行为: 这两个方法使用延迟执行（lazy evaluation）。它们返回一个可迭代的集合，该集合在遍历过程中一次只加载一个目录或文件。
        /// 适用场景: 当处理大型目录，或者不需要立即访问所有结果时，这些方法更为合适。它们允许开始处理找到的第一个项目，而不必等待整个目录树的遍历完成。
        /// 效率: 对于大型目录，这些方法通常更高效，因为它们不需要一开始就加载所有数据。它们在内存占用方面也更加高效，特别是在处理大型文件系统时。
        ///
        /// </summary>
        /// <param name="rootPath">根路径</param>
        /// <param name="processorCount">处理器数量</param>
        /// <param name="reportProgress">是否报告进度</param>
        /// <param name="progressIntervalMs">进度报告间隔(毫秒)</param>
        /// <param name="followSymlinks">是否跟踪符号链接</param>
        /// <param name="excludeDirs">排除的目录名列表</param>
        /// <param name="cancellationToken">取消令牌</param>
        /// <returns>文件信息元组集合 (文件路径, 大小, 修改时间)</returns>
        public static ConcurrentBag<(string, long, DateTime)> GetFiles(
            string rootPath,
            int processorCount = 4,
            bool reportProgress = false,
            int progressIntervalMs = 100,
            bool followSymlinks = false,
            IList<string> excludeDirs = null,
            CancellationToken cancellationToken = default)
        {
            var fileInfoBag = new ConcurrentBag<(string, long, DateTime)>();
            var options = new ParallelOptions
            {
                MaxDegreeOfParallelism = processorCount,
                CancellationToken = cancellationToken
            };
            var stopwatch = new Stopwatch();
            var lastProgressReport = DateTime.UtcNow;
            var processedItems = 0;
            var visitedPaths = new ConcurrentDictionary<string, bool>(StringComparer.OrdinalIgnoreCase);

            var ignoreRule = new FileIgnoreRuleSet(rootPath, excludeDirs);

            stopwatch.Start();

            try
            {
                // 使用延迟枚举目录，过滤排除的目录
                var directories = Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories)
                    .Where(dir => !ignoreRule.ShouldIgnore(dir) &&
                        (followSymlinks || !IsSymbolicLink(dir)));

                Parallel.ForEach(directories, options, directory =>
                {
                    if (cancellationToken.IsCancellationRequested)
                        return;

                    try
                    {
                        foreach (var file in Directory.EnumerateFiles(directory))
                        {
                            if (cancellationToken.IsCancellationRequested)
                                break;

                            if (ignoreRule.ShouldIgnore(file))
                                continue;

                            try
                            {
                                var fileInfo = new FileInfo(file);
                                fileInfoBag.Add((file, fileInfo.Length, fileInfo.LastWriteTime));
                                Interlocked.Increment(ref processedItems);

                                // 更新进度
                                if (reportProgress && (DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= progressIntervalMs)
                                {
                                    lock (stopwatch)
                                    {
                                        if ((DateTime.UtcNow - lastProgressReport).TotalMilliseconds >= progressIntervalMs)
                                        {
                                            ReportProgress(processedItems, fileInfoBag.Count, stopwatch.Elapsed);
                                            lastProgressReport = DateTime.UtcNow;
                                        }
                                    }
                                }
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"处理文件失败: {file}，错误: {ex.Message}");
                            }
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"枚举目录文件失败: {directory}，错误: {ex.Message}");
                    }
                });

                // 处理根目录下的文件
                if (!cancellationToken.IsCancellationRequested)
                {
                    foreach (var file in Directory.EnumerateFiles(rootPath))
                    {
                        if (cancellationToken.IsCancellationRequested)
                            break;

                        try
                        {
                            var fileInfo = new FileInfo(file);
                            fileInfoBag.Add((file, fileInfo.Length, fileInfo.LastWriteTime));
                            Interlocked.Increment(ref processedItems);
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine($"处理文件失败: {file}，错误: {ex.Message}");
                        }
                    }
                }

                // 最终进度报告
                if (reportProgress && !cancellationToken.IsCancellationRequested)
                {
                    ReportProgress(processedItems, fileInfoBag.Count, stopwatch.Elapsed, true);
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("搜索操作已取消");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"搜索过程发生错误: {ex.Message}");
            }
            finally
            {
                stopwatch.Stop();
            }

            return fileInfoBag;
        }


        /// <summary>
        /// 判断是否是符号链接
        /// </summary>
        private static bool IsSymbolicLink(string path)
        {
            var attr = File.GetAttributes(path);
            return (attr & FileAttributes.ReparsePoint) == FileAttributes.ReparsePoint;
        }

        /// <summary>
        /// 报告进度
        /// </summary>
        private static void ReportProgress(int processedItems, int fileCount, TimeSpan elapsed, bool final = false)
        {
            double itemsPerSec = elapsed.TotalSeconds > 0
                ? processedItems / elapsed.TotalSeconds
                : 0;

            Console.Write($"\r已处理: {processedItems:N0} | 文件: {fileCount:N0} | 耗时: {elapsed.TotalSeconds:F2}秒 | {itemsPerSec:N0} 项/秒");

            if (final)
                Console.WriteLine();
        }
    }
}