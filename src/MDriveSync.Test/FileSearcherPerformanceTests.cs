using MDriveSync.Core.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MDriveSync.Test
{
    /// <summary>
    /// 测试FileSearcher的性能指标
    /// </summary>
    public class FileSearcherPerformanceTests : BaseTests
    {
        /// <summary>
        /// 大量文件 - 多线程性能测试
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task TestFileSearcherPerformanceBig()
        {
            // 配置测试参数
            string[] testDirectories = [@"E:\guanpeng\__my"];
            int[] processorCounts = { Environment.ProcessorCount, Environment.ProcessorCount * 2 };

            // 收集结果
            var results = new Dictionary<string, Dictionary<int, List<(double SearchTime, int FileCount)>>>();
            foreach (var dir in testDirectories)
            {
                results[dir] = new Dictionary<int, List<(double, int)>>();
                foreach (var count in processorCounts)
                {
                    results[dir][count] = new List<(double, int)>();
                }
            }

            // 运行测试
            int iterations = 3; // 执行多次取平均值
            Console.WriteLine("开始测试FileSearcher性能...");

            foreach (var directory in testDirectories)
            {
                if (!Directory.Exists(directory))
                {
                    Console.WriteLine($"目录不存在，跳过: {directory}");
                    continue;
                }

                Console.WriteLine($"测试目录: {directory}");
                string dirInfo = GetDirectoryInfo(directory);
                Console.WriteLine(dirInfo);

                foreach (var processorCount in processorCounts)
                {

                    // 测试 UltraFastScanner
                    Console.WriteLine($"  测试UltraFastScanner方法 (并行度: {processorCount})");

                    for (int i = 0; i < iterations; i++)
                    {
                        var scanner = new FileUltraScanner(
                            directory,
                        maxConcurrency: processorCount,
                        batchSize: 8192,
                        followSymlinks: false,
                        []
                    );

                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        var result = await scanner.ScanAsync(reportProgress: true);
                        stopwatch.Stop();
                        double searchTime = stopwatch.Elapsed.TotalMilliseconds;
                        Console.WriteLine($"    迭代 {i + 1}: 搜索时间 = {searchTime:F2} ms, 文件数 = {result.FileCount}");
                        // 清理
                        GC.Collect();
                        Thread.Sleep(50);
                    }

                    // 测试GetFiles方法
                    Console.WriteLine($"  测试GetFiles方法 (并行度: {processorCount})");
                    for (int i = 0; i < iterations; i++)
                    {
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        var fileInfos = FileSearcher.GetFiles(directory, processorCount, true);
                        stopwatch.Stop();
                        double searchTime = stopwatch.Elapsed.TotalMilliseconds;

                        Console.WriteLine($"    迭代 {i + 1}: 搜索时间 = {searchTime:F2} ms, 文件数 = {fileInfos.Count}");

                        // 清理
                        GC.Collect();
                        Thread.Sleep(50);
                    }

                  
                }
            }

            // 生成性能报告
            GeneratePerformanceReport(results, testDirectories, processorCounts);
        }

        [Fact]
        public async Task TestFileSearcherPerformance()
        {
            // 配置测试参数
            string[] testDirectories = GetTestDirectories();
            int[] processorCounts = { 1, 2, 4, 8, Environment.ProcessorCount };

            // 收集结果
            var results = new Dictionary<string, Dictionary<int, List<(double SearchTime, int FileCount)>>>();
            foreach (var dir in testDirectories)
            {
                results[dir] = new Dictionary<int, List<(double, int)>>();
                foreach (var count in processorCounts)
                {
                    results[dir][count] = new List<(double, int)>();
                }
            }

            // 运行测试
            int iterations = 3; // 执行多次取平均值
            Console.WriteLine("开始测试FileSearcher性能...");

            foreach (var directory in testDirectories)
            {
                if (!Directory.Exists(directory))
                {
                    Console.WriteLine($"目录不存在，跳过: {directory}");
                    continue;
                }

                Console.WriteLine($"测试目录: {directory}");
                string dirInfo = GetDirectoryInfo(directory);
                Console.WriteLine(dirInfo);

                foreach (var processorCount in processorCounts)
                {
                    Console.WriteLine($"   测试SearchFiles方法 并行度: {processorCount}");

                    for (int i = 0; i < iterations; i++)
                    {
                        // 测试SearchFiles方法
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        var files = FileSearcher.SearchFiles(directory, processorCount, false);
                        stopwatch.Stop();
                        double searchTime = stopwatch.Elapsed.TotalMilliseconds;

                        results[directory][processorCount].Add((searchTime, files.Count));
                        Console.WriteLine($"    迭代 {i + 1}: 搜索时间 = {searchTime:F2} ms, 文件数 = {files.Count}");

                        // 清理
                        GC.Collect();
                        Thread.Sleep(500); // 给系统时间回收资源
                    }

                    // 测试GetFiles方法
                    Console.WriteLine($"  测试GetFiles方法 (并行度: {processorCount})");
                    for (int i = 0; i < iterations; i++)
                    {
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        var fileInfos = FileSearcher.GetFiles(directory, processorCount, false);
                        stopwatch.Stop();
                        double searchTime = stopwatch.Elapsed.TotalMilliseconds;

                        Console.WriteLine($"    迭代 {i + 1}: 搜索时间 = {searchTime:F2} ms, 文件数 = {fileInfos.Count}");

                        // 清理
                        GC.Collect();
                        Thread.Sleep(500);
                    }

                    // 测试 UltraFastScanner
                    Console.WriteLine($"  测试UltraFastScanner方法 (并行度: {processorCount})");

                    for (int i = 0; i < iterations; i++)
                    {
                        var scanner = new FileUltraScanner(
                             directory,
                             maxConcurrency: processorCount,
                             batchSize: 8192,
                             followSymlinks: false,
                             []
                         );

                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        var result = await scanner.ScanAsync();
                        stopwatch.Stop();
                        double searchTime = stopwatch.Elapsed.TotalMilliseconds;
                        Console.WriteLine($"    迭代 {i + 1}: 搜索时间 = {searchTime:F2} ms, 文件数 = {result.FileCount}");
                        // 清理
                        GC.Collect();
                        Thread.Sleep(500);
                    }
                }
            }

            // 生成性能报告
            GeneratePerformanceReport(results, testDirectories, processorCounts);
        }

        /// <summary>
        /// 获取测试目录列表
        /// </summary>
        private string[] GetTestDirectories()
        {
            // 返回一组不同大小的目录用于测试
            return new string[]
            {
                // 小型目录 (例如少于100个文件)
                //Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SmallTestFolder"),

                // 中型目录 (几百到几千个文件)
                //Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),

                // 大型目录 (上万个文件)
                //Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),

                // 根据实际情况可以添加或修改这些路径
                //@"E:\guanpeng\__my"

                @"E:\guanpeng\docs"
            };
        }

        /// <summary>
        /// 获取目录的基本信息
        /// </summary>
        private string GetDirectoryInfo(string directory)
        {
            try
            {
                int fileCount = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly).Length;
                int dirCount = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly).Length;

                return $"  顶层文件数: {fileCount}, 顶层子目录数: {dirCount}";
            }
            catch (Exception ex)
            {
                return $"  获取目录信息失败: {ex.Message}";
            }
        }

        /// <summary>
        /// 生成性能测试报告
        /// </summary>
        private void GeneratePerformanceReport(
            Dictionary<string, Dictionary<int, List<(double SearchTime, int FileCount)>>> results,
            string[] directories,
            int[] processorCounts)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# FileSearcher 性能测试报告");
            report.AppendLine();
            report.AppendLine("## 测试环境");
            report.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            report.AppendLine($"- 处理器: {Environment.ProcessorCount} 核心");
            report.AppendLine($"- .NET 版本: {Environment.Version}");
            report.AppendLine($"- 测试时间: {DateTime.Now}");
            report.AppendLine();

            report.AppendLine("## 测试结果");
            report.AppendLine();

            foreach (var directory in directories)
            {
                if (!results.ContainsKey(directory))
                    continue;

                report.AppendLine($"### 目录: {directory}");
                report.AppendLine();
                report.AppendLine("| 并行度 | 平均搜索时间 (ms) | 文件数 | 每秒处理文件数 |");
                report.AppendLine("|--------|-----------------|--------|---------------|");

                foreach (var processorCount in processorCounts)
                {
                    if (!results[directory].ContainsKey(processorCount) ||
                        results[directory][processorCount].Count == 0)
                        continue;

                    // 跳过第一次迭代的结果（预热）
                    var validResults = results[directory][processorCount].Skip(1).ToList();
                    if (validResults.Count == 0)
                        validResults = results[directory][processorCount];

                    double avgTime = validResults.Average(r => r.SearchTime);
                    int avgFileCount = (int)validResults.Average(r => r.FileCount);
                    double filesPerSecond = avgFileCount / (avgTime / 1000.0);

                    report.AppendLine($"| {processorCount} | {avgTime:F2} | {avgFileCount} | {filesPerSecond:F0} |");
                }
                report.AppendLine();

                // 添加加速比分析
                if (results[directory].ContainsKey(1) && results[directory][1].Count > 0)
                {
                    report.AppendLine("### 并行加速比分析");
                    report.AppendLine();
                    report.AppendLine("| 并行度 | 加速比 | 效率 |");
                    report.AppendLine("|--------|--------|------|");

                    double singleThreadTime = results[directory][1].Skip(1).Average(r => r.SearchTime);

                    foreach (var processorCount in processorCounts.Where(p => p > 1))
                    {
                        if (!results[directory].ContainsKey(processorCount) ||
                            results[directory][processorCount].Count <= 1)
                            continue;

                        double multiThreadTime = results[directory][processorCount].Skip(1).Average(r => r.SearchTime);
                        double speedup = singleThreadTime / multiThreadTime;
                        double efficiency = speedup / processorCount;

                        report.AppendLine($"| {processorCount} | {speedup:F2}x | {efficiency:P2} |");
                    }
                    report.AppendLine();
                }
            }

            // 性能优化建议
            report.AppendLine("## 性能优化建议");
            report.AppendLine();
            report.AppendLine("基于测试结果，我们可以得出以下建议:");
            report.AppendLine();
            report.AppendLine("1. **最佳并行度**: 并行度设置为处理器核心数通常能获得最佳性能，但在IO密集型操作上可能需要更高的并行度。");
            report.AppendLine("2. **大型目录处理**: 对于大型目录，`GetFiles`方法(使用`EnumerateFiles`)通常比`SearchFiles`更高效，特别是在内存受限的环境中。");
            report.AppendLine("3. **并行效率**: 随着并行度增加，效率通常会下降。这主要受磁盘IO限制，增加处理器数量可能不会线性提升性能。");
            report.AppendLine("4. **优化建议**: 考虑在产品中根据文件系统类型(SSD vs HDD)和目录大小动态调整并行度。");
            report.AppendLine();

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "FileSearcher性能测试报告.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"性能测试报告已保存至: {reportPath}");
            Assert.True(File.Exists(reportPath));
        }

        [Fact]
        public async Task TestFileSearcherMemoryUsage()
        {
            // 选一个较大的目录用于测试内存使用情况
            string testDirectory = @"E:\guanpeng\docs"; // Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(testDirectory))
            {
                Console.WriteLine($"测试目录不存在: {testDirectory}");
                return;
            }

            Console.WriteLine($"测试目录: {testDirectory}");
            Console.WriteLine("测试SearchFiles方法和GetFiles方法的内存使用情况...");

            // 测试前记录内存基准
            GC.Collect();
            long beforeMemory = GC.GetTotalMemory(true);

            // 测试SearchFiles方法内存使用
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var files = FileSearcher.SearchFiles(testDirectory);
            stopwatch.Stop();

            // 收集搜索后的内存使用
            long afterSearchFilesMemory = GC.GetTotalMemory(false);
            long searchFilesMemoryUsage = afterSearchFilesMemory - beforeMemory;

            Console.WriteLine($"SearchFiles 结果: {files.Count} 文件");
            Console.WriteLine($"SearchFiles 耗时: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"SearchFiles 内存使用: {searchFilesMemoryUsage / 1024 / 1024} MB");

            // 清理
            files = null;
            GC.Collect();
            Thread.Sleep(1000);
            beforeMemory = GC.GetTotalMemory(true);

            // 测试GetFiles方法内存使用
            stopwatch.Restart();
            var fileInfos = FileSearcher.GetFiles(testDirectory);
            stopwatch.Stop();

            // 收集搜索后的内存使用
            long afterGetFilesMemory = GC.GetTotalMemory(false);
            long getFilesMemoryUsage = afterGetFilesMemory - beforeMemory;

            Console.WriteLine($"GetFiles 结果: {fileInfos.Count} 文件");
            Console.WriteLine($"GetFiles 耗时: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"GetFiles 内存使用: {getFilesMemoryUsage / 1024 / 1024} MB");

            // 测试 UltraFastScanner 内存使用

            GC.Collect();
            Thread.Sleep(1000);
            beforeMemory = GC.GetTotalMemory(true);

            var scanner = new FileUltraScanner(
                testDirectory,
                maxConcurrency: Environment.ProcessorCount,
                batchSize: 8192,
                followSymlinks: false,
                []
            );
            stopwatch.Restart();
            var result = await scanner.ScanAsync();
            stopwatch.Stop();
            long afterUltraFastScannerMemory = GC.GetTotalMemory(false);
            long ultraFastScannerMemoryUsage = afterUltraFastScannerMemory - afterGetFilesMemory;
            Console.WriteLine($"UltraFastScanner 结果: {result.FileCount} 文件");
            Console.WriteLine($"UltraFastScanner 耗时: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"UltraFastScanner 内存使用: {ultraFastScannerMemoryUsage / 1024 / 1024} MB");
            // 清理
            result = null;
            GC.Collect();
            Thread.Sleep(1000);


            // 简单断言确保测试有效进行
            Assert.True(files != null && fileInfos != null);

            // 生成简单的内存使用报告
            StringBuilder report = new StringBuilder();
            report.AppendLine("# FileSearcher 内存使用测试报告");
            report.AppendLine();
            report.AppendLine($"测试目录: {testDirectory}");
            report.AppendLine($"测试时间: {DateTime.Now}");
            report.AppendLine();
            report.AppendLine("| 方法 | 文件数量 | 执行时间(ms) | 内存使用(MB) |");
            report.AppendLine("|------|----------|--------------|-------------|");
            report.AppendLine($"| SearchFiles | {files.Count} | {stopwatch.ElapsedMilliseconds} | {searchFilesMemoryUsage / 1024 / 1024} |");
            report.AppendLine($"| GetFiles | {fileInfos.Count} | {stopwatch.ElapsedMilliseconds} | {getFilesMemoryUsage / 1024 / 1024} |");

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "FileSearcher内存使用报告.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"内存使用报告已保存至: {reportPath}");
        }
    }
}