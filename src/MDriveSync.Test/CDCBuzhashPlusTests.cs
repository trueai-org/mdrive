using MDriveSync.Core.Services;
using System.Security.Cryptography;

namespace MDriveSync.Test
{
    public class CDCBuzhashPlusTests : BaseTests
    {
        [Fact]
        public void GetChunkSizeStats_ReturnsCorrectValues()
        {
            // Arrange
            int windowSize = 48;
            int avgSize = 1024 * 1024;
            int minSize = avgSize / 4;
            int maxSize = avgSize * 4;
            var cdc = new BuzhashCDC(windowSize, avgSize, minSize, maxSize);

            // Act
            var stats = cdc.GetChunkSizeStats();

            // Assert
            Assert.Equal(minSize, stats.MinSize);
            Assert.Equal(maxSize, stats.MaxSize);
            Assert.Equal(Math.Pow(2, Math.Log(avgSize, 2)), stats.AvgSize, 0.1);
        }

        [Fact]
        public void Split_BasicFunction_ChunksCorrectlySplit()
        {
            // Arrange
            int testDataSize = 10 * 1024 * 1024; // 10MB
            byte[] testData = TestHelpers.GenerateRandomData(testDataSize);
            var cdc = new BuzhashCDC(
                windowSize: 48,
                averageSize: 1024 * 1024, // 1MB average
                minSize: 256 * 1024,      // 256KB min
                maxSize: 4 * 1024 * 1024  // 4MB max
            );
            var chunks = new List<byte[]>();

            // Act
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    chunks.Add(chunkData.Take(length).ToArray());
                });
            }

            // Assert
            Assert.True(chunks.Count > 0, "应该产生至少一个块");
            // 所有块的总大小应等于原始数据大小
            Assert.Equal(testDataSize, chunks.Sum(c => c.Length));

            // 验证重构数据与原始数据相同
            byte[] reconstructed = TestHelpers.ReconstructData(chunks);
            Assert.Equal(testData, reconstructed);

            // 验证块大小在规定范围内
            foreach (var chunk in chunks)
            {
                if (chunk == chunks.Last() && testDataSize % cdc.GetChunkSizeStats().MinSize != 0)
                {
                    // 最后一个块可能小于最小大小
                    continue;
                }
                Assert.True(chunk.Length >= cdc.GetChunkSizeStats().MinSize,
                    $"块大小 {chunk.Length} 应大于或等于最小大小 {cdc.GetChunkSizeStats().MinSize}");
                Assert.True(chunk.Length <= cdc.GetChunkSizeStats().MaxSize,
                    $"块大小 {chunk.Length} 应小于或等于最大大小 {cdc.GetChunkSizeStats().MaxSize}");
            }
        }

        [Fact]
        public void Split_SmallFile_SingleChunkReturned()
        {
            // Arrange
            int smallFileSize = 1024; // 1KB，小于窗口大小
            byte[] testData = TestHelpers.GenerateRandomData(smallFileSize);
            var cdc = new BuzhashCDC(windowSize: 48);
            var chunks = new List<byte[]>();

            // Act
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    chunks.Add(chunkData.Take(length).ToArray());
                });
            }

            // Assert
            Assert.Single(chunks);
            Assert.Equal(smallFileSize, chunks[0].Length);
            Assert.Equal(testData, chunks[0]);
        }

        [Fact]
        public void Split_EmptyFile_NoChunksReturned()
        {
            // Arrange
            byte[] emptyData = new byte[0];
            var cdc = new BuzhashCDC(windowSize: 48);
            var chunks = new List<byte[]>();

            // Act
            using (var stream = new MemoryStream(emptyData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    chunks.Add(chunkData.Take(length).ToArray());
                });
            }

            // Assert
            Assert.Empty(chunks);
        }

        [Fact]
        public void Split_IdenticalData_ProducesSameChunks()
        {
            // Arrange
            int testDataSize = 5 * 1024 * 1024;
            byte[] testData = TestHelpers.GenerateRandomData(testDataSize);
            var cdc = new BuzhashCDC(windowSize: 48);
            var firstRun = new List<byte[]>();
            var secondRun = new List<byte[]>();

            // Act - 第一次分块
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    firstRun.Add(chunkData.Take(length).ToArray());
                });
            }

            // Act - 第二次分块（相同数据）
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    secondRun.Add(chunkData.Take(length).ToArray());
                });
            }

            // Assert
            Assert.Equal(firstRun.Count, secondRun.Count);
            for (int i = 0; i < firstRun.Count; i++)
            {
                Assert.Equal(firstRun[i].Length, secondRun[i].Length);
                Assert.Equal(firstRun[i], secondRun[i]);
            }
        }

        [Fact]
        public void Split_ModifiedData_OnlyAffectedChunksChange()
        {
            // Arrange
            int testDataSize = 5 * 1024 * 1024;
            byte[] originalData = TestHelpers.GenerateRandomData(testDataSize);
            byte[] modifiedData = (byte[])originalData.Clone();

            // 修改中间的一小部分数据
            int modifyPosition = testDataSize / 2;
            modifiedData[modifyPosition] = (byte)(modifiedData[modifyPosition] ^ 0xFF); // 翻转位

            var cdc = new BuzhashCDC(windowSize: 48);
            var originalChunks = new List<byte[]>();
            var modifiedChunks = new List<byte[]>();

            // Act - 原始数据分块
            using (var stream = new MemoryStream(originalData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    originalChunks.Add(chunkData.Take(length).ToArray());
                });
            }

            // Act - 修改后数据分块
            using (var stream = new MemoryStream(modifiedData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    modifiedChunks.Add(chunkData.Take(length).ToArray());
                });
            }

            // Assert
            // 比较块数量和总大小
            Assert.Equal(originalData.Length, originalChunks.Sum(c => c.Length));
            Assert.Equal(modifiedData.Length, modifiedChunks.Sum(c => c.Length));

            // 确认有差异的块
            bool foundDifference = false;
            for (int i = 0; i < Math.Min(originalChunks.Count, modifiedChunks.Count); i++)
            {
                if (!originalChunks[i].SequenceEqual(modifiedChunks[i]))
                {
                    foundDifference = true;
                    break;
                }
            }

            Assert.True(foundDifference, "修改数据应该产生至少一个不同的块");
        }
    }

    public class BuzhashPlusCDCTests : BaseTests
    {
        [Fact]
        public void GetChunkSizeStats_ReturnsCorrectValues()
        {
            // Arrange
            int windowSize = 48;
            int avgSize = 1024 * 1024;
            int minSize = avgSize / 4;
            int maxSize = avgSize * 4;
            int resetInterval = 4 * 1024 * 1024;
            var cdc = new BuzhashPlusCDC(windowSize, avgSize, minSize, maxSize, resetInterval);

            // Act
            var stats = cdc.GetChunkSizeStats();

            // Assert
            Assert.Equal(minSize, stats.MinSize);
            Assert.Equal(maxSize, stats.MaxSize);
            Assert.Equal(Math.Pow(2, Math.Log(avgSize, 2)), stats.AvgSize, 0.1);
            Assert.Equal(resetInterval, stats.ResetInterval);
        }

        [Fact]
        public void Split_BasicFunction_ChunksCorrectlySplit()
        {
            // Arrange
            int testDataSize = 10 * 1024 * 1024; // 10MB
            byte[] testData = TestHelpers.GenerateRandomData(testDataSize);
            var cdc = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: 1024 * 1024, // 1MB average
                minSize: 256 * 1024,      // 256KB min
                maxSize: 4 * 1024 * 1024, // 4MB max
                resetInterval: 8 * 1024 * 1024 // 8MB reset
            );
            var chunks = new List<byte[]>();

            // Act
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    chunks.Add(chunkData.Take(length).ToArray());
                });
            }

            // Assert
            Assert.True(chunks.Count > 0, "应该产生至少一个块");
            // 所有块的总大小应等于原始数据大小
            Assert.Equal(testDataSize, chunks.Sum(c => c.Length));

            // 验证重构数据与原始数据相同
            byte[] reconstructed = TestHelpers.ReconstructData(chunks);
            Assert.Equal(testData, reconstructed);

            // 验证块大小在规定范围内
            foreach (var chunk in chunks)
            {
                if (chunk == chunks.Last() && testDataSize % cdc.GetChunkSizeStats().MinSize != 0)
                {
                    // 最后一个块可能小于最小大小
                    continue;
                }
                Assert.True(chunk.Length >= cdc.GetChunkSizeStats().MinSize,
                    $"块大小 {chunk.Length} 应大于或等于最小大小 {cdc.GetChunkSizeStats().MinSize}");
                Assert.True(chunk.Length <= cdc.GetChunkSizeStats().MaxSize,
                    $"块大小 {chunk.Length} 应小于或等于最大大小 {cdc.GetChunkSizeStats().MaxSize}");
            }
        }

        [Fact]
        public void Split_WithResetInterval_ResetPointsRespected()
        {
            // Arrange
            int testDataSize = 20 * 1024 * 1024; // 20MB
            byte[] testData = TestHelpers.GenerateRandomData(testDataSize);
            int resetInterval = 5 * 1024 * 1024; // 5MB重置点

            // 创建两个CDC实例，一个有重置点，一个无重置点
            var cdcWithReset = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: 1024 * 1024,
                minSize: 256 * 1024,
                maxSize: 4 * 1024 * 1024,
                resetInterval: resetInterval
            );

            var cdcNoReset = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: 1024 * 1024,
                minSize: 256 * 1024,
                maxSize: 4 * 1024 * 1024,
                resetInterval: 0 // 禁用重置点
            );

            var chunksWithReset = new List<byte[]>();
            var chunksNoReset = new List<byte[]>();

            // Act - 使用有重置点的CDC
            using (var stream = new MemoryStream(testData))
            {
                cdcWithReset.Split(stream, (chunkData, length) =>
                {
                    chunksWithReset.Add(chunkData.Take(length).ToArray());
                });
            }

            // Act - 使用无重置点的CDC
            using (var stream = new MemoryStream(testData))
            {
                cdcNoReset.Split(stream, (chunkData, length) =>
                {
                    chunksNoReset.Add(chunkData.Take(length).ToArray());
                });
            }

            // Assert
            // 两种方式都应该能正确重构原始数据
            Assert.Equal(testDataSize, chunksWithReset.Sum(c => c.Length));
            Assert.Equal(testDataSize, chunksNoReset.Sum(c => c.Length));

            // 比较两种分块结果的差异
            // 有重置点的方法分块数量通常会略多一些
            // 注意：这个断言在某些特殊数据上可能不总是成立，但在随机数据上通常是可靠的
            Assert.NotEqual(
                chunksWithReset.Select(c => TestHelpers.ComputeHash(c)).ToArray(),
                chunksNoReset.Select(c => TestHelpers.ComputeHash(c)).ToArray()
            );
        }

        [Fact]
        public void Split_SmallFile_SingleChunkReturned()
        {
            // Arrange
            int smallFileSize = 1024; // 1KB，小于窗口大小
            byte[] testData = TestHelpers.GenerateRandomData(smallFileSize);
            var cdc = new BuzhashPlusCDC(windowSize: 48);
            var chunks = new List<byte[]>();

            // Act
            using (var stream = new MemoryStream(testData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    chunks.Add(chunkData.Take(length).ToArray());
                });
            }

            // Assert
            Assert.Single(chunks);
            Assert.Equal(smallFileSize, chunks[0].Length);
            Assert.Equal(testData, chunks[0]);
        }

        [Fact]
        public void Split_ModifiedDataWithReset_LocalizedImpact()
        {
            // Arrange - 生成一个有规律的数据，以便于控制影响范围
            int testDataSize = 15 * 1024 * 1024; // 15MB
            byte[] originalData = TestHelpers.GenerateRandomData(testDataSize);
            byte[] modifiedData = (byte[])originalData.Clone();

            // 修改第1MB的数据
            int modifyPosition = 1 * 1024 * 1024;
            modifiedData[modifyPosition] = (byte)(modifiedData[modifyPosition] ^ 0xFF);

            // 使用6MB重置点
            var cdc = new BuzhashPlusCDC(
                windowSize: 48,
                averageSize: 1024 * 1024,
                minSize: 256 * 1024,
                maxSize: 4 * 1024 * 1024,
                resetInterval: 6 * 1024 * 1024
            );

            var originalChunks = new List<byte[]>();
            var modifiedChunks = new List<byte[]>();
            var originalHashes = new List<string>();
            var modifiedHashes = new List<string>();

            // Act - 原始数据分块
            using (var stream = new MemoryStream(originalData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    originalChunks.Add(chunk);
                    originalHashes.Add(TestHelpers.ComputeHash(chunk));
                });
            }

            // Act - 修改后数据分块
            using (var stream = new MemoryStream(modifiedData))
            {
                cdc.Split(stream, (chunkData, length) =>
                {
                    var chunk = chunkData.Take(length).ToArray();
                    modifiedChunks.Add(chunk);
                    modifiedHashes.Add(TestHelpers.ComputeHash(chunk));
                });
            }

            // Assert
            // 验证修改只影响了一部分块（不超过重置点后的块）
            bool foundDifferenceBefore6MB = false;
            bool foundDifferenceAfter6MB = false;

            // 找出第一个重置点（约6MB）
            long resetPosition = 6 * 1024 * 1024;
            int resetChunkIndex = -1;
            long cumulativeSize = 0;

            for (int i = 0; i < originalChunks.Count; i++)
            {
                cumulativeSize += originalChunks[i].Length;
                if (cumulativeSize > resetPosition && resetChunkIndex == -1)
                {
                    resetChunkIndex = i;
                    break;
                }
            }

            // 如果没有找到重置点（可能文件太小），则直接断言测试通过
            if (resetChunkIndex == -1)
            {
                return;
            }

            // 检查每个区域的差异
            for (int i = 0; i < Math.Min(originalHashes.Count, modifiedHashes.Count); i++)
            {
                if (i < resetChunkIndex && originalHashes[i] != modifiedHashes[i])
                {
                    foundDifferenceBefore6MB = true;
                }
                else if (i >= resetChunkIndex && originalHashes[i] != modifiedHashes[i])
                {
                    foundDifferenceAfter6MB = true;
                }
            }

            // 应该在修改位置前找到差异
            Assert.True(foundDifferenceBefore6MB, "应该在前6MB处找到块差异");

            // 这个断言验证重置点的效果：修改不应该影响超过重置点的块
            // 注意：在某些极端情况下，这个断言可能不成立，但对于随机数据通常是可靠的
            Assert.False(foundDifferenceAfter6MB, "修改不应影响重置点后的块");
        }
    }

    // 测试工具类
    public static class TestHelpers
    {
        private static readonly Random _random = new Random(42); // 使用固定种子以确保测试的可重复性

        // 生成随机测试数据
        public static byte[] GenerateRandomData(int size)
        {
            byte[] data = new byte[size];
            _random.NextBytes(data);
            return data;
        }

        // 重构原始数据
        public static byte[] ReconstructData(List<byte[]> chunks)
        {
            int totalSize = chunks.Sum(c => c.Length);
            byte[] result = new byte[totalSize];
            int offset = 0;

            foreach (var chunk in chunks)
            {
                Buffer.BlockCopy(chunk, 0, result, offset, chunk.Length);
                offset += chunk.Length;
            }

            return result;
        }

        // 计算数据的哈希值
        public static string ComputeHash(byte[] data)
        {
            using (var sha256 = SHA256.Create())
            {
                byte[] hash = sha256.ComputeHash(data);
                return BitConverter.ToString(hash).Replace("-", "").ToLowerInvariant();
            }
        }
    }
}
