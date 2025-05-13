using MDriveSync.Core.Services;
using MDriveSync.Infrastructure;
using MDriveSync.Security;
using System.Diagnostics;
using System.Security.Cryptography;

namespace MDriveSync.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            await FastCDCTest1.Start();

            Console.ReadKey();

            return;

            await FastCDCPlusTest1.Start();


            Console.ReadKey();

            return;

            var sw = new Stopwatch();
            var rootPath = "E:\\program_files"; // args.Length > 0 ? args[0] : Environment.CurrentDirectory;
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns("**/node_modules/*", "**/bin/*", "**/obj/*", "**/.git/*");
            Console.WriteLine($"开始扫描目录: {rootPath}");


            var cts = new CancellationTokenSource();

            // 注册取消处理
            Console.CancelKeyPress += (s, e) =>
            {
                Console.WriteLine("\n取消扫描...");
                cts.Cancel();
                e.Cancel = true;
            };

            var progress = new Progress<FileFastScanner.ScanProgress>(p =>
            {
                Console.Write($"\r文件: {p.FileCount:N0}, 目录: {p.DirectoryCount:N0}, " +
                            $"耗时: {p.ElapsedTime.TotalSeconds}s, 速度: {(int)p.ItemsPerSecond}/s");
            });

            sw.Start();
            var fileRes = new FileFastScanner().ScanAsync(
                rootPath,
                "*",
                ignorePatterns,
                maxDegreeOfParallelism: 4,
                progress: progress,
                cancellationToken: cts.Token
            );
            sw.Stop();
            Console.WriteLine($"扫描完成! 耗时: {sw.Elapsed.TotalSeconds:F2} 秒, count: {fileRes.Files.Count}");


            try
            {
                var scanner = new FileUltraScanner(
                    rootPath,
                    maxConcurrency: Environment.ProcessorCount * 2,
                    batchSize: 8192,
                    followSymlinks: false,
                    ignorePatterns
                );

                var result = await scanner.ScanAsync(
                    reportProgress: true,
                    progressIntervalMs: 100,
                    cancellationToken: cts.Token
                );

                Console.WriteLine("\n\n扫描完成!");
                Console.WriteLine($"总文件数: {result.FileCount:N0}");
                Console.WriteLine($"总目录数: {result.DirectoryCount:N0}");
                Console.WriteLine($"总项目数: {result.FileCount + result.DirectoryCount:N0}");
                Console.WriteLine($"扫描耗时: {result.ElapsedTime.TotalSeconds:F2} 秒");
                Console.WriteLine($"处理速度: {result.ItemsPerSecond:N0} 项/秒");


                if (result.Errors.Count > 0)
                {
                    Console.WriteLine($"扫描过程中发生了 {result.Errors.Count} 个错误");

                    // 显示前5个错误
                    int errorsToShow = Math.Min(5, result.Errors.Count);
                    for (int i = 0; i < errorsToShow; i++)
                    {
                        Console.WriteLine($"- {result.Errors[i]}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("\n扫描已取消");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\n扫描过程中发生错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }

            //var cts = new CancellationTokenSource();

            //// 捕获Ctrl+C以取消扫描
            //Console.CancelKeyPress += (s, e) => {
            //    e.Cancel = true;
            //    cts.Cancel();
            //    Console.WriteLine("\n扫描已被用户取消...");
            //};

            //try
            //{
            //    var result = await FastDirectoryScanner.ScanDirectoryFastAsync(
            //        path,
            //        reportProgress: true,
            //        cancellationToken: cts.Token
            //    );

            //    Console.WriteLine("\n\n扫描结果摘要:");
            //    Console.WriteLine($"目录数量: {result.DirectoryCount:N0}");
            //    Console.WriteLine($"文件数量: {result.FileCount:N0}");
            //    Console.WriteLine($"总数量: {result.DirectoryCount + result.FileCount:N0}");
            //    Console.WriteLine($"扫描耗时: {result.ElapsedTime.TotalSeconds:F2} 秒");
            //    Console.WriteLine($"处理速度: {result.ItemsPerSecond:F2} 项/秒");

            //    if (result.Errors.Count > 0)
            //    {
            //        Console.WriteLine($"发生 {result.Errors.Count} 个错误");
            //        // 可选: 输出前几个错误
            //        int errorsToPrint = Math.Min(5, result.Errors.Count);
            //        for (int i = 0; i < errorsToPrint; i++)
            //        {
            //            Console.WriteLine($"- {result.Errors[i]}");
            //        }
            //    }

            //    // 如果需要，可以访问完整的文件和目录列表
            //    // foreach (var dir in result.Directories) { ... }
            //    // foreach (var file in result.Files) { ... }
            //}
            //catch (OperationCanceledException)
            //{
            //    Console.WriteLine("\n扫描已取消");
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine($"\n扫描时发生错误: {ex.Message}");
            //}

            //string path = "E:\\guanpeng\\docs"; // args.Length > 0 ? args[0] : Environment.CurrentDirectory;
            //Console.WriteLine($"开始扫描目录: {path}");

            //var progress = new Progress<ScanProgress>(p =>
            //{
            //    Console.Write($"\r文件: {p.FilesProcessed:N0}, 目录: {p.DirectoriesProcessed:N0}, " +
            //                $"大小: {FormatSize(p.BytesProcessed)}, 速度: {p.FormattedBytesPerSecond}");
            //});

            //var options = new ScanOptions
            //{
            //    MaxDepth = 0,  // 无限深度
            //    FollowSymlinks = false,
            //    IgnoreHiddenFiles = true,
            //    IgnoreSystemFiles = true,
            //    IgnoreHiddenDirectories = true,
            //    FileFoundCallback = (file) => {
            //        // 可选文件处理回调，此处为空以提高性能
            //    }
            //};

            //var scanner = new FastFileScanner(options, progress: progress);
            //var cts = new CancellationTokenSource();

            //// 捕获Ctrl+C以取消扫描
            //Console.CancelKeyPress += (s, e) => {
            //    e.Cancel = true;
            //    cts.Cancel();
            //    Console.WriteLine("\n扫描已被用户取消...");
            //};

            //try
            //{
            //    var result = await scanner.ScanAsync(path, cts.Token);

            //    Console.WriteLine("\n\n扫描完成!");
            //    Console.WriteLine($"总计文件: {result.TotalFiles:N0}");
            //    Console.WriteLine($"总计目录: {result.TotalDirectories:N0}");
            //    Console.WriteLine($"总计大小: {result.FormattedTotalSize}");
            //    Console.WriteLine($"耗时: {result.ElapsedTime.TotalSeconds:F2} 秒");
            //    Console.WriteLine($"速度: {result.FormattedBytesPerSecond}");
            //    Console.WriteLine($"每秒处理项目: {result.ItemsPerSecond:F2} 项/秒");

            //    if (result.Errors.Count > 0)
            //        Console.WriteLine($"错误数量: {result.Errors.Count}");
            //}
            //catch (OperationCanceledException)
            //{
            //    Console.WriteLine("\n扫描已取消");
            //}
            //catch (Exception ex)
            //{
            //    Console.WriteLine($"\n扫描时发生错误: {ex.Message}");
            //}

            return;

            //sw.Restart();
            //LocalStorage.RunRestore();
            //sw.Stop();

            //Console.WriteLine($"还原用时：{sw.ElapsedMilliseconds}ms");
            //Console.WriteLine("Hello, World!");
            //Console.ReadKey();
            //return;

            sw.Restart();
            LocalStorage.RunBackup();
            sw.Stop();

            Console.WriteLine($"备份用时：{sw.ElapsedMilliseconds}ms");
            Console.WriteLine("Hello, World!");
            Console.ReadKey();

            Console.WriteLine("Hello, World!");
        }
    }

    /// <summary>
    /// FastCDC+ 控制台应用程序
    /// </summary>
    public class FastCDCPlusTest1
    {
        public static async Task Start()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine($"FastCDC+ 内容定义分块工具 [当前时间: {DateTime.UtcNow:yyyy-MM-dd HH:mm:ss}]");
            Console.WriteLine($"用户: {Environment.UserName}\n");

            //if (args.Length < 1)
            //{
            //    Console.WriteLine("用法: FastCDCPlus <文件路径> [最小块大小MB] [平均块大小MB] [最大块大小MB]");
            //    return;
            //}

            string filePath = @"E:\downs\fdm\__分块测试\Driver.rar";

            // 解析可选参数
            int minChunkSize = 1 * 1024 * 1024;  // 默认1MB
            int avgChunkSize = 16 * 1024 * 1024; // 默认16MB
            int maxChunkSize = 64 * 1024 * 1024; // 默认64MB

            //if (args.Length >= 2 && int.TryParse(args[1], out int min))
            //    minChunkSize = min * 1024 * 1024;

            //if (args.Length >= 3 && int.TryParse(args[2], out int avg))
            //    avgChunkSize = avg * 1024 * 1024;

            //if (args.Length >= 4 && int.TryParse(args[3], out int max))
            //    maxChunkSize = max * 1024 * 1024;

            try
            {
                Console.WriteLine($"处理文件: {filePath}");
                Console.WriteLine($"块大小设置: 最小={minChunkSize / 1024 / 1024}MB, 平均={avgChunkSize / 1024 / 1024}MB, 最大={maxChunkSize / 1024 / 1024}MB");

                var sw = Stopwatch.StartNew();

                // 选择哈希算法 - 可以使用Blake3/XXHash等更快的算法
                using HashAlgorithm hashAlg = SHA256.Create();

                // 创建FastCDC+实例并处理
                using var chunker = new FastCDCPlus(minChunkSize, avgChunkSize, maxChunkSize, hashAlg);

                // 创建取消令牌，以便支持按Ctrl+C取消
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                    Console.WriteLine("\n操作已取消！");
                };

                // 执行分块
                var chunks = await chunker.ChunkFileAsync(filePath, parallelProcessing: true, cts.Token);

                sw.Stop();

                Console.WriteLine($"分块完成，耗时: {sw.ElapsedMilliseconds}ms");
                Console.WriteLine($"文件被分割为 {chunks.Count} 个块");

                // 计算统计信息
                long totalSize = 0;
                long minSize = long.MaxValue;
                long maxSize = 0;

                foreach (var chunk in chunks)
                {
                    totalSize += chunk.Length;
                    minSize = Math.Min(minSize, chunk.Length);
                    maxSize = Math.Max(maxSize, chunk.Length);
                }

                double avgSize = chunks.Count > 0 ? totalSize / (double)chunks.Count : 0;

                Console.WriteLine($"块统计信息:");
                Console.WriteLine($"  总大小: {totalSize.FormatSize()}");
                Console.WriteLine($"  平均大小: {avgSize.FormatSize()}");
                Console.WriteLine($"  最小块: {minSize.FormatSize()}");
                Console.WriteLine($"  最大块: {maxSize.FormatSize()}");
                Console.WriteLine($"  处理速度: {(totalSize / (sw.ElapsedMilliseconds / 1000.0)).FormatSize()}/s");

                // 显示部分块信息
                Console.WriteLine("\n前5个块:");
                for (int i = 0; i < Math.Min(5, chunks.Count); i++)
                {
                    Console.WriteLine($"{i + 1}: {chunks[i]}");
                }

                if (chunks.Count > 10)
                {
                    Console.WriteLine("\n后5个块:");
                    for (int i = Math.Max(5, chunks.Count - 5); i < chunks.Count; i++)
                    {
                        Console.WriteLine($"{i + 1}: {chunks[i]}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("操作已取消。");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }
    }


    /// <summary>
    /// FastCDC 算法演示程序
    /// </summary>
    public class FastCDCTest1
    {
        public static async Task Start()
        {
            Console.OutputEncoding = System.Text.Encoding.UTF8;
            Console.WriteLine($"FastCDC 内容定义分块工具 [当前时间: {DateTime.Now:yyyy-MM-dd HH:mm:ss}]");
            Console.WriteLine($"用户: {Environment.UserName}");
            Console.WriteLine();

            //if (args.Length < 1)
            //{
            //    Console.WriteLine("用法: FastCDC <文件路径> [最小块大小MB] [平均块大小MB] [最大块大小MB]");
            //    return;
            //}

            //string filePath = "";// args[0];
            string filePath = @"E:\downs\fdm\__分块测试\Driver.rar";

            // 解析可选参数
            int minChunkSize = 1 * 1024 * 1024;  // 默认1MB
            int avgChunkSize = 16 * 1024 * 1024; // 默认16MB
            int maxChunkSize = 64 * 1024 * 1024; // 默认64MB

            //if (args.Length >= 2 && int.TryParse(args[1], out int min))
            //    minChunkSize = min * 1024 * 1024;

            //if (args.Length >= 3 && int.TryParse(args[2], out int avg))
            //    avgChunkSize = avg * 1024 * 1024;

            //if (args.Length >= 4 && int.TryParse(args[3], out int max))
            //    maxChunkSize = max * 1024 * 1024;

            try
            {
                if (!File.Exists(filePath))
                {
                    Console.WriteLine($"错误: 文件 '{filePath}' 不存在");
                    return;
                }

                var fileInfo = new FileInfo(filePath);
                Console.WriteLine($"处理文件: {filePath}");
                Console.WriteLine($"文件大小: {FormatSize(fileInfo.Length)}");
                Console.WriteLine($"块大小设置: 最小={FormatSize(minChunkSize)}, 平均={FormatSize(avgChunkSize)}, 最大={FormatSize(maxChunkSize)}");
                Console.WriteLine();

                var sw = Stopwatch.StartNew();

                using var hashAlg = SHA256.Create();

                // 创建允许取消的令牌
                using var cts = new CancellationTokenSource();
                Console.CancelKeyPress += (s, e) =>
                {
                    e.Cancel = true;
                    cts.Cancel();
                    Console.WriteLine("\n操作已取消！");
                };

                // 创建分块器实例
                using var chunker = new FastCDCChunker(
                    minSize: minChunkSize,
                    avgSize: avgChunkSize,
                    maxSize: maxChunkSize,
                    bufferSize: Math.Min(128 * 1024 * 1024, Math.Max(maxChunkSize * 2, (int)Math.Min(fileInfo.Length, int.MaxValue))),
                    hashAlgorithm: hashAlg
                );

                // 异步执行分块
                var chunks = await chunker.ChunkFileAsync(filePath, cts.Token);

                sw.Stop();

                Console.WriteLine($"文件已被分割为 {chunks.Count} 个块");
                Console.WriteLine($"处理时间: {sw.ElapsedMilliseconds}ms ({FormatSize((long)(fileInfo.Length / (sw.ElapsedMilliseconds / 1000.0)))}/s)");
                Console.WriteLine();

                // 计算统计信息
                long totalSize = chunks.Sum(c => (long)c.Length);
                double avgChunkSizeActual = chunks.Count > 0 ? totalSize / (double)chunks.Count : 0;
                var minChunk = chunks.Count > 0 ? chunks.Min(c => c.Length) : 0;
                var maxChunk = chunks.Count > 0 ? chunks.Max(c => c.Length) : 0;

                Console.WriteLine("块统计信息:");
                Console.WriteLine($"  总大小: {FormatSize(totalSize)}");
                Console.WriteLine($"  平均大小: {FormatSize((long)avgChunkSizeActual)}");
                Console.WriteLine($"  最小块: {FormatSize(minChunk)}");
                Console.WriteLine($"  最大块: {FormatSize(maxChunk)}");
                Console.WriteLine();

                // 显示部分块信息
                Console.WriteLine("前5个块:");
                for (int i = 0; i < Math.Min(5, chunks.Count); i++)
                {
                    Console.WriteLine($"{i + 1}: {chunks[i]}");
                }

                if (chunks.Count > 10)
                {
                    Console.WriteLine("\n后5个块:");
                    for (int i = Math.Max(5, chunks.Count - 5); i < chunks.Count; i++)
                    {
                        Console.WriteLine($"{i + 1}: {chunks[i]}");
                    }
                }
            }
            catch (OperationCanceledException)
            {
                Console.WriteLine("操作已取消");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"错误: {ex.Message}");
                Console.WriteLine(ex.StackTrace);
            }
        }

        // 格式化文件大小显示
        private static string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB" };
            int i = 0;
            double size = bytes;

            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }

            return $"{size:F2} {suffixes[i]}";
        }
    }
}