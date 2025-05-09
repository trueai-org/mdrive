using MDriveSync.Security;
using System.Diagnostics;

namespace MDriveSync.Client
{
    internal class Program
    {
        static void Main(string[] args)
        {

            var sw = new Stopwatch();

            //sw.Restart();
            //LocalStorage.RunRestore();
            //sw.Stop();

            //Console.WriteLine($"还原用时：{sw.ElapsedMilliseconds}ms");
            //Console.WriteLine("Hello, World!");
            //Console.ReadKey();
            //return;

            sw.Restart();
            LocalStorage.RunBackup();
            sw.Stop();

            Console.WriteLine($"备份用时：{sw.ElapsedMilliseconds}ms");
            Console.WriteLine("Hello, World!");
            Console.ReadKey();

            Console.WriteLine("Hello, World!");
        }
    }
}
