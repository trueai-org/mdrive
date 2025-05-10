using MDriveSync.Core.Services;
using MDriveSync.Security;
using System.Collections.Concurrent;
using System.Diagnostics;

namespace MDriveSync.Client
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            var sw = new Stopwatch();
            var rootPath = "E:\\guanpeng"; // args.Length > 0 ? args[0] : Environment.CurrentDirectory;
            var ignorePatterns = FileIgnoreHelper.BuildIgnorePatterns("**/node_modules/*", "**/bin/*", "**/obj/*");
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
            var fileRes = FileFastScanner.ScanAsync(
                rootPath,
                "*.*",
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

                var f1Files = new ConcurrentDictionary<string, byte>();
                foreach (var file in fileRes.Files)
                {
                    f1Files.TryAdd(file, 0);
                }

                var f2Files = new ConcurrentDictionary<string, byte>();
                foreach (var file in result.Files)
                {
                    f2Files.TryAdd(file, 0);
                }

                // 比较f1 与 f2 的差异，如果 f1中有而 f2 中没有，则输出
                var f1Only = f1Files.Keys.Except(f2Files.Keys).ToList();
                if (f1Only.Count > 0)
                {
                    Console.WriteLine($"\n\nf1中有而f2中没有的文件: {f1Only.Count}");
                    foreach (var file in f1Only)
                    {
                        Console.WriteLine(file);
                    }
                }
                else
                {
                    Console.WriteLine("\nf1与f2完全一致");
                }

                // 比较f2 与 f1 的差异，如果 f2中有而 f1 中没有，则输出
                var f2Only = f2Files.Keys.Except(f1Files.Keys).ToList();
                if (f2Only.Count > 0)
                {
                    Console.WriteLine($"\n\nf2中有而f1中没有的文件: {f2Only.Count}");
                    foreach (var file in f2Only)
                    {
                        Console.WriteLine(file);
                    }
                }
                else
                {
                    Console.WriteLine("\nf2与f1完全一致");
                }

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

        private static string FormatSize(long bytes)
        {
            string[] units = { "B", "KB", "MB", "GB", "TB" };
            double size = bytes;
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