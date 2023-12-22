# WebApplication1


## 注意

> 注意系统日志路径配置，不同操作系统之间的差异。
```json
# window
"path": "logs\\log.txt"

# linux
"path": "logs/log.txt"
```

## todo
APP 启动时，通过接口获取欢迎语，检查版本等

```csharp

// cron 表达式 基于 Quartz 3.8.0
// https://www.bejson.com/othertools/cron/


// 每 5 秒
0/5 * * * * ?

// 每分钟
0 * * * * ?

// 每 5 分钟
0 0/5 * * * ?

// 每 10 分钟
0 0/10 * * * ?

// 每天 9 点
0 0 9 * * ?

// 每天 8 点 10 分
0 10 8 * * ?

```

## 过滤文件夹示例
```
/Recovery/*
/Recovery/
/System Volume Information/
/System Volume Information/*
/Boot/
/Boot/*
/$RECYCLE.BIN/*
/$RECYCLE.BIN/
/bootmgr
/bootTel.dat
/@Recycle/*
/@Recently-Snapshot/*
/@Recycle/
/@Recently-Snapshot/
**/@Recycle/*
**/@Recently-Snapshot/*
**/.@__thumb/*
**/@Transcode/*
**/.obsidian/*
**/.git/*
**/.svn/*
**/node_modules/*
**/bin/Debug/*
**/bin/Release/*
**/logs/*
**/obj/*
**/packages/*
**/.next/*
```


## 测试代码

```

                //using (var scope = _serviceScopeFactory.CreateScope())
                //{
                //    var mediaService = scope.ServiceProvider.GetRequiredService<IMediaService>();
                //    await mediaService.RefreshMetaJob();
                //}

                //_jobs[jobId] = job;
                //var tt = new FastFileSearcher334();
                //tt.Search("E:\\_backups");
                //tt.Test();


        //private void DoWork(object state)
        //{
        //    // 加锁，以防万一重复执行
        //    lock (_lock)
        //    {
        //        // 重新设定定时器，防止在当前工作完成前触发下一次执行
        //        _timer?.Change(Timeout.Infinite, 0);

        //        try
        //        {
        //            _logger.LogInformation("刷新图片、视频大小服务开始工作.");

        //            // 执行刷新图片、视频大小服务
        //            var task = Task.Run(async () => await _mediaService.RefreshMetaJob());
        //            task.Wait();
        //        }
        //        finally
        //        {
        //            // 任务完成后重新启动定时器
        //            _timer?.Change(TimeSpan.FromMinutes(1), TimeSpan.FromMinutes(1));
        //        }
        //    }
        //}
```

```

using System;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Threading.Tasks;

class FileSearcher3
{
    public static ConcurrentBag<string> files = new ConcurrentBag<string>();

    public void Search(string rootPath)
    {
        try
        {
            foreach (var directory in Directory.GetDirectories(rootPath))
            {
                Task.Factory.StartNew(() => Search(directory), TaskCreationOptions.AttachedToParent);
            }

            foreach (var file in Directory.GetFiles(rootPath))
            {
                files.Add(file);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // 处理权限问题
            Console.WriteLine("Access Denied: " + ex.Message);
        }
        catch (Exception ex)
        {
            // 处理其他潜在的异常
            Console.WriteLine("Exception: " + ex.Message);
        }
    }

    public void DisplayFiles()
    {
        foreach (var file in files)
        {
            Console.WriteLine(file);
        }
    }
}

class FileSearcher22
{
    public static ConcurrentBag<string> files = new ConcurrentBag<string>();
    private static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(10); // 最多允许10个并发任务

    public async Task SearchAsync(string rootPath)
    {
        await semaphoreSlim.WaitAsync(); // 等待获取信号量

        try
        {
            string[] directories = Directory.GetDirectories(rootPath);
            string[] filesInCurrentDir = Directory.GetFiles(rootPath);

            foreach (var file in filesInCurrentDir)
            {
                files.Add(file);
            }

            var tasks = new List<Task>();
            foreach (var dir in directories)
            {
                // 限制同时运行的任务数量
                if (semaphoreSlim.CurrentCount == 0)
                {
                    // 等待已启动的任务之一完成
                    await Task.WhenAny(tasks.ToArray());
                    tasks.RemoveAll(t => t.IsCompleted); // 移除已完成的任务
                }

                semaphoreSlim.Wait(); // 同步获取信号量
                var task = SearchAsync(dir);
                tasks.Add(task);

                // 异步释放信号量，允许其他任务继续
                task.ContinueWith(t => semaphoreSlim.Release());
            }

            await Task.WhenAll(tasks); // 等待所有任务完成
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine("Access Denied: " + ex.Message);
        }
        catch (Exception ex)
        {
            Console.WriteLine("Exception: " + ex.Message);
        }
        finally
        {
            semaphoreSlim.Release(); // 释放信号量
        }
    }

    public void DisplayFiles()
    {
        foreach (var file in files)
        {
            Console.WriteLine(file);
        }
    }
}
class FileSearcher21
{
    public static ConcurrentBag<string> files = new ConcurrentBag<string>();
    private static SemaphoreSlim semaphoreSlim = new SemaphoreSlim(10); // 最多10个线程

    public async Task SearchAsync(string rootPath)
    {
        await semaphoreSlim.WaitAsync(); // 等待信号量
        try
        {
            string[] directories = Directory.GetDirectories(rootPath);
            string[] filesInCurrentDir = Directory.GetFiles(rootPath);

            foreach (var file in filesInCurrentDir)
            {
                files.Add(file);
            }

            if (directories.Length > 0)
            {
                var tasks = new Task[directories.Length];
                for (int i = 0; i < directories.Length; i++)
                {
                    string dir = directories[i];
                    tasks[i] = SearchAsync(dir);
                }
                await Task.WhenAll(tasks);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // 处理权限问题
            Console.WriteLine("Access Denied: " + ex.Message);
        }
        catch (Exception ex)
        {
            // 处理其他潜在的异常
            Console.WriteLine("Exception: " + ex.Message);
        }
        finally
        {
            semaphoreSlim.Release(); // 释放信号量
        }
    }

    public void DisplayFiles()
    {
        foreach (var file in files)
        {
            Console.WriteLine(file);
        }
    }
}

class FileSearcher2
{
    public static ConcurrentBag<string> files = new ConcurrentBag<string>();

    public async Task SearchAsync(string rootPath)
    {
        try
        {
            string[] directories = Directory.GetDirectories(rootPath);
            string[] filesInCurrentDir = Directory.GetFiles(rootPath);

            foreach (var file in filesInCurrentDir)
            {
                files.Add(file);
            }

            if (directories.Length > 0)
            {
                var tasks = new Task[directories.Length];
                for (int i = 0; i < directories.Length; i++)
                {
                    string dir = directories[i];
                    tasks[i] = SearchAsync(dir);
                }
                await Task.WhenAll(tasks);
            }
        }
        catch (UnauthorizedAccessException ex)
        {
            // 处理权限问题
            Console.WriteLine("Access Denied: " + ex.Message);
        }
        catch (Exception ex)
        {
            // 处理其他潜在的异常
            Console.WriteLine("Exception: " + ex.Message);
        }
    }

    public void DisplayFiles()
    {
        foreach (var file in files)
        {
            //Console.WriteLine(file);
        }
    }
}


class FileSearcher
{
    public static ConcurrentBag<(string, long, DateTime)> fileInfoBag = new ConcurrentBag<(string, long, DateTime)>();

    private async Task ProcessDirectoryAsync(string directoryPath)
    {
        try
        {
            // 获取目录下的所有文件和子目录
            var fileEntries = Directory.GetFiles(directoryPath);
            var directoryEntries = Directory.GetDirectories(directoryPath);

            foreach (var file in fileEntries)
            {
                var fileInfo = new FileInfo(file);
                fileInfoBag.Add((file, fileInfo.Length, fileInfo.CreationTime));
            }

            // 对于每个子目录，使用Task进行异步处理
            var tasks = new Task[directoryEntries.Length];
            for (int i = 0; i < directoryEntries.Length; i++)
            {
                var subDir = directoryEntries[i];
                tasks[i] = ProcessDirectoryAsync(subDir);
            }

            await Task.WhenAll(tasks);
        }
        catch (UnauthorizedAccessException ex)
        {
            Console.WriteLine($"Access denied to directory {directoryPath}: {ex.Message}");
        }
        catch (Exception ex)
        {
            Console.WriteLine($"Error processing directory {directoryPath}: {ex.Message}");
        }
    }

    public async Task SearchAsync(string rootPath)
    {
        await ProcessDirectoryAsync(rootPath);
    }

    public void DisplayResults()
    {
        foreach (var fileInfo in fileInfoBag)
        {
            //Console.WriteLine($"File: {fileInfo.Item1}, Size: {fileInfo.Item2} bytes, Created: {fileInfo.Item3}");
        }
    }
}

class FileSearcher23
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
        foreach (var file in files)
        {
            //Console.WriteLine(file);
        }
    }
}
class Program
{
    static async Task Main(string[] args)
    {


        //var searcher = new FileSearcher();
        //await searcher.SearchAsync("C:\\your\\path"); // 替换为你的起始路径
        //searcher.DisplayResults();

        var sw = new Stopwatch();
        sw.Start();
        var searcher = new FileSearcher23();
         searcher.Search("E:\\program_files"); // 替换为你的起始路径
        searcher.DisplayFiles();

        //var searcher = new FileSearcher();
        //await searcher.SearchAsync("E:\\program_files"); // 替换为你的起始路径
        //searcher.DisplayResults();
        //sw.Stop();

        Console.WriteLine("end, " + FileSearcher23.files.Count + ", " + sw.ElapsedMilliseconds);
        Console.ReadKey();
    }
}


//using System;
//using System.Collections.Concurrent;
//using System.IO;
//using System.IO.MemoryMappedFiles;
//using System.Threading.Tasks;

//class FileSearcher
//{
//    private ConcurrentDictionary<string, string> fileContents = new ConcurrentDictionary<string, string>();

//    private async Task ProcessFileAsync(string filePath)
//    {
//        try
//        {
//            // 使用内存映射文件读取文件内容
//            using (var mappedFile = MemoryMappedFile.CreateFromFile(filePath))
//            {
//                using (var reader = new StreamReader(mappedFile.CreateViewStream()))
//                {
//                    var content = await reader.ReadToEndAsync();
//                    fileContents.TryAdd(filePath, content);
//                }
//            }
//        }
//        catch (Exception ex)
//        {
//            Console.WriteLine($"Error processing file {filePath}: {ex.Message}");
//        }
//    }

//    public async Task SearchAsync(string rootPath)
//    {
//        var files = Directory.GetFiles(rootPath, "*", SearchOption.AllDirectories);
//        var tasks = new List<Task>();

//        foreach (var file in files)
//        {
//            tasks.Add(ProcessFileAsync(file));
//        }

//        await Task.WhenAll(tasks);
//    }

//    public void DisplayResults()
//    {
//        foreach (var kvp in fileContents)
//        {
//            Console.WriteLine($"{kvp.Key}: {kvp.Value.Length} characters");
//        }
//    }
//}

//class Program
//{
//    static async Task Main(string[] args)
//    {
//        var searcher = new FileSearcher();
//        await searcher.SearchAsync("E:\\_todo\\___guanpeng"); // 替换为你的起始路径
//        searcher.DisplayResults();

//        Console.WriteLine("end");
//        Console.ReadKey();
//    }
//}

```