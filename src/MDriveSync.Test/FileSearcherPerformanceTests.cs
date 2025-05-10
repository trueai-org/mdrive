using MDriveSync.Core.Services;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MDriveSync.Test
{
    /// <summary>
    /// ����FileSearcher������ָ��
    /// </summary>
    public class FileSearcherPerformanceTests : BaseTests
    {
        /// <summary>
        /// �����ļ� - ���߳����ܲ���
        /// </summary>
        /// <returns></returns>
        [Fact]
        public async Task TestFileSearcherPerformanceBig()
        {
            // ���ò��Բ���
            string[] testDirectories = [@"E:\guanpeng\__my"];
            int[] processorCounts = { Environment.ProcessorCount, Environment.ProcessorCount * 2 };

            // �ռ����
            var results = new Dictionary<string, Dictionary<int, List<(double SearchTime, int FileCount)>>>();
            foreach (var dir in testDirectories)
            {
                results[dir] = new Dictionary<int, List<(double, int)>>();
                foreach (var count in processorCounts)
                {
                    results[dir][count] = new List<(double, int)>();
                }
            }

            // ���в���
            int iterations = 3; // ִ�ж��ȡƽ��ֵ
            Console.WriteLine("��ʼ����FileSearcher����...");

            foreach (var directory in testDirectories)
            {
                if (!Directory.Exists(directory))
                {
                    Console.WriteLine($"Ŀ¼�����ڣ�����: {directory}");
                    continue;
                }

                Console.WriteLine($"����Ŀ¼: {directory}");
                string dirInfo = GetDirectoryInfo(directory);
                Console.WriteLine(dirInfo);

                foreach (var processorCount in processorCounts)
                {

                    // ���� UltraFastScanner
                    Console.WriteLine($"  ����UltraFastScanner���� (���ж�: {processorCount})");

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
                        Console.WriteLine($"    ���� {i + 1}: ����ʱ�� = {searchTime:F2} ms, �ļ��� = {result.FileCount}");
                        // ����
                        GC.Collect();
                        Thread.Sleep(50);
                    }

                    // ����GetFiles����
                    Console.WriteLine($"  ����GetFiles���� (���ж�: {processorCount})");
                    for (int i = 0; i < iterations; i++)
                    {
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        var fileInfos = FileSearcher.GetFiles(directory, processorCount, true);
                        stopwatch.Stop();
                        double searchTime = stopwatch.Elapsed.TotalMilliseconds;

                        Console.WriteLine($"    ���� {i + 1}: ����ʱ�� = {searchTime:F2} ms, �ļ��� = {fileInfos.Count}");

                        // ����
                        GC.Collect();
                        Thread.Sleep(50);
                    }

                  
                }
            }

            // �������ܱ���
            GeneratePerformanceReport(results, testDirectories, processorCounts);
        }

        [Fact]
        public async Task TestFileSearcherPerformance()
        {
            // ���ò��Բ���
            string[] testDirectories = GetTestDirectories();
            int[] processorCounts = { 1, 2, 4, 8, Environment.ProcessorCount };

            // �ռ����
            var results = new Dictionary<string, Dictionary<int, List<(double SearchTime, int FileCount)>>>();
            foreach (var dir in testDirectories)
            {
                results[dir] = new Dictionary<int, List<(double, int)>>();
                foreach (var count in processorCounts)
                {
                    results[dir][count] = new List<(double, int)>();
                }
            }

            // ���в���
            int iterations = 3; // ִ�ж��ȡƽ��ֵ
            Console.WriteLine("��ʼ����FileSearcher����...");

            foreach (var directory in testDirectories)
            {
                if (!Directory.Exists(directory))
                {
                    Console.WriteLine($"Ŀ¼�����ڣ�����: {directory}");
                    continue;
                }

                Console.WriteLine($"����Ŀ¼: {directory}");
                string dirInfo = GetDirectoryInfo(directory);
                Console.WriteLine(dirInfo);

                foreach (var processorCount in processorCounts)
                {
                    Console.WriteLine($"   ����SearchFiles���� ���ж�: {processorCount}");

                    for (int i = 0; i < iterations; i++)
                    {
                        // ����SearchFiles����
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        var files = FileSearcher.SearchFiles(directory, processorCount, false);
                        stopwatch.Stop();
                        double searchTime = stopwatch.Elapsed.TotalMilliseconds;

                        results[directory][processorCount].Add((searchTime, files.Count));
                        Console.WriteLine($"    ���� {i + 1}: ����ʱ�� = {searchTime:F2} ms, �ļ��� = {files.Count}");

                        // ����
                        GC.Collect();
                        Thread.Sleep(500); // ��ϵͳʱ�������Դ
                    }

                    // ����GetFiles����
                    Console.WriteLine($"  ����GetFiles���� (���ж�: {processorCount})");
                    for (int i = 0; i < iterations; i++)
                    {
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        var fileInfos = FileSearcher.GetFiles(directory, processorCount, false);
                        stopwatch.Stop();
                        double searchTime = stopwatch.Elapsed.TotalMilliseconds;

                        Console.WriteLine($"    ���� {i + 1}: ����ʱ�� = {searchTime:F2} ms, �ļ��� = {fileInfos.Count}");

                        // ����
                        GC.Collect();
                        Thread.Sleep(500);
                    }

                    // ���� UltraFastScanner
                    Console.WriteLine($"  ����UltraFastScanner���� (���ж�: {processorCount})");

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
                        Console.WriteLine($"    ���� {i + 1}: ����ʱ�� = {searchTime:F2} ms, �ļ��� = {result.FileCount}");
                        // ����
                        GC.Collect();
                        Thread.Sleep(500);
                    }
                }
            }

            // �������ܱ���
            GeneratePerformanceReport(results, testDirectories, processorCounts);
        }

        /// <summary>
        /// ��ȡ����Ŀ¼�б�
        /// </summary>
        private string[] GetTestDirectories()
        {
            // ����һ�鲻ͬ��С��Ŀ¼���ڲ���
            return new string[]
            {
                // С��Ŀ¼ (��������100���ļ�)
                //Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments), "SmallTestFolder"),

                // ����Ŀ¼ (���ٵ���ǧ���ļ�)
                //Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments),

                // ����Ŀ¼ (������ļ�)
                //Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),

                // ����ʵ�����������ӻ��޸���Щ·��
                //@"E:\guanpeng\__my"

                @"E:\guanpeng\docs"
            };
        }

        /// <summary>
        /// ��ȡĿ¼�Ļ�����Ϣ
        /// </summary>
        private string GetDirectoryInfo(string directory)
        {
            try
            {
                int fileCount = Directory.GetFiles(directory, "*", SearchOption.TopDirectoryOnly).Length;
                int dirCount = Directory.GetDirectories(directory, "*", SearchOption.TopDirectoryOnly).Length;

                return $"  �����ļ���: {fileCount}, ������Ŀ¼��: {dirCount}";
            }
            catch (Exception ex)
            {
                return $"  ��ȡĿ¼��Ϣʧ��: {ex.Message}";
            }
        }

        /// <summary>
        /// �������ܲ��Ա���
        /// </summary>
        private void GeneratePerformanceReport(
            Dictionary<string, Dictionary<int, List<(double SearchTime, int FileCount)>>> results,
            string[] directories,
            int[] processorCounts)
        {
            StringBuilder report = new StringBuilder();
            report.AppendLine("# FileSearcher ���ܲ��Ա���");
            report.AppendLine();
            report.AppendLine("## ���Ի���");
            report.AppendLine($"- ����ϵͳ: {RuntimeInformation.OSDescription}");
            report.AppendLine($"- ������: {Environment.ProcessorCount} ����");
            report.AppendLine($"- .NET �汾: {Environment.Version}");
            report.AppendLine($"- ����ʱ��: {DateTime.Now}");
            report.AppendLine();

            report.AppendLine("## ���Խ��");
            report.AppendLine();

            foreach (var directory in directories)
            {
                if (!results.ContainsKey(directory))
                    continue;

                report.AppendLine($"### Ŀ¼: {directory}");
                report.AppendLine();
                report.AppendLine("| ���ж� | ƽ������ʱ�� (ms) | �ļ��� | ÿ�봦���ļ��� |");
                report.AppendLine("|--------|-----------------|--------|---------------|");

                foreach (var processorCount in processorCounts)
                {
                    if (!results[directory].ContainsKey(processorCount) ||
                        results[directory][processorCount].Count == 0)
                        continue;

                    // ������һ�ε����Ľ����Ԥ�ȣ�
                    var validResults = results[directory][processorCount].Skip(1).ToList();
                    if (validResults.Count == 0)
                        validResults = results[directory][processorCount];

                    double avgTime = validResults.Average(r => r.SearchTime);
                    int avgFileCount = (int)validResults.Average(r => r.FileCount);
                    double filesPerSecond = avgFileCount / (avgTime / 1000.0);

                    report.AppendLine($"| {processorCount} | {avgTime:F2} | {avgFileCount} | {filesPerSecond:F0} |");
                }
                report.AppendLine();

                // ��Ӽ��ٱȷ���
                if (results[directory].ContainsKey(1) && results[directory][1].Count > 0)
                {
                    report.AppendLine("### ���м��ٱȷ���");
                    report.AppendLine();
                    report.AppendLine("| ���ж� | ���ٱ� | Ч�� |");
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

            // �����Ż�����
            report.AppendLine("## �����Ż�����");
            report.AppendLine();
            report.AppendLine("���ڲ��Խ�������ǿ��Եó����½���:");
            report.AppendLine();
            report.AppendLine("1. **��Ѳ��ж�**: ���ж�����Ϊ������������ͨ���ܻ��������ܣ�����IO�ܼ��Ͳ����Ͽ�����Ҫ���ߵĲ��жȡ�");
            report.AppendLine("2. **����Ŀ¼����**: ���ڴ���Ŀ¼��`GetFiles`����(ʹ��`EnumerateFiles`)ͨ����`SearchFiles`����Ч���ر������ڴ����޵Ļ����С�");
            report.AppendLine("3. **����Ч��**: ���Ų��ж����ӣ�Ч��ͨ�����½�������Ҫ�ܴ���IO���ƣ����Ӵ������������ܲ��������������ܡ�");
            report.AppendLine("4. **�Ż�����**: �����ڲ�Ʒ�и����ļ�ϵͳ����(SSD vs HDD)��Ŀ¼��С��̬�������жȡ�");
            report.AppendLine();

            // ���汨��
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "FileSearcher���ܲ��Ա���.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"���ܲ��Ա����ѱ�����: {reportPath}");
            Assert.True(File.Exists(reportPath));
        }

        [Fact]
        public async Task TestFileSearcherMemoryUsage()
        {
            // ѡһ���ϴ��Ŀ¼���ڲ����ڴ�ʹ�����
            string testDirectory = @"E:\guanpeng\docs"; // Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (!Directory.Exists(testDirectory))
            {
                Console.WriteLine($"����Ŀ¼������: {testDirectory}");
                return;
            }

            Console.WriteLine($"����Ŀ¼: {testDirectory}");
            Console.WriteLine("����SearchFiles������GetFiles�������ڴ�ʹ�����...");

            // ����ǰ��¼�ڴ��׼
            GC.Collect();
            long beforeMemory = GC.GetTotalMemory(true);

            // ����SearchFiles�����ڴ�ʹ��
            var stopwatch = new Stopwatch();
            stopwatch.Start();
            var files = FileSearcher.SearchFiles(testDirectory);
            stopwatch.Stop();

            // �ռ���������ڴ�ʹ��
            long afterSearchFilesMemory = GC.GetTotalMemory(false);
            long searchFilesMemoryUsage = afterSearchFilesMemory - beforeMemory;

            Console.WriteLine($"SearchFiles ���: {files.Count} �ļ�");
            Console.WriteLine($"SearchFiles ��ʱ: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"SearchFiles �ڴ�ʹ��: {searchFilesMemoryUsage / 1024 / 1024} MB");

            // ����
            files = null;
            GC.Collect();
            Thread.Sleep(1000);
            beforeMemory = GC.GetTotalMemory(true);

            // ����GetFiles�����ڴ�ʹ��
            stopwatch.Restart();
            var fileInfos = FileSearcher.GetFiles(testDirectory);
            stopwatch.Stop();

            // �ռ���������ڴ�ʹ��
            long afterGetFilesMemory = GC.GetTotalMemory(false);
            long getFilesMemoryUsage = afterGetFilesMemory - beforeMemory;

            Console.WriteLine($"GetFiles ���: {fileInfos.Count} �ļ�");
            Console.WriteLine($"GetFiles ��ʱ: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"GetFiles �ڴ�ʹ��: {getFilesMemoryUsage / 1024 / 1024} MB");

            // ���� UltraFastScanner �ڴ�ʹ��

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
            Console.WriteLine($"UltraFastScanner ���: {result.FileCount} �ļ�");
            Console.WriteLine($"UltraFastScanner ��ʱ: {stopwatch.ElapsedMilliseconds} ms");
            Console.WriteLine($"UltraFastScanner �ڴ�ʹ��: {ultraFastScannerMemoryUsage / 1024 / 1024} MB");
            // ����
            result = null;
            GC.Collect();
            Thread.Sleep(1000);


            // �򵥶���ȷ��������Ч����
            Assert.True(files != null && fileInfos != null);

            // ���ɼ򵥵��ڴ�ʹ�ñ���
            StringBuilder report = new StringBuilder();
            report.AppendLine("# FileSearcher �ڴ�ʹ�ò��Ա���");
            report.AppendLine();
            report.AppendLine($"����Ŀ¼: {testDirectory}");
            report.AppendLine($"����ʱ��: {DateTime.Now}");
            report.AppendLine();
            report.AppendLine("| ���� | �ļ����� | ִ��ʱ��(ms) | �ڴ�ʹ��(MB) |");
            report.AppendLine("|------|----------|--------------|-------------|");
            report.AppendLine($"| SearchFiles | {files.Count} | {stopwatch.ElapsedMilliseconds} | {searchFilesMemoryUsage / 1024 / 1024} |");
            report.AppendLine($"| GetFiles | {fileInfos.Count} | {stopwatch.ElapsedMilliseconds} | {getFilesMemoryUsage / 1024 / 1024} |");

            // ���汨��
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "FileSearcher�ڴ�ʹ�ñ���.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"�ڴ�ʹ�ñ����ѱ�����: {reportPath}");
        }
    }
}