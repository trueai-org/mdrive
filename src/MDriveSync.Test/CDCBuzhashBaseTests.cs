using MDriveSync.Core.Services;
using System.Diagnostics;
using System.Text;
using Xunit.Abstractions;

namespace MDriveSync.Test
{
    /// <summary>
    /// 内容定义分块算法的单元测试
    /// 测试 BuzhashCDC 的分块行为和性能特性
    /// </summary>
    public class CDCBuzhashBaseTests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public CDCBuzhashBaseTests(ITestOutputHelper output)
        {
            _output = output;
        }

        [Fact]
        public void TestBasicChunking()
        {
            // 准备测试数据
            byte[] testData = GenerateTestData(5 * 1024 * 1024); // 5MB 测试数据

            // 创建 CDC 实例（使用较小的块大小以便测试）
            var cdc = new BuzhashCDC(
                windowSize: 32,
                averageSize: 64 * 1024,   // 64KB 平均大小
                minSize: 16 * 1024,       // 16KB 最小大小
                maxSize: 256 * 1024       // 256KB 最大大小
            );

            // 收集分块结果
            var chunks = new List<byte[]>();

            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunk, size) =>
                {
                    chunks.Add(chunk.Take(size).ToArray());
                });
            }

            // 验证基本行为
            Assert.True(chunks.Count > 0, "应该至少产生一个块");
            // 所有块的总大小应等于原始数据大小
            Assert.Equal(testData.Length, chunks.Sum(c => c.Length));

            // 验证块大小限制
            var stats = cdc.GetChunkSizeStats();
            foreach (var chunk in chunks)
            {
                Assert.True(chunk.Length >= stats.MinSize || chunk == chunks.Last(),
                    $"除最后一块外，所有块大小应不小于最小块大小 ({chunk.Length} < {stats.MinSize})");
                Assert.True(chunk.Length <= stats.MaxSize,
                    $"所有块大小应不大于最大块大小 ({chunk.Length} > {stats.MaxSize})");
            }

            // 输出分块统计信息
            _output.WriteLine($"总块数: {chunks.Count}");
            _output.WriteLine($"平均块大小: {chunks.Average(c => c.Length):F0} 字节");
            _output.WriteLine($"最小块大小: {chunks.Min(c => c.Length):F0} 字节");
            _output.WriteLine($"最大块大小: {chunks.Max(c => c.Length):F0} 字节");
        }

        [Fact]
        public void TestChunkingDeterminism()
        {
            // 准备测试数据
            byte[] testData = GenerateTestData(2 * 1024 * 1024); // 2MB 测试数据

            // 创建 CDC 实例
            var cdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: 128 * 1024,  // 128KB 平均大小
                minSize: 32 * 1024,       // 32KB 最小大小
                maxSize: 512 * 1024       // 512KB 最大大小
            );

            // 第一次分块
            var firstRun = new List<byte[]>();
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunk, size) =>
                {
                    firstRun.Add(chunk.Take(size).ToArray());
                });
            }

            // 第二次分块（相同数据）
            var secondRun = new List<byte[]>();
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunk, size) =>
                {
                    secondRun.Add(chunk.Take(size).ToArray());
                });
            }

            // 验证确定性（相同输入应产生相同的分块）
            // 相同数据的多次分块应产生相同数量的块
            Assert.Equal(firstRun.Count, secondRun.Count);

            for (int i = 0; i < firstRun.Count; i++)
            {
                // $"第 {i + 1} 个块的大小应相同"
                Assert.Equal(firstRun[i].Length, secondRun[i].Length);
                Assert.True(firstRun[i].SequenceEqual(secondRun[i]), $"第 {i + 1} 个块的内容应相同");
            }
        }

        [Fact]
        public void TestChunkingWithPrependedData()
        {
            // 准备原始测试数据
            byte[] originalData = GenerateTestData(1024 * 1024); // 1MB

            // 准备修改后的数据（前面添加100字节）
            byte[] prependedData = new byte[originalData.Length + 100];
            Array.Fill<byte>(prependedData, 0xAA, 0, 100); // 填充前100字节
            Buffer.BlockCopy(originalData, 0, prependedData, 100, originalData.Length);

            // 创建 CDC 实例
            var cdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: 128 * 1024
            );

            // 对原始数据分块
            var originalChunks = new List<byte[]>();
            using (var stream = new MemoryStream(originalData))
            {
                cdc.Split(stream, (chunk, size) =>
                {
                    originalChunks.Add(chunk.Take(size).ToArray());
                });
            }

            // 对修改后数据分块
            var modifiedChunks = new List<byte[]>();
            using (var stream = new MemoryStream(prependedData))
            {
                cdc.Split(stream, (chunk, size) =>
                {
                    modifiedChunks.Add(chunk.Take(size).ToArray());
                });
            }

            // 输出块数信息
            _output.WriteLine($"原始数据块数: {originalChunks.Count}");
            _output.WriteLine($"修改后数据块数: {modifiedChunks.Count}");

            // 验证内容定义分块的本地化特性（后续块应部分保持一致）
            int matchedChunks = 0;
            for (int i = 0; i < Math.Min(originalChunks.Count, modifiedChunks.Count) - 1; i++)
            {
                // 从第二个块开始尝试寻找匹配
                for (int j = 1; j < modifiedChunks.Count; j++)
                {
                    if (originalChunks[i].SequenceEqual(modifiedChunks[j]))
                    {
                        matchedChunks++;
                        break;
                    }
                }
            }

            _output.WriteLine($"匹配的块数: {matchedChunks}");

            // 应该有一些块能够匹配上（具体数量取决于数据特性和分块参数）
            Assert.True(matchedChunks > 0, "修改后应该有一些块与原始数据的块匹配");
        }

        [Fact]
        public void TestPerformance()
        {
            // 准备较大的测试数据
            byte[] testData = GenerateTestData(20 * 1024 * 1024); // 20MB

            // 创建 CDC 实例
            var cdc = new Core.Services.BuzhashCDC(
                windowSize: 48,
                averageSize: 1024 * 1024  // 1MB 平均大小
            );

            // 测量性能
            var stopwatch = new Stopwatch();
            var chunks = new List<byte[]>();

            stopwatch.Start();
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunk, size) =>
                {
                    chunks.Add(chunk.Take(size).ToArray());
                });
            }
            stopwatch.Stop();

            double mbPerSecond = testData.Length / (1024.0 * 1024.0) / (stopwatch.ElapsedMilliseconds / 1000.0);

            _output.WriteLine($"分块速度: {mbPerSecond:F2} MB/s");
            _output.WriteLine($"总块数: {chunks.Count}");
            _output.WriteLine($"平均块大小: {chunks.Average(c => c.Length):F0} 字节");

            // 没有严格的性能要求，但应该在合理范围内
            Assert.True(mbPerSecond > 10, "分块速度应该至少达到 10MB/s");
        }

        [Fact]
        public void TestCompareWithBuzhashPlusCDC()
        {
            // 准备测试数据
            byte[] testData = GenerateTestData(5 * 1024 * 1024); // 5MB

            // 常规 Buzhash CDC
            var standardCdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: 256 * 1024  // 256KB 平均大小
            );

            // Buzhash Plus CDC (带重置点)
            var plusCdc = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: 256 * 1024,  // 256KB 平均大小
                resetInterval: 2 * 1024 * 1024  // 2MB 重置点
            );

            // 收集常规 CDC 的分块结果
            var standardChunks = new List<byte[]>();
            using (var stream = new MemoryStream(testData))
            {
                standardCdc.Split(stream, (chunk, size) =>
                {
                    standardChunks.Add(chunk.Take(size).ToArray());
                });
            }

            // 收集 Plus CDC 的分块结果
            var plusChunks = new List<byte[]>();
            using (var stream = new MemoryStream(testData))
            {
                plusCdc.Split(stream, (chunk, size) =>
                {
                    plusChunks.Add(chunk.Take(size).ToArray());
                });
            }

            _output.WriteLine($"BuzhashCDC 块数: {standardChunks.Count}");
            _output.WriteLine($"BuzhashPlusCDC 块数: {plusChunks.Count}");
            _output.WriteLine($"BuzhashCDC 平均块大小: {standardChunks.Average(c => c.Length):F0} 字节");
            _output.WriteLine($"BuzhashPlusCDC 平均块大小: {plusChunks.Average(c => c.Length):F0} 字节");

            // 不需要块完全匹配，但应该总大小相同并符合各自的分块特性
            Assert.Equal(testData.Length, standardChunks.Sum(c => c.Length));
            Assert.Equal(testData.Length, plusChunks.Sum(c => c.Length));
        }

        [Fact]
        public void TestCDCWithModifiedData()
        {
            // 1. 原始数据
            byte[] originalData = GenerateTestData(4 * 1024 * 1024); // 4MB 随机数据

            // 2. 创建修改后的数据副本（中间修改一小部分）
            byte[] modifiedData = new byte[originalData.Length];
            Buffer.BlockCopy(originalData, 0, modifiedData, 0, originalData.Length);

            // 在 2MB 处修改 1KB 数据
            var random = new Random(42);
            random.NextBytes(modifiedData.AsSpan(2 * 1024 * 1024, 1024));

            // 3. 初始化分块器
            var cdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: 256 * 1024,  // 256KB 平均块
                minSize: 64 * 1024,       // 64KB 最小块
                maxSize: 1024 * 1024      // 1MB 最大块
            );

            // 4. 分别对原始数据和修改后的数据进行分块
            var originalChunks = ChunkData(cdc, originalData);
            var modifiedChunks = ChunkData(cdc, modifiedData);

            // 5. 找出相同的块
            int originalMatches = 0;
            int modifiedMatches = 0;

            // 记录已匹配的块，避免重复计数
            HashSet<int> matchedOriginal = new HashSet<int>();
            HashSet<int> matchedModified = new HashSet<int>();

            // 比较所有块
            for (int i = 0; i < originalChunks.Count; i++)
            {
                for (int j = 0; j < modifiedChunks.Count; j++)
                {
                    if (matchedModified.Contains(j))
                        continue;

                    if (originalChunks[i].SequenceEqual(modifiedChunks[j]))
                    {
                        originalMatches++;
                        matchedOriginal.Add(i);
                        matchedModified.Add(j);
                        break;
                    }
                }
            }

            // 6. 计算匹配率
            double originalMatchRate = (double)originalMatches / originalChunks.Count;

            _output.WriteLine($"原始块数: {originalChunks.Count}");
            _output.WriteLine($"修改后块数: {modifiedChunks.Count}");
            _output.WriteLine($"匹配的块数: {originalMatches}");
            _output.WriteLine($"匹配率: {originalMatchRate:P2}");

            // 由于只修改了小部分数据，应该有较高的块匹配率
            Assert.True(originalMatchRate > 0.7, "修改小部分数据后，块匹配率应高于70%");
        }

        // 辅助方法：对数据进行分块，并返回块列表
        private List<byte[]> ChunkData(Core.Services.BuzhashCDC cdc, byte[] data)
        {
            var chunks = new List<byte[]>();
            using (var stream = new MemoryStream(data))
            {
                cdc.Split(stream, (chunk, size) =>
                {
                    chunks.Add(chunk.Take(size).ToArray());
                });
            }
            return chunks;
        }

        // 辅助方法：生成测试数据（随机内容，但包含一些重复模式）
        private byte[] GenerateTestData(int size)
        {
            var data = new byte[size];
            var random = new Random(42); // 固定种子以确保结果可重现

            // 填充大部分随机数据
            random.NextBytes(data);

            // 每 128KB 插入一些有规律的数据，使其更类似实际文件
            byte[] pattern = Encoding.ASCII.GetBytes("This is a repeating pattern to simulate real file content. ");
            for (int i = 0; i < size; i += 128 * 1024)
            {
                int copyLength = Math.Min(pattern.Length, size - i);
                Buffer.BlockCopy(pattern, 0, data, i, copyLength);
            }

            return data;
        }
    }
}
