using MDriveSync.Core.Services;
using MDriveSync.Security.Models;
using Quartz;
using Serilog;
using System.Text;
using System.Text.Json;

namespace MDriveSync.Cli
{
    internal class Program
    {
        private static async Task<int> Main(string[] args)
        {
            // 配置日志
            ConfigureLogging();

            // 检查是否为开发模式
            bool isDevMode = args.Contains("--dev");
            if (isDevMode)
            {
                // 从参数列表中移除 --dev 参数
                args = args.Where(arg => arg != "--dev").ToArray();
                Log.Information("正在开发模式下运行，程序将保持运行状态等待新命令，按 Ctrl+C 退出");
            }

            int exitCode = 0;
            try
            {
                do
                {
                    if (isDevMode && args.Length == 0)
                    {
                        // 在开发模式下，如果没有参数，显示提示符
                        Console.Write("mdrive> ");
                        string input = Console.ReadLine();
                        if (string.IsNullOrWhiteSpace(input))
                            continue;

                        // 解析输入的命令行
                        args = input.Split(' ').ToArray(); // ParseCommandLine(input).ToArray();
                        if (args.Length == 0)
                            continue;
                    }

                    // 处理参数
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
                    }
                    else
                    {
                        string command = args[0].ToLower();

                        switch (command)
                        {
                            case "sync":
                                exitCode = await HandleSyncCommand(args.Skip(1).ToArray());
                                break;

                            case "config":
                                exitCode = HandleConfigCommand(args.Skip(1).ToArray());
                                break;

                            case "version":
                                ShowVersion();
                                exitCode = 0;
                                break;

                            case "exit":
                            case "quit":
                                if (isDevMode)
                                {
                                    Log.Information("退出程序");
                                    return 0;
                                }
                                goto default;

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
                                    exitCode = 1;
                                }
                                break;
                        }
                    }

                    // 如果不是开发模式，或者是带参数的正常调用，处理完后就退出
                    if (!isDevMode || (isDevMode && args.Length > 0 && args != null))
                    {
                        args = Array.Empty<string>(); // 清空参数，准备下一次输入
                    }

                } while (isDevMode); // 在开发模式下循环执行

                return exitCode;
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
        /// 解析命令行字符串为参数数组
        /// </summary>
        private static IEnumerable<string> ParseCommandLine(string commandLine)
        {
            bool inQuotes = false;
            bool isEscaping = false;
            var arguments = new List<string>();
            var currentArgument = new StringBuilder();

            // 处理空字符串
            if (string.IsNullOrEmpty(commandLine))
                return arguments;

            // 逐字符处理命令行
            for (int i = 0; i < commandLine.Length; i++)
            {
                char c = commandLine[i];

                if (isEscaping)
                {
                    // 转义字符后面的字符直接添加
                    currentArgument.Append(c);
                    isEscaping = false;
                }
                else if (c == '\\')
                {
                    // 检查是否是转义引号的情况
                    if (i + 1 < commandLine.Length && commandLine[i + 1] == '"')
                    {
                        currentArgument.Append('"');
                        i++; // 跳过下一个字符（引号）
                    }
                    else
                    {
                        isEscaping = true;
                    }
                }
                else if (c == '"')
                {
                    // 切换引号状态，但不将引号加入参数
                    inQuotes = !inQuotes;
                }
                else if (c == ' ' && !inQuotes)
                {
                    // 空格且不在引号内，表示一个参数结束
                    if (currentArgument.Length > 0)
                    {
                        arguments.Add(currentArgument.ToString());
                        currentArgument.Clear();
                    }
                }
                else
                {
                    // 普通字符，添加到当前参数
                    currentArgument.Append(c);
                }
            }

            // 添加最后一个参数
            if (currentArgument.Length > 0)
            {
                arguments.Add(currentArgument.ToString());
            }

            // 检查是否有未闭合的引号
            if (inQuotes)
            {
                Log.Warning("命令行中存在未闭合的引号");
            }

            return arguments;
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
            Console.WriteLine("  sync     执行文件同步操作");
            Console.WriteLine("  config   管理同步配置文件");
            Console.WriteLine("  version  显示程序版本信息");
            Console.WriteLine("  exit     退出程序 (仅在开发模式下有效)");
            Console.WriteLine("  quit     退出程序 (仅在开发模式下有效)");
            Console.WriteLine();
            Console.WriteLine("选项:");
            Console.WriteLine("  --help, -h     显示帮助信息");
            Console.WriteLine("  --dev          开发模式，交互式运行程序");
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

                // 如果配置了定时任务，则一直执行
                if (!string.IsNullOrEmpty(options.CronExpression) || options.Interval > 0)
                {
                    Console.ReadKey();
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
            Console.WriteLine("  --source, -s                源目录路径 (必需)");
            Console.WriteLine("  --target, -t                目标目录路径 (必需)");
            Console.WriteLine("  --mode, -m                  同步模式: OneWay(单向), Mirror(镜像), TwoWay(双向) (默认: OneWay)");
            Console.WriteLine("  --compare, -c               比较方法: Size(大小), DateTime(修改时间), DateTimeAndSize(时间和大小), Content(内容), Hash(哈希) (默认: DateTimeAndSize)");
            Console.WriteLine("  --hash                      哈希算法: MD5, SHA1, SHA256(默认), SHA3, SHA384, SHA512, BLAKE3, XXH3, XXH128");
            Console.WriteLine("  --sampling-rate             哈希抽样率 (0.0-1.0之间的小数，默认: 0.1)");
            Console.WriteLine("  --sampling-min-size         参与抽样的最小文件大小 (字节，默认: 1MB)");
            Console.WriteLine("  --date-threshold            修改时间比较阈值 (秒，默认: 0)");
            Console.WriteLine("  --parallel                  是否启用并行文件操作 (默认: true)");
            Console.WriteLine("  --config, -f                配置文件路径, 示例: -f sync.json");
            Console.WriteLine("  --exclude, -e               排除的文件或目录模式 (支持通配符，可多次指定)");
            Console.WriteLine("  --preview, -p               预览模式，不实际执行操作 (默认: false)");
            Console.WriteLine("  --verbose, -v               显示详细日志信息 (默认: false)");
            Console.WriteLine("  --threads, -j               并行操作的最大线程数 (默认: CPU核心数)");
            Console.WriteLine("  --recycle-bin, -r           使用回收站代替直接删除文件 (默认: true)");
            Console.WriteLine("  --preserve-time             保留原始文件时间 (默认: true)");
            Console.WriteLine("  --continue-on-error         发生错误时是否继续执行 (默认: true)");
            Console.WriteLine("  --retry                     操作失败时的最大重试次数 (默认: 3)");
            Console.WriteLine("  --conflict                  冲突解决策略: SourceWins, TargetWins, KeepBoth, Skip, Newer(默认), Older, Larger");
            Console.WriteLine("  --follow-symlinks           是否跟踪符号链接 (默认: false)");
            Console.WriteLine("  --interval, -i              同步间隔, 单位秒");
            Console.WriteLine("  --cron                      Cron表达式，设置后将优先使用Cron表达式进行调度");
            Console.WriteLine("  --execute-immediately, -ei  配置定时执行时，是否立即执行一次同步 (默认: true)");
            Console.WriteLine("  --chunk-size, --chunk       文件同步分块大小（MB），大于0启用分块传输");
            Console.WriteLine("  --sync-last-modified-time   同步完成后是否同步文件的最后修改时间 (默认: true)");
            Console.WriteLine("  --temp-file-suffix          临时文件后缀 (默认: .mdrivetmp)");
            Console.WriteLine("  --verify-after-copy         文件传输完成后验证文件完整性 (默认: true)");
            Console.WriteLine();
            Console.WriteLine("  --help                      显示帮助信息");
            Console.WriteLine();
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
                    // 基本路径选项
                    case "--source":
                    case "-s":
                        if (value != null) options.SourcePath = value;
                        break;

                    case "--target":
                    case "-t":
                        if (value != null) options.TargetPath = value;
                        break;

                    // 同步模式和方法
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

                    // 配置文件加载
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

                    // 哈希相关选项
                    case "--hash":
                        if (value != null && Enum.TryParse<EHashType>(value, true, out var hash))
                            options.HashAlgorithm = hash;
                        break;

                    case "--sampling-rate":
                        if (value != null && double.TryParse(value, out double samplingRate) && samplingRate >= 0.0 && samplingRate <= 1.0)
                            options.SamplingRate = samplingRate;
                        break;

                    case "--sampling-min-size":
                        if (value != null && int.TryParse(value, out int minSize) && minSize > 0)
                            options.SamplingRateMinFileSize = minSize;
                        break;

                    // 时间比较选项
                    case "--date-threshold":
                        if (value != null && int.TryParse(value, out int threshold) && threshold >= 0)
                            options.DateTimeThresholdSeconds = threshold;
                        break;

                    // 并行处理选项
                    case "--parallel":
                        if (value != null && bool.TryParse(value, out bool enableParallel))
                            options.EnableParallelFileOperations = enableParallel;
                        else
                            options.EnableParallelFileOperations = true;
                        break;

                    case "--threads":
                    case "-j":
                        if (value != null && int.TryParse(value, out int threads) && threads > 0)
                            options.MaxParallelOperations = threads;
                        break;

                    // 文件处理方式选项
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

                    // 错误处理选项
                    case "--continue-on-error":
                        if (value != null && bool.TryParse(value, out bool continueOnError))
                            options.ContinueOnError = continueOnError;
                        else
                            options.ContinueOnError = true;
                        break;

                    case "--retry":
                        if (value != null && int.TryParse(value, out int retries) && retries >= 0)
                            options.MaxRetries = retries;
                        break;

                    // 符号链接选项
                    case "--follow-symlinks":
                        if (value != null && bool.TryParse(value, out bool followSymlinks))
                            options.FollowSymlinks = followSymlinks;
                        else
                            options.FollowSymlinks = true;
                        break;

                    // 冲突处理选项
                    case "--conflict":
                        if (value != null && Enum.TryParse<ESyncConflictResolution>(value, true, out var conflict))
                            options.ConflictResolution = conflict;
                        break;

                    // 定时任务选项
                    case "--interval":
                    case "-i":
                        if (value != null && int.TryParse(value, out int interval) && interval > 0)
                            options.Interval = interval;
                        break;

                    case "--cron":
                        if (value != null)
                        {
                            // 验证 Cron 表达式
                            if (!CronExpression.IsValidExpression(value))
                            {
                                Log.Error($"无效的 Cron 表达式: {value}");
                                return null;
                            }
                            options.CronExpression = value;
                        }
                        break;

                    case "--execute-immediately":
                    case "-ei":
                        if (value != null && bool.TryParse(value, out bool executeImmediately))
                            options.ExecuteImmediately = executeImmediately;
                        else
                            options.ExecuteImmediately = true;
                        break;

                    // 文件传输选项
                    case "--chunk-size":
                    case "--chunk":
                        if (value != null && int.TryParse(value, out int chunkSize) && chunkSize > 0)
                            options.ChunkSizeMB = chunkSize;
                        break;

                    case "--sync-last-modified-time":
                        if (value != null && bool.TryParse(value, out bool syncTime))
                            options.SyncLastModifiedTime = syncTime;
                        else
                            options.SyncLastModifiedTime = true;
                        break;

                    case "--temp-file-suffix":
                        if (value != null)
                            options.TempFileSuffix = value;
                        break;

                    case "--verify-after-copy":
                        if (value != null && bool.TryParse(value, out bool verify))
                            options.VerifyAfterCopy = verify;
                        else
                            options.VerifyAfterCopy = true;
                        break;
                }
            }

            // 如果有排除模式，则设置到选项中
            if (excludePatterns.Count > 0)
            {
                options.IgnorePatterns = excludePatterns.ToList();
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
            Console.WriteLine("  mdrive config [子命令] [选项]");
            Console.WriteLine();
            Console.WriteLine("子命令:");
            Console.WriteLine("  create    创建新的配置文件");
            Console.WriteLine("  view      查看现有配置文件内容");
            Console.WriteLine();
            Console.WriteLine("使用 'mdrive config [子命令] --help' 查看特定子命令的帮助信息");
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
            Console.WriteLine("  mdrive config create [选项]");
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
            Console.WriteLine("  mdrive config view [选项]");
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
                    }.ToList()
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