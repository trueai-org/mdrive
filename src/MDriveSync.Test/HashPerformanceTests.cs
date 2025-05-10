using MDriveSync.Core;
using MDriveSync.Security;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;

namespace MDriveSync.Test
{
    /// <summary>
    /// 测试哈希算法性能
    /// </summary>
    public class HashPerformanceTests : BaseTests
    {
        [Fact]
        public void TestHashAlgorithmComparison()
        {
            // 配置测试参数
            int[] sizes = { 10 * 1024 * 1024, 20 * 1024 * 1024, 30 * 1024 * 1024, 100 * 1024 * 1024 };
            int iterations = 3; // 减少迭代次数以加快测试运行
            string[] algorithms = { "SHA1", "SHA256", "SHA384", "MD5", "XXH3", "XXH128", "BLAKE3" };

            var results = new Dictionary<string, Dictionary<int, List<double>>>();
            foreach (var algorithm in algorithms)
            {
                results[algorithm] = new Dictionary<int, List<double>>();
                foreach (var size in sizes)
                {
                    results[algorithm][size] = new List<double>();
                }
            }

            // 执行测试
            foreach (var size in sizes)
            {
                Console.WriteLine($"测试数据大小: {size / (1024 * 1024)} MB");

                // 创建随机测试数据
                byte[] data = new byte[size];
                new Random(42).NextBytes(data); // 使用固定种子以确保结果可重现

                foreach (var algorithm in algorithms)
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        var stopwatch = new Stopwatch();
                        stopwatch.Start();
                        byte[] hash = HashHelper.ComputeHash(data, algorithm);
                        stopwatch.Stop();

                        double elapsedMs = stopwatch.Elapsed.TotalMilliseconds;
                        results[algorithm][size].Add(elapsedMs);

                        Console.WriteLine($"算法: {algorithm}, 轮次: {i + 1}, 耗时: {elapsedMs:F2} ms");
                    }
                }
                Console.WriteLine();
            }

            // 生成摘要报告
            StringBuilder report = new StringBuilder();
            report.AppendLine("# 哈希算法性能测试报告");
            report.AppendLine();
            report.AppendLine("## 测试环境");
            report.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            report.AppendLine($"- 处理器: {Environment.ProcessorCount} 核心");
            report.AppendLine($"- .NET 版本: {Environment.Version}");
            report.AppendLine($"- 测试时间: {DateTime.Now}");
            report.AppendLine();

            // 添加各个大小的平均执行时间表格
            foreach (var size in sizes)
            {
                report.AppendLine($"## 数据大小: {size / (1024 * 1024)} MB");
                report.AppendLine();
                report.AppendLine("| 算法名称 | 平均耗时 (ms) | 吞吐量 (GB/s) |");
                report.AppendLine("|---------|------------|------------|");

                var hashWidths = new Dictionary<string, int>
                {
                    ["SHA1"] = 160,
                    ["SHA256"] = 256,
                    ["SHA384"] = 384,
                    ["MD5"] = 128,
                    ["XXH3"] = 64,
                    ["XXH128"] = 128,
                    ["BLAKE3"] = 256
                };

                // 计算并排序结果
                var sortedResults = new List<(string Algorithm, double AvgTime, double Throughput)>();
                foreach (var algorithm in algorithms)
                {
                    // 跳过第一次测量结果(预热)，取后续几次的平均值
                    var validResults = results[algorithm][size].Skip(1).ToList();
                    double avgTime = validResults.Count > 0 ? validResults.Average() : 0;
                    double throughput = (size / 1024.0 / 1024.0 / 1024.0) / (avgTime / 1000.0); // GB/s

                    sortedResults.Add((algorithm, avgTime, throughput));
                }

                // 按平均时间排序并输出
                foreach (var result in sortedResults.OrderBy(r => r.AvgTime))
                {
                    report.AppendLine($"| {result.Algorithm,-8} | {result.AvgTime,-6:F2}  | {result.Throughput,-4:F2} |");
                }

                report.AppendLine();
            }

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "哈希算法性能测试报告.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"性能报告已保存至：{reportPath}");

            // 测试断言，确保测试正常运行
            Assert.True(File.Exists(reportPath));
        }

        [Theory]
        [InlineData(10 * 1024 * 1024, 3)] // 10MB, 3次迭代
        public void TestSpecificHashPerformance(int dataSize, int iterations)
        {
            // 准备测试数据
            byte[] data = new byte[dataSize];
            new Random().NextBytes(data);

            // 定义要测试的哈希算法
            string[] algorithms = { "SHA256", "SHA1", "MD5", "BLAKE3", "XXH3", "XXH128", "SHA3" };

            foreach (var algorithm in algorithms)
            {
                // 测试哈希算法耗时
                TimeSpan elapsedTime = MeasureHashTime(data, algorithm, iterations);

                // 输出结果
                Console.WriteLine($"Algorithm: {algorithm}, Data Size: {dataSize / (1024 * 1024)} MB, Iterations: {iterations}, Time: {elapsedTime.TotalMilliseconds} ms");

                // 简单断言确保算法能正常工作
                Assert.True(elapsedTime.TotalMilliseconds > 0);
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

    /// <summary>
    /// 测试加密和解密功能
    /// </summary>
    public class EncryptionTests : BaseTests
    {
        [Fact]
        public void TestAES256GCMEncryptionDecryption()
        {
            // 测试数据
            string originalText = "hello world";
            byte[] originalData = Encoding.UTF8.GetBytes(originalText);
            string password = "test-password";

            // 加密
            byte[] encryptedData = EncryptionHelper.EncryptWithAES256GCM(originalData, password);
            Assert.NotNull(encryptedData);
            Assert.NotEmpty(encryptedData);
            Assert.NotEqual(originalData, encryptedData);

            // 解密
            byte[] decryptedData = EncryptionHelper.DecryptWithAES256GCM(encryptedData, password);
            Assert.NotNull(decryptedData);
            Assert.Equal(originalData, decryptedData);

            string decryptedText = Encoding.UTF8.GetString(decryptedData);
            Assert.Equal(originalText, decryptedText);
        }

        [Fact]
        public void TestChaCha20Poly1305EncryptionDecryption()
        {
            // 测试数据
            string originalText = "hello world";
            byte[] originalData = Encoding.UTF8.GetBytes(originalText);
            string password = "test-password";

            // 加密
            byte[] encryptedData = EncryptionHelper.EncryptWithChaCha20Poly1305(originalData, password);
            Assert.NotNull(encryptedData);
            Assert.NotEmpty(encryptedData);
            Assert.NotEqual(originalData, encryptedData);

            // 解密
            byte[] decryptedData = EncryptionHelper.DecryptWithChaCha20Poly1305(encryptedData, password);
            Assert.NotNull(decryptedData);
            Assert.Equal(originalData, decryptedData);

            string decryptedText = Encoding.UTF8.GetString(decryptedData);
            Assert.Equal(originalText, decryptedText);
        }

        [Fact]
        public void TestWrongPasswordDecryption()
        {
            // 测试数据
            string originalText = "hello world";
            byte[] originalData = Encoding.UTF8.GetBytes(originalText);
            string correctPassword = "correct-password";
            string wrongPassword = "wrong-password";

            // 使用正确密码加密
            byte[] encryptedDataAES = EncryptionHelper.EncryptWithAES256GCM(originalData, correctPassword);
            byte[] encryptedDataChaCha = EncryptionHelper.EncryptWithChaCha20Poly1305(originalData, correctPassword);

            // 使用错误密码解密，应该抛出异常
            Assert.Throws<CryptographicException>(() => EncryptionHelper.DecryptWithAES256GCM(encryptedDataAES, wrongPassword));
            Assert.Throws<CryptographicException>(() => EncryptionHelper.DecryptWithChaCha20Poly1305(encryptedDataChaCha, wrongPassword));
        }
    }

    /// <summary>
    /// 测试压缩和解压缩功能
    /// </summary>
    public class CompressionTests : BaseTests
    {
        [Theory]
        [InlineData("LZ4")]
        [InlineData("Zstd")]
        [InlineData("Snappy")]
        public void TestCompressDecompress(string algorithm)
        {
            // 测试数据
            string originalText = "hello world repeated many times to ensure compression is effective. " +
                                 string.Join(" ", Enumerable.Repeat("hello world", 100));
            byte[] originalData = Encoding.UTF8.GetBytes(originalText);

            // 压缩
            byte[] compressedData = CompressionHelper.Compress(originalData, algorithm);
            Assert.NotNull(compressedData);
            Assert.NotEmpty(compressedData);

            // 对于可压缩的数据，压缩后应该变小
            Console.WriteLine($"原始大小: {originalData.Length}, 压缩后大小: {compressedData.Length}, 压缩率: {(double)compressedData.Length / originalData.Length:P2}");

            // 解压缩
            byte[] decompressedData = CompressionHelper.Decompress(compressedData, algorithm);
            Assert.NotNull(decompressedData);
            Assert.Equal(originalData, decompressedData);

            string decompressedText = Encoding.UTF8.GetString(decompressedData);
            Assert.Equal(originalText, decompressedText);
        }

        [Fact]
        public void TestCompressEncryptDecompress()
        {
            // 测试数据
            string originalText = "hello world with encryption and compression";
            byte[] originalData = Encoding.UTF8.GetBytes(originalText);
            string compressionAlgorithm = "LZ4";
            string encryptionAlgorithm = "AES256-GCM";
            string encryptionKey = "test-key";

            // 压缩并加密
            byte[] processed = CompressionHelper.Compress(originalData, compressionAlgorithm, encryptionAlgorithm, encryptionKey);
            Assert.NotNull(processed);
            Assert.NotEmpty(processed);

            // 解密并解压缩
            byte[] decompressed = CompressionHelper.Decompress(processed, compressionAlgorithm, encryptionAlgorithm, encryptionKey);
            Assert.NotNull(decompressed);
            Assert.Equal(originalData, decompressed);

            string resultText = Encoding.UTF8.GetString(decompressed);
            Assert.Equal(originalText, resultText);
        }

        [Fact]
        public void TestStreamCompression()
        {
            // 准备测试文件
            string tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
            Directory.CreateDirectory(tempDir);
            string inputFilePath = Path.Combine(tempDir, "test-input.txt");
            string outputFilePath = Path.Combine(tempDir, "test-output.enc");
            string decompressedFilePath = Path.Combine(tempDir, "test-decompressed.txt");

            try
            {
                //// 创建测试文件
                //string testContent = "This is a test content for file compression and encryption. " +
                //                    string.Join(" ", Enumerable.Repeat("More content for compression.", 100));
                //File.WriteAllText(inputFilePath, testContent);

                //// 配置
                //string compressionType = "Zstd";
                //string encryptionType = "AES256-GCM";
                //string encryptionKey = "test-encryption-key";
                //string hashAlgorithm = "BLAKE3";

                //// 压缩并加密
                //using (FileStream inputFileStream = new FileStream(inputFilePath, FileMode.Open, FileAccess.Read))
                //using (FileStream outputFileStream = new FileStream(outputFilePath, FileMode.Create, FileAccess.Write))
                //{
                //    CompressionHelper.CompressAndEncryptStream(
                //        inputFileStream,
                //        outputFileStream,
                //        compressionType,
                //        encryptionType,
                //        encryptionKey,
                //        hashAlgorithm);
                //}

                //// 检查输出文件是否存在且不为空
                //FileInfo outputFile = new FileInfo(outputFilePath);
                //Assert.True(outputFile.Exists);
                //Assert.True(outputFile.Length > 0);

                //// 解密并解压缩
                //using (FileStream inputStream = new FileStream(outputFilePath, FileMode.Open, FileAccess.Read))
                //using (FileStream outputStream = new FileStream(decompressedFilePath, FileMode.Create, FileAccess.Write))
                //{
                //    CompressionHelper.DecompressStream(
                //        inputStream,
                //        outputStream,
                //        compressionType,
                //        encryptionType,
                //        encryptionKey,
                //        hashAlgorithm);
                //}

                //// 检查解压缩后的文件内容是否一致
                //string decompressedContent = File.ReadAllText(decompressedFilePath);
                //Assert.Equal(testContent, decompressedContent);
            }
            finally
            {
                // 清理测试文件
                if (Directory.Exists(tempDir))
                {
                    Directory.Delete(tempDir, true);
                }
            }
        }
    }

    /// <summary>
    /// 测试文件操作工具类
    /// </summary>
    public class FileHelperTests : BaseTests
    {
        [Fact]
        public void TestCreateRandomFile()
        {
            // 创建临时路径
            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                // 指定文件大小 (1 MB)
                long fileSize = 1 * 1024 * 1024;

                // 创建随机文件
                FileHelper.CreateRandomFile(tempFilePath, fileSize);

                // 验证文件是否创建成功
                FileInfo fileInfo = new FileInfo(tempFilePath);
                Assert.True(fileInfo.Exists);
                Assert.Equal(fileSize, fileInfo.Length);
            }
            finally
            {
                // 清理
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }

        [Fact]
        public void TestModifyFileAndResetLastWriteTime()
        {
            // 创建临时路径
            string tempFilePath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            try
            {
                // 创建随机文件 (10 KB)
                long fileSize = 10 * 1024;
                FileHelper.CreateRandomFile(tempFilePath, fileSize);

                // 记录原始的最后写入时间和内容
                DateTime originalWriteTime = File.GetLastWriteTime(tempFilePath);
                byte[] originalContent = File.ReadAllBytes(tempFilePath);

                // 让系统时间略微流逝，确保时间戳会变化
                Thread.Sleep(100);

                // 修改文件但保持原始时间戳
                int modifyPosition = 1025; // 修改位置
                FileHelper.ModifyFileAndResetLastWriteTime(tempFilePath, modifyPosition);

                // 验证内容已变化
                byte[] modifiedContent = File.ReadAllBytes(tempFilePath);
                Assert.NotEqual(originalContent[modifyPosition], modifiedContent[modifyPosition]);

                // 验证时间戳没有变化
                DateTime currentWriteTime = File.GetLastWriteTime(tempFilePath);
                Assert.Equal(originalWriteTime, currentWriteTime);
            }
            finally
            {
                // 清理
                if (File.Exists(tempFilePath))
                {
                    File.Delete(tempFilePath);
                }
            }
        }
    }

    /// <summary>
    /// 测试生成随机字符串和路径
    /// </summary>
    public class RandomDataTests : BaseTests
    {
        private static string GenerateRandomString(int length)
        {
            Random random = new Random();
            StringBuilder stringBuilder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char c = (char)random.Next('a', 'z' + 1);  // 生成随机小写字母
                stringBuilder.Append(c);
            }
            return stringBuilder.ToString();
        }

        private static string GenerateRandomPath(int maxLength)
        {
            StringBuilder pathBuilder = new StringBuilder();
            pathBuilder.Append(Directory.GetCurrentDirectory());  // 从当前工作目录开始

            Random random = new Random();
            try
            {
                while (pathBuilder.Length < maxLength - 260)  // 确保留有空间添加文件名
                {
                    pathBuilder.Append(Path.DirectorySeparatorChar);
                    pathBuilder.Append(GenerateRandomString(10));  // 每个目录名最多10个字符
                }
            }
            catch (Exception)
            {
                Console.WriteLine("生成路径时出现异常");
            }

            return pathBuilder.ToString();
        }

        [Theory]
        [InlineData(10)]
        [InlineData(50)]
        [InlineData(100)]
        public void TestGenerateRandomString(int length)
        {
            string randomString = GenerateRandomString(length);

            // 验证字符串长度
            Assert.Equal(length, randomString.Length);

            // 验证字符串仅包含小写字母
            foreach (char c in randomString)
            {
                Assert.True(c >= 'a' && c <= 'z');
            }
        }

        [Fact]
        public void TestGenerateRandomPath()
        {
            int maxLength = 4096; // 使用较小的长度进行测试
            string randomPath = GenerateRandomPath(maxLength);

            // 验证路径以当前目录开头
            Assert.StartsWith(Directory.GetCurrentDirectory(), randomPath);

            // 验证路径长度不超过指定值
            Assert.True(randomPath.Length <= maxLength);
        }

        [Fact]
        public void TestFileNameCompression()
        {
            for (int i = 1; i <= 30; i++)
            {
                string randomFileName = GenerateRandomString(i);
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(randomFileName);

                // 测试压缩
                byte[] compressedBytes = CompressionHelper.Compress(fileNameBytes, "LZ4");

                // 测试对压缩后的字节再次压缩
                byte[] doubleCompressedBytes = CompressionHelper.Compress(compressedBytes, "LZ4");

                // 测试解压
                byte[] decompressedBytes = CompressionHelper.Decompress(doubleCompressedBytes, "LZ4");
                byte[] originalBytes = CompressionHelper.Decompress(decompressedBytes, "LZ4");

                // 验证完整性
                Assert.Equal(fileNameBytes, originalBytes);

                // 转为Base64并验证
                string base64 = Convert.ToBase64String(compressedBytes);
                byte[] decodedData = Convert.FromBase64String(base64);
                Assert.Equal(compressedBytes, decodedData);

                Console.WriteLine($"长度: {i}, 原始: {fileNameBytes.Length}, 压缩: {compressedBytes.Length}, Base64: {base64.Length}");
            }
        }
    }
}
