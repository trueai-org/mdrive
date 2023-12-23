using System.Collections.Concurrent;

namespace MDriveSync.Core.Services
{
    public class FileSearcher
    {
        /// <summary>
        /// 文件搜索 - 在处理小型目录或需要立即访问所有文件和目录的场景中更有优势。
        ///
        /// Directory.GetDirectories 和 Directory.GetFiles
        /// 行为: 这两个方法在返回结果之前，会检索并存储目标路径下的所有目录或文件。也就是说，它们会完全遍历指定路径下的所有目录或文件，并将它们作为一个数组返回。
        /// 适用场景: 当您需要立即访问所有结果，并且结果集不太大（不会导致显著的内存占用）时，这两个方法是合适的。
        /// 效率: 对于小型目录，这些方法通常效率很高，因为它们一次性加载所有数据。但对于包含大量文件或目录的路径，它们可能会导致显著的性能开销，因为需要等待整个目录树被遍历完毕。
        /// </summary>
        /// <param name="rootPath"></param>
        /// <param name="processorCount"></param>
        /// <returns></returns>
        public static ConcurrentBag<string> SearchFiles(string rootPath, int processorCount = 4)
        {
            var files = new ConcurrentBag<string>();
            var options = new ParallelOptions { MaxDegreeOfParallelism = processorCount };

            try
            {
                Parallel.ForEach(Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories), options, directory =>
                {
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        files.Add(file);
                    }
                });

                foreach (var file in Directory.GetFiles(rootPath))
                {
                    files.Add(file);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("Access Denied: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }
            return files;
        }

        /// <summary>
        /// 文件搜索  - 在处理大型目录和需要逐个处理文件或目录的场景中更有效，尤其是在性能和内存使用方面。
        ///
        /// Directory.EnumerateDirectories 和 Directory.EnumerateFiles
        /// 行为: 这两个方法使用延迟执行（lazy evaluation）。它们返回一个可迭代的集合，该集合在遍历过程中一次只加载一个目录或文件。
        /// 适用场景: 当处理大型目录，或者不需要立即访问所有结果时，这些方法更为合适。它们允许开始处理找到的第一个项目，而不必等待整个目录树的遍历完成。
        /// 效率: 对于大型目录，这些方法通常更高效，因为它们不需要一开始就加载所有数据。它们在内存占用方面也更加高效，特别是在处理大型文件系统时。
        ///
        /// </summary>
        /// <param name="rootPath"></param>
        /// <param name="processorCount"></param>
        /// <returns></returns>
        public static ConcurrentBag<(string, long, DateTime)> GetFiles(string rootPath, int processorCount = 4)
        {
            var fileInfoBag = new ConcurrentBag<(string, long, DateTime)>();

            var options = new ParallelOptions { MaxDegreeOfParallelism = processorCount };
            Parallel.ForEach(Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories), options, directory =>
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            fileInfoBag.Add((file, fileInfo.Length, fileInfo.LastWriteTime));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            });

            return fileInfoBag;
        }

        /*
         * 测试其他方案
         *
        private static ConcurrentBag<string> files = new ConcurrentBag<string>();
        private static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(10);

        public async Task SearchAsync(string rootPath)
        {
            await semaphoreSlim.WaitAsync(); // 等待获取信号量

            try
            {
                string[] directories = Directory.GetDirectories(rootPath);
                string[] filesInCurrentDir = Directory.GetFiles(rootPath);

                foreach (var file in filesInCurrentDir)
                {
                    files.Add(file);
                }

                var tasks = new List<Task>();
                foreach (var dir in directories)
                {
                    // 限制同时运行的任务数量
                    if (semaphoreSlim.CurrentCount == 0)
                    {
                        // 等待已启动的任务之一完成
                        await Task.WhenAny(tasks.ToArray());
                        tasks.RemoveAll(t => t.IsCompleted); // 移除已完成的任务
                    }

                    semaphoreSlim.Wait(); // 同步获取信号量
                    var task = SearchAsync(dir);
                    tasks.Add(task);

                    // 异步释放信号量，允许其他任务继续
                    _ = task.ContinueWith(t => semaphoreSlim.Release());
                }

                await Task.WhenAll(tasks); // 等待所有任务完成
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("Access Denied: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }
            finally
            {
                semaphoreSlim.Release(); // 释放信号量
            }
        }
        */
    }
}