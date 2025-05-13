using MDriveSync.Core.Services;
using MDriveSync.Infrastructure;
using System.Diagnostics;
using System.Security.Cryptography;
using Xunit.Abstractions;

namespace MDriveSync.Test
{
    /// <summary>
    /// 内容定义分块处理单元测试
    /// 与原始程序保持逻辑一致，但转换为可测试的形式
    /// </summary>
    public class CDCChunkProcessingTests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public CDCChunkProcessingTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void ChunkAndAnalyzeFile_ExactlyMatchesOriginalLogic()
        {
            // Arrange
            int testDataSize = 20 * 1024 * 1024; // 20MB
            byte[] testData = GenerateTestData(testDataSize);

            // string filePath = @"E:\downs\fdm\__分块测试\Driver.rar";

            //// 对文件的第一个未知添加 1 个字节
            //byte[] data = File.ReadAllBytes(filePath);
            //data[0] = 0x01;
            //File.WriteAllBytes(filePath, data);

            //// 对文件的最后个未知追加 1 个字节
            //byte[] data = File.ReadAllBytes(filePath);
            //data[data.Length - 1] = 0x01;
            //File.WriteAllBytes(filePath, data);

            //// 复制文件自身，复制 2 次，最后重新写入
            //var data = File.ReadAllBytes(filePath);
            //var data2 = new byte[data.Length * 2];
            //Buffer.BlockCopy(data, 0, data2, 0, data.Length);
            //Buffer.BlockCopy(data, 0, data2, data.Length, data.Length);
            //File.WriteAllBytes(filePath, data2);

            //// 复制文件自身，复制 n 次，最后重新写入
            //var fn = 3;
            //var data = File.ReadAllBytes(filePath);
            //var data2 = new byte[data.Length * fn];
            //for (int i = 0; i < fn; i++)
            //{
            //    Buffer.BlockCopy(data, 0, data2, i * data.Length, data.Length);
            //}
            //File.WriteAllBytes(filePath, data2);

            // 设置分块参数，与原程序保持一致
            int avgChunkSizeKB = 1024 * 4; // 4MB
            int averageChunkSize = avgChunkSizeKB * 1024;

            // 模拟文件路径和输出目录 - 仅用于输出显示
            string mockFilePath = "test_data.bin";
            string outputDir = $"chunks_{Path.GetFileNameWithoutExtension(mockFilePath)}_{DateTime.Now:yyyyMMdd_HHmmss}";

            _output.WriteLine($"处理文件: {Path.GetFileName(mockFilePath)}");
            _output.WriteLine($"文件大小: {testData.Length.FormatSize()}");
            _output.WriteLine($"配置参数: 平均块 {averageChunkSize.FormatSize()}, 窗口 48 字节");
            _output.WriteLine("");

            // 创建CDC实例，参照原程序配置
            var cdc = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: averageChunkSize,
                minSize: averageChunkSize / 4,
                maxSize: averageChunkSize * 4,
                resetInterval: averageChunkSize * 4 * 2
            );

            var stats = cdc.GetChunkSizeStats();
            _output.WriteLine($"块大小配置: 最小={stats.MinSize.FormatSize()}, 平均={stats.AvgSize.FormatSize()}, 最大={stats.MaxSize.FormatSize()}");

            // 准备统计变量
            var chunkSizes = new List<int>();
            var uniqueChunks = new HashSet<string>();
            var stopwatch = Stopwatch.StartNew();
            long totalBytes = 0;
            int chunkCount = 0;

            // 测试中不实际保存块文件
            // 设置为 true 将保存实际文件块
            bool saveChunks = false; 

            // 创建一个字典保存分块内容和哈希，用于验证
            var chunkData = new Dictionary<string, byte[]>();

            _output.WriteLine("\n开始分块处理...");

            if (saveChunks && !Directory.Exists(outputDir))
                Directory.CreateDirectory(outputDir);

            // Act
            using (var memoryStream = new MemoryStream(testData))
            {
                cdc.Split(memoryStream, (chunk, length) =>
                {
                    chunkCount++;
                    totalBytes += length;
                    chunkSizes.Add(length);

                    // 计算块哈希值
                    string hash = ComputeHash(chunk, length);
                    bool isUnique = uniqueChunks.Add(hash);

                    // 记录块数据（测试中模拟文件保存）
                    if (saveChunks)
                    {
                        chunkData[hash] = chunk.Take(length).ToArray();
                    }

                    // 可选: 保存块到文件
                    if (saveChunks)
                    {
                        string chunkFile = Path.Combine(outputDir, $"{hash.Substring(0, 16)}_{length}.bin");
                        File.WriteAllBytes(chunkFile, chunk.Take(length).ToArray());
                    }

                    // 显示进度（每100个块或前10个块）
                    if (chunkCount % 100 == 0 || chunkCount < 10)
                    {
                        _output.WriteLine($"处理进度: {totalBytes * 100 / memoryStream.Length}% | 已处理 {chunkCount} 个块");
                    }
                });
            }

            stopwatch.Stop();
            _output.WriteLine($"处理完成: {chunkCount} 个块, 总大小: {totalBytes.FormatSize()}");

            // 显示统计信息
            DisplayStatistics(chunkSizes, uniqueChunks.Count, stopwatch.Elapsed, totalBytes);

            // Assert
            // 总处理字节数应等于原始数据大小
            Assert.Equal(testData.Length, totalBytes);
            Assert.True(chunkCount > 0, "应至少生成一个块");

            // 验证块大小在配置范围内
            if (chunkSizes.Count > 1) // 如果数据小于最小块大小可能只有一个块
            {
                Assert.True(chunkSizes.Min() >= stats.MinSize || chunkSizes.Count == 1,
                    $"最小块大小 {chunkSizes.Min()} 应大于等于配置的最小大小 {stats.MinSize}");
                Assert.True(chunkSizes.Max() <= stats.MaxSize,
                    $"最大块大小 {chunkSizes.Max()} 应小于等于配置的最大大小 {stats.MaxSize}");
            }
        }

        [Fact]
        public void ChunkProcessing_ModifyFirstByte()
        {
            // Arrange - 生成基础数据
            int dataSize = 10 * 1024 * 1024; // 10MB
            byte[] originalData = GenerateTestData(dataSize);
            byte[] modifiedData = (byte[])originalData.Clone();

            // 修改第一个字节
            modifiedData[0] = 0x01;

            // 执行分块测试并比较结果
            var result = RunComparativeChunkTest(originalData, modifiedData, "修改第一个字节");

            // Assert
            _output.WriteLine($"相同块比例: {result.SameChunkPercentage:F2}%");
            _output.WriteLine($"第一个不同块的索引: {result.FirstDifferentChunkIndex}");

            // 验证修改的影响是局部的，而不是全局的
            Assert.True(result.FirstDifferentChunkIndex == 0, "第一个块应该受到影响");
            Assert.True(result.SameChunkPercentage > 50, "应有超过50%的块保持不变");
        }

        [Fact]
        public void ChunkProcessing_ModifyLastByte()
        {
            // Arrange - 生成基础数据
            int dataSize = 10 * 1024 * 1024; // 10MB
            byte[] originalData = GenerateTestData(dataSize);
            byte[] modifiedData = (byte[])originalData.Clone();

            // 修改最后一个字节
            modifiedData[modifiedData.Length - 1] = 0x01;

            // 执行分块测试并比较结果
            var result = RunComparativeChunkTest(originalData, modifiedData, "修改最后一个字节");

            // Assert
            _output.WriteLine($"相同块比例: {result.SameChunkPercentage:F2}%");
            _output.WriteLine($"第一个不同块的索引: {result.FirstDifferentChunkIndex}");

            // 验证修改的影响是局部的
            Assert.True(result.FirstDifferentChunkIndex > 0, "第一个块不应受到影响");
            Assert.True(result.SameChunkPercentage > 70, "应有超过70%的块保持不变");
        }

        [Fact]
        public void ChunkProcessing_DuplicateContent_TwoTimes()
        {
            // Arrange
            int originalSize = 5 * 1024 * 1024; // 5MB
            byte[] originalData = GenerateTestData(originalSize);

            // 复制数据两次
            byte[] duplicatedData = new byte[originalSize * 2];
            Buffer.BlockCopy(originalData, 0, duplicatedData, 0, originalSize);
            Buffer.BlockCopy(originalData, 0, duplicatedData, originalSize, originalSize);

            // 执行分块测试
            var result = ChunkAndAnalyzeWithStats(duplicatedData, "复制2次");

            // Assert
            double expectedDuplicateRatio = 50.0; // 理论上有50%的块是重复的
            double actualDuplicateRatio = 100 - (result.UniqueChunks * 100.0 / result.TotalChunks);

            _output.WriteLine($"理论重复率: {expectedDuplicateRatio:F2}%");
            _output.WriteLine($"实际重复率: {actualDuplicateRatio:F2}%");

            Assert.True(actualDuplicateRatio > 40, "重复率应接近理论值(50%)");

            // 总字节数应等于原始数据大小
            Assert.Equal(duplicatedData.Length, result.TotalBytes);
        }

        [Fact]
        public void ChunkProcessing_DuplicateContent_ThreeTimes()
        {
            // Arrange
            int originalSize = 3 * 1024 * 1024; // 3MB
            byte[] originalData = GenerateTestData(originalSize);

            // 复制数据三次
            int fn = 3;
            byte[] duplicatedData = new byte[originalSize * fn];
            for (int i = 0; i < fn; i++)
            {
                Buffer.BlockCopy(originalData, 0, duplicatedData, i * originalSize, originalSize);
            }

            // 执行分块测试
            var result = ChunkAndAnalyzeWithStats(duplicatedData, "复制3次");

            // Assert
            double expectedDuplicateRatio = 66.7; // 理论上有2/3的块是重复的
            double actualDuplicateRatio = 100 - (result.UniqueChunks * 100.0 / result.TotalChunks);

            _output.WriteLine($"理论重复率: {expectedDuplicateRatio:F2}%");
            _output.WriteLine($"实际重复率: {actualDuplicateRatio:F2}%");

            Assert.True(actualDuplicateRatio > 50, "重复率应接近理论值(66.7%)");

            // 总字节数应等于原始数据大小
            Assert.Equal(duplicatedData.Length, result.TotalBytes);
        }

        #region Helper Methods

        /// <summary>
        /// 运行比较测试，分析原始数据和修改后数据的分块差异
        /// </summary>
        private (double SameChunkPercentage, int FirstDifferentChunkIndex, int SameChunkCount)
            RunComparativeChunkTest(byte[] originalData, byte[] modifiedData, string testName)
        {
            _output.WriteLine($"===== 比较测试: {testName} =====");

            int avgChunkSizeKB = 1024 * 1; // 1MB 平均块大小
            int averageChunkSize = avgChunkSizeKB * 1024;

            var cdc = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: averageChunkSize,
                minSize: averageChunkSize / 4,
                maxSize: averageChunkSize * 4,
                resetInterval: averageChunkSize * 4 * 2
            );

            // 分块结果
            var originalChunks = new List<byte[]>();
            var modifiedChunks = new List<byte[]>();
            var originalHashes = new List<string>();
            var modifiedHashes = new List<string>();

            // 原始数据分块
            using (var stream = new MemoryStream(originalData))
            {
                cdc.Split(stream, (chunk, length) =>
                {
                    originalChunks.Add(chunk.Take(length).ToArray());
                    originalHashes.Add(ComputeHash(chunk, length));
                });
            }

            // 修改后数据分块
            using (var stream = new MemoryStream(modifiedData))
            {
                cdc.Split(stream, (chunk, length) =>
                {
                    modifiedChunks.Add(chunk.Take(length).ToArray());
                    modifiedHashes.Add(ComputeHash(chunk, length));
                });
            }

            // 分析差异
            int sameChunkCount = 0;
            int firstDifferentIndex = -1;

            for (int i = 0; i < Math.Min(originalHashes.Count, modifiedHashes.Count); i++)
            {
                if (originalHashes[i] == modifiedHashes[i])
                {
                    sameChunkCount++;
                }
                else if (firstDifferentIndex == -1)
                {
                    firstDifferentIndex = i;
                }
            }

            // 计算相同块的百分比
            double sameChunkPercentage = (double)sameChunkCount / Math.Max(originalHashes.Count, modifiedHashes.Count) * 100;

            // 输出差异统计
            _output.WriteLine($"原始数据块数: {originalChunks.Count}");
            _output.WriteLine($"修改后数据块数: {modifiedChunks.Count}");
            _output.WriteLine($"相同块数: {sameChunkCount}");

            return (sameChunkPercentage, firstDifferentIndex, sameChunkCount);
        }

        /// <summary>
        /// 对数据进行分块并返回详细统计信息
        /// </summary>
        private (int TotalChunks, int UniqueChunks, long TotalBytes, TimeSpan ProcessingTime, List<int> ChunkSizes)
            ChunkAndAnalyzeWithStats(byte[] data, string testName)
        {
            _output.WriteLine($"===== 分块测试: {testName} =====");

            int avgChunkSizeKB = 1024 * 1; // 1MB 平均块大小
            int averageChunkSize = avgChunkSizeKB * 1024;

            var cdc = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: averageChunkSize,
                minSize: averageChunkSize / 4,
                maxSize: averageChunkSize * 4,
                resetInterval: averageChunkSize * 4 * 2
            );

            var chunkSizes = new List<int>();
            var uniqueChunks = new HashSet<string>();
            var stopwatch = Stopwatch.StartNew();
            long totalBytes = 0;
            int chunkCount = 0;

            using (var stream = new MemoryStream(data))
            {
                cdc.Split(stream, (chunk, length) =>
                {
                    chunkCount++;
                    totalBytes += length;
                    chunkSizes.Add(length);

                    // 计算块哈希值
                    string hash = ComputeHash(chunk, length);
                    uniqueChunks.Add(hash);
                });
            }

            stopwatch.Stop();

            // 显示统计信息
            DisplayStatistics(chunkSizes, uniqueChunks.Count, stopwatch.Elapsed, totalBytes);

            return (chunkCount, uniqueChunks.Count, totalBytes, stopwatch.Elapsed, chunkSizes);
        }

        /// <summary>
        /// 计算哈希值，与原程序保持一致
        /// </summary>
        private string ComputeHash(byte[] data, int length)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(data, 0, length);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// 显示块大小分布，与原程序保持一致
        /// </summary>
        private void DisplayStatistics(List<int> chunkSizes, int uniqueChunks, TimeSpan elapsed, long totalBytes)
        {
            _output.WriteLine("\n=== 分块统计信息 ===");
            _output.WriteLine($"总块数: {chunkSizes.Count}");
            _output.WriteLine($"唯一块数: {uniqueChunks} ({uniqueChunks * 100.0 / chunkSizes.Count:F2}%)");
            _output.WriteLine($"处理速度: {(totalBytes / Math.Max(1, elapsed.TotalSeconds)).FormatSize()}/秒");
            _output.WriteLine($"处理时间: {elapsed.TotalSeconds:F2} 秒");

            if (chunkSizes.Count > 0)
            {
                _output.WriteLine("\n=== 块大小分布 ===");
                _output.WriteLine($"最小块: {chunkSizes.Min().FormatSize()}");
                _output.WriteLine($"最大块: {chunkSizes.Max().FormatSize()}");
                _output.WriteLine($"平均块: {chunkSizes.Average().FormatSize()}");
                _output.WriteLine($"中位数: {Median(chunkSizes).FormatSize()}");

                // 显示直方图
                DisplayHistogram(chunkSizes);
            }
        }

        /// <summary>
        /// 计算中位数，与原程序保持一致
        /// </summary>
        private int Median(List<int> values)
        {
            var sortedValues = values.OrderBy(v => v).ToList();
            int mid = sortedValues.Count / 2;
            if (sortedValues.Count % 2 == 0)
                return (sortedValues[mid - 1] + sortedValues[mid]) / 2;
            else
                return sortedValues[mid];
        }

        /// <summary>
        /// 显示直方图，与原程序保持一致
        /// </summary>
        private void DisplayHistogram(List<int> sizes)
        {
            // 创建块大小分布的简单直方图
            const int numBuckets = 10;
            int min = sizes.Min();
            int max = sizes.Max();
            double bucketSize = (max - min) / (double)numBuckets;

            if (bucketSize <= 0)
            {
                _output.WriteLine("所有块大小相同，无法创建直方图");
                return;
            }

            int[] histogram = new int[numBuckets];

            foreach (int size in sizes)
            {
                int bucketIndex = Math.Min(numBuckets - 1,
                                        (int)Math.Floor((size - min) / bucketSize));
                histogram[bucketIndex]++;
            }

            _output.WriteLine("\n块大小分布直方图:");
            int maxCount = histogram.Max();

            if (maxCount == 0)
            {
                _output.WriteLine("直方图为空");
                return;
            }

            for (int i = 0; i < numBuckets; i++)
            {
                int lower = (int)(min + i * bucketSize);
                int upper = (int)(min + (i + 1) * bucketSize);

                // 计算星号数量
                int stars = (int)Math.Round(histogram[i] * 50.0 / maxCount);

                _output.WriteLine($"{lower.FormatSize()}-{upper.FormatSize()}: {new string('*', stars)} ({histogram[i]})");
            }
        }

        /// <summary>
        /// 生成测试数据 - 包含一些模式以模拟真实文件
        /// </summary>
        private byte[] GenerateTestData(int size)
        {
            // 使用固定种子确保测试结果可重现
            var random = new Random(2025_05_13);
            byte[] data = new byte[size];

            // 填充随机数据
            random.NextBytes(data);

            // 添加一些重复模式
            if (size > 100000)
            {
                byte[] pattern = new byte[10000];
                random.NextBytes(pattern);

                // 在数据中插入重复模式
                for (int i = 0; i < size / 50000; i++)
                {
                    int position = random.Next(0, size - pattern.Length);
                    Buffer.BlockCopy(pattern, 0, data, position, pattern.Length);
                }

                // 添加一些零区块
                for (int i = 0; i < size / 100000; i++)
                {
                    int position = random.Next(0, size - 5000);
                    int length = random.Next(1000, 5000);
                    for (int j = 0; j < length; j++)
                    {
                        if (position + j < data.Length)
                            data[position + j] = 0;
                    }
                }
            }

            return data;
        }

        #endregion
    }
}
