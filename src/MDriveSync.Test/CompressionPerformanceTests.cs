using MDriveSync.Security;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;

namespace MDriveSync.Test
{
    /// <summary>
    /// 测试压缩算法性能
    /// </summary>
    public class CompressionPerformanceTests : BaseTests
    {
        [Fact]
        public void TestCompressorPerformance()
        {
            // 配置测试参数
            int[] sizes = { 1 * 1024 * 1024, 5 * 1024 * 1024, 10 * 1024 * 1024, 20 * 1024 * 1024 };
            int iterations = 3;
            string[] algorithms = { "LZ4", "Zstd", "Snappy", "LZMA", "Deflate", "Brotli" };

            var results = new Dictionary<string, Dictionary<int, List<(double CompressTime, double DecompressTime, double CompressRatio)>>>();
            foreach (var algorithm in algorithms)
            {
                results[algorithm] = new Dictionary<int, List<(double, double, double)>>();
                foreach (var size in sizes)
                {
                    results[algorithm][size] = new List<(double, double, double)>();
                }
            }

            // 执行测试
            foreach (var size in sizes)
            {
                Console.WriteLine($"测试数据大小: {size / (1024 * 1024)} MB");

                // 创建模拟可压缩数据 (有一些重复)
                byte[] data = GenerateCompressibleData(size);
                Console.WriteLine($"生成测试数据, 长度: {data.Length} 字节");

                foreach (var algorithm in algorithms)
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        // 压缩计时
                        var stopwatchCompress = new Stopwatch();
                        stopwatchCompress.Start();
                        byte[] compressed = CompressionHelper.Compress(data, algorithm);
                        stopwatchCompress.Stop();
                        double compressTime = stopwatchCompress.Elapsed.TotalMilliseconds;

                        // 解压缩计时
                        var stopwatchDecompress = new Stopwatch();
                        stopwatchDecompress.Start();
                        byte[] decompressed = CompressionHelper.Decompress(compressed, algorithm);
                        stopwatchDecompress.Stop();
                        double decompressTime = stopwatchDecompress.Elapsed.TotalMilliseconds;

                        // 计算压缩率
                        double compressionRatio = (double)compressed.Length / data.Length;

                        // 验证数据完整性
                        bool isValid = data.SequenceEqual(decompressed);
                        if (!isValid)
                        {
                            Console.WriteLine($"警告: {algorithm} 算法解压缩结果与原始数据不匹配!");
                        }

                        results[algorithm][size].Add((compressTime, decompressTime, compressionRatio));

                        Console.WriteLine($"算法: {algorithm,-7}, 轮次: {i + 1}, 压缩: {compressTime:F2} ms, 解压: {decompressTime:F2} ms, 压缩率: {compressionRatio:P2}");
                    }
                }
                Console.WriteLine();
            }

            // 生成报告
            StringBuilder report = new StringBuilder();
            report.AppendLine("# 压缩算法性能测试报告");
            report.AppendLine();
            report.AppendLine("## 测试环境");
            report.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            report.AppendLine($"- 处理器: {Environment.ProcessorCount} 核心");
            report.AppendLine($"- .NET 版本: {Environment.Version}");
            report.AppendLine($"- 测试时间: {DateTime.Now}");
            report.AppendLine();

            foreach (var size in sizes)
            {
                report.AppendLine($"## 数据大小: {size / (1024 * 1024)} MB");
                report.AppendLine();
                report.AppendLine("| 算法 | 压缩时间 (ms) | 解压时间 (ms) | 压缩率 | 压缩速度 (MB/s) | 解压速度 (MB/s) |");
                report.AppendLine("|------|-------------|-------------|--------|----------------|----------------|");

                var sizeResults = new List<(string Algorithm, double AvgCompressTime, double AvgDecompressTime, double AvgRatio, double CompressSpeed, double DecompressSpeed)>();

                foreach (var algorithm in algorithms)
                {
                    // 跳过第一轮结果（预热）
                    var validResults = results[algorithm][size].Skip(1).ToList();
                    if (validResults.Count > 0)
                    {
                        double avgCompressTime = validResults.Average(r => r.CompressTime);
                        double avgDecompressTime = validResults.Average(r => r.DecompressTime);
                        double avgRatio = validResults.Average(r => r.CompressRatio);

                        // 计算速度 (MB/s)
                        double compressSpeed = size / 1024.0 / 1024.0 / (avgCompressTime / 1000.0);
                        double decompressSpeed = size / 1024.0 / 1024.0 / (avgDecompressTime / 1000.0);

                        sizeResults.Add((algorithm, avgCompressTime, avgDecompressTime, avgRatio, compressSpeed, decompressSpeed));
                    }
                }

                // 按压缩速度排序
                foreach (var result in sizeResults.OrderByDescending(r => r.CompressSpeed))
                {
                    report.AppendLine($"| {result.Algorithm,-6} | {result.AvgCompressTime,-13:F2} | {result.AvgDecompressTime,-13:F2} | {result.AvgRatio,-6:P2} | {result.CompressSpeed,-16:F2} | {result.DecompressSpeed,-16:F2} |");
                }

                report.AppendLine();
            }

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "压缩算法性能测试报告.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"压缩性能报告已保存至: {reportPath}");
            Assert.True(File.Exists(reportPath));
        }

        /// <summary>
        /// 测试压缩并加密流程的性能
        /// </summary>
        [Fact]
        public void TestCompressAndEncryptPerformance()
        {
            // 测试配置
            int dataSize = 10 * 1024 * 1024; // 10MB
            string[] compressionAlgorithms = { "LZ4", "Zstd", "Snappy", "LZMA", "Deflate", "Brotli" };
            string[] encryptionAlgorithms = { "AES256-GCM", "ChaCha20-Poly1305" };
            int iterations = 3;

            // 生成测试数据
            byte[] data = GenerateCompressibleData(dataSize);

            // 准备结果收集
            var results = new Dictionary<string, List<(double CompressEncryptTime, double DecryptDecompressTime, double CompressRatio)>>();

            // 为每种组合创建一个键
            foreach (var compAlgo in compressionAlgorithms)
            {
                foreach (var encAlgo in encryptionAlgorithms)
                {
                    string key = $"{compAlgo}/{encAlgo}";
                    results[key] = new List<(double, double, double)>();
                }
            }

            Console.WriteLine($"测试压缩+加密性能 (数据大小: {dataSize / (1024 * 1024)} MB)");

            // 执行测试
            foreach (var compAlgo in compressionAlgorithms)
            {
                foreach (var encAlgo in encryptionAlgorithms)
                {
                    string key = $"{compAlgo}/{encAlgo}";
                    string encryptionKey = "test-encryption-key-123456";

                    for (int i = 0; i < iterations; i++)
                    {
                        // 压缩并加密
                        var compressEncryptSw = new Stopwatch();
                        compressEncryptSw.Start();
                        byte[] processed = CompressionHelper.Compress(data, compAlgo, encAlgo, encryptionKey);
                        compressEncryptSw.Stop();
                        double compressEncryptTime = compressEncryptSw.Elapsed.TotalMilliseconds;

                        // 解密并解压缩
                        var decryptDecompressSw = new Stopwatch();
                        decryptDecompressSw.Start();
                        byte[] decompressed = CompressionHelper.Decompress(processed, compAlgo, encAlgo, encryptionKey);
                        decryptDecompressSw.Stop();
                        double decryptDecompressTime = decryptDecompressSw.Elapsed.TotalMilliseconds;

                        // 计算压缩率
                        double compressionRatio = (double)processed.Length / data.Length;

                        // 验证数据
                        bool isValid = data.SequenceEqual(decompressed);
                        if (!isValid)
                        {
                            Console.WriteLine($"警告: {key} 处理后的数据与原始数据不匹配!");
                        }

                        results[key].Add((compressEncryptTime, decryptDecompressTime, compressionRatio));

                        Console.WriteLine($"组合: {key,-20}, 轮次: {i + 1}, 压缩+加密: {compressEncryptTime:F2} ms, 解密+解压: {decryptDecompressTime:F2} ms, 压缩率: {compressionRatio:P2}");
                    }
                }
            }

            // 生成报告
            StringBuilder report = new StringBuilder();
            report.AppendLine("# 压缩和加密组合性能测试报告");
            report.AppendLine();
            report.AppendLine("## 测试环境");
            report.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            report.AppendLine($"- 处理器: {Environment.ProcessorCount} 核心");
            report.AppendLine($"- .NET 版本: {Environment.Version}");
            report.AppendLine($"- 测试数据大小: {dataSize / (1024 * 1024)} MB");
            report.AppendLine($"- 测试时间: {DateTime.Now}");
            report.AppendLine();

            report.AppendLine("| 算法组合 | 压缩+加密 (ms) | 解密+解压 (ms) | 压缩率 | 压缩+加密速度 (MB/s) | 解密+解压速度 (MB/s) |");
            report.AppendLine("|----------|--------------|--------------|--------|---------------------|---------------------|");

            var summaryResults = new List<(string Combo, double EncryptTime, double DecryptTime, double Ratio, double EncSpeed, double DecSpeed)>();

            foreach (var entry in results)
            {
                // 跳过第一轮结果（预热）
                var validResults = entry.Value.Skip(1).ToList();
                if (validResults.Count > 0)
                {
                    double avgEncryptTime = validResults.Average(r => r.CompressEncryptTime);
                    double avgDecryptTime = validResults.Average(r => r.DecryptDecompressTime);
                    double avgRatio = validResults.Average(r => r.CompressRatio);

                    // 计算速度 (MB/s)
                    double encSpeed = dataSize / 1024.0 / 1024.0 / (avgEncryptTime / 1000.0);
                    double decSpeed = dataSize / 1024.0 / 1024.0 / (avgDecryptTime / 1000.0);

                    summaryResults.Add((entry.Key, avgEncryptTime, avgDecryptTime, avgRatio, encSpeed, decSpeed));
                }
            }

            // 按加密+压缩速度排序
            foreach (var result in summaryResults.OrderByDescending(r => r.EncSpeed))
            {
                report.AppendLine($"| {result.Combo,-10} | {result.EncryptTime,-14:F2} | {result.DecryptTime,-14:F2} | {result.Ratio,-6:P2} | {result.EncSpeed,-21:F2} | {result.DecSpeed,-21:F2} |");
            }

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "压缩加密组合性能测试报告.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"压缩加密组合性能报告已保存至: {reportPath}");
            Assert.True(File.Exists(reportPath));
        }

        /// <summary>
        /// 生成具有一定可压缩性的随机数据
        /// </summary>
        /// <param name="size">数据大小（字节）</param>
        /// <returns>随机生成的数据</returns>
        private byte[] GenerateCompressibleData(int size)
        {
            byte[] data = new byte[size];
            Random random = new Random(42); // 使用固定种子以确保结果可复现

            // 创建一些重复模式以增加压缩率
            byte[] pattern = new byte[1024];
            random.NextBytes(pattern);

            for (int i = 0; i < size; i++)
            {
                // 70%的数据使用重复模式，30%完全随机
                if (random.NextDouble() < 0.7)
                {
                    data[i] = pattern[i % pattern.Length];
                }
                else
                {
                    data[i] = (byte)random.Next(256);
                }
            }

            return data;
        }
    }

    /// <summary>
    /// 测试加密算法性能
    /// </summary>
    public class EncryptionPerformanceTests : BaseTests
    {
        [Fact]
        public void TestEncryptionPerformance()
        {
            // 配置测试参数
            int[] sizes = { 1 * 1024 * 1024, 5 * 1024 * 1024, 10 * 1024 * 1024, 20 * 1024 * 1024 };
            int iterations = 3;
            var algorithms = new Dictionary<string, Func<byte[], string, byte[]>>
            {
                ["AES256-GCM"] = (data, key) => EncryptionHelper.EncryptWithAES256GCM(data, key),
                ["ChaCha20-Poly1305"] = (data, key) => EncryptionHelper.EncryptWithChaCha20Poly1305(data, key)
            };

            var decryptionAlgorithms = new Dictionary<string, Func<byte[], string, byte[]>>
            {
                ["AES256-GCM"] = (data, key) => EncryptionHelper.DecryptWithAES256GCM(data, key),
                ["ChaCha20-Poly1305"] = (data, key) => EncryptionHelper.DecryptWithChaCha20Poly1305(data, key)
            };

            string encryptionKey = "test-encryption-key-for-performance-benchmarks";

            var results = new Dictionary<string, Dictionary<int, List<(double EncryptTime, double DecryptTime)>>>();
            foreach (var algorithm in algorithms.Keys)
            {
                results[algorithm] = new Dictionary<int, List<(double, double)>>();
                foreach (var size in sizes)
                {
                    results[algorithm][size] = new List<(double, double)>();
                }
            }

            // 执行测试
            foreach (var size in sizes)
            {
                Console.WriteLine($"测试数据大小: {size / (1024 * 1024)} MB");

                // 创建随机数据
                byte[] data = new byte[size];
                new Random(42).NextBytes(data);

                foreach (var algorithm in algorithms.Keys)
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        // 加密测试
                        var encryptSw = new Stopwatch();
                        encryptSw.Start();
                        byte[] encrypted = algorithms[algorithm](data, encryptionKey);
                        encryptSw.Stop();
                        double encryptTime = encryptSw.Elapsed.TotalMilliseconds;

                        // 解密测试
                        var decryptSw = new Stopwatch();
                        decryptSw.Start();
                        byte[] decrypted = decryptionAlgorithms[algorithm](encrypted, encryptionKey);
                        decryptSw.Stop();
                        double decryptTime = decryptSw.Elapsed.TotalMilliseconds;

                        // 验证数据
                        bool isValid = data.SequenceEqual(decrypted);
                        if (!isValid)
                        {
                            Console.WriteLine($"警告: {algorithm} 解密后的数据与原始数据不匹配!");
                        }

                        results[algorithm][size].Add((encryptTime, decryptTime));

                        Console.WriteLine($"算法: {algorithm,-16}, 轮次: {i + 1}, 加密: {encryptTime:F2} ms, 解密: {decryptTime:F2} ms");
                    }
                }
                Console.WriteLine();
            }

            // 生成报告
            StringBuilder report = new StringBuilder();
            report.AppendLine("# 加密算法性能测试报告");
            report.AppendLine();
            report.AppendLine("## 测试环境");
            report.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            report.AppendLine($"- 处理器: {Environment.ProcessorCount} 核心");
            report.AppendLine($"- .NET 版本: {Environment.Version}");
            report.AppendLine($"- 测试时间: {DateTime.Now}");
            report.AppendLine();

            foreach (var size in sizes)
            {
                report.AppendLine($"## 数据大小: {size / (1024 * 1024)} MB");
                report.AppendLine();
                report.AppendLine("| 算法 | 加密时间 (ms) | 解密时间 (ms) | 加密速度 (MB/s) | 解密速度 (MB/s) |");
                report.AppendLine("|------|-------------|-------------|----------------|----------------|");

                var sizeResults = new List<(string Algorithm, double AvgEncryptTime, double AvgDecryptTime, double EncryptSpeed, double DecryptSpeed)>();

                foreach (var algorithm in algorithms.Keys)
                {
                    // 跳过第一轮结果（预热）
                    var validResults = results[algorithm][size].Skip(1).ToList();
                    if (validResults.Count > 0)
                    {
                        double avgEncryptTime = validResults.Average(r => r.EncryptTime);
                        double avgDecryptTime = validResults.Average(r => r.DecryptTime);

                        // 计算速度 (MB/s)
                        double encryptSpeed = size / 1024.0 / 1024.0 / (avgEncryptTime / 1000.0);
                        double decryptSpeed = size / 1024.0 / 1024.0 / (avgDecryptTime / 1000.0);

                        sizeResults.Add((algorithm, avgEncryptTime, avgDecryptTime, encryptSpeed, decryptSpeed));
                    }
                }

                // 按加密速度排序
                foreach (var result in sizeResults.OrderByDescending(r => r.EncryptSpeed))
                {
                    report.AppendLine($"| {result.Algorithm,-6} | {result.AvgEncryptTime,-13:F2} | {result.AvgDecryptTime,-13:F2} | {result.EncryptSpeed,-16:F2} | {result.DecryptSpeed,-16:F2} |");
                }

                report.AppendLine();
            }

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "加密算法性能测试报告.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"加密性能报告已保存至: {reportPath}");
            Assert.True(File.Exists(reportPath));
        }

        /// <summary>
        /// 测试不同大小文件名的加密性能
        /// </summary>
        [Fact]
        public void TestFileNameEncryptionPerformance()
        {
            // 配置
            int iterations = 10;
            int maxFileNameLength = 200;
            string[] encryptionAlgorithms = { "AES256-GCM", "ChaCha20-Poly1305" };
            string encryptionKey = "test-key-for-filename-encryption";

            // 结果收集
            var results = new Dictionary<string, Dictionary<int, List<(double EncryptTime, double DecryptTime, int EncryptedSize)>>>();
            foreach (var algorithm in encryptionAlgorithms)
            {
                results[algorithm] = new Dictionary<int, List<(double, double, int)>>();
                for (int i = 1; i <= maxFileNameLength; i++)
                {
                    results[algorithm][i] = new List<(double, double, int)>();
                }
            }

            // 执行测试
            for (int length = 1; length <= maxFileNameLength; length += 10) // 增量10以减少测试时间
            {
                // 生成随机文件名
                string randomFileName = GenerateRandomString(length);
                byte[] fileNameBytes = Encoding.UTF8.GetBytes(randomFileName);

                foreach (var algorithm in encryptionAlgorithms)
                {
                    for (int i = 0; i < iterations; i++)
                    {
                        // 加密文件名
                        var encryptSw = new Stopwatch();
                        encryptSw.Start();
                        var encrypted = CompressionHelper.Compress(fileNameBytes, null, algorithm, encryptionKey);
                        encryptSw.Stop();

                        // 解密文件名
                        var decryptSw = new Stopwatch();
                        decryptSw.Start();
                        var decrypted = CompressionHelper.Decompress(encrypted, null, algorithm, encryptionKey);
                        decryptSw.Stop();

                        // 验证
                        bool isValid = fileNameBytes.SequenceEqual(decrypted);
                        if (!isValid)
                        {
                            Console.WriteLine($"警告: {algorithm} 文件名解密失败! 长度: {length}");
                        }

                        results[algorithm][length].Add((encryptSw.Elapsed.TotalMilliseconds,
                                                     decryptSw.Elapsed.TotalMilliseconds,
                                                     encrypted.Length));
                    }

                    // 输出当前测试的平均结果
                    var currentResults = results[algorithm][length];
                    double avgEncryptTime = currentResults.Average(r => r.EncryptTime);
                    double avgDecryptTime = currentResults.Average(r => r.DecryptTime);
                    double avgEncryptedSize = currentResults.Average(r => r.EncryptedSize);

                    Console.WriteLine($"文件名长度: {length,-3}, 算法: {algorithm,-16}, " +
                                    $"加密: {avgEncryptTime:F2} ms, 解密: {avgDecryptTime:F2} ms, " +
                                    $"加密后大小: {avgEncryptedSize:F0} 字节");
                }
            }

            // 生成报告
            StringBuilder report = new StringBuilder();
            report.AppendLine("# 文件名加密性能测试报告");
            report.AppendLine();
            report.AppendLine("## 测试环境");
            report.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            report.AppendLine($"- 处理器: {Environment.ProcessorCount} 核心");
            report.AppendLine($"- .NET 版本: {Environment.Version}");
            report.AppendLine($"- 测试时间: {DateTime.Now}");
            report.AppendLine();

            report.AppendLine("## 文件名加密性能比较");
            report.AppendLine();
            report.AppendLine("| 文件名长度 | 算法 | 加密时间 (ms) | 解密时间 (ms) | 加密后大小 (字节) | 膨胀比率 |");
            report.AppendLine("|------------|------|--------------|--------------|-----------------|----------|");

            for (int length = 1; length <= maxFileNameLength; length += 10)
            {
                foreach (var algorithm in encryptionAlgorithms)
                {
                    if (results[algorithm].ContainsKey(length) && results[algorithm][length].Count > 0)
                    {
                        double avgEncryptTime = results[algorithm][length].Average(r => r.EncryptTime);
                        double avgDecryptTime = results[algorithm][length].Average(r => r.DecryptTime);
                        double avgEncryptedSize = results[algorithm][length].Average(r => r.EncryptedSize);
                        double inflationRatio = avgEncryptedSize / length;

                        report.AppendLine($"| {length,-12} | {algorithm,-6} | {avgEncryptTime,-14:F2} | {avgDecryptTime,-14:F2} | {avgEncryptedSize,-17:F0} | {inflationRatio,-8:F2} |");
                    }
                }
            }

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "文件名加密性能测试报告.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"文件名加密性能报告已保存至: {reportPath}");
            Assert.True(File.Exists(reportPath));
        }

        private static string GenerateRandomString(int length)
        {
            Random random = new Random(42); // 使用固定种子以确保结果可复现
            StringBuilder stringBuilder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char c = (char)random.Next('a', 'z' + 1);  // 生成随机小写字母
                stringBuilder.Append(c);
            }
            return stringBuilder.ToString();
        }
    }

    /// <summary>
    /// 比较不同编码方式对加密和压缩数据的表示效率
    /// </summary>
    public class EncodingEfficiencyTests
    {
        [Fact]
        public void TestDataEncodingEfficiency()
        {
            // 测试不同长度的字符串
            int[] lengths = { 10, 20, 50, 100, 200 };
            int iterations = 5;

            // 定义编码方式
            var encodings = new Dictionary<string, Func<byte[], string>>
            {
                ["Base64"] = data => Convert.ToBase64String(data),
                ["Hex"] = data => BitConverter.ToString(data).Replace("-", "").ToLowerInvariant(),
                //["Base85"] = data => Base85.Ascii85.Encode(data), // 需要添加相应的库
                ["SafeBase64"] = data => ToSafeBase64(data)
            };

            // 收集结果
            var results = new Dictionary<int, Dictionary<string, (double OriginalSize, double EncodedSize, double Ratio)>>();

            foreach (int length in lengths)
            {
                results[length] = new Dictionary<string, (double, double, double)>();

                for (int i = 0; i < iterations; i++)
                {
                    // 生成随机文件名
                    string randomText = GenerateRandomString(length);
                    byte[] originalBytes = Encoding.UTF8.GetBytes(randomText);

                    // 压缩文件名
                    byte[] compressedBytes = CompressionHelper.Compress(originalBytes, "LZ4");

                    // 加密文件名
                    byte[] encryptedBytes = CompressionHelper.Compress(originalBytes, null, "AES256-GCM", "test-key");

                    // 压缩并加密
                    byte[] compressedEncryptedBytes = CompressionHelper.Compress(originalBytes, "LZ4", "AES256-GCM", "test-key");

                    // 测试各种编码的效率
                    TestEncodingEfficiency(encodings, "原始", originalBytes, length, results);
                    TestEncodingEfficiency(encodings, "压缩", compressedBytes, length, results);
                    TestEncodingEfficiency(encodings, "加密", encryptedBytes, length, results);
                    TestEncodingEfficiency(encodings, "压缩加密", compressedEncryptedBytes, length, results);
                }
            }

            // 生成报告
            StringBuilder report = new StringBuilder();
            report.AppendLine("# 数据编码效率测试报告");
            report.AppendLine();
            report.AppendLine("## 测试环境");
            report.AppendLine($"- 操作系统: {RuntimeInformation.OSDescription}");
            report.AppendLine($"- .NET 版本: {Environment.Version}");
            report.AppendLine($"- 测试时间: {DateTime.Now}");
            report.AppendLine();

            foreach (int length in lengths)
            {
                report.AppendLine($"## 原始长度: {length} 字符");
                report.AppendLine();
                report.AppendLine("| 数据类型 | 编码方式 | 编码前大小 (字节) | 编码后大小 (字符) | 膨胀比率 |");
                report.AppendLine("|----------|----------|-------------------|-------------------|----------|");

                foreach (var dataType in new[] { "原始", "压缩", "加密", "压缩加密" })
                {
                    foreach (var encoding in encodings.Keys)
                    {
                        string key = $"{dataType}_{encoding}";
                        if (results[length].ContainsKey(key))
                        {
                            var (originalSize, encodedSize, ratio) = results[length][key];
                            report.AppendLine($"| {dataType,-8} | {encoding,-8} | {originalSize,-19:F0} | {encodedSize,-19:F0} | {ratio,-8:F2} |");
                        }
                    }
                }

                report.AppendLine();
            }

            // 保存报告
            string reportPath = Path.Combine(Directory.GetCurrentDirectory(), "数据编码效率测试报告.md");
            File.WriteAllText(reportPath, report.ToString(), Encoding.UTF8);

            Console.WriteLine($"数据编码效率报告已保存至: {reportPath}");
            Assert.True(File.Exists(reportPath));
        }

        private void TestEncodingEfficiency(
            Dictionary<string, Func<byte[], string>> encodings,
            string dataType,
            byte[] data,
            int originalLength,
            Dictionary<int, Dictionary<string, (double, double, double)>> results)
        {
            foreach (var encoding in encodings)
            {
                string encodedString = encoding.Value(data);
                string key = $"{dataType}_{encoding.Key}";

                double originalSize = data.Length;
                double encodedSize = encodedString.Length;
                double ratio = encodedSize / originalSize;

                // 更新或添加结果
                if (!results[originalLength].ContainsKey(key))
                {
                    results[originalLength][key] = (originalSize, encodedSize, ratio);
                }
                else
                {
                    var (oldOriginalSize, oldEncodedSize, oldRatio) = results[originalLength][key];
                    // 取平均值
                    results[originalLength][key] = (
                        (oldOriginalSize + originalSize) / 2,
                        (oldEncodedSize + encodedSize) / 2,
                        (oldRatio + ratio) / 2
                    );
                }

                Console.WriteLine($"长度: {originalLength}, 类型: {dataType}, 编码: {encoding.Key}, " +
                                $"原始大小: {originalSize} 字节, 编码后: {encodedSize} 字符, 比率: {ratio:F2}");
            }
        }

        private static string ToSafeBase64(byte[] data)
        {
            return Convert.ToBase64String(data)
                .Replace('+', '-')
                .Replace('/', '_')
                .TrimEnd('=');
        }

        private static string GenerateRandomString(int length)
        {
            Random random = new Random(42);
            StringBuilder sb = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                sb.Append((char)random.Next('a', 'z' + 1));
            }
            return sb.ToString();
        }
    }
}
