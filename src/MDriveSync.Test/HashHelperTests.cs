using MDriveSync.Security;
using System.Diagnostics;

namespace MDriveSync.Test
{
    public class HashHelperTests : BaseTests
    {
        [Theory]
        [InlineData(10 * 1024 * 1024, 10)] // 测试 10MB 数据，迭代 10 次
        [InlineData(50 * 1024 * 1024, 5)]  // 测试 50MB 数据，迭代 5 次
        public void TestHashPerformance(int dataSize, int iterations)
        {
            // 准备测试数据
            byte[] data = new byte[dataSize];
            new Random().NextBytes(data);

            // 定义要测试的哈希算法
            string[] algorithms = { "SHA256", "SHA1", "MD5", "BLAKE3", "XXH3", "XXH128" };

            foreach (var algorithm in algorithms)
            {
                // 测试哈希算法耗时
                TimeSpan elapsedTime = MeasureHashTime(data, algorithm, iterations);

                // 输出结果
                Console.WriteLine($"Algorithm: {algorithm}, Data Size: {dataSize / (1024 * 1024)} MB, Iterations: {iterations}, Time: {elapsedTime.TotalMilliseconds} ms");
            }
        }

        private TimeSpan MeasureHashTime(byte[] data, string algorithm, int iterations)
        {
            Stopwatch stopwatch = new Stopwatch();

            stopwatch.Start();
            for (int i = 0; i < iterations; i++)
            {
                HashHelper.ComputeHash(data, algorithm);
            }
            stopwatch.Stop();

            return stopwatch.Elapsed;
        }
    }
}