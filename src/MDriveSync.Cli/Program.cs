using MDriveSync.Core.Services;
using MDriveSync.Infrastructure;
using MDriveSync.Security.Models;
using Serilog;
using System.Text.Json;

namespace MDriveSync.Cli
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // 配置日志
            ConfigureLogging();

            try
            {
                if (args.Length == 0 || args[0] == "--help" || args[0] == "-h")
                {
                    ShowHelp();

                    // sync 参数
                    ShowSyncHelp();
                    Console.WriteLine();

                    // config 参数
                    ShowConfigHelp();
                    Console.WriteLine();

                    // 示例
                    Console.WriteLine("示例:");
                    Console.WriteLine("mdrive sync --source C:\\Source --target D:\\Targetd");

                    return 0;
                }

                string command = args[0].ToLower();

                switch (command)
                {
                    case "sync":
                        return await HandleSyncCommand(args.Skip(1).ToArray());

                    case "config":
                        return HandleConfigCommand(args.Skip(1).ToArray());

                    case "version":
                        ShowVersion();
                        return 0;

                    default:
                        {
                            Log.Error($"未知命令: {command}");
                            ShowHelp();

                            // sync 参数
                            ShowSyncHelp();
                            Console.WriteLine();

                            // config 参数
                            ShowConfigHelp();
                            Console.WriteLine();

                            // 示例
                            Console.WriteLine("示例:");
                            Console.WriteLine("mdrive sync --source C:\\Source --target D:\\Targetd");
                        }
                        return 1;
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "程序执行过程中发生错误");
                return 1;
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// 配置日志系统
        /// </summary>
        private static void ConfigureLogging()
        {
            Log.Logger = new LoggerConfiguration()
                .MinimumLevel.Information()
                .WriteTo.Console()
                .WriteTo.File("logs/mdrive-cli.log", rollingInterval: RollingInterval.Day)
                .CreateLogger();

            AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();
        }

        /// <summary>
        /// 显示帮助信息
        /// </summary>
        private static void ShowHelp()
        {
            var version = "v" + string.Join(".", typeof(Program).Assembly.GetName().Version.ToString().Split('.').Where((a, b) => b <= 2));

            Console.WriteLine($"mdrive - 多平台文件同步工具 {version}");
            Console.WriteLine();
            Console.WriteLine("用法:");
            Console.WriteLine("mdrive [命令] [选项]");
            Console.WriteLine();
            Console.WriteLine("命令:");
            Console.WriteLine(" sync     执行文件同步操作");
            Console.WriteLine(" config   管理同步配置文件");
            Console.WriteLine(" version  显示程序版本信息");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine(" --help, -h     显示帮助信息");
            Console.WriteLine();
            Console.WriteLine("使用 'mdrive [命令] --help' 查看特定命令的帮助信息");
            Console.WriteLine();
        }

        /// <summary>
        /// 处理同步命令
        /// </summary>
        private static async Task<int> HandleSyncCommand(string[] args)
        {
            if (args.Contains("--help") || args.Contains("-h"))
            {
                ShowSyncHelp();
                return 0;
            }
            else
            {
                ShowHelp();
            }

            var options = ParseSyncOptions(args);
            if (options == null)
            {
                return 1;
            }

            try
            {
                // 验证同步选项
                if (!Directory.Exists(options.SourcePath))
                {
                    Log.Error($"源目录不存在: {options.SourcePath}");
                    return 1;
                }

                Log.Information($"开始同步操作...");
                Log.Information($"源目录: {options.SourcePath}");
                Log.Information($"目标目录: {options.TargetPath}");
                Log.Information($"同步模式: {options.SyncMode}");
                Log.Information($"比较方法: {options.CompareMethod}");

                if (options.PreviewOnly)
                {
                    Log.Information("预览模式：不会执行实际文件操作");
                }

                // 配置进度报告
                var progress = new Progress<SyncProgress>(progress =>
                {
                    if (options.Verbose || progress.ProgressPercentage == 100 || progress.ProgressPercentage % 10 == 0)
                    {
                        if (progress.ProgressPercentage < 0)
                        {
                            Log.Information($"{progress.Message}");
                        }
                        else
                        {
                            Log.Information($"{progress.Message} - {progress.ProgressPercentage}% - {progress.FormattedSpeed} - 剩余时间: {progress.FormattedTimeRemaining}");
                        }
                    }
                });

                // 执行同步
                var syncHelper = new FileSyncHelper(options, progress);
                var result = await syncHelper.SyncAsync();

                // 显示结果
                Log.Information($"同步操作完成，状态: {result.Status}");
                Log.Information($"总耗时: {result.ElapsedTime.TotalSeconds:F2} 秒");

                if (result.Statistics != null)
                {
                    Log.Information($"文件复制: {result.Statistics.FilesCopied} 个");
                    Log.Information($"文件更新: {result.Statistics.FilesUpdated} 个");
                    Log.Information($"文件删除: {result.Statistics.FilesDeleted} 个");
                    Log.Information($"文件跳过: {result.Statistics.FilesSkipped} 个");
                    Log.Information($"目录创建: {result.Statistics.DirectoriesCreated} 个");
                    Log.Information($"目录删除: {result.Statistics.DirectoriesDeleted} 个");
                    Log.Information($"错误数量: {result.Statistics.Errors} 个");
                    Log.Information($"处理总量: {result.Statistics.BytesProcessed.FormatSize()}");
                }

                return result.Status == ESyncStatus.Completed ? 0 : 1;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "同步操作失败");
                return 1;
            }
        }

        /// <summary>
        /// 显示同步命令帮助
        /// </summary>
        private static void ShowSyncHelp()
        {
            Console.WriteLine("执行文件同步操作");
            Console.WriteLine();
            Console.WriteLine("用法:");
            Console.WriteLine("  mdrive sync [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  --source, -s           源目录路径 (必需)");
            Console.WriteLine("  --target, -t           目标目录路径 (必需)");
            Console.WriteLine("  --mode, -m             同步模式: OneWay(单向), Mirror(镜像), TwoWay(双向) (默认: OneWay)");
            Console.WriteLine("  --compare, -c          比较方法: Size(大小), DateTime(修改时间), DateTimeAndSize(时间和大小), Content(内容), Hash(哈希) (默认: DateTimeAndSize)");
            Console.WriteLine("  --hash, -h             哈希算法: MD5, SHA1, SHA256(默认), SHA3, SHA384, SHA512, BLAKE3, XXH3, XXH128");
            Console.WriteLine("  --config, -f           配置文件路径, 示例: -f sync.json");
            Console.WriteLine("  --exclude, -e          排除的文件或目录模式 (支持通配符，可多次指定)");
            Console.WriteLine("  --preview, -p          预览模式，不实际执行操作 (默认: false)");
            Console.WriteLine("  --verbose, -v          显示详细日志信息 (默认: false)");
            Console.WriteLine("  --threads, -j          并行操作的最大线程数 (默认: CPU核心数)");
            Console.WriteLine("  --recycle-bin, -r      使用回收站代替直接删除文件 (默认: true)");
            Console.WriteLine("  --preserve-time        保留原始文件时间 (默认: true)");
            Console.WriteLine("  --help                 显示帮助信息");
        }

        /// <summary>
        /// 解析同步命令参数
        /// </summary>
        private static SyncOptions ParseSyncOptions(string[] args)
        {
            var options = new SyncOptions
            {
                SyncMode = ESyncMode.OneWay,
                CompareMethod = ESyncCompareMethod.DateTimeAndSize,
                MaxParallelOperations = Math.Max(1, Environment.ProcessorCount),
                PreviewOnly = false,
                UseRecycleBin = true,
                PreserveFileTime = true,
                Verbose = false
            };

            string configFilePath = null;
            var excludePatterns = new List<string>();
            bool configLoaded = false;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = (i + 1 < args.Length) ? args[i + 1] : null;

                if (value != null && value.StartsWith("-"))
                {
                    value = null;
                }
                else if (value != null)
                {
                    i++;
                }

                switch (arg)
                {
                    case "--source":
                    case "-s":
                        if (value != null) options.SourcePath = value;
                        break;

                    case "--target":
                    case "-t":
                        if (value != null) options.TargetPath = value;
                        break;

                    case "--mode":
                    case "-m":
                        if (value != null && Enum.TryParse<ESyncMode>(value, true, out var mode))
                            options.SyncMode = mode;
                        break;

                    case "--compare":
                    case "-c":
                        if (value != null && Enum.TryParse<ESyncCompareMethod>(value, true, out var compare))
                            options.CompareMethod = compare;
                        break;

                    case "--hash":
                    case "-h":
                        if (value != null && Enum.TryParse<EHashType>(value, true, out var hash))
                            options.HashAlgorithm = hash;
                        break;

                    case "--config":
                    case "-f":
                        if (value != null)
                        {
                            configFilePath = value;
                            // 加载配置文件
                            if (File.Exists(configFilePath))
                            {
                                try
                                {
                                    var loadedOptions = FileSyncHelper.LoadFromJsonFile(configFilePath);
                                    options = loadedOptions;
                                    configLoaded = true;
                                    Log.Information($"从配置文件加载选项: {configFilePath}");
                                }
                                catch (Exception ex)
                                {
                                    Log.Error(ex, $"无法加载配置文件: {configFilePath}");
                                    return null;
                                }
                            }
                            else
                            {
                                Log.Error($"配置文件不存在: {configFilePath}");
                                return null;
                            }
                        }
                        break;

                    case "--exclude":
                    case "-e":
                        if (value != null)
                            excludePatterns.Add(value);
                        break;

                    case "--preview":
                    case "-p":
                        options.PreviewOnly = true;
                        break;

                    case "--verbose":
                    case "-v":
                        options.Verbose = true;
                        break;

                    case "--threads":
                    case "-j":
                        if (value != null && int.TryParse(value, out int threads) && threads > 0)
                            options.MaxParallelOperations = threads;
                        break;

                    case "--recycle-bin":
                    case "-r":
                        if (value != null && bool.TryParse(value, out bool useRecycle))
                            options.UseRecycleBin = useRecycle;
                        else
                            options.UseRecycleBin = true;
                        break;

                    case "--preserve-time":
                        if (value != null && bool.TryParse(value, out bool preserveTime))
                            options.PreserveFileTime = preserveTime;
                        else
                            options.PreserveFileTime = true;
                        break;
                }
            }

            // 如果有排除模式，则设置到选项中
            if (excludePatterns.Count > 0)
            {
                options.IgnorePatterns = excludePatterns.ToArray();
            }

            // 验证必需参数
            bool isValid = true;

            if (string.IsNullOrEmpty(options.SourcePath))
            {
                Log.Error("必须指定源目录 (--source, -s)");
                isValid = false;
            }

            if (string.IsNullOrEmpty(options.TargetPath))
            {
                Log.Error("必须指定目标目录 (--target, -t)");
                isValid = false;
            }

            return isValid ? options : null;
        }

        /// <summary>
        /// 处理配置命令
        /// </summary>
        private static int HandleConfigCommand(string[] args)
        {
            if (args.Length == 0 || args.Contains("--help") || args.Contains("-h"))
            {
                ShowConfigHelp();
                return 0;
            }

            string subCommand = args[0].ToLower();

            switch (subCommand)
            {
                case "create":
                    return HandleConfigCreateCommand(args.Skip(1).ToArray());

                case "view":
                    return HandleConfigViewCommand(args.Skip(1).ToArray());

                default:
                    Log.Error($"未知的配置子命令: {subCommand}");
                    ShowConfigHelp();
                    return 1;
            }
        }

        /// <summary>
        /// 显示配置命令帮助
        /// </summary>
        private static void ShowConfigHelp()
        {
            Console.WriteLine("管理同步配置文件");
            Console.WriteLine();
            Console.WriteLine("用法:");
            Console.WriteLine("  mdrivesync config [子命令] [选项]");
            Console.WriteLine();
            Console.WriteLine("子命令:");
            Console.WriteLine("  create    创建新的配置文件");
            Console.WriteLine("  view      查看现有配置文件内容");
            Console.WriteLine();
            Console.WriteLine("使用 'mdrivesync config [子命令] --help' 查看特定子命令的帮助信息");
        }

        /// <summary>
        /// 处理配置创建命令
        /// </summary>
        private static int HandleConfigCreateCommand(string[] args)
        {
            if (args.Contains("--help") || args.Contains("-h"))
            {
                ShowConfigCreateHelp();
                return 0;
            }

            string outputPath = null;
            string sourcePath = null;
            string targetPath = null;
            ESyncMode mode = ESyncMode.OneWay;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = (i + 1 < args.Length) ? args[i + 1] : null;

                if (value != null && value.StartsWith("-"))
                {
                    value = null;
                }
                else if (value != null)
                {
                    i++;
                }

                switch (arg)
                {
                    case "--output":
                    case "-o":
                        if (value != null) outputPath = value;
                        break;

                    case "--source":
                    case "-s":
                        if (value != null) sourcePath = value;
                        break;

                    case "--target":
                    case "-t":
                        if (value != null) targetPath = value;
                        break;

                    case "--mode":
                    case "-m":
                        if (value != null && Enum.TryParse<ESyncMode>(value, true, out var syncMode))
                            mode = syncMode;
                        break;
                }
            }

            // 验证必需参数
            bool isValid = true;

            if (string.IsNullOrEmpty(outputPath))
            {
                Log.Error("必须指定输出文件路径 (--output, -o)");
                isValid = false;
            }

            if (string.IsNullOrEmpty(sourcePath))
            {
                Log.Error("必须指定源目录路径 (--source, -s)");
                isValid = false;
            }

            if (string.IsNullOrEmpty(targetPath))
            {
                Log.Error("必须指定目标目录路径 (--target, -t)");
                isValid = false;
            }

            if (!isValid)
            {
                ShowConfigCreateHelp();
                return 1;
            }

            try
            {
                CreateConfigFile(new FileInfo(outputPath), new DirectoryInfo(sourcePath), new DirectoryInfo(targetPath), mode);
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "创建配置文件失败");
                return 1;
            }
        }

        /// <summary>
        /// 显示配置创建命令帮助
        /// </summary>
        private static void ShowConfigCreateHelp()
        {
            Console.WriteLine("创建新的配置文件");
            Console.WriteLine();
            Console.WriteLine("用法:");
            Console.WriteLine("  mdrivesync config create [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  --output, -o    输出文件路径 (必需)");
            Console.WriteLine("  --source, -s    源目录路径 (必需)");
            Console.WriteLine("  --target, -t    目标目录路径 (必需)");
            Console.WriteLine("  --mode, -m      同步模式: OneWay(单向), Mirror(镜像), TwoWay(双向) (默认: OneWay)");
            Console.WriteLine("  --help, -h      显示帮助信息");
        }

        /// <summary>
        /// 处理配置查看命令
        /// </summary>
        private static int HandleConfigViewCommand(string[] args)
        {
            if (args.Contains("--help") || args.Contains("-h"))
            {
                ShowConfigViewHelp();
                return 0;
            }

            string filePath = null;

            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i];
                string value = (i + 1 < args.Length) ? args[i + 1] : null;

                if (value != null && value.StartsWith("-"))
                {
                    value = null;
                }
                else if (value != null)
                {
                    i++;
                }

                switch (arg)
                {
                    case "--file":
                    case "-f":
                        if (value != null) filePath = value;
                        break;
                }
            }

            if (string.IsNullOrEmpty(filePath))
            {
                Log.Error("必须指定配置文件路径 (--file, -f)");
                ShowConfigViewHelp();
                return 1;
            }

            try
            {
                ViewConfigFile(new FileInfo(filePath));
                return 0;
            }
            catch (Exception ex)
            {
                Log.Error(ex, "查看配置文件失败");
                return 1;
            }
        }

        /// <summary>
        /// 显示配置查看命令帮助
        /// </summary>
        private static void ShowConfigViewHelp()
        {
            Console.WriteLine("查看现有配置文件内容");
            Console.WriteLine();
            Console.WriteLine("用法:");
            Console.WriteLine("  mdrivesync config view [选项]");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  --file, -f      配置文件路径 (必需)");
            Console.WriteLine("  --help, -h      显示帮助信息");
        }

        /// <summary>
        /// 创建配置文件
        /// </summary>
        private static void CreateConfigFile(FileInfo output, DirectoryInfo source, DirectoryInfo target, ESyncMode mode)
        {
            try
            {
                // 创建基本配置
                var options = new SyncOptions
                {
                    SourcePath = source.FullName,
                    TargetPath = target.FullName,
                    SyncMode = mode,
                    CompareMethod = ESyncCompareMethod.DateTimeAndSize,

                    MaxParallelOperations = Math.Max(1, Environment.ProcessorCount),
                    PreviewOnly = false,
                    UseRecycleBin = true,
                    PreserveFileTime = true,
                    IgnorePatterns = new string[]
                    {
                        "**/System Volume Information/**",
                        "**/$RECYCLE.BIN/**",
                        "**/Thumbs.db",
                        "**/*.tmp",
                        "**/*.temp",
                        "**/*.bak"
                    }
                };

                // 确保目录存在
                Directory.CreateDirectory(Path.GetDirectoryName(output.FullName));

                // 保存到文件
                FileSyncHelper.SaveToJsonFile(options, output.FullName);
                Log.Information($"配置文件已创建: {output.FullName}");
            }
            catch (Exception ex)
            {
                Log.Error(ex, "创建配置文件失败");
                throw;
            }
        }

        /// <summary>
        /// 查看配置文件内容
        /// </summary>
        private static void ViewConfigFile(FileInfo file)
        {
            try
            {
                if (!file.Exists)
                {
                    Log.Error($"配置文件不存在: {file.FullName}");
                    throw new FileNotFoundException($"配置文件不存在: {file.FullName}", file.FullName);
                }

                // 加载配置
                var options = FileSyncHelper.LoadFromJsonFile(file.FullName);

                // 格式化输出
                var json = JsonSerializer.Serialize(options, new JsonSerializerOptions { WriteIndented = true });
                Console.WriteLine(json);
            }
            catch (Exception ex)
            {
                Log.Error(ex, "查看配置文件失败");
                throw;
            }
        }

        /// <summary>
        /// 显示版本信息
        /// </summary>
        private static void ShowVersion()
        {
            var version = System.Reflection.Assembly.GetExecutingAssembly().GetName().Version;
            Console.WriteLine($"MDriveSync CLI 版本: {version}");
        }
    }
}