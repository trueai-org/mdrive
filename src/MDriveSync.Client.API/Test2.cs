using System.Collections.Concurrent;
using System.Diagnostics;

internal class Program22
{
    private static void Main2()
    {
        //var sw = new Stopwatch();
        //sw.Start();

        //var searcher = new FastFileSearcher334();
        //searcher.Search("E:\\_backups"); // 替换为你的起始路径
        ////searcher.DisplayResults();

        //sw.Stop();

        //Console.WriteLine($"end. {FastFileSearcher334.fileInfoBag.Count}, {sw.ElapsedMilliseconds}ms");

        ////sw.Restart();
        ////var scanner2 = new EfficientFileScanner();
        ////scanner2.ScanDirectory("E:\\_backups"); // 替换为你的起始路径
        ////scanner2.DisplayResults();

        ////// 注意，针对快捷方式，可能会出现问题

        ////Console.WriteLine($"end. {EfficientFileScanner.fileInfos.Count}, {sw.ElapsedMilliseconds}ms");

        //Console.ReadKey();
    }

    internal class FileSearcher2
    {
        public static ConcurrentBag<string> files = new ConcurrentBag<string>();

        public void Search(string rootPath)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = 10 }; // 最多10个并发线程

            try
            {
                // 使用Parallel.ForEach来处理所有目录
                Parallel.ForEach(Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories), options, directory =>
                {
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        files.Add(file);
                    }
                });

                // 处理根目录中的文件
                foreach (var file in Directory.GetFiles(rootPath))
                {
                    files.Add(file);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("Access Denied: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }
        }
    }

    internal class EfficientFileScanner
    {
        public static ConcurrentDictionary<string, (long, DateTime)> fileInfos = new ConcurrentDictionary<string, (long, DateTime)>();

        public void ScanDirectory(string directoryPath)
        {
            try
            {
                Parallel.ForEach(Directory.EnumerateDirectories(directoryPath), (subDir) =>
                {
                    ScanDirectory(subDir); // 递归扫描子目录
                });

                foreach (var file in Directory.EnumerateFiles(directoryPath))
                {
                    var fileInfo = new FileInfo(file);
                    fileInfos.TryAdd(file, (fileInfo.Length, fileInfo.LastWriteTime)); // 只存储必要信息
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning directory {directoryPath}: {ex.Message}");
            }
        }
    }

    internal class FileSearcher23
    {
        public static ConcurrentBag<string> files = new ConcurrentBag<string>();

        public void Search(string rootPath)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = 10 }; // 最多10个并发线程

            try
            {
                // 使用Parallel.ForEach来处理所有目录
                Parallel.ForEach(Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories), options, directory =>
                {
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        files.Add(file);
                    }
                });

                // 处理根目录中的文件
                foreach (var file in Directory.GetFiles(rootPath))
                {
                    files.Add(file);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("Access Denied: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }
        }
    }

    internal class EfficientFileScanner4
    {
        public static ConcurrentDictionary<string, (long, DateTime)> fileInfos = new ConcurrentDictionary<string, (long, DateTime)>();

        public void ScanDirectory(string directoryPath)
        {
            try
            {
                Parallel.ForEach(Directory.EnumerateDirectories(directoryPath), (subDir) =>
                {
                    ScanDirectory(subDir); // 递归扫描子目录
                });

                foreach (var file in Directory.EnumerateFiles(directoryPath))
                {
                    var fileInfo = new FileInfo(file);
                    fileInfos.TryAdd(file, (fileInfo.Length, fileInfo.LastWriteTime)); // 只存储必要信息
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning directory {directoryPath}: {ex.Message}");
            }
        }
    }

    private class FileSearcher266
    {
        public static ConcurrentBag<string> files = new ConcurrentBag<string>();

        public void Search(string rootPath)
        {
            var options = new ParallelOptions { MaxDegreeOfParallelism = 10 }; // 最多10个并发线程

            try
            {
                // 使用Parallel.ForEach来处理所有目录
                Parallel.ForEach(Directory.GetDirectories(rootPath, "*", SearchOption.AllDirectories), options, directory =>
                {
                    foreach (var file in Directory.GetFiles(directory))
                    {
                        files.Add(file);
                    }
                });

                // 处理根目录中的文件
                foreach (var file in Directory.GetFiles(rootPath))
                {
                    files.Add(file);
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine("Access Denied: " + ex.Message);
            }
            catch (Exception ex)
            {
                Console.WriteLine("Exception: " + ex.Message);
            }
        }

        public void DisplayFiles()
        {
            //foreach (var file in files)
            //{
            //    //Console.WriteLine(file);
            //}
        }
    }

    private class FastFileSearcher55
    {
        public static ConcurrentBag<(string, long, DateTime)> fileInfoBag = new ConcurrentBag<(string, long, DateTime)>();

        public void Search(string rootPath)
        {
            Console.WriteLine($"Environment.ProcessorCount: {Environment.ProcessorCount}");
            var options = new ParallelOptions { MaxDegreeOfParallelism = 10 };

            Parallel.ForEach(Directory.EnumerateDirectories(rootPath, "*", SearchOption.AllDirectories), options, directory =>
            {
                try
                {
                    foreach (var file in Directory.EnumerateFiles(directory))
                    {
                        try
                        {
                            var fileInfo = new FileInfo(file);
                            fileInfoBag.Add((file, fileInfo.Length, fileInfo.LastWriteTime));
                        }
                        catch (Exception ex)
                        {
                            Console.WriteLine(ex.Message);
                        }
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine(ex.Message);
                }
            });
        }

        public void DisplayResults()
        {
            //foreach (var fileInfo in fileInfoBag)
            //{
            //    //Console.WriteLine($"File: {fileInfo.Item1}, Size: {fileInfo.Item2} bytes, Last Modified: {fileInfo.Item3}");
            //}
        }
    }

    private class EfficientFileScanner44
    {
        public static ConcurrentDictionary<string, (long, DateTime)> fileInfos = new ConcurrentDictionary<string, (long, DateTime)>();

        public void ScanDirectory(string directoryPath)
        {
            try
            {
                Parallel.ForEach(Directory.EnumerateDirectories(directoryPath), (subDir) =>
                {
                    ScanDirectory(subDir); // 递归扫描子目录
                });

                foreach (var file in Directory.EnumerateFiles(directoryPath))
                {
                    var fileInfo = new FileInfo(file);
                    fileInfos.TryAdd(file, (fileInfo.Length, fileInfo.LastWriteTime)); // 只存储必要信息
                }
            }
            catch (UnauthorizedAccessException ex)
            {
                Console.WriteLine($"Access denied: {ex.Message}");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error scanning directory {directoryPath}: {ex.Message}");
            }
        }

        public void DisplayResults()
        {
            //foreach (var fileInfo in fileInfos)
            //{
            //    Console.WriteLine($"File: {fileInfo.Key}, Size: {fileInfo.Value.Item1} bytes, Last Modified: {fileInfo.Value.Item2}");
            //}
        }
    }

    private class filesearch
    {
        private static void Main33(string[] args)
        {
            var sw = new Stopwatch();
            sw.Start();

            var searcher = new FastFileSearcher55();
            searcher.Search("E:\\guanpeng"); // 替换为你的起始路径
            searcher.DisplayResults();

            sw.Stop();

            Console.WriteLine($"end. {FastFileSearcher55.fileInfoBag.Count}, {sw.ElapsedMilliseconds}ms");

            sw.Restart();
            var scanner2 = new EfficientFileScanner44();
            scanner2.ScanDirectory("E:\\guanpeng"); // 替换为你的起始路径
            scanner2.DisplayResults();

            // 注意，针对快捷方式，可能会出现问题

            Console.WriteLine($"end. {EfficientFileScanner44.fileInfos.Count}, {sw.ElapsedMilliseconds}ms");

            Console.ReadKey();
        }
    }
}