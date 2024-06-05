using EasyCompressor;
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;

namespace MDriveSync.Security.Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            var sw = new Stopwatch();

            for (int i = 0; i < 120; i++)
            {
                // 生成随机的最大长度文件名（255字符）和扩展名（例如：.txt）
                // 最大200长度
                string randomFileName = GenerateRandomString(i + 1) + ".xfdsdf";  // 保留5字符用于扩展名

                // 生成随机的最大长度路径（例如Windows的4096字符或更长）
                string randomPath = GenerateRandomPath(4096);  // 路径最大长度，可以调整为更长以测试长路径支持

                var x1 = Encoding.UTF8.GetBytes(randomFileName);

                //var x2 = DeflateCompressor.Shared.Compress(x1);


                var bytes = CompressionHelper.Compress(x1, "LZ4", "ChaCha20-Poly1305", "12342342SDFSDFAS");

                var b2 = CompressionHelper.Compress(bytes, "LZ4");
                var b3 = CompressionHelper.Decompress(b2, "LZ4");
                var b4 = Convert.ToBase64String(b2);

                // bytes 转为转为最小长度的字符串
                // 转为 base64 字符串
                var base64 = Convert.ToBase64String(bytes);

                //var bbb = Base16384.ConvertFromUtf16BEBytesToString(bytes).ToString();
                //var resul333t = Base85.Ascii85.Encode(bytes);

                // 适合压缩小数据，其他算法更不合适
                // BrotliCompressor
                // DeflateCompressor

                var b5 = ZstdSharpCompressor.Shared.Compress(bytes);
                var b6 = SnappierCompressor.Shared.Compress(bytes);
                var b7 = LZ4Compressor.Shared.Compress(bytes); // 最小，但仍然比base64大+1~2
                var b8 = LZMACompressor.Shared.Compress(bytes);
                var b9 = DeflateCompressor.Shared.Compress(bytes);
                var b10 = BrotliCompressor.Shared.Compress(bytes);
                //var b10base64 = Convert.ToBase64String(b10);


                //var hex = BitConverter.ToString(bytes).Replace("-", "");

                var result = CompressionHelper.Decompress(bytes, "LZ4", "ChaCha20-Poly1305", "12342342SDFSDFAS");
                var str = Encoding.UTF8.GetString(result);

                //Console.WriteLine($"压缩后: {bytes.Length}, base64: {base64.Length}, cbase64: {b10base64.Length}");

                // 控制台显示b5~b10 长度
                Console.WriteLine($"byte: {bytes.Length}, b5: {b5.Length}, b6: {b6.Length}, b7: {b7.Length}, b8: {b8.Length}, b9: {b9.Length}, b10: {b10.Length}, base64: {base64.Length}, {str}");

                Thread.Sleep(1);
            }

            Console.WriteLine("Hello, World!");
            Console.ReadKey();
            return;


            sw.Restart();
            Testfile.RunRestore();
            sw.Stop();

            Console.WriteLine($"还原用时：{sw.ElapsedMilliseconds}ms");
            Console.WriteLine("Hello, World!");
            Console.ReadKey();
            return;

            sw.Restart();
            Testfile.RunBackup();
            sw.Stop();

            Console.WriteLine($"备份用时：{sw.ElapsedMilliseconds}ms");
            Console.WriteLine("Hello, World!");
            Console.ReadKey();


            //Test2();

            //Console.WriteLine("Hello, World!");
            //Console.ReadKey();
        }

        static string GenerateRandomPath(int maxLength)
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

        static Random random = new Random();
        static string GenerateRandomString(int length)
        {

            StringBuilder stringBuilder = new StringBuilder(length);
            for (int i = 0; i < length; i++)
            {
                char c = (char)random.Next('a', 'z' + 1);  // 生成随机小写字母
                stringBuilder.Append(c);
            }
            return stringBuilder.ToString();
        }


        /// <summary>
        /// 测试加密时间
        /// </summary>
        static void Test2()
        {
            int[] sizes = { 10 * 1024 * 1024, 20 * 1024 * 1024, 30 * 1024 * 1024, 100 * 1024 * 1024 };
            int iterations = 10;

            foreach (int size in sizes)
            {
                byte[] data = new byte[size];
                new Random().NextBytes(data);

                TimeSpan sha256Time = ComputeSha256(data, iterations);
                TimeSpan sha1Time = ComputeSha1(data, iterations);

                Console.WriteLine($"Data size: {size / (1024 * 1024)} MB");
                Console.WriteLine($"SHA-256 Time: {sha256Time.TotalMilliseconds} ms");
                Console.WriteLine($"SHA-1 Time: {sha1Time.TotalMilliseconds} ms");
                Console.WriteLine();
            }
        }



        public static TimeSpan ComputeSha1(byte[] data, int iterations)
        {
            Stopwatch stopwatch = new Stopwatch();
            using (var sha = SHA1.Create())
            {
                stopwatch.Start();
                for (int i = 0; i < iterations; i++)
                {
                    sha.ComputeHash(data);
                }
                stopwatch.Stop();
            }
            return stopwatch.Elapsed;
        }

        public static TimeSpan ComputeSha256(byte[] data, int iterations)
        {
            Stopwatch stopwatch = new Stopwatch();
            using (var sha = SHA256.Create())
            {
                stopwatch.Start();
                for (int i = 0; i < iterations; i++)
                {
                    sha.ComputeHash(data);
                }
                stopwatch.Stop();
            }
            return stopwatch.Elapsed;
        }
    }
}
