using System.Collections.Concurrent;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 高性能文件系统遍历工具
    /// </summary>
    public class FileFastScanner
    {
        /// <summary>
        /// 采用生产者-消费者模式遍历文件系统
        /// </summary>
        public static IEnumerable<string> EnumerateFiles(
            string rootPath,
            string searchPattern = "*",
            IEnumerable<string> ignorePatterns = null,
            int? maxDegreeOfParallelism = null,
            Action<string, Exception> errorHandler = null)
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

            // 启动目录生产者任务
            var directoryProducerTask = Task.Run(() =>
            {
                try
                {
                    // 第一个目录是根目录（如果不应被忽略）
                    if (!ignoreRules.ShouldIgnore(rootPath))
                    {
                        directoryQueue.Add(rootPath);
                    }

                    // 使用广度优先遍历而不是预先收集所有目录
                    ProcessDirectories(rootPath, ignoreRules, directoryQueue, errorHandler);
                }
                finally
                {
                    // 标记目录队列已完成
                    directoryQueue.CompleteAdding();
                }
            });

            // 启动文件处理任务
            var fileProcessorTask = Task.Run(() =>
            {
                // 创建并行选项
                var options = new ParallelOptions { MaxDegreeOfParallelism = parallelism };

                // 从目录队列中并行处理目录
                Parallel.ForEach(directoryQueue.GetConsumingEnumerable(), options, directory =>
                {
                    try
                    {
                        // 处理单个目录中的文件
                        foreach (var file in ProcessFiles(directory, searchPattern, ignoreRules, errorHandler))
                        {
                            fileResults.Add(file);
                        }
                    }
                    catch (Exception ex)
                    {
                        errorHandler?.Invoke(directory, ex);
                    }
                });

                // 所有目录处理完成后，标记文件结果集已完成
                fileResults.CompleteAdding();
            });

            // 返回文件结果
            foreach (var file in fileResults.GetConsumingEnumerable())
            {
                yield return file;
            }

            // 确保所有任务完成
            Task.WaitAll(directoryProducerTask, fileProcessorTask);
        }

        /// <summary>
        /// 处理目录（流式方式）
        /// </summary>
        private static void ProcessDirectories(
            string rootPath,
            FileIgnoreRuleSet ignoreRules,
            BlockingCollection<string> directoryQueue,
            Action<string, Exception> errorHandler = null)
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

            while (pendingDirs.Count > 0)
            {
                string currentDir = pendingDirs.Dequeue();

                try
                {
                    foreach (var subDir in Directory.EnumerateDirectories(currentDir, "*", options))
                    {
                        // 检查目录是否应被忽略
                        if (!ignoreRules.ShouldIgnore(subDir) && processedDirs.Add(subDir))
                        {
                            directoryQueue.Add(subDir);
                            pendingDirs.Enqueue(subDir);
                        }
                    }
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
            Action<string, Exception> errorHandler = null)
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
                    RecurseSubdirectories = false,
                    AttributesToSkip = FileAttributes.System,
                    IgnoreInaccessible = true
                };

                files = Directory.EnumerateFiles(directory, searchPattern, options);
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
                // 检查文件是否应被忽略
                if (!ignoreRules.ShouldIgnore(file))
                {
                    yield return file;
                }
            }
        }
    }
}