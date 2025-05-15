using MDriveSync.Core.Services;
using Xunit;
using Xunit.Abstractions;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace MDriveSync.Test
{
    /// <summary>
    /// 哈希采样参数计算优化测试
    /// </summary>
    public class SamplingParametersTests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public SamplingParametersTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Theory]
        [InlineData(100 * 1024, 0.1)]             // 100KB, 10%采样率
        [InlineData(1 * 1024 * 1024, 0.1)]        // 1MB, 10%采样率
        [InlineData(10 * 1024 * 1024, 0.1)]       // 10MB, 10%采样率
        [InlineData(100 * 1024 * 1024, 0.1)]      // 100MB, 10%采样率
        [InlineData(1 * 1024 * 1024 * 1024L, 0.1)] // 1GB, 10%采样率
        [InlineData(10 * 1024 * 1024, 0.05)]      // 10MB, 5%采样率
        [InlineData(10 * 1024 * 1024, 0.2)]       // 10MB, 20%采样率
        [InlineData(10 * 1024 * 1024, 0.5)]       // 10MB, 50%采样率
        [InlineData(10 * 1024 * 1024, 0.01)]      // 10MB, 1%采样率
        public void CalculateOptimalSamplingParameters_ReturnsReasonableValues(long fileSize, double rate)
        {
            // Act
            var (blockSize, blockCount, samplesToCheck) = FileSyncHelper.CalculateOptimalSamplingParameters(fileSize, rate);

            // 计算实际采样率
            double actualSamplingRate = (double)samplesToCheck / blockCount;

            // 计算覆盖率
            double coveragePercentage = (double)(blockSize * samplesToCheck) / fileSize * 100;

            // 输出结果
            _output.WriteLine($"文件大小: {FormatSize(fileSize)}");
            _output.WriteLine($"目标采样率: {rate:P2}");
            _output.WriteLine($"计算结果:");
            _output.WriteLine($"  块大小: {FormatSize(blockSize)}");
            _output.WriteLine($"  块数量: {blockCount}");
            _output.WriteLine($"  抽样数: {samplesToCheck}");
            _output.WriteLine($"  实际采样率: {actualSamplingRate:P2}");
            _output.WriteLine($"  数据覆盖率: {coveragePercentage:F2}%");
            _output.WriteLine("");

            // Assert
            // 1. 采样数量应在合理范围内
            Assert.InRange(samplesToCheck, 2, 100); // 最小2个样本，最大100个样本

            // 2. 块大小应满足最小块限制
            Assert.True(blockSize >= 64 * 1024, "块大小应大于等于64KB");

            // 3. 块数量应该合理
            Assert.True(blockCount > 0, "块数量应大于0");

            // 4. 块大小 * 块数量应该接近文件大小
            Assert.True(blockSize * blockCount >= fileSize, "总块大小应覆盖整个文件");

            // 5. 实际采样率应接近目标采样率
            if (fileSize > 1 * 1024 * 1024) // 对于大于1MB的文件做此检查
            {
                // 对于量较小的文件，允许较大的偏差
                double tolerance = rate < 0.1 ? 1.0 : 0.5; // 非常低的采样率可能有较大偏差
                Assert.True(actualSamplingRate >= rate / tolerance && actualSamplingRate <= rate * tolerance,
                    $"实际采样率 {actualSamplingRate:P2} 应接近目标采样率 {rate:P2}");
            }
        }

        [Fact]
        public void CalculateOptimalSamplingParameters_PerformanceBenchmark()
        {
            // 准备测试数据
            var fileSizes = new List<long>
            {
                16 * 1024,         // 16KB
                64 * 1024,         // 64KB
                128 * 1024,         // 128KB
                1 * 1024 * 1024,         // 1MB
                10 * 1024 * 1024,        // 10MB
                20 * 1024 * 1024,        // 20MB
                50 * 1024 * 1024,       // 100MB
                100 * 1024 * 1024,       // 100MB
                200 * 1024 * 1024,       // 200MB
                300 * 1024 * 1024,       // 300MB
                800 * 1024 * 1024,       // 800MB
                1 * 1024 * 1024 * 1024L, // 1GB
                10 * 1024 * 1024 * 1024L, // 10GB
                50 * 1024 * 1024 * 1024L // 50GB
            };

            // 随机填充 1b - 1TB
            for (int i = 0; i < 10; i++)
            {
                fileSizes.Add((long)(new Random().NextDouble() * 1024 * 1024 * 1024 * 1024));
            }

            // 随机填充 1b - 1G
            for (int i = 0; i < 10; i++)
            {
                fileSizes.Add((long)(new Random().NextDouble() * 1024 * 1024 * 1024));
            }

            // 排序
            fileSizes.Sort();

            var samplingRates = new List<double> { 0.01, 0.05, 0.1, 0.2, 0.5, 0.6, 0.7, 0.75, 0.8, 0.85, 0.9, 0.95, 1 };
            // 随机填充
            for (int i = 0; i < 10; i++)
            {
                samplingRates.Add(new Random().NextDouble());
            }
            samplingRates.Sort();

            var results = new List<(long FileSize, double Rate, long BlockSize, int BlockCount, int SamplesCount,
                double ActualRate, double Coverage, double ElapsedMs)>();

            // 执行基准测试
            foreach (var fileSize in fileSizes)
            {
                foreach (var rate in samplingRates)
                {
                    var stopwatch = new Stopwatch();
                    stopwatch.Start();

                    // 重复调用以获得更准确的性能测量
                    const int iterations = 1000;
                    var blockSizeSum = 0L;
                    var blockCountSum = 0;
                    var samplesSum = 0;

                    for (int i = 0; i < iterations; i++)
                    {
                        var (blockSize, blockCount, samples) = FileSyncHelper.CalculateOptimalSamplingParameters(fileSize, rate);
                        blockSizeSum += blockSize;
                        blockCountSum += blockCount;
                        samplesSum += samples;
                    }

                    stopwatch.Stop();

                    // 计算平均值
                    var avgBlockSize = blockSizeSum / iterations;
                    var avgBlockCount = blockCountSum / (double)iterations;
                    var avgSamples = samplesSum / (double)iterations;

                    // 计算实际采样率和覆盖率
                    double actualRate = avgSamples / avgBlockCount;
                    double coverage = (avgBlockSize * avgSamples) / (double)fileSize * 100;

                    // 记录结果
                    results.Add((fileSize, rate, avgBlockSize, (int)avgBlockCount, (int)avgSamples,
                        actualRate, coverage, stopwatch.Elapsed.TotalMilliseconds / iterations));
                }
            }

            // 输出结果
            var sb = new StringBuilder();
            sb.AppendLine("## 采样参数计算性能基准测试");
            sb.AppendLine("| 文件大小 | 目标采样率 | 块大小 | 块数量 | 抽样数 | 实际采样率 | 覆盖率 (%) | 计算时间 (μs) |");
            sb.AppendLine("|----------|------------|--------|--------|--------|------------|------------|--------------|");

            foreach (var result in results)
            {
                // 修复字符串格式化错误，将对齐说明符放在格式说明符之前
                var str = $"| {FormatSize(result.FileSize),-8} | {result.Rate,-10:P2} | " +
                          $"{FormatSize(result.BlockSize),-6} | {result.BlockCount,-6} | {result.SamplesCount,-6} | " +
                          $"{result.ActualRate,-10:P2} | {result.Coverage,-10:F2} | {result.ElapsedMs * 1000,-12:F2} |";
                sb.AppendLine(str);
            }
            // 保存到文件以便查看完整结果
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "采样参数性能测试报告.md");

            // 使用 _output.ToString() 获取 Xunit 输出到内存的内容
            File.WriteAllText(reportPath, sb.ToString(), Encoding.UTF8);
            _output.WriteLine(sb.ToString());
            _output.WriteLine($"\n报告已保存至: {reportPath}");

            // 简单断言以确保测试运行完成
            Assert.True(results.Count == fileSizes.Count * samplingRates.Count, "应该测试了所有文件大小和采样率组合");
        }

        [Fact]
        public void CalculateOptimalSamplingParameters_EdgeCases()
        {
            // 测试极端情况

            // 1. 非常小的文件
            var smallResult = FileSyncHelper.CalculateOptimalSamplingParameters(1024, 0.1);
            _output.WriteLine($"非常小的文件 (1KB):");
            _output.WriteLine($"  块大小: {FormatSize(smallResult.BlockSize)}");
            _output.WriteLine($"  块数量: {smallResult.BlockCount}");
            _output.WriteLine($"  抽样数: {smallResult.SamplesToCheck}");

            // 2. 非常大的文件
            var largeResult = FileSyncHelper.CalculateOptimalSamplingParameters(100L * 1024 * 1024 * 1024, 0.1);
            _output.WriteLine($"\n非常大的文件 (100GB):");
            _output.WriteLine($"  块大小: {FormatSize(largeResult.BlockSize)}");
            _output.WriteLine($"  块数量: {largeResult.BlockCount}");
            _output.WriteLine($"  抽样数: {largeResult.SamplesToCheck}");

            // 3. 非常低的采样率
            var lowRateResult = FileSyncHelper.CalculateOptimalSamplingParameters(100 * 1024 * 1024, 0.001);
            _output.WriteLine($"\n非常低的采样率 (0.1%):");
            _output.WriteLine($"  块大小: {FormatSize(lowRateResult.BlockSize)}");
            _output.WriteLine($"  块数量: {lowRateResult.BlockCount}");
            _output.WriteLine($"  抽样数: {lowRateResult.SamplesToCheck}");

            // 4. 非常高的采样率
            var highRateResult = FileSyncHelper.CalculateOptimalSamplingParameters(100 * 1024 * 1024, 0.9);
            _output.WriteLine($"\n非常高的采样率 (90%):");
            _output.WriteLine($"  块大小: {FormatSize(highRateResult.BlockSize)}");
            _output.WriteLine($"  块数量: {highRateResult.BlockCount}");
            _output.WriteLine($"  抽样数: {highRateResult.SamplesToCheck}");

            // 断言
            Assert.True(smallResult.BlockCount >= 1, "即使是小文件也应至少有1个块");
            Assert.True(smallResult.SamplesToCheck >= 1, "即使是小文件也应至少有1个样本");

            Assert.True(largeResult.BlockSize <= 64 * 1024 * 1024, "即使是大文件块大小也应该有上限");
            Assert.True(largeResult.SamplesToCheck <= 100, "样本数应该有上限");

            Assert.True(lowRateResult.SamplesToCheck >= 2, "即使采样率很低也应至少有2个样本");

            Assert.True(highRateResult.SamplesToCheck < highRateResult.BlockCount ||
                       highRateResult.BlockCount <= 10, "高采样率不应导致不必要的全部抽样");
        }

        /// <summary>
        /// 格式化数据大小显示
        /// </summary>
        private string FormatSize(long bytes)
        {
            string[] suffixes = { "B", "KB", "MB", "GB", "TB", "PB" };
            int i = 0;
            double size = bytes;
            while (size >= 1024 && i < suffixes.Length - 1)
            {
                size /= 1024;
                i++;
            }
            return $"{size:0.##} {suffixes[i]}";
        }
    }
}
