namespace MDriveSync.Core.Services
{
    /// <summary>
    /// 基于 Buzhash 算法的内容定义分块实现
    /// 适用于数据同步与备份场景，确保稳定性和确定性
    /// 标准 CDC 算法的行为：标准的内容定义分块算法会受到早期字节变化的影响，即：第一个字节发生变化，将会重新计算所有分块。
    /// </summary>
    public class BuzhashCDC
    {
        // 固定的哈希表 - 使用确定性种子生成的伪随机值
        private readonly uint[] _hashTable = new uint[256];

        // 预计算的移位表，用于高效移除窗口最左侧字节
        private readonly uint[] _shiftedTable = new uint[256];

        // 分块参数
        private readonly int _windowSize;     // 滑动窗口大小

        private readonly uint _maskBits;      // 掩码位数
        private readonly uint _mask;          // 分块掩码
        private readonly int _minSize;        // 最小块大小
        private readonly int _maxSize;        // 最大块大小

        /// <summary>
        /// 初始化 Buzhash CDC 分块器
        /// </summary>
        /// <param name="windowSize">滑动窗口大小，影响对局部变化的敏感度</param>
        /// <param name="averageSize">目标平均块大小，影响同步效率和元数据大小</param>
        /// <param name="minSize">最小块大小，避免过多小块</param>
        /// <param name="maxSize">最大块大小，避免块太大无法有效同步</param>
        public BuzhashCDC(int windowSize = 48, int averageSize = 1024 * 1024,
                          int minSize = 64 * 1024, int maxSize = 4 * 1024 * 1024)
        {
            if (windowSize <= 0) throw new ArgumentOutOfRangeException(nameof(windowSize), "窗口大小必须为正值");
            if (averageSize <= 0) throw new ArgumentOutOfRangeException(nameof(averageSize), "平均块大小必须为正值");
            if (minSize <= 0) throw new ArgumentOutOfRangeException(nameof(minSize), "最小块大小必须为正值");
            if (maxSize <= minSize) throw new ArgumentOutOfRangeException(nameof(maxSize), "最大块大小必须大于最小块大小");

            _windowSize = windowSize;
            _minSize = minSize;
            _maxSize = maxSize;

            // 根据平均大小计算掩码位数 (log₂(avg))
            _maskBits = (uint)Math.Log(averageSize, 2);
            _mask = (1U << (int)_maskBits) - 1;

            // 初始化哈希表和移位表
            InitializeHashTable();
        }

        /// <summary>
        /// 初始化预计算的哈希表和移位表 - 使用固定种子确保确定性
        /// </summary>
        private void InitializeHashTable()
        {
            // 使用固定种子的随机数生成器，确保每次生成相同的哈希表
            // 种子值在 int 范围内，0x3FD79A47 = 1070677575
            var random = new Random(0x3FD79A47);

            for (int i = 0; i < 256; i++)
            {
                // 生成32位随机值，确保高位也有随机性
                _hashTable[i] = (uint)((random.Next() & 0x7FFFFFFF) | (random.Next() & 0x1) << 31);

                // 预计算每个字节的循环右移值，用于高效更新哈希
                _shiftedTable[i] = RotateRight(_hashTable[i], _windowSize - 1);
            }
        }

        /// <summary>
        /// 对数据流进行内容定义分块，不改变流的原始位置
        /// </summary>
        /// <param name="inputStream">输入数据流</param>
        /// <param name="chunkHandler">块处理回调，接收(块数据,块长度)</param>
        public void Split(Stream inputStream, Action<byte[], int> chunkHandler)
        {
            if (inputStream == null) throw new ArgumentNullException(nameof(inputStream));
            if (chunkHandler == null) throw new ArgumentNullException(nameof(chunkHandler));
            if (!inputStream.CanRead) throw new ArgumentException("输入流必须可读", nameof(inputStream));

            // 保存原始位置，确保处理后还原
            long originalPosition = inputStream.Position;

            try
            {
                // 执行实际分块
                SplitInternal(inputStream, chunkHandler);
            }
            finally
            {
                // 恢复流的原始位置
                if (inputStream.CanSeek)
                {
                    inputStream.Position = originalPosition;
                }
            }
        }

        /// <summary>
        /// 内部分块实现
        /// </summary>
        private void SplitInternal(Stream inputStream, Action<byte[], int> chunkHandler)
        {
            byte[] buffer = new byte[Math.Max(_windowSize, 16 * 1024)]; // 读取缓冲区
            byte[] window = new byte[_windowSize]; // 滑动窗口

            // 文件长度检查
            long fileLength = inputStream.CanSeek ? inputStream.Length - inputStream.Position : -1;

            // 1. 处理小文件情况（小于窗口大小）
            if (fileLength >= 0 && fileLength <= _windowSize)
            {
                byte[] smallFile = new byte[fileLength];
                int bytesRead = inputStream.Read(smallFile, 0, (int)fileLength);
                if (bytesRead > 0)
                {
                    chunkHandler(smallFile, bytesRead);
                }
                return;
            }

            // 初始窗口读取
            int initialBytesRead = inputStream.Read(window, 0, _windowSize);
            if (initialBytesRead < _windowSize)
            {
                // 文件小于窗口大小，通常不应发生（已在前面检查）
                if (initialBytesRead > 0)
                {
                    chunkHandler(window, initialBytesRead);
                }
                return;
            }

            // 用于块数据的内存流，使用指定初始容量减少内存重分配
            using (var memoryStream = new MemoryStream(Math.Min(_maxSize, 1024 * 1024)))
            {
                // 2. 处理主体分块逻辑
                ProcessChunks(inputStream, chunkHandler, buffer, window, memoryStream);
            }
        }

        /// <summary>
        /// 处理数据流分块的主要逻辑
        /// </summary>
        private void ProcessChunks(Stream inputStream, Action<byte[], int> chunkHandler,
                                  byte[] buffer, byte[] window, MemoryStream memoryStream)
        {
            // 初始化 Buzhash 值
            uint hash = CalculateInitialHash(window);

            // 写入初始窗口
            memoryStream.Write(window, 0, _windowSize);
            int currentSize = _windowSize;

            // 缓冲区读取循环
            while (true)
            {
                int bufferSize = inputStream.Read(buffer, 0, buffer.Length);
                if (bufferSize <= 0) break; // 到达流末尾

                // 处理缓冲区内的每个字节
                for (int i = 0; i < bufferSize; i++)
                {
                    byte inByte = buffer[i];
                    byte outByte = window[currentSize % _windowSize];

                    // 滑动窗口
                    window[currentSize % _windowSize] = inByte;

                    // 更新哈希值 - 标准 Buzhash 滚动哈希更新公式
                    hash = ((hash << 1) | (hash >> 31)) ^ // 循环左移
                           _hashTable[inByte] ^ // 添加新字节
                           _shiftedTable[outByte]; // 移除旧字节

                    // 写入当前字节
                    memoryStream.WriteByte(inByte);
                    currentSize++;

                    // 检查是否达到分块点
                    if (currentSize >= _minSize &&
                        ((hash & _mask) == 0 || currentSize >= _maxSize))
                    {
                        // 生成块
                        byte[] chunk = memoryStream.ToArray();
                        chunkHandler(chunk, chunk.Length);

                        // 重置状态准备下一个块
                        memoryStream.SetLength(0);
                        currentSize = 0;

                        // 也需要重置哈希值，以保持块间独立性
                        // 这确保了即使在相同数据流中，每个块的边界判定相互独立
                        hash = 0;
                    }
                }
            }

            // 处理最后一个块（如果有）
            if (memoryStream.Length > 0)
            {
                byte[] chunk = memoryStream.ToArray();
                chunkHandler(chunk, chunk.Length);
            }
        }

        /// <summary>
        /// 计算初始窗口的哈希值
        /// </summary>
        private uint CalculateInitialHash(byte[] window)
        {
            uint hash = 0;
            for (int i = 0; i < _windowSize; i++)
            {
                hash = ((hash << 1) | (hash >> 31)) ^ _hashTable[window[i]];
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
        public (int MinSize, int MaxSize, double AvgSize) GetChunkSizeStats()
        {
            return (_minSize, _maxSize, Math.Pow(2, _maskBits));
        }
    }
}