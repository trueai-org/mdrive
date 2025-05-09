using MDriveSync.Security;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using Xunit.Abstractions;

namespace MDriveSync.Test
{
    /// <summary>
    /// 测试多种哈希算法的耗时，并输出结果
    /// </summary>
    public class HashAlgorithmPerformanceTests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public HashAlgorithmPerformanceTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestHashAlgorithmPerformance()
        {
            // 测试配置
            int dataSizeMB = 100;
            int numRounds = 10;
            string[] algorithms = { "SHA1", "SHA256", "SHA384", "MD5", "XXH3", "XXH128", "BLAKE3" };

            // 准备测试数据 (100MB)
            int dataSize = dataSizeMB * 1024 * 1024;
            byte[] data = new byte[dataSize];
            new Random(42).NextBytes(data); // 使用固定种子以确保结果可重现

            // 收集测试结果
            var results = new Dictionary<string, List<double>>();
            foreach (var algorithm in algorithms)
            {
                results[algorithm] = new List<double>();
            }

            _output.WriteLine($"比较各种哈希算法性能 ({dataSizeMB}MB 数据):\n");

            // 执行多轮测试
            for (int round = 1; round <= numRounds; round++)
            {
                _output.WriteLine($"测试轮次 {round}:");

                foreach (var algorithm in algorithms)
                {
                    // 测量性能
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();
                    byte[] hash = HashHelper.ComputeHash(data, algorithm);
                    stopwatch.Stop();

                    double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
                    results[algorithm].Add(elapsedMs);

                    _output.WriteLine($"{algorithm,-10}: {elapsedMs,6:F0} ms");
                }

                _output.WriteLine("");

                // 在每轮之间稍微暂停，让系统稳定
                if (round < numRounds)
                {
                    Thread.Sleep(100);
                }
            }

            // 计算平均结果并生成报告
            string report = GenerateReport(results, dataSizeMB, numRounds);

            // 输出到控制台
            _output.WriteLine(report);

            // 保存报告到文件
            // 保存文件到当前项目目录
            var currentDirectory = Directory.GetCurrentDirectory();
            var baseDir = AppDomain.CurrentDomain.BaseDirectory;
            var baseDir2 = AppContext.BaseDirectory;

            string reportPath = Path.Combine(AppContext.BaseDirectory, "哈希算法性能测试报告.md");

            File.WriteAllText(reportPath, report, Encoding.UTF8);

            _output.WriteLine($"\n性能报告已保存至：{reportPath}");
        }

        private string GenerateReport(Dictionary<string, List<double>> results, int dataSizeMB, int numRounds)
        {
            var sb = new StringBuilder();
            sb.AppendLine("# 哈希算法性能测试报告");
            sb.AppendLine();

            // 添加测试环境信息
            sb.AppendLine("## 测试环境");
            sb.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            sb.AppendLine($"- 处理器: {Environment.ProcessorCount} 核心");
            sb.AppendLine($"- .NET 版本: {Environment.Version}");
            sb.AppendLine($"- 测试数据大小: {dataSizeMB} MB");
            sb.AppendLine($"- 测试时间: {DateTime.Now}");
            sb.AppendLine();

            // 计算每个算法的平均耗时
            var averages = new Dictionary<string, double>();
            var hashWidths = new Dictionary<string, int>
            {
                ["SHA1"] = 160,
                ["SHA256"] = 256,
                ["SHA384"] = 384,
                ["MD5"] = 128,
                ["XXH3"] = 64,
                ["XXH128"] = 128,
                ["BLAKE3"] = 256
            };

            foreach (var algorithm in results.Keys)
            {
                // 去掉第一轮的结果（可能受JIT编译影响）
                var validResults = results[algorithm].Skip(1).ToList();
                averages[algorithm] = validResults.Average();
            }

            // 添加平均执行时间表格
            sb.AppendLine("## 测试结果");
            sb.AppendLine();
            sb.AppendLine("### 平均执行时间");
            sb.AppendLine();
            sb.AppendLine("| 算法名称 | 哈希宽度 (位) | 平均耗时 (ms) | 吞吐量 (GB/s) |");
            sb.AppendLine("|---------|------------|------------|------------|");

            foreach (var entry in averages.OrderBy(a => a.Value))
            {
                string algorithm = entry.Key;
                double avgMs = entry.Value;
                int width = hashWidths.ContainsKey(algorithm) ? hashWidths[algorithm] : 0;

                // 计算吞吐量 (GB/s)
                double throughput = (dataSizeMB / 1024.0) / (avgMs / 1000.0);

                sb.AppendLine($"| {algorithm,-8} | {width,-3}  | {avgMs,-6:F2}  | {throughput,-4:F2} |");
            }

            sb.AppendLine();

            // 添加速度比较表格
            sb.AppendLine("### 算法速度比较");
            sb.AppendLine();
            sb.AppendLine("| 比较项 | 速度比率 |");
            sb.AppendLine("|-------|--------|");

            // 添加一些有意义的速度比较
            AddSpeedComparison(sb, averages, "SHA1", "SHA256");
            AddSpeedComparison(sb, averages, "SHA1", "SHA384");
            AddSpeedComparison(sb, averages, "XXH3", "SHA1");
            AddSpeedComparison(sb, averages, "XXH3", "SHA256");
            AddSpeedComparison(sb, averages, "BLAKE3", "SHA1");
            AddSpeedComparison(sb, averages, "BLAKE3", "SHA256");
            AddSpeedComparison(sb, averages, "BLAKE3", "XXH3");

            return sb.ToString();
        }

        private void AddSpeedComparison(StringBuilder sb, Dictionary<string, double> averages, string algo1, string algo2)
        {
            if (averages.ContainsKey(algo1) && averages.ContainsKey(algo2))
            {
                double ratio = averages[algo2] / averages[algo1];
                sb.AppendLine($"| {algo1} 比 {algo2} 快 | {ratio,-4:F2} 倍 |");
            }
        }
    }
}
