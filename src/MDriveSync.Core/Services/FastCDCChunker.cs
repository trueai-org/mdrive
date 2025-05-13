using System.Buffers;
using System.Runtime.CompilerServices;
using System.Security.Cryptography;

namespace MDriveSync.Core.Services
{
    /// <summary>
    /// FastCDC (快速内容定义分块) 算法实现
    /// 根据文件内容的特征自动识别分块点，适用于去重和增量备份等场景
    /// </summary>
    public class FastCDCChunker : IDisposable
    {
        #region 常量和字段

        // 哈希算法掩码常量
        private const uint GEAR_MASK_BIT = 0x0000D8F3;

        // 标准化掩码，用于改善块大小分布
        private const uint NORMALIZATION_MASK_16 = 0x00007FFF; // 16位掩码，产生约8KB的块
        private const uint NORMALIZATION_MASK_20 = 0x0007FFFF; // 20位掩码，产生约1MB的块
        private const uint NORMALIZATION_MASK_24 = 0x007FFFFF; // 24位掩码，产生约16MB的块
        private const uint NORMALIZATION_MASK_28 = 0x07FFFFFF; // 28位掩码，产生约256MB的块

        // 默认块大小配置
        private const int DEFAULT_MIN_SIZE = 2 * 1024;      // 默认最小块大小 2KB
        private const int DEFAULT_AVG_SIZE = 16 * 1024;     // 默认平均块大小 16KB
        private const int DEFAULT_MAX_SIZE = 64 * 1024;     // 默认最大块大小 64KB
        private const int DEFAULT_BUFFER_SIZE = 8 * 1024 * 1024; // 默认缓冲区大小 8MB

        // 实例字段
        private readonly uint _normalizationMask;
        private readonly uint _secondaryMask; // 用于第二阶段的宽松掩码
        private readonly int _minSize;
        private readonly int _maxSize;
        private readonly int _avgSize;
        private readonly uint[] _gearTable;
        private readonly int _bufferSize;
        private bool _disposed;

        // 哈希计算相关
        private readonly HashAlgorithm _hashAlgorithm;

        #endregion

        #region 构造函数和初始化

        /// <summary>
        /// 初始化FastCDC分块器
        /// </summary>
        /// <param name="minSize">最小块大小</param>
        /// <param name="avgSize">目标平均块大小</param>
        /// <param name="maxSize">最大块大小</param>
        /// <param name="bufferSize">读取缓冲区大小</param>
        /// <param name="hashAlgorithm">可选的哈希算法，默认使用SHA256</param>
        public FastCDCChunker(
            int minSize = DEFAULT_MIN_SIZE,
            int avgSize = DEFAULT_AVG_SIZE,
            int maxSize = DEFAULT_MAX_SIZE,
            int bufferSize = DEFAULT_BUFFER_SIZE,
            HashAlgorithm hashAlgorithm = null)
        {
            // 参数验证
            if (minSize <= 0)
                throw new ArgumentException("最小块大小必须大于0", nameof(minSize));

            if (avgSize <= minSize)
                throw new ArgumentException("平均块大小必须大于最小块大小", nameof(avgSize));

            if (maxSize <= avgSize)
                throw new ArgumentException("最大块大小必须大于平均块大小", nameof(maxSize));

            if (bufferSize < maxSize * 2)
                throw new ArgumentException("缓冲区大小应至少为最大块大小的两倍", nameof(bufferSize));

            _minSize = minSize;
            _avgSize = avgSize;
            _maxSize = maxSize;
            _bufferSize = bufferSize;
            _hashAlgorithm = hashAlgorithm ?? SHA256.Create();

            // 基于平均块大小选择合适的掩码
            if (avgSize <= 8 * 1024)
                _normalizationMask = NORMALIZATION_MASK_16;
            else if (avgSize <= 1024 * 1024)
                _normalizationMask = NORMALIZATION_MASK_20;
            else if (avgSize <= 16 * 1024 * 1024)
                _normalizationMask = NORMALIZATION_MASK_24;
            else
                _normalizationMask = NORMALIZATION_MASK_28;

            // 设置第二阶段掩码（更宽松）
            _secondaryMask = _normalizationMask >> 1;

            // 初始化Gear哈希表
            _gearTable = InitializeGearTable();
        }

        /// <summary>
        /// 以确定性方式初始化Gear哈希表
        /// </summary>
        private uint[] InitializeGearTable()
        {
            var table = new uint[256];

            // 使用固定种子确保哈希表的一致性
            byte[] seed = new byte[] {
                0x12, 0x83, 0x45, 0x67, 0x89, 0xAB, 0xCD, 0xEF,
                0xFE, 0xDC, 0xBA, 0x98, 0x76, 0x54, 0x32, 0x10
            };

            using (var rng = new DeterministicRng(seed))
            {
                for (int i = 0; i < 256; i++)
                {
                    table[i] = rng.NextUInt32() & GEAR_MASK_BIT;
                }
            }

            return table;
        }

        #endregion

        #region 公共方法

        /// <summary>
        /// 分割文件为多个块
        /// </summary>
        /// <param name="filePath">要分块的文件路径</param>
        /// <returns>块信息列表</returns>
        public List<ChunkInfo> ChunkFile(string filePath)
        {
            return ChunkFileAsync(filePath, CancellationToken.None).GetAwaiter().GetResult();
        }

        /// <summary>
        /// 异步分割文件为多个块
        /// </summary>
        /// <param name="filePath">要分块的文件路径</param>
        /// <param name="cancellationToken">取消标记</param>
        /// <returns>块信息列表</returns>
        public async Task<List<ChunkInfo>> ChunkFileAsync(string filePath, CancellationToken cancellationToken = default)
        {
            if (_disposed)
                throw new ObjectDisposedException(nameof(FastCDCChunker));

            if (!File.Exists(filePath))
                throw new FileNotFoundException("找不到指定的文件", filePath);

            var chunkPoints = new List<ChunkInfo>();
            long currentPosition = 0;

            // 使用基于池的缓冲区以减少内存分配
            byte[] buffer = ArrayPool<byte>.Shared.Rent(_bufferSize);

            try
            {
                using (var fileStream = new FileStream(filePath, FileMode.Open, FileAccess.Read, FileShare.Read, 4096, true))
                {
                    int bytesRead;
                    int bufferOffset = 0;

                    while ((bytesRead = await fileStream.ReadAsync(buffer, bufferOffset, buffer.Length - bufferOffset, cancellationToken)) > 0)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        // 总可用数据量
                        int availableBytes = bufferOffset + bytesRead;

                        // 处理缓冲区内的数据
                        int processedBytes = 0;

                        while (processedBytes < availableBytes)
                        {
                            // 确保剩余足够的数据以进行分块
                            int remainingBytes = availableBytes - processedBytes;

                            // 如果剩余不足最小块大小且还有更多文件数据，等待更多数据
                            if (remainingBytes < _minSize && fileStream.Position < fileStream.Length)
                                break;

                            // 计算本次处理的实际长度
                            int lengthToProcess = Math.Min(remainingBytes, _maxSize);

                            // 查找分块点
                            int cutpoint = FindCutpoint(buffer, processedBytes, processedBytes + lengthToProcess);
                            int chunkLength = cutpoint - processedBytes;

                            // 计算哈希值
                            string hash = ComputeHash(buffer, processedBytes, chunkLength);

                            // 添加块信息
                            chunkPoints.Add(new ChunkInfo
                            {
                                Offset = currentPosition,
                                Length = chunkLength,
                                Hash = hash
                            });

                            // 更新位置
                            processedBytes += chunkLength;
                            currentPosition += chunkLength;
                        }

                        // 移动未处理的数据到缓冲区开始
                        if (processedBytes < availableBytes)
                        {
                            Buffer.BlockCopy(buffer, processedBytes, buffer, 0, availableBytes - processedBytes);
                            bufferOffset = availableBytes - processedBytes;
                        }
                        else
                        {
                            bufferOffset = 0;
                        }
                    }
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(buffer);
            }

            return chunkPoints;
        }

        #endregion

        #region 内部方法

        /// <summary>
        /// 找到下一个分块点
        /// </summary>
        /// <param name="buffer">缓冲区</param>
        /// <param name="start">起始位置</param>
        /// <param name="end">结束位置</param>
        /// <returns>分块点位置</returns>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private int FindCutpoint(byte[] buffer, int start, int end)
        {
            // 如果剩余数据不足最小块大小，直接返回剩余所有数据
            if (end - start <= _minSize)
                return end;

            // 计算阶段边界
            int phase1End = Math.Min(start + _minSize + (_avgSize >> 1), end);
            int phase2End = Math.Min(start + _avgSize * 2, end);

            uint hash = 0;
            int i = start;

            // 第一阶段：跳过最小块大小区域
            i = start + _minSize;

            // 第二阶段：从minSize到avgSize*0.5之间，使用标准掩码
            for (; i < phase1End; i++)
            {
                hash = (hash << 1) + _gearTable[buffer[i]];
                if ((hash & _normalizationMask) == 0)
                {
                    return i + 1;
                }
            }

            // 第三阶段：从avgSize*0.5到avgSize*2之间，继续使用标准掩码
            for (; i < phase2End; i++)
            {
                hash = (hash << 1) + _gearTable[buffer[i]];
                if ((hash & _normalizationMask) == 0)
                {
                    return i + 1;
                }
            }

            // 第四阶段：从avgSize*2到maxSize，使用更宽松的掩码
            for (; i < end; i++)
            {
                hash = (hash << 1) + _gearTable[buffer[i]];
                if ((hash & _secondaryMask) == 0)
                {
                    return i + 1;
                }
            }

            // 如果没找到分块点，返回最大允许块大小或数据结束位置
            return end;
        }

        /// <summary>
        /// 计算数据块的哈希值
        /// </summary>
        private string ComputeHash(byte[] buffer, int offset, int length)
        {
            lock (_hashAlgorithm) // 确保线程安全
            {
                var hashBytes = _hashAlgorithm.ComputeHash(buffer, offset, length);
                return BitConverter.ToString(hashBytes).Replace("-", "").ToLowerInvariant();
            }
        }

        #endregion

        #region IDisposable实现

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

        ~FastCDCChunker()
        {
            Dispose(false);
        }

        #endregion
    }

    /// <summary>
    /// 确定性随机数生成器，确保哈希表初始化的一致性
    /// </summary>
    internal class DeterministicRng : IDisposable
    {
        private readonly SHA256 _sha256;
        private byte[] _state;
        private int _position;

        public DeterministicRng(byte[] seed)
        {
            if (seed == null || seed.Length < 16)
                throw new ArgumentException("种子必须至少为16字节");

            _sha256 = SHA256.Create();
            _state = new byte[32];
            _position = 0;

            // 初始化状态
            _state = _sha256.ComputeHash(seed);
        }

        public uint NextUInt32()
        {
            if (_position + 4 > _state.Length)
            {
                // 重新生成状态
                _state = _sha256.ComputeHash(_state);
                _position = 0;
            }

            uint value = BitConverter.ToUInt32(_state, _position);
            _position += 4;
            return value;
        }

        public void Dispose()
        {
            _sha256?.Dispose();
        }
    }
}
