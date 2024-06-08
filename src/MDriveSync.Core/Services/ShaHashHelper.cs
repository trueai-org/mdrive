using MDriveSync.Security;

namespace MDriveSync.Core
{
    /// <summary>
    /// 算法
    /// </summary>
    public static class ShaHashHelper
    {
        /// <summary>
        /// 计算文件的采样 hash 值，结合头部、尾部、中间固定采样点和随机采样点
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="alg">哈希算法名称</param>
        /// <param name="seed">随机种子，用于生成随机采样点（Size + 当天时间）</param>
        /// <returns>采样 hash 字符串</returns>
        public static string ComputeFileSampleHash(string filePath, string alg, int seed)
        {
            // 种子 += 当天时间
            // Ticks 需要除以 10000，因为 DateTime.Now.Ticks 是以 100ns 为单位的
            // 除以 1000 是为了转换为秒
            // 除以 3600 是为了转换为小时
            // 除以 24 是为了转换为天
            seed += (int)(DateTime.Now.Date.Ticks / 10000 / 1000 / 3600 / 24);

            var hash = ComputeFileSampleHashHex(filePath, alg, seed);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
        }

        /// <summary>
        /// 根据文件大小和随机种子计算采样 hash 值
        /// </summary>
        /// <param name="filePath">文件路径</param>
        /// <param name="algorithm">哈希算法实例</param>
        /// <param name="seed">随机种子，用于生成随机采样点</param>
        /// <returns>采样 hash 字节数组</returns>
        private static byte[] ComputeFileSampleHashHex(string filePath, string algorithm, int seed)
        {
            const int sampleSize = 1024; // 每个采样点的大小
            const int numberOfRandomSamples = 16; // 随机采样点的数量

            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            long fileLength = stream.Length;

            // 如果文件较小，直接计算整个文件的哈希值
            if (fileLength <= sampleSize * (3 + numberOfRandomSamples))
            {
                return HashHelper.ComputeHash(stream, algorithm);
            }

            // 定义采样点：头部、中间、尾部和随机采样点
            long[] samplePoints = GetSamplePoints(fileLength, sampleSize, numberOfRandomSamples, seed);

            using var combinedStream = new MemoryStream();

            // 读取采样点数据并合并到内存流
            foreach (var point in samplePoints)
            {
                var buffer = new byte[sampleSize];
                stream.Seek(point, SeekOrigin.Begin);
                int bytesRead = stream.Read(buffer, 0, sampleSize);
                combinedStream.Write(buffer, 0, bytesRead);
            }

            combinedStream.Seek(0, SeekOrigin.Begin);

            return HashHelper.ComputeHash(combinedStream, algorithm);
        }

        /// <summary>
        /// 获取所有采样点，包括头部、中间、尾部和随机采样点
        /// </summary>
        /// <param name="fileLength">文件长度</param>
        /// <param name="sampleSize">每个采样点的大小</param>
        /// <param name="numberOfRandomSamples">随机采样点的数量</param>
        /// <param name="seed">随机种子，用于生成随机采样点</param>
        /// <returns>所有采样点的数组</returns>
        private static long[] GetSamplePoints(long fileLength, int sampleSize, int numberOfRandomSamples, int seed)
        {
            var random = new Random(seed);
            var samplePoints = new long[3 + numberOfRandomSamples];

            // 固定采样点：头部、中间和尾部
            samplePoints[0] = 0;
            samplePoints[1] = Math.Max(0, fileLength / 2 - sampleSize / 2);
            samplePoints[2] = Math.Max(0, fileLength - sampleSize);

            // 随机采样点
            for (int i = 0; i < numberOfRandomSamples; i++)
            {
                samplePoints[3 + i] = random.Next(0, (int)(fileLength - sampleSize));
            }

            Array.Sort(samplePoints);
            return samplePoints;
        }

        /// <summary>
        /// 计算文件 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="level"></param>
        /// <param name="alg"></param>
        /// <returns></returns>
        public static string ComputeFileHash(string filePath, int level, string alg, long seed)
        {
            if (level == 2)
            {
                // 整个文件
                return ComputeFileHash(filePath, alg);
            }
            else
            {
                // 采样计算
                return ComputeFileSampleHash(filePath, alg, (int)seed);
            }
        }

        /// <summary>
        /// 计算文件完整的 hash
        /// </summary>
        /// <param name="filePath"></param>
        /// <param name="alg"></param>
        /// <returns></returns>
        public static string ComputeFileHash(string filePath, string alg)
        {
            using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
            var hash = HashHelper.ComputeHash(stream, alg);
            return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
        }

        ///// <summary>
        ///// 采样间隔
        ///// </summary>
        ///// <param name="fileSize"></param>
        ///// <returns></returns>
        //private static long GetSampleInterval(long fileSize)
        //{
        //    // < 10KB 文件不采样
        //    if (fileSize < 1024 * 10)
        //    {
        //        return 0;
        //    }

        //    // < 1MB 文件, 每100KB 采样 1KB
        //    if (fileSize < 1024 * 1024)
        //    {
        //        return 1024 * 100;
        //    }

        //    // > 10MB 每1MB 采样1KB, 采样数量 > 10
        //    // > 20MB 每2MB 采样1KB, 采样数量 > 10
        //    // > 30MB 每3MB 采样1KB, 采样数量 > 10
        //    // ...
        //    // > 100MB 每10MB 采样1KB, 采样数量 > 10
        //    // > 110MB 每11MB 采样1KB, 采样数量 > 10
        //    // ...
        //    // > 500MB 每50MB 采样1KB, 采样数量 > 10
        //    // ...
        //    // > 1000MB/1GB 每100MB 采样1KB, 采样数量 > 10
        //    // > 2GB 每200MB 采样1KB, 采样数量 > 10
        //    // > 10GB 每1000MB/1GB 采样1KB, 采样数量 > 10
        //    // > 20GB 每2GB 采样1KB, 采样数量 > 10
        //    // > 100GB 每10GB 采样1KB, 采样数量 > 10
        //    // > 1000GB/1TB 每100GB 采样1KB, 采样数量 > 10

        //    // 每10MB增加1MB的采样间隔
        //    long interval = 1024 * 1024; // 默认间隔为1MB
        //    for (long size = 10L * 1024 * 1024; size < 1024 * 1024 * 1024 * 1024L; size += 10L * 1024 * 1024)
        //    {
        //        if (fileSize <= size)
        //            return interval;
        //        interval += 1024 * 1024;
        //    }

        //    // 对于大于1000GB的文件，每100GB采样 1KB
        //    return 1024 * 1024 * 1024 * 100L;
        //}

        ///// <summary>
        ///// 获取算法（根据算法名称）
        ///// </summary>
        ///// <param name="alg"></param>
        ///// <returns></returns>
        ///// <exception cref="ArgumentException"></exception>
        //private static HashAlgorithm GetAlgorithm(string alg)
        //{
        //    return alg.ToLower() switch
        //    {
        //        "SHA1" => SHA1.Create(),
        //        "SHA256" => SHA256.Create(),
        //        _ => throw new ArgumentException("Unsupported algorithm", nameof(alg)),
        //    };
        //}

        ///// <summary>
        ///// 采样算法文件 hash
        ///// </summary>
        ///// <param name="filePath"></param>
        ///// <param name="alg"></param>
        ///// <returns></returns>
        ///// <exception cref="ArgumentException"></exception>
        //public static string ComputeFileSampleHash(string filePath, string alg)
        //{
        //    using var algorithm = GetAlgorithm(alg);
        //    var hash = ComputeFileSampleHash(filePath, algorithm);
        //    return BitConverter.ToString(hash).Replace("-", string.Empty).ToLower();
        //}

        ///// <summary>
        ///// 采样计算文件 hash
        ///// </summary>
        ///// <param name="filePath"></param>
        ///// <param name="algorithm"></param>
        ///// <returns></returns>
        //private static byte[] ComputeFileSampleHash(string filePath, HashAlgorithm algorithm)
        //{
        //    // 每次采样1KB
        //    const int sampleSize = 1024;

        //    // 最终计算的 byte
        //    byte[] finalData = new byte[0];

        //    using (var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read))
        //    {
        //        long fileLength = stream.Length;
        //        long sampleInterval = GetSampleInterval(fileLength);

        //        // 不需要采样
        //        if (sampleInterval <= 0)
        //        {
        //            return algorithm.ComputeHash(stream);
        //        }

        //        for (long offset = 0; offset < fileLength; offset += sampleInterval)
        //        {
        //            var buffer = new byte[sampleSize];
        //            stream.Seek(offset, SeekOrigin.Begin);
        //            stream.Read(buffer, 0, sampleSize);
        //            finalData = Combine(finalData, buffer);
        //        }

        //        // 在文件末尾进行额外的1KB采样
        //        if (fileLength > sampleSize)
        //        {
        //            var endBuffer = new byte[sampleSize];
        //            stream.Seek(-sampleSize, SeekOrigin.End);
        //            stream.Read(endBuffer, 0, sampleSize);
        //            finalData = Combine(finalData, endBuffer);
        //        }
        //    }

        //    return algorithm.ComputeHash(finalData);
        //}

        ///// <summary>
        ///// 计算文件开始部分 size 的 hash
        ///// </summary>
        ///// <param name="filePath"></param>
        ///// <param name="alg"></param>
        ///// <param name="size"></param>
        ///// <returns></returns>
        //public static string ComputeFileStartHash(string filePath, string alg, int size = 1024)
        //{
        //    using var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        //    byte[] buffer = new byte[size];
        //    using var algorithm = GetAlgorithm(alg);
        //    int numRead = inputStream.Read(buffer, 0, buffer.Length);
        //    if (numRead == 0)
        //        throw new InvalidOperationException("未能从文件中读取数据");

        //    algorithm.TransformFinalBlock(buffer, 0, numRead);
        //    return BitConverter.ToString(algorithm.Hash!).Replace("-", string.Empty).ToLower();
        //}

        ///// <summary>
        ///// 计算文件结束部分 size 的 hash
        ///// </summary>
        ///// <param name="filePath"></param>
        ///// <param name="alg"></param>
        ///// <param name="size"></param>
        ///// <returns></returns>
        ///// <exception cref="InvalidOperationException"></exception>
        //public static string ComputeFileEndHash(string filePath, string alg, int size = 1024)
        //{
        //    using var inputStream = new FileStream(filePath, FileMode.Open, FileAccess.Read);

        //    long fileSize = inputStream.Length;

        //    // 如果文件大小小于size，则使用整个文件大小
        //    size = (int)Math.Min(size, fileSize);

        //    byte[] buffer = new byte[size];
        //    using var algorithm = GetAlgorithm(alg);

        //    // 移动到文件末尾的size位置
        //    inputStream.Seek(-size, SeekOrigin.End);
        //    int numRead = inputStream.Read(buffer, 0, buffer.Length);

        //    // 确保读取了数据
        //    if (numRead <= 0)
        //        throw new InvalidOperationException("未能从文件末尾读取数据");

        //    algorithm.TransformFinalBlock(buffer, 0, numRead);
        //    return BitConverter.ToString(algorithm.Hash!).Replace("-", string.Empty).ToLower();
        //}

        ///// <summary>
        ///// 计算文件完整的 hash
        ///// </summary>
        ///// <param name="filePath"></param>
        ///// <param name="algorithm"></param>
        ///// <returns></returns>
        //private static byte[] ComputeFileHash(string filePath, HashAlgorithm algorithm)
        //{
        //    using var stream = new FileStream(filePath, FileMode.Open, FileAccess.Read);
        //    return algorithm.ComputeHash(stream);
        //}

        ///// <summary>
        ///// 合并 byte
        ///// </summary>
        ///// <param name="first"></param>
        ///// <param name="second"></param>
        ///// <returns></returns>
        //private static byte[] Combine(byte[] first, byte[] second)
        //{
        //    var ret = new byte[first.Length + second.Length];
        //    Buffer.BlockCopy(first, 0, ret, 0, first.Length);
        //    Buffer.BlockCopy(second, 0, ret, first.Length, second.Length);
        //    return ret;
        //}
    }
}