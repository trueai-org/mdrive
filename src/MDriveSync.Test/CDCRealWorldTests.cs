using MDriveSync.Core.Services;
using MDriveSync.Infrastructure;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Xunit.Abstractions;

namespace MDriveSync.Test
{
    public class CDCRealWorldTests : BaseTests
    {
        private readonly ITestOutputHelper _output;

        public CDCRealWorldTests(ITestOutputHelper output)
        {
            _output = output;
        }

        /// <summary>
        /// 测试修改文件首字节对分块的影响
        /// </summary>
        [Fact]
        public void ModifyFirstByte_ShouldOnlyAffectFirstFewChunks()
        {
            // Arrange
            int testDataSize = 10 * 1024 * 1024; // 10MB
            byte[] originalData = GenerateRealisticData(testDataSize);
            byte[] modifiedData = (byte[])originalData.Clone();

            // 修改第一个字节
            modifiedData[0] = (byte)(originalData[0] ^ 0xFF);

            var chunkResults = RunChunkingTest(originalData, modifiedData);

            // Assert
            _output.WriteLine($"总块数: {chunkResults.OriginalChunks.Count}");
            _output.WriteLine($"修改影响的块数: {chunkResults.DifferentChunkCount}");

            // 验证修改仅影响前面的块，不影响后面的块
            bool foundSameChunkAfterDifference = false;
            for (int i = chunkResults.FirstDifferentChunkIndex + 1; i < Math.Min(chunkResults.OriginalChunks.Count, chunkResults.ModifiedChunks.Count); i++)
            {
                if (chunkResults.OriginalHashes[i] == chunkResults.ModifiedHashes[i])
                {
                    foundSameChunkAfterDifference = true;
                    break;
                }
            }

            Assert.True(chunkResults.FirstDifferentChunkIndex >= 0, "应至少有一个块受到修改影响");
            Assert.True(foundSameChunkAfterDifference, "修改后应该有相同的块（局部性特性）");
            Assert.True(chunkResults.DifferentChunkCount < chunkResults.OriginalChunks.Count, "不应影响所有块");
        }

        /// <summary>
        /// 测试修改文件末尾字节对分块的影响
        /// </summary>
        [Fact]
        public void ModifyLastByte_ShouldOnlyAffectLastFewChunks()
        {
            // Arrange
            int testDataSize = 10 * 1024 * 1024; // 10MB
            byte[] originalData = GenerateRealisticData(testDataSize);
            byte[] modifiedData = (byte[])originalData.Clone();

            // 修改最后一个字节
            modifiedData[modifiedData.Length - 1] = (byte)(originalData[originalData.Length - 1] ^ 0xFF);

            var chunkResults = RunChunkingTest(originalData, modifiedData);

            // Assert
            _output.WriteLine($"总块数: {chunkResults.OriginalChunks.Count}");
            _output.WriteLine($"修改影响的块数: {chunkResults.DifferentChunkCount}");

            // 验证修改只影响最后部分的块
            Assert.True(chunkResults.FirstDifferentChunkIndex > chunkResults.OriginalChunks.Count / 2,
                "修改最后一个字节应只影响后半部分的块");
            Assert.True(chunkResults.DifferentChunkCount < chunkResults.OriginalChunks.Count / 2,
                "不应影响超过一半的块");
        }

        /// <summary>
        /// 测试重复数据的分块识别
        /// </summary>
        [Fact]
        public void DuplicateContent_ShouldProduceSameChunks()
        {
            // Arrange
            int originalSize = 2 * 1024 * 1024; // 2MB 原始数据
            byte[] originalPart = GenerateRealisticData(originalSize);

            // 创建一个包含2份相同数据的文件
            byte[] duplicatedData = new byte[originalSize * 2];
            Buffer.BlockCopy(originalPart, 0, duplicatedData, 0, originalSize);
            Buffer.BlockCopy(originalPart, 0, duplicatedData, originalSize, originalSize);

            // 分块参数
            int averageChunkSize = 512 * 1024; // 512KB 平均块大小
            var cdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: averageChunkSize,
                minSize: averageChunkSize / 4,
                maxSize: averageChunkSize * 4
            );

            var chunks = new List<byte[]>();
            var hashes = new List<string>();

            // Act
            using (var stream = new MemoryStream(duplicatedData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    chunks.Add(chunk);
                    hashes.Add(ComputeHash(chunk));
                });
            }

            // 计算重复哈希的数量
            var hashCounts = new Dictionary<string, int>();
            foreach (var hash in hashes)
            {
                if (!hashCounts.ContainsKey(hash))
                    hashCounts[hash] = 0;
                hashCounts[hash]++;
            }

            int duplicateHashes = hashCounts.Count(kv => kv.Value > 1);
            int totalDuplicates = hashCounts.Sum(kv => kv.Value - 1);

            // Assert
            _output.WriteLine($"总块数: {chunks.Count}");
            _output.WriteLine($"唯一块数: {hashCounts.Count}");
            _output.WriteLine($"有重复的块种类: {duplicateHashes}");
            _output.WriteLine($"重复块总数: {totalDuplicates}");

            Assert.True(duplicateHashes > 0, "应该识别出重复块");
            Assert.True(hashCounts.Count < chunks.Count, "应该有重复块被识别");
        }

        /// <summary>
        /// 测试大规模重复内容的分块效率
        /// </summary>
        [Fact]
        public void MultipleRepeats_ShouldOptimizeStorage()
        {
            // Arrange - 创建一个具有N个重复部分的大文件
            int repeatCount = 5;
            int originalSize = 1 * 1024 * 1024; // 1MB 原始数据
            byte[] originalPart = GenerateRealisticData(originalSize);

            // 创建一个包含N份相同数据的文件
            byte[] repeatedData = new byte[originalSize * repeatCount];
            for (int i = 0; i < repeatCount; i++)
            {
                Buffer.BlockCopy(originalPart, 0, repeatedData, i * originalSize, originalSize);
            }

            // 分块参数
            int averageChunkSize = 256 * 1024; // 256KB 平均块大小
            var cdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: averageChunkSize,
                minSize: averageChunkSize / 4,
                maxSize: averageChunkSize * 4
            );

            var chunks = new List<byte[]>();
            var hashes = new List<string>();
            var stopwatch = Stopwatch.StartNew();

            // Act
            using (var stream = new MemoryStream(repeatedData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    chunks.Add(chunk);
                    hashes.Add(ComputeHash(chunk));
                });
            }

            stopwatch.Stop();

            // 计算存储效率
            var uniqueHashes = new HashSet<string>(hashes);
            double compressionRatio = (double)uniqueHashes.Count / hashes.Count;
            var theoreticalSize = uniqueHashes.Count * chunks.Average(c => c.Length);

            // Assert
            _output.WriteLine($"处理时间: {stopwatch.ElapsedMilliseconds} 毫秒");
            _output.WriteLine($"总块数: {chunks.Count}");
            _output.WriteLine($"唯一块数: {uniqueHashes.Count}");
            _output.WriteLine($"理论最小存储大小: {theoreticalSize.FormatSize()}");
            _output.WriteLine($"原始数据大小: {repeatedData.Length.FormatSize()}");
            _output.WriteLine($"存储比例: {compressionRatio:P2}");

            Assert.True(uniqueHashes.Count < chunks.Count, "应该有重复块被识别");
            Assert.True(compressionRatio < 0.5, "存储效率应至少提高50%以上");

            // 所有块的总大小应等于原始数据大小
            Assert.Equal(repeatedData.Length, chunks.Sum(c => c.Length));
        }

        /// <summary>
        /// 测试复杂模式的分块鲁棒性
        /// </summary>
        [Fact]
        public void ComplexPatterns_ChunkStability()
        {
            // Arrange - 创建一个包含复杂模式的数据块
            int baseSize = 5 * 1024 * 1024; // 5MB
            byte[] baseData = GenerateRealisticData(baseSize);

            // 创建三种修改的变体
            byte[] variant1 = (byte[])baseData.Clone();
            byte[] variant2 = (byte[])baseData.Clone();
            byte[] variant3 = (byte[])baseData.Clone();

            // 变体1：修改前25%处的几个字节
            ModifyRange(variant1, 0, baseSize / 4, 10);

            // 变体2：修改中间的几个字节
            ModifyRange(variant2, baseSize / 2 - 100, 200, 10);

            // 变体3：在3/4处插入新内容
            ModifyRange(variant3, baseSize * 3 / 4, 1000, 50);

            // 对所有变体进行分块
            var baseResult = ChunkAndAnalyze(baseData);
            var variant1Result = ChunkAndAnalyze(variant1);
            var variant2Result = ChunkAndAnalyze(variant2);
            var variant3Result = ChunkAndAnalyze(variant3);

            // 计算与基准数据相同的块比例
            double sameRatio1 = CalculateSameChunkRatio(baseResult.Hashes, variant1Result.Hashes);
            double sameRatio2 = CalculateSameChunkRatio(baseResult.Hashes, variant2Result.Hashes);
            double sameRatio3 = CalculateSameChunkRatio(baseResult.Hashes, variant3Result.Hashes);

            // Assert
            _output.WriteLine($"基准数据块数: {baseResult.Chunks.Count}");
            _output.WriteLine($"变体1相同块比例: {sameRatio1:P2}");
            _output.WriteLine($"变体2相同块比例: {sameRatio2:P2}");
            _output.WriteLine($"变体3相同块比例: {sameRatio3:P2}");

            // 验证修改对分块的影响是局部的
            Assert.True(sameRatio1 > 0.7, "前25%的修改应保留70%以上的块");
            Assert.True(sameRatio2 > 0.8, "中间区域的小修改应保留80%以上的块");
            Assert.True(sameRatio3 > 0.7, "3/4处的修改应保留70%以上的块");
        }

        /// <summary>
        /// 测试分块重置点功能
        /// </summary>
        [Fact]
        public void ResetInterval_LimitsChangeImpact()
        {
            // Arrange
            int testDataSize = 20 * 1024 * 1024; // 20MB
            byte[] originalData = GenerateRealisticData(testDataSize);
            byte[] modifiedData = (byte[])originalData.Clone();

            // 在第1MB位置修改数据
            int modifyPosition = 1 * 1024 * 1024;
            ModifyRange(modifiedData, modifyPosition, 1000, 20);

            // 创建带重置点的CDC
            int averageChunkSize = 1024 * 1024; // 1MB
            var cdcWithReset = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: averageChunkSize,
                minSize: averageChunkSize / 4,
                maxSize: averageChunkSize * 4,
                resetInterval: 8 * 1024 * 1024 // 8MB重置点
            );

            // 分块结果
            var originalChunks = new List<byte[]>();
            var modifiedChunks = new List<byte[]>();
            var originalHashes = new List<string>();
            var modifiedHashes = new List<string>();

            // 对原始数据分块
            using (var stream = new MemoryStream(originalData))
            {
                cdcWithReset.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    originalChunks.Add(chunk);
                    originalHashes.Add(ComputeHash(chunk));
                });
            }

            // 对修改后数据分块
            using (var stream = new MemoryStream(modifiedData))
            {
                cdcWithReset.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    modifiedChunks.Add(chunk);
                    modifiedHashes.Add(ComputeHash(chunk));
                });
            }

            // 计算每个位置的累计大小，找出重置点所在的块
            long[] originalCumulativeSize = new long[originalChunks.Count];
            long cumulativeSize = 0;
            for (int i = 0; i < originalChunks.Count; i++)
            {
                cumulativeSize += originalChunks[i].Length;
                originalCumulativeSize[i] = cumulativeSize;
            }

            // 查找第一个重置点（8MB）后的第一个块
            int resetChunkIndex = Array.FindIndex(originalCumulativeSize, size => size >= 8 * 1024 * 1024);

            // 计算修改前后每个区域相同块的比例
            int beforeResetSameCount = 0;
            int afterResetSameCount = 0;

            for (int i = 0; i < Math.Min(resetChunkIndex, Math.Min(originalHashes.Count, modifiedHashes.Count)); i++)
            {
                if (originalHashes[i] == modifiedHashes[i])
                    beforeResetSameCount++;
            }

            for (int i = resetChunkIndex; i < Math.Min(originalHashes.Count, modifiedHashes.Count); i++)
            {
                if (originalHashes[i] == modifiedHashes[i])
                    afterResetSameCount++;
            }

            double beforeResetSameRatio = (double)beforeResetSameCount / resetChunkIndex;
            double afterResetSameRatio = (double)afterResetSameCount / (Math.Min(originalHashes.Count, modifiedHashes.Count) - resetChunkIndex);

            // Assert
            _output.WriteLine($"总块数: {originalChunks.Count}");
            _output.WriteLine($"重置点索引: {resetChunkIndex}");
            _output.WriteLine($"重置点前相同块比例: {beforeResetSameRatio:P2}");
            _output.WriteLine($"重置点后相同块比例: {afterResetSameRatio:P2}");

            // 重置点后的块应该有高比例保持相同
            Assert.True(afterResetSameRatio > 0.9, "重置点后的块应有90%以上保持相同");
            // 重置点前由于有修改，应该有一定比例的块发生变化
            Assert.True(beforeResetSameRatio < 0.9, "重置点前的块由于修改应有变化");
        }

        #region Helper Methods

        private (List<byte[]> OriginalChunks, List<byte[]> ModifiedChunks,
                List<string> OriginalHashes, List<string> ModifiedHashes,
                int FirstDifferentChunkIndex, int DifferentChunkCount)
            RunChunkingTest(byte[] originalData, byte[] modifiedData)
        {
            // 分块参数
            int averageChunkSize = 1024 * 1024; // 1MB average
            var cdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: averageChunkSize,
                minSize: averageChunkSize / 4,
                maxSize: averageChunkSize * 4
            );

            // 分块结果
            var originalChunks = new List<byte[]>();
            var modifiedChunks = new List<byte[]>();
            var originalHashes = new List<string>();
            var modifiedHashes = new List<string>();

            // 对原始数据分块
            using (var stream = new MemoryStream(originalData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    originalChunks.Add(chunk);
                    originalHashes.Add(ComputeHash(chunk));
                });
            }

            // 对修改后数据分块
            using (var stream = new MemoryStream(modifiedData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    modifiedChunks.Add(chunk);
                    modifiedHashes.Add(ComputeHash(chunk));
                });
            }

            // 找出第一个不同的块和不同块的总数
            int firstDifferentIndex = -1;
            int differentCount = 0;

            for (int i = 0; i < Math.Min(originalHashes.Count, modifiedHashes.Count); i++)
            {
                if (originalHashes[i] != modifiedHashes[i])
                {
                    if (firstDifferentIndex == -1)
                        firstDifferentIndex = i;
                    differentCount++;
                }
            }

            // 如果长度不同，余下的块都算作不同
            differentCount += Math.Abs(originalHashes.Count - modifiedHashes.Count);

            return (originalChunks, modifiedChunks, originalHashes, modifiedHashes,
                   firstDifferentIndex, differentCount);
        }

        private (List<byte[]> Chunks, List<string> Hashes) ChunkAndAnalyze(byte[] data)
        {
            var chunks = new List<byte[]>();
            var hashes = new List<string>();

            int averageChunkSize = 512 * 1024; // 512KB
            var cdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: averageChunkSize,
                minSize: averageChunkSize / 4,
                maxSize: averageChunkSize * 4
            );

            using (var stream = new MemoryStream(data))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    chunks.Add(chunk);
                    hashes.Add(ComputeHash(chunk));
                });
            }

            return (chunks, hashes);
        }

        private double CalculateSameChunkRatio(List<string> baseHashes, List<string> compareHashes)
        {
            // 创建哈希查找表
            var baseHashSet = new HashSet<string>(baseHashes);
            int sameCount = compareHashes.Count(h => baseHashSet.Contains(h));

            return (double)sameCount / compareHashes.Count;
        }

        private void ModifyRange(byte[] data, int startPos, int length, int step)
        {
            for (int i = 0; i < length; i += step)
            {
                if (startPos + i < data.Length)
                {
                    data[startPos + i] = (byte)(data[startPos + i] ^ 0xFF);
                }
            }
        }

        private byte[] GenerateRealisticData(int size)
        {
            // 生成一些模拟真实数据的字节流，包含重复模式和结构
            var random = new Random(42); // 使用固定种子以确保测试的可重复性
            var data = new byte[size];

            // 分段填充数据
            int position = 0;

            // 创建一些基本模式
            byte[] pattern1 = Encoding.UTF8.GetBytes("这是一个模拟文件内容的测试数据，包含一些重复的模式和结构...");
            byte[] pattern2 = new byte[1024]; // 1KB的随机数据
            random.NextBytes(pattern2);

            while (position < size)
            {
                // 决定写入什么类型的数据
                int choice = random.Next(5);
                int blockSize = Math.Min(random.Next(4096, 65536), size - position);

                switch (choice)
                {
                    case 0:
                        // 重复模式1
                        for (int i = 0; i < blockSize; i++)
                        {
                            data[position + i] = pattern1[i % pattern1.Length];
                        }
                        break;
                    case 1:
                        // 重复模式2
                        for (int i = 0; i < blockSize; i++)
                        {
                            data[position + i] = pattern2[i % pattern2.Length];
                        }
                        break;
                    case 2:
                        // 全零段
                        for (int i = 0; i < blockSize; i++)
                        {
                            data[position + i] = 0;
                        }
                        break;
                    case 3:
                        // 递增数据
                        for (int i = 0; i < blockSize; i++)
                        {
                            data[position + i] = (byte)(i % 256);
                        }
                        break;
                    case 4:
                        // 随机数据
                        random.NextBytes(new Span<byte>(data, position, blockSize));
                        break;
                }

                position += blockSize;
            }

            return data;
        }

        private string ComputeHash(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }

        #endregion
    }
}
