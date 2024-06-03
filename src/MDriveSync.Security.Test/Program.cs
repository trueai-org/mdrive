
using System.Diagnostics;
using System.Security.Cryptography;
using static System.Net.Mime.MediaTypeNames;

namespace MDriveSync.Security.Test
{
    internal class Program
    {
        static void Main(string[] args)
        {
            //Test2();

            //Console.WriteLine("Hello, World!");
            //Console.ReadKey();

            var sw = new Stopwatch();
            sw.Start();
            Testfile.Run();
            sw.Stop();

            Console.WriteLine($"用时：{sw.ElapsedMilliseconds}ms");
            Console.WriteLine("Hello, World!");
            Console.ReadKey();
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
