using MDriveSync.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Quartz.Logging;
using Serilog;
using Serilog.Debugging;
using Serilog.Settings.Configuration;
using System.Windows;

namespace MDriveSync.WPF
{
    /// <summary>
    /// Interaction logic for App.xaml
    /// </summary>
    public partial class App : Application
    {
        private readonly IHost _host;

        // 服务实例
        public static TimedHostedService TimedHostedService { get; private set; }

        // 定义一个静态事件，用于传递消息
        public static event EventHandler<string> MessageReceived;

        // 定义一个方法用来发布消息
        public static void PublishMessage(string message)
        {
            MessageReceived?.Invoke(null, message);
        }

        public App()
        {
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, builder) =>
                {
                    var env = context.HostingEnvironment;

                    // 在 ASP.NET Core 应用中，调用 SetBasePath 通常不是必需的，因为 ASP.NET Core 默认会将应用程序的根目录作为基路径。
                    // 这意味着，当您添加配置文件（如 appsettings.json 或环境特定的配置文件）时，系统默认会从应用程序的根目录开始查找这些文件。

                    // 在以下情况下，您可能需要显式设置基路径：
                    // 1.非标准目录结构：如果您的配置文件不在标准的项目根目录中，或者您有一个复杂的目录结构，您可能需要使用 SetBasePath 来明确指定配置文件的位置。
                    // 2.在不同的上下文中运行：例如，在单元测试或某些类型的应用程序中，当前工作目录可能与项目根目录不同。在这种情况下，使用 SetBasePath 指定正确的路径会是必要的。
                    // 3.跨项目共享配置文件：如果您有多个项目共享同一个配置文件，且该文件位于这些项目的共同根目录中，设置基路径可以指向这个共享位置。

                    // 如果您遵循 ASP.NET Core 的标准约定，即在项目根目录下有 appsettings.json 和针对不同环境的 appsettings.{Environment}.json 文件，那么这些文件将自动根据当前环境被加载。
                    // 在这种情况下，您不需要手动添加代码来加载这些文件。

                    // 在这里添加配置文件
                    var configuration = builder
                    .SetBasePath(AppContext.BaseDirectory)
                    .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)
                    .AddJsonFile($"{ClientSettings.ClientSettingsPath}", optional: true, reloadOnChange: true)
                    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
                    .Build();

                    var loggerConfig = new LoggerConfiguration();

                    if (env.IsDevelopment())
                    {
                        loggerConfig.MinimumLevel.Debug().Enrich.FromLogContext();

                        // 使用 Serilog.Debugging.SelfLog.Enable(Console.Error) 来启用 Serilog 的自我诊断，这将帮助诊断配置问题。
                        SelfLog.Enable(Console.Error);
                    }

                    // 当打包为单个 exe 程序时，使用代码显式配置 Serilog，而不是完全依赖于配置文件。
                    // 这可能导致 Serilog 在尝试自动发现和加载其扩展程序集（如 Sinks）时遇到问题
                    // 显式配置 Serilog：在程序的 Main 方法中，使用代码显式配置 Serilog，而不是完全依赖于配置文件。
                    // 即：要么手动列出 Sinks 或者通过下面这种方式
                    var logOptions = new ConfigurationReaderOptions(
                        typeof(ConsoleLoggerConfigurationExtensions).Assembly);

                    Log.Logger = loggerConfig
                    .ReadFrom.Configuration(configuration, logOptions)
                    // 打包为单个 exe 文件，无法写日志，因此在这里配置写死
                    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                    .CreateLogger();

                    // Quartz Log
                    var loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);
                    LogProvider.SetLogProvider(loggerFactory);
                })
                .ConfigureServices((context, services) =>
                {
                    // 注册服务
                    services.Configure<ClientOptions>(context.Configuration.GetSection("Client"));

                    // 注册后台服务
                    //services.AddHostedService<TimedHostedService>();

                    // 使用单例模式
                    services.AddSingleton<TimedHostedService>();
                    services.AddHostedService(provider => provider.GetRequiredService<TimedHostedService>());

                    //// 注册WPF的主窗体
                    //services.AddSingleton<MainWindow>();
                })
                .ConfigureLogging(logging =>
                {
                    // 这种方式允许您先加载配置，然后基于这些配置来初始化 Serilog。这提供了更大的灵活性。
                    logging.AddSerilog()
                    .AddProvider(new WpfLoggerProvider(WriteToWindow));
                })
                .Build();

            // 确保在应用程序结束时关闭并刷新日志
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();
        }

        private void WriteToWindow(string message)
        {
            // 这里实现将消息写入WPF窗口的逻辑

            // 假设你有一个名为 LogMessages 的 ObservableCollection<string>
            // 确保在UI线程上执行更新
            Current.Dispatcher.Invoke(() =>
            {
                PublishMessage(message);

                // 从某个地方发布消息
                //App.PublishMessage("这是一条新消息");
            });
        }

        protected override async void OnStartup(StartupEventArgs e)
        {
            await _host.StartAsync();
            TimedHostedService = _host.Services.GetRequiredService<TimedHostedService>();

            //var mainWindow = _host.Services.GetRequiredService<MainWindow>();
            //mainWindow.Show();
        }

        protected override async void OnExit(ExitEventArgs e)
        {
            using (_host)
            {
                await _host.StopAsync();
            }

            base.OnExit(e);
        }
    }
}