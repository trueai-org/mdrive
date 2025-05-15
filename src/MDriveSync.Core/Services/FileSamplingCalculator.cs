namespace MDriveSync.Core.Services
{
    public class SamplingResult
    {
        /// <summary>
        /// 总块数
        /// </summary>
        public long TotalChunks { get; set; }

        /// <summary>
        /// 块大小
        /// </summary>
        public long ChunkSize { get; set; }

        /// <summary>
        /// 抽样块数
        /// </summary>
        public int SampledChunks { get; set; }

        /// <summary>
        /// 实际覆盖率
        /// </summary>
        public double Coverage { get; set; }
    }

    /// <summary>
    /// 文件采样计算器，抽样块数尽可能少
    /// </summary>
    public class FileSamplingCalculator
    {
        private const long MinChunkSizeBytes = 16 * 1024;   // 16KB

        private const long MaxChunkSizeBytes = 1024 * 1024 * 1024 * 1024L;  // 1TB

        /// <summary>
        /// 文件采样计算器，抽样块数尽可能少
        /// </summary>
        /// <param name="fileSize">文件大小</param>
        /// <param name="sampleRate">抽样率 0-1</param>
        /// <param name="maxSampledChunks">
        /// 最大抽样块数(1-16)
        /// 当系数为 2 时，抽样率大于 0.65 时，将始终抽样。
        /// 当系数为 4 时，抽样率大于 0.80 时，将始终抽样。
        /// ...
        /// 当系数为 4 时，抽样率大于 0.90 时，将始终抽样。
        /// </param>
        /// <returns></returns>
        public static SamplingResult CalculateSampling(long fileSize, double sampleRate, int maxSampledChunks = 4)
        {
            if (fileSize < MinChunkSizeBytes)
                return FullCoverageResult(fileSize);

            if (sampleRate <= 0 || sampleRate >= 1)
                return FullCoverageResult(fileSize);

            // 核心算法
            var bestResult = FindOptimalSampling(fileSize, sampleRate, maxSampledChunks);
            return bestResult ?? AdjustEdgeCase(fileSize, sampleRate, maxSampledChunks);
        }

        private static SamplingResult FindOptimalSampling(long fileSize, double sampleRate, int maxSampledChunks)
        {
            SamplingResult best = null;
            double minError = double.MaxValue;

            // 遍历所有可能的抽样块数（优先小值）
            for (int k = 1; k <= maxSampledChunks; k++)
            {
                // 计算理论分块数
                double exactN = k / sampleRate;
                var n = (long)Math.Floor(exactN);

                // 有效性检查
                if (n < k || n == 0) continue;

                // 计算块尺寸
                long chunkSize = fileSize / n;

                // 块尺寸合法性检查
                if (chunkSize < MinChunkSizeBytes || chunkSize > MaxChunkSizeBytes)
                    continue;

                // 计算实际覆盖率
                double actualRate = (double)k / n;
                double error = Math.Abs(actualRate - sampleRate);

                // 择优策略：误差更小，或误差相同但块数更少
                if (error < minError || (Math.Abs(error - minError) < 1e-9 && k < best.SampledChunks))
                {
                    best = new SamplingResult
                    {
                        TotalChunks = n,
                        ChunkSize = chunkSize,
                        SampledChunks = k,
                        Coverage = actualRate
                    };
                    minError = error;
                }
            }
            return best;
        }

        // 边界情况调整（当无法找到合法分块时）
        private static SamplingResult AdjustEdgeCase(long fileSize, double sampleRate, int maxSampledChunks)
        {
            // 判断需要调整的方向
            bool isOversized = (fileSize / (long)(1 / sampleRate)) > MaxChunkSizeBytes;

            if (isOversized)
                return AdjustForMaxChunkSize(fileSize, sampleRate, maxSampledChunks);
            else
                return AdjustForMinChunkSize(fileSize, sampleRate, maxSampledChunks);
        }

        // 调整到最小块尺寸
        private static SamplingResult AdjustForMinChunkSize(long fileSize, double sampleRate, int maxSampledChunks)
        {
            long n = fileSize / MinChunkSizeBytes;
            n = Math.Max(n, 1);  // 确保至少1个块

            // 在允许范围内寻找最优k值
            int bestK = FindBestK(n, sampleRate, maxSampledChunks);
            return new SamplingResult
            {
                TotalChunks = n,
                ChunkSize = fileSize / n,
                SampledChunks = bestK,
                Coverage = (double)bestK / n
            };
        }

        // 调整到最大块尺寸
        private static SamplingResult AdjustForMaxChunkSize(long fileSize, double sampleRate, int maxSampledChunks)
        {
            long n = (long)Math.Ceiling((double)fileSize / MaxChunkSizeBytes);
            n = Math.Max(n, 1);  // 确保至少1个块

            // 在允许范围内寻找最优k值
            int bestK = FindBestK(n, sampleRate, maxSampledChunks);
            return new SamplingResult
            {
                TotalChunks = n,
                ChunkSize = fileSize / n,
                SampledChunks = bestK,
                Coverage = (double)bestK / n
            };
        }

        // 在给定总块数n时寻找最佳k
        private static int FindBestK(long n, double targetRate, int maxSampledChunks)
        {
            int bestK = 1;
            double minError = double.MaxValue;

            for (int k = 1; k <= Math.Min(maxSampledChunks, n); k++)
            {
                double error = Math.Abs((double)k / n - targetRate);
                if (error < minError || (Math.Abs(error - minError) < 1e-9 && k > bestK))
                {
                    bestK = k;
                    minError = error;
                }
            }
            return bestK;
        }

        private static SamplingResult FullCoverageResult(long fileSize) => new SamplingResult
        {
            TotalChunks = 1,
            ChunkSize = fileSize,
            SampledChunks = 1,
            Coverage = 1
        };
    }
}