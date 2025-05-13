namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 优化的 Buzhash 内容定义分块实现
    /// 支持分块重置点，提供可预测的分块结果
    /// 
    /// 分块重置点：
    /// 每隔固定字节（默认4MB）重置分块状态
    /// 防止早期变化影响整个文件的分块
    /// 实现优点：
    /// 简单高效，低内存占用
    /// 变化影响限制在重置点范围内
    /// 支持任意大小的文件或流
    /// </summary>
    public class BuzhashPlusCDC
    {
        // 分块参数
        private readonly int _windowSize;     // 滑动窗口大小
        private readonly int _maskBits;       // 掩码位数
        private readonly uint _mask;          // 分块掩码
        private readonly int _minSize;        // 最小块大小
        private readonly int _maxSize;        // 最大块大小
        private readonly int _resetInterval;  // 分块重置间隔（字节数）

        // 哈希表和预计算表
        private readonly uint[] _hashTable = new uint[256];
        private readonly uint[] _shiftedTable = new uint[256];

        // 缓冲区
        private readonly byte[] _window;

        /// <summary>
        /// 初始化 Buzhash CDC 分块器
        /// </summary>
        /// <param name="windowSize">滑动窗口大小，通常为 32-64 字节</param>
        /// <param name="averageSize">目标平均块大小，通常为 1MB 左右</param>
        /// <param name="minSize">最小块大小（字节）</param>
        /// <param name="maxSize">最大块大小（字节）</param>
        /// <param name="resetInterval">分块重置间隔（字节），0表示禁用重置</param>
        public BuzhashPlusCDC(
            int windowSize = 48,
            int averageSize = 1024 * 1024,
            int minSize = 64 * 1024,
            int maxSize = 4 * 1024 * 1024,
            int resetInterval = 2 * 4 * 1024 * 1024)
        {
            // 参数验证
            if (windowSize <= 0) throw new ArgumentOutOfRangeException(nameof(windowSize));
            if (averageSize <= 0) throw new ArgumentOutOfRangeException(nameof(averageSize));
            if (minSize <= 0) throw new ArgumentOutOfRangeException(nameof(minSize));
            if (maxSize <= minSize) throw new ArgumentOutOfRangeException(nameof(maxSize));

            _windowSize = windowSize;
            _minSize = minSize;
            _maxSize = maxSize;
            _maskBits = (int)Math.Log(averageSize, 2);
            _mask = (1U << _maskBits) - 1;
            _resetInterval = resetInterval;

            // 预分配窗口缓冲区
            _window = new byte[windowSize];

            // 初始化哈希表
            InitializeHashTable();
        }

        /// <summary>
        /// 初始化哈希表和移位表
        /// </summary>
        private void InitializeHashTable()
        {
            // 使用固定种子确保确定性
            var random = new Random(0x3FD79A47);

            for (int i = 0; i < 256; i++)
            {
                _hashTable[i] = (uint)((random.Next() & 0x7FFFFFFF) | (random.Next() & 0x1) << 31);
                _shiftedTable[i] = RotateRight(_hashTable[i], _windowSize - 1);
            }
        }

        /// <summary>
        /// 对数据流进行分块
        /// </summary>
        public void Split(Stream inputStream, Action<byte[], int> chunkHandler)
        {
            if (inputStream == null) throw new ArgumentNullException(nameof(inputStream));
            if (chunkHandler == null) throw new ArgumentNullException(nameof(chunkHandler));

            // 保存原始位置
            long originalPosition = inputStream.Position;

            try
            {
                // 执行分块
                DoSplit(inputStream, chunkHandler);
            }
            finally
            {
                // 恢复流位置
                if (inputStream.CanSeek)
                {
                    inputStream.Position = originalPosition;
                }
            }
        }

        /// <summary>
        /// 主分块方法
        /// </summary>
        private void DoSplit(Stream inputStream, Action<byte[], int> chunkHandler)
        {
            // 处理小文件
            if (inputStream.CanSeek && (inputStream.Length - inputStream.Position) <= _windowSize)
            {
                HandleSmallFile(inputStream, chunkHandler);
                return;
            }

            // 读取初始窗口
            int bytesRead = inputStream.Read(_window, 0, _windowSize);
            if (bytesRead < _windowSize)
            {
                if (bytesRead > 0)
                {
                    chunkHandler(_window, bytesRead);
                }
                return;
            }

            // 内存流和缓冲区
            using (var memoryStream = new MemoryStream(Math.Min(_maxSize * 2, 4 * 1024 * 1024)))
            {
                byte[] buffer = new byte[64 * 1024]; // 读取缓冲区

                // 跟踪状态
                uint hash = CalculateHash(_window, 0, _windowSize);
                int chunkSize = 0;
                long totalBytes = 0;
                long nextResetPoint = _resetInterval > 0 ? _resetInterval : long.MaxValue;

                // 写入初始窗口
                memoryStream.Write(_window, 0, _windowSize);
                chunkSize += _windowSize;
                totalBytes += _windowSize;

                // 循环处理
                while (true)
                {
                    int bufferSize = inputStream.Read(buffer, 0, buffer.Length);
                    if (bufferSize <= 0) break;

                    for (int i = 0; i < bufferSize; i++)
                    {
                        byte inByte = buffer[i];

                        // 检查是否到达重置点
                        if (_resetInterval > 0 && totalBytes == nextResetPoint && chunkSize >= _minSize)
                        {
                            // 强制输出当前块
                            OutputChunk(memoryStream, chunkHandler);

                            // 重置重置点和状态
                            nextResetPoint = totalBytes + _resetInterval;

                            // 读取新窗口
                            if (i + _windowSize <= bufferSize)
                            {
                                // 窗口可以完全从缓冲区读取
                                Array.Copy(buffer, i, _window, 0, _windowSize);
                                memoryStream.Write(_window, 0, _windowSize);

                                // 重新计算哈希值和更新状态
                                hash = CalculateHash(_window, 0, _windowSize);
                                chunkSize = _windowSize;
                                totalBytes += _windowSize;
                                i += _windowSize - 1; // -1 是因为循环会递增 i
                                continue;
                            }
                            else
                            {
                                // 窗口需要从缓冲区和流中读取
                                int remaining = bufferSize - i;
                                Array.Copy(buffer, i, _window, 0, remaining);

                                // 从流中读取剩余的窗口数据
                                int needMore = _windowSize - remaining;
                                int moreRead = inputStream.Read(_window, remaining, needMore);

                                // 如果流结束，处理剩余部分
                                if (moreRead < needMore)
                                {
                                    int totalRead = remaining + moreRead;
                                    if (totalRead > 0)
                                    {
                                        chunkHandler(_window, totalRead);
                                    }
                                    return;
                                }

                                // 重新计算哈希值和更新状态
                                hash = CalculateHash(_window, 0, _windowSize);
                                memoryStream.Write(_window, 0, _windowSize);
                                chunkSize = _windowSize;
                                totalBytes += _windowSize;

                                // 跳出本次循环，开始新循环
                                i = bufferSize; // 设置为缓冲区末尾，强制读取新数据
                                break;
                            }
                        }

                        // 正常分块处理
                        // 滑动窗口
                        byte outByte = _window[chunkSize % _windowSize];
                        _window[chunkSize % _windowSize] = inByte;

                        // 更新哈希值
                        hash = ((hash << 1) | (hash >> 31)) ^
                               _hashTable[inByte] ^
                               _shiftedTable[outByte];

                        // 写入字节
                        memoryStream.WriteByte(inByte);
                        chunkSize++;
                        totalBytes++;

                        // 检查是否达到分块点
                        if (chunkSize >= _minSize &&
                            ((hash & _mask) == 0 || chunkSize >= _maxSize))
                        {
                            OutputChunk(memoryStream, chunkHandler);

                            // 重置哈希值
                            hash = 0;
                            chunkSize = 0;
                        }
                    }
                }

                // 处理最后一个块
                if (memoryStream.Length > 0)
                {
                    OutputChunk(memoryStream, chunkHandler);
                }
            }
        }

        /// <summary>
        /// 输出当前块
        /// </summary>
        private void OutputChunk(MemoryStream memoryStream, Action<byte[], int> chunkHandler)
        {
            byte[] chunk = memoryStream.ToArray();
            chunkHandler(chunk, chunk.Length);
            memoryStream.SetLength(0);
        }

        /// <summary>
        /// 处理小文件
        /// </summary>
        private void HandleSmallFile(Stream inputStream, Action<byte[], int> chunkHandler)
        {
            int bytesToRead = (int)(inputStream.Length - inputStream.Position);
            byte[] data = new byte[bytesToRead];
            int bytesRead = inputStream.Read(data, 0, bytesToRead);

            if (bytesRead > 0)
            {
                chunkHandler(data, bytesRead);
            }
        }

        /// <summary>
        /// 计算哈希值
        /// </summary>
        private uint CalculateHash(byte[] data, int offset, int count)
        {
            uint hash = 0;
            for (int i = 0; i < count; i++)
            {
                hash = ((hash << 1) | (hash >> 31)) ^ _hashTable[data[offset + i]];
            }
            return hash;
        }

        /// <summary>
        /// 循环右移操作
        /// </summary>
        private static uint RotateRight(uint value, int bits)
        {
            return (value >> bits) | (value << (32 - bits));
        }


        /// <summary>
        /// 获取当前配置的块大小统计信息
        /// </summary>
        public (int MinSize, int MaxSize, double AvgSize, int ResetInterval) GetChunkSizeStats()
        {
            return (_minSize, _maxSize, Math.Pow(2, _maskBits), _resetInterval);
        }
    }
}
