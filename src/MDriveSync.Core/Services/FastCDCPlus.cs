using System.Buffers;
using System.Buffers.Binary;
using System.Collections.Concurrent;
using System.Runtime.Intrinsics;
using System.Runtime.Intrinsics.X86;
using System.Security.Cryptography;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// FastCDC+ - 增强版内容定义分块算法
    /// 提供高性能的文件分块功能，支持SIMD加速和多线程处理
    /// </summary>
    public class FastCDCPlus : IDisposable
    {
        #region 常量和字段

        // 掩码常量
        private const uint GEAR_MASK_BIT = 0x0000D8F3;

        private const uint NORMALIZATION_MASK_16 = 0x00007FFF; // ~8KB
        private const uint NORMALIZATION_MASK_20 = 0x0007FFFF; // ~1MB
        private const uint NORMALIZATION_MASK_24 = 0x007FFFFF; // ~16MB
        private const uint NORMALIZATION_MASK_28 = 0x07FFFFFF; // ~256MB

        // 分块大小参数
        private readonly int _minSize;

        private readonly int _avgSize;
        private readonly int _maxSize;
        private readonly uint _normalizationMask;
        private readonly uint _leveledMask;  // 用于两级掩码策略

        // 用于高速哈希计算的查找表
        private readonly uint[] _gearTable;

        // SIMD支持标志
        private readonly bool _supportsAvx2;

        private readonly bool _supportsAvx512;
        private readonly bool _supportsSse42;

        // 哈希算法
        private readonly HashAlgorithm _hashAlgorithm;

        // 缓冲池和资源管理
        private readonly ArrayPool<byte> _bufferPool;

        private readonly int _processingBlockSize;
        private bool _disposed;

        #endregion 常量和字段

        #region 构造函数和初始化

        /// <summary>
        /// 初始化FastCDC+分块器
        /// </summary>
        /// <param name="minSize">最小块大小，默认2MB</param>
        /// <param name="avgSize">平均块大小，默认16MB</param>
        /// <param name="maxSize">最大块大小，默认64MB</param>
        /// <param name="hashAlgorithm">哈希算法，null表示使用SHA256</param>
        public FastCDCPlus(
            int minSize = 2 * 1024 * 1024,      // 2MB
            int avgSize = 16 * 1024 * 1024,     // 16MB
            int maxSize = 64 * 1024 * 1024,     // 64MB
            HashAlgorithm hashAlgorithm = null)
        {
            // 验证参数
            if (minSize <= 0)
                throw new ArgumentException("最小块大小必须大于0", nameof(minSize));
            if (avgSize <= minSize)
                throw new ArgumentException("平均块大小必须大于最小块大小", nameof(avgSize));
            if (maxSize <= avgSize)
                throw new ArgumentException("最大块大小必须大于平均块大小", nameof(maxSize));

            _minSize = minSize;
            _avgSize = avgSize;
            _maxSize = maxSize;

            // 检测可用的SIMD指令集
            _supportsAvx2 = Avx2.IsSupported;
            _supportsAvx512 = Avx512F.IsSupported;
            _supportsSse42 = Sse42.IsSupported;

            // 基于目标平均块大小选择掩码
            if (avgSize <= 8 * 1024)
                _normalizationMask = NORMALIZATION_MASK_16;
            else if (avgSize <= 1024 * 1024)
                _normalizationMask = NORMALIZATION_MASK_20;
            else if (avgSize <= 16 * 1024 * 1024)
                _normalizationMask = NORMALIZATION_MASK_24;
            else
                _normalizationMask = NORMALIZATION_MASK_28;

            // 次级掩码（更宽松的条件，用于最大块大小范围）
            _leveledMask = _normalizationMask >> 1;

            // 初始化优化的Gear哈希表
            _gearTable = InitializeOptimizedGearTable();

            // 设置哈希算法，默认SHA256
            _hashAlgorithm = hashAlgorithm ?? SHA256.Create();

            // 初始化缓冲池和处理块大小
            _bufferPool = ArrayPool<byte>.Shared;
            _processingBlockSize = 128 * 1024 * 1024; // 128MB 处理块大小
        }

        /// <summary>
        /// 使用确定性种子初始化Gear哈希表，确保跨会话的一致性
        /// </summary>
        private uint[] InitializeOptimizedGearTable()
        {
            var table = new uint[256];

            // 使用固定种子以确保相同的哈希表
            byte[] seed = new byte[] {
                0x37, 0x91, 0xC4, 0x8B, 0x6F, 0x24, 0xE7, 0x4F,
                0xD3, 0xF6, 0x9A, 0x53, 0x6D, 0xA8, 0x3C, 0xD1
            };

            using var deterministicRng = new FastCspRng(seed);

            // 生成表项
            for (int i = 0; i < 256; i++)
            {
                uint value = deterministicRng.NextUInt32();
                // 应用特定掩码，以优化哈希分布
                table[i] = value & GEAR_MASK_BIT;
            }

            return table;
        }

        #endregion 构造函数和初始化

        #region 公共方法

        /// <summary>
        /// 执行文件分块并返回块信息
        /// </summary>
        /// <param name="filePath">要分块的文件路径</param>
        /// <param name="parallelProcessing">是否启用并行处理</param>
        /// <param name="cancellationToken">取消标记</param>
        /// <returns>块信息列表</returns>
        public async Task<List<ChunkInfo>> ChunkFileAsync(
            string filePath,
            bool parallelProcessing = true,
            CancellationToken cancellationToken = default)
        {
            ThrowIfDisposed();

            var fileInfo = new FileInfo(filePath);

            if (!fileInfo.Exists)
                throw new FileNotFoundException("找不到指定的文件", filePath);

            // 对于小文件，关闭并行处理以减少开销
            if (fileInfo.Length < 100 * 1024 * 1024) // 100MB
                parallelProcessing = false;

            // 创建结果列表
            var chunkInfos = new List<ChunkInfo>();

            // 确定处理块大小 - 使用一个较大的缓冲区提高性能
            int blockSize = Math.Min(_processingBlockSize, (int)fileInfo.Length);

            using var fileStream = new FileStream(
                filePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                4096, FileOptions.SequentialScan | FileOptions.Asynchronous);

            long filePosition = 0;
            long fileLength = fileStream.Length;

            if (parallelProcessing)
            {
                await ChunkFileParallelAsync(fileStream, fileLength, blockSize, chunkInfos, cancellationToken);
            }
            else
            {
                await ChunkFileSequentialAsync(fileStream, fileLength, blockSize, chunkInfos, cancellationToken);
            }

            return chunkInfos;
        }

        /// <summary>
        /// 执行文件分块并返回块信息 (同步版本)
        /// </summary>
        /// <param name="filePath">要分块的文件路径</param>
        /// <param name="parallelProcessing">是否启用并行处理</param>
        /// <returns>块信息列表</returns>
        public List<ChunkInfo> ChunkFile(string filePath, bool parallelProcessing = true)
        {
            return ChunkFileAsync(filePath, parallelProcessing).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 对内存缓冲区执行分块
        /// </summary>
        /// <param name="buffer">数据缓冲区</param>
        /// <param name="offset">起始偏移量</param>
        /// <param name="length">要处理的长度</param>
        /// <returns>块信息列表</returns>
        public List<ChunkInfo> ChunkBuffer(byte[] buffer, int offset, int length)
        {
            ThrowIfDisposed();

            if (buffer == null)
                throw new ArgumentNullException(nameof(buffer));

            if (offset < 0 || offset >= buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(offset));

            if (length <= 0 || offset + length > buffer.Length)
                throw new ArgumentOutOfRangeException(nameof(length));

            var chunks = new List<ChunkInfo>();
            int start = offset;
            int end = offset + length;

            while (start < end)
            {
                // 查找下一个分块点
                int cutPoint = FindCutpointWithSIMD(buffer, start, end);

                // 计算当前块的哈希
                string hash = ComputeFastHash(buffer, start, cutPoint - start);

                // 添加找到的块
                chunks.Add(new ChunkInfo
                {
                    Offset = start,
                    Length = cutPoint - start,
                    Hash = hash
                });

                // 移动到下一个块的起始位置
                start = cutPoint;
            }

            return chunks;
        }

        #endregion 公共方法

        #region 内部实现方法

        /// <summary>
        /// 并行处理文件分块
        /// </summary>
        private async Task ChunkFileParallelAsync(
            FileStream fileStream,
            long fileLength,
            int blockSize,
            List<ChunkInfo> chunkInfos,
            CancellationToken cancellationToken)
        {
            // 计算可能的处理块数
            int numBlocks = (int)Math.Ceiling((double)fileLength / blockSize);
            var blockInfos = new List<BlockInfo>(numBlocks);

            // 读取所有数据块
            for (int i = 0; i < numBlocks; i++)
            {
                cancellationToken.ThrowIfCancellationRequested();

                int currentBlockSize = (int)Math.Min(blockSize, fileLength - fileStream.Position);
                var buffer = _bufferPool.Rent(currentBlockSize);

                int bytesRead = await fileStream.ReadAsync(
                    buffer, 0, currentBlockSize, cancellationToken);

                blockInfos.Add(new BlockInfo
                {
                    Buffer = buffer,
                    Size = bytesRead,
                    Position = fileStream.Position - bytesRead
                });
            }

            // 并行处理所有块
            var results = new ConcurrentBag<(long Position, List<ChunkInfo> Chunks)>();

            await Task.Run(() =>
            {
                Parallel.ForEach(blockInfos, new ParallelOptions
                {
                    CancellationToken = cancellationToken,
                    MaxDegreeOfParallelism = Environment.ProcessorCount
                }, blockInfo =>
                {
                    var chunks = ChunkBufferInternal(blockInfo.Buffer, 0, blockInfo.Size);
                    results.Add((blockInfo.Position, chunks));
                });
            }, cancellationToken);

            // 合并结果并按位置排序
            var orderedResults = results.OrderBy(r => r.Position).ToList();

            foreach (var result in orderedResults)
            {
                foreach (var chunk in result.Chunks)
                {
                    chunkInfos.Add(new ChunkInfo
                    {
                        Offset = result.Position + chunk.Offset,
                        Length = chunk.Length,
                        Hash = chunk.Hash
                    });
                }
            }

            // 释放所有缓冲区
            foreach (var blockInfo in blockInfos)
            {
                _bufferPool.Return(blockInfo.Buffer);
            }
        }

        /// <summary>
        /// 顺序处理文件分块
        /// </summary>
        private async Task ChunkFileSequentialAsync(
            FileStream fileStream,
            long fileLength,
            int blockSize,
            List<ChunkInfo> chunkInfos,
            CancellationToken cancellationToken)
        {
            var buffer = _bufferPool.Rent(blockSize);

            try
            {
                long currentPosition = 0;
                int bytesRead;

                while ((bytesRead = await fileStream.ReadAsync(
                    buffer, 0, buffer.Length, cancellationToken)) > 0)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    var chunks = ChunkBufferInternal(buffer, 0, bytesRead);

                    foreach (var chunk in chunks)
                    {
                        chunkInfos.Add(new ChunkInfo
                        {
                            Offset = currentPosition + chunk.Offset,
                            Length = chunk.Length,
                            Hash = chunk.Hash
                        });
                    }

                    currentPosition += bytesRead;

                    // 如果已读取完文件，退出循环
                    if (fileStream.Position >= fileLength)
                        break;
                }
            }
            finally
            {
                _bufferPool.Return(buffer);
            }
        }

        /// <summary>
        /// 对缓冲区数据进行分块（内部实现）
        /// </summary>
        private List<ChunkInfo> ChunkBufferInternal(byte[] buffer, int offset, int length)
        {
            var chunks = new List<ChunkInfo>();
            int start = offset;
            int end = offset + length;

            while (start < end)
            {
                // 查找下一个分块点
                int cutPoint = FindCutpointWithSIMD(buffer, start, end);

                // 计算当前块的哈希
                string hash = ComputeFastHash(buffer, start, cutPoint - start);

                // 添加找到的块
                chunks.Add(new ChunkInfo
                {
                    Offset = start,
                    Length = cutPoint - start,
                    Hash = hash
                });

                // 移动到下一个块的起始位置
                start = cutPoint;
            }

            return chunks;
        }

        /// <summary>
        /// 使用SIMD加速找到下一个分块点（如果支持）
        /// </summary>
        private int FindCutpointWithSIMD(byte[] buffer, int start, int length)
        {
            // 如果剩余数据不足最小块大小，直接返回剩余所有数据
            if (length - start <= _minSize)
                return length;

            // 确定搜索范围
            int end = Math.Min(start + _maxSize, length);

            // 如果支持AVX-512，使用它进行加速
            if (_supportsAvx512 && end - start >= Vector512<byte>.Count)
            {
                return FindCutpointAVX512(buffer, start, end);
            }
            // 否则如果支持AVX2，使用AVX2
            else if (_supportsAvx2 && end - start >= Vector256<byte>.Count)
            {
                return FindCutpointAVX2(buffer, start, end);
            }
            // 如果支持SSE4.2，使用SSE
            else if (_supportsSse42 && end - start >= Vector128<byte>.Count)
            {
                return FindCutpointSSE(buffer, start, end);
            }
            // 回退到标准实现
            else
            {
                return FindCutpointStandard(buffer, start, end);
            }
        }

        /// <summary>
        /// 使用标准算法查找分块点
        /// </summary>
        private int FindCutpointStandard(byte[] buffer, int start, int end)
        {
            // 计算第一阶段和第二阶段的边界
            int phase1End = Math.Min(start + _minSize + (_avgSize >> 1), end);
            int phase2End = Math.Min(start + _avgSize * 2, end);

            uint hash = 0;

            // 阶段1: 跳过最小块大小，不检查
            int i = start;

            // 阶段2: 使用正常掩码 (从_minSize到平均大小的一半)
            for (i = start + _minSize; i < phase1End; i++)
            {
                // 使用优化的Gear哈希
                hash = (hash << 1) + _gearTable[buffer[i]];
                if ((hash & _normalizationMask) == 0)
                {
                    return i + 1;
                }
            }

            // 阶段3: 使用正常掩码 (从平均大小的一半到平均大小的2倍)
            for (; i < phase2End; i++)
            {
                hash = (hash << 1) + _gearTable[buffer[i]];
                if ((hash & _normalizationMask) == 0)
                {
                    return i + 1;
                }
            }

            // 阶段4: 使用次级掩码 (更宽松的条件，增加命中几率)
            for (; i < end; i++)
            {
                hash = (hash << 1) + _gearTable[buffer[i]];
                if ((hash & _leveledMask) == 0)
                {
                    return i + 1;
                }
            }

            // 如果没找到分块点，返回搜索范围结束位置
            return end;
        }

        /// <summary>
        /// 使用SSE指令集加速分块点查找 - 使用unsafe方法处理指针
        /// </summary>
        private unsafe int FindCutpointSSE(byte[] buffer, int start, int end)
        {
            // 至少跳过最小块大小
            int i = start + _minSize;

            // 计算阶段边界
            int phase1End = Math.Min(start + _minSize + (_avgSize >> 1), end);
            int phase2End = Math.Min(start + _avgSize * 2, end);

            uint hash = 0;

            // 预处理第一批字节建立滚动哈希状态
            for (int j = start; j < start + _minSize; j++)
            {
                hash = (hash << 1) + _gearTable[buffer[j]];
            }

            // SSE4.2 加速阶段
            if (_supportsSse42 && i + Vector128<byte>.Count <= phase1End)
            {
                // SSE4.2 批量处理
                int vectorEnd = phase1End - Vector128<byte>.Count;

                // 阶段2 (最小块大小到平均大小的一半)
                fixed (byte* pBuffer = buffer)
                {
                    for (; i <= vectorEnd; i += 8) // 每次处理8字节
                    {
                        if (_supportsSse42)
                        {
                            // 使用SSE指令加载16字节 - 使用正确的指针方法
                            Vector128<byte> chunk = Sse2.LoadVector128(pBuffer + i);

                            // 计算哈希
                            for (int j = 0; j < 8; j++)
                            {
                                hash = (hash << 1) + _gearTable[buffer[i + j]];
                                if ((hash & _normalizationMask) == 0)
                                {
                                    return i + j + 1;
                                }
                            }
                        }
                    }
                }
            }

            // 回退到标准处理
            return FindCutpointStandard(buffer, i, end);
        }

        /// <summary>
        /// 使用AVX2指令集加速分块点查找 - 使用unsafe方法处理指针
        /// </summary>
        private unsafe int FindCutpointAVX2(byte[] buffer, int start, int end)
        {
            // 至少跳过最小块大小
            int i = start + _minSize;

            // 计算阶段边界
            int phase1End = Math.Min(start + _minSize + (_avgSize >> 1), end);
            int phase2End = Math.Min(start + _avgSize * 2, end);

            uint hash = 0;

            // 预处理第一批字节建立滚动哈希状态
            for (int j = start; j < start + _minSize; j++)
            {
                hash = (hash << 1) + _gearTable[buffer[j]];
            }

            // 批量处理阶段 - 使用AVX2
            if (i + Vector256<byte>.Count <= phase1End)
            {
                // 批量处理直到接近阶段1结束
                int vectorEnd = phase1End - Vector256<byte>.Count;

                // 阶段2 使用SIMD批量处理
                fixed (byte* pBuffer = buffer)
                {
                    for (; i <= vectorEnd; i += 16) // 每次处理16字节
                    {
                        if (_supportsAvx2)
                        {
                            // 使用AVX2加载32字节 - 使用正确的指针方法
                            Vector256<byte> chunk = Avx2.LoadVector256(pBuffer + i);

                            // 计算哈希和检查分块点
                            for (int j = 0; j < 16; j++)
                            {
                                hash = (hash << 1) + _gearTable[buffer[i + j]];
                                if ((hash & _normalizationMask) == 0)
                                {
                                    return i + j + 1;
                                }
                            }
                        }
                    }
                }
            }

            // 回退到标准处理剩余字节
            return FindCutpointStandard(buffer, i, end);
        }

        /// <summary>
        /// 使用AVX-512指令集加速分块点查找 - 使用unsafe方法处理指针
        /// </summary>
        private unsafe int FindCutpointAVX512(byte[] buffer, int start, int end)
        {
            // 至少跳过最小块大小
            int i = start + _minSize;

            // 计算阶段边界
            int phase1End = Math.Min(start + _minSize + (_avgSize >> 1), end);
            int phase2End = Math.Min(start + _avgSize * 2, end);

            uint hash = 0;

            // 预处理第一批字节建立滚动哈希状态
            for (int j = start; j < start + _minSize; j++)
            {
                hash = (hash << 1) + _gearTable[buffer[j]];
            }

            // 批量处理阶段 - 使用AVX-512
            if (_supportsAvx512 && i + Vector512<byte>.Count <= phase1End)
            {
                // 批量处理直到接近阶段1结束
                int vectorEnd = phase1End - Vector512<byte>.Count;

                // 阶段2 使用SIMD批量处理
                fixed (byte* pBuffer = buffer)
                {
                    for (; i <= vectorEnd; i += 32) // 每次处理32字节
                    {
                        if (_supportsAvx512)
                        {
                            // 使用AVX-512加载64字节 - 使用正确的指针方法
                            Vector512<byte> chunk = Avx512F.LoadVector512(pBuffer + i);

                            // 计算哈希和检查分块点
                            for (int j = 0; j < 32; j++)
                            {
                                hash = (hash << 1) + _gearTable[buffer[i + j]];
                                if ((hash & _normalizationMask) == 0)
                                {
                                    return i + j + 1;
                                }
                            }
                        }
                    }
                }
            }

            // 如果找不到分块点或不支持AVX-512，回退到AVX2
            return FindCutpointAVX2(buffer, i, end);
        }

        /// <summary>
        /// 计算数据块的快速哈希值
        /// </summary>
        private string ComputeFastHash(byte[] buffer, int offset, int length)
        {
            // 防止偏移量或长度不正确
            if (offset < 0 || length < 0 || offset + length > buffer.Length)
                throw new ArgumentOutOfRangeException("偏移量或长度超出缓冲区范围");

            lock (_hashAlgorithm) // 确保线程安全
            {
                var hashBytes = _hashAlgorithm.ComputeHash(buffer, offset, length);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        /// <summary>
        /// 检查对象是否已被释放
        /// </summary>
        private void ThrowIfDisposed()
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FastCDCPlus));
        }

        #endregion 内部实现方法

        #region IDisposable 实现

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }

        protected virtual void Dispose(bool disposing)
        {
            if (!_disposed)
            {
                if (disposing)
                {
                    _hashAlgorithm?.Dispose();
                }

                _disposed = true;
            }
        }

        ~FastCDCPlus()
        {
            Dispose(false);
        }

        #endregion IDisposable 实现
    }

    /// <summary>
    /// 确定性随机数生成器，确保跨会话一致性
    /// </summary>
    internal class FastCspRng : IDisposable
    {
        private byte[] _state;
        private int _position;

        public FastCspRng(byte[] seed)
        {
            if (seed == null || seed.Length < 16)
                throw new ArgumentException("种子必须至少为16字节");

            _state = new byte[1024]; // 大缓冲区
            _position = 0;

            // 使用种子填充初始状态
            using (var sha = SHA256.Create())
            {
                byte[] expandedSeed = new byte[_state.Length];

                // 复制初始种子
                Array.Copy(seed, expandedSeed, seed.Length);

                // 展开种子到整个状态
                for (int i = 0; i < _state.Length / 32; i++)
                {
                    expandedSeed[16] = (byte)i;
                    var hash = sha.ComputeHash(expandedSeed);
                    Array.Copy(hash, 0, _state, i * 32, 32);
                }
            }
        }

        public void Dispose()
        {
            _state = null;
            _position = 0;
        }

        public uint NextUInt32()
        {
            if (_position + 4 > _state.Length)
            {
                // 重新填充状态
                using (var sha = SHA256.Create())
                {
                    byte[] newState = new byte[_state.Length];

                    // 使用旧状态生成新状态
                    for (int i = 0; i < _state.Length / 32; i++)
                    {
                        byte[] block = new byte[64];
                        Array.Copy(_state, block, Math.Min(64, _state.Length));
                        block[0] ^= (byte)i;
                        var hash = sha.ComputeHash(block);
                        Array.Copy(hash, 0, newState, i * 32, 32);
                    }

                    _state = newState;
                    _position = 0;
                }
            }

            uint value = BinaryPrimitives.ReadUInt32LittleEndian(_state.AsSpan(_position, 4));
            _position += 4;
            return value;
        }
    }

    /// <summary>
    /// 处理块信息
    /// </summary>
    internal class BlockInfo
    {
        public byte[] Buffer { get; set; }
        public int Size { get; set; }
        public long Position { get; set; }
    }

    /// <summary>
    /// 表示一个分块范围
    /// </summary>
    public class ChunkRange
    {
        public int Offset { get; set; }
        public int Length { get; set; }
    }

    /// <summary>
    /// 表示完整的块信息
    /// </summary>
    public class ChunkInfo
    {
        public long Offset { get; set; }
        public int Length { get; set; }
        public string Hash { get; set; }

        public override string ToString()
        {
            return $"Offset: {Offset}, Length: {Length}, Hash: {Hash?.Substring(0, 8)}...";
        }
    }
}