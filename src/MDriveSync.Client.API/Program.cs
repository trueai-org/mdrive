using MDriveSync.Core;
using MDriveSync.Core.BaseAuth;
using MDriveSync.Core.Dashboard;
using MDriveSync.Core.Filters;
using MDriveSync.Core.Middlewares;
using Microsoft.AspNetCore.Mvc;
using Quartz.Logging;
using Serilog;
using Serilog.Debugging;
using Serilog.Settings.Configuration;

namespace MDriveSync.Client.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var env = builder.Environment;

            // 添加配置文件
            var configuration = builder.Configuration
                .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)
                // 环境变量中的
                .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true)
                // 加载默认的
                .AddJsonFile($"{ClientSettings.ClientSettingsPath}", optional: true, reloadOnChange: true);

            //// 添加自定义配置文件
            //builder.Configuration.AddJsonFile($"{ClientSettings.ClientSettingsPath}", optional: true, reloadOnChange: true);

            // 当打包为单个 exe 程序时，使用代码显式配置 Serilog，而不是完全依赖于配置文件。
            // 这可能导致 Serilog 在尝试自动发现和加载其扩展程序集（如 Sinks）时遇到问题
            // 显式配置 Serilog：在程序的 Main 方法中，使用代码显式配置 Serilog，而不是完全依赖于配置文件。
            // 即：要么手动列出 Sinks 或者通过下面这种方式
            var logOptions = new ConfigurationReaderOptions(
                typeof(ConsoleLoggerConfigurationExtensions).Assembly);

            // 配置 Serilog
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration, logOptions)

                // 打包为单个 exe 文件，无法写日志，因此在这里配置写死
                .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day);

            if (env.IsDevelopment())
            {
                logger.MinimumLevel.Debug()
                      .Enrich.FromLogContext()
                      .WriteTo.Console();

                // 使用 Serilog.Debugging.SelfLog.Enable(Console.Error) 来启用 Serilog 的自我诊断，这将帮助诊断配置问题。
                SelfLog.Enable(Console.Error);
            }

            Log.Logger = logger.CreateLogger();

            // Quartz Log
            var loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);
            LogProvider.SetLogProvider(loggerFactory);

            // 确保在应用程序结束时关闭并刷新日志
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();

            try
            {
                Log.Information($"Current: {Directory.GetCurrentDirectory()}");

                // 使用 Serilog
                builder.Host.UseSerilog();

                // 作业客户端配置
                builder.Services.Configure<ClientOptions>(builder.Configuration.GetSection("Client"));

                // API 视图模型验证 400 错误处理
                builder.Services.Configure<ApiBehaviorOptions>(options =>
                {
                    options.SuppressModelStateInvalidFilter = true;
                });

                // API 异常过滤器
                // API 方法/模型过滤器
                builder.Services.AddControllers(options =>
                {
                    options.Filters.Add<CustomLogicExceptionFilterAttribute>();
                    options.Filters.Add<CustomActionFilterAttribute>();
                });

                // 后台服务
                //builder.Services.AddHostedService<TimedHostedService>();

                // 使用单例模式
                builder.Services.AddSingleton<TimedHostedService>();
                builder.Services.AddHostedService(provider => provider.GetRequiredService<TimedHostedService>());

                var app = builder.Build();

                //// 区分 API 和静态/客户端路由
                //app.Use(async (context, next) =>
                //{
                //    if (context.Request.Path.StartsWithSegments("/api"))
                //    {
                //        // 如果是 API 请求，继续执行后续中间件
                //        await next();
                //    }
                //    //else
                //    //{
                //    //    app.MapFallbackToFile("index.html");

                //    //    //// 非 API 请求，尝试作为静态文件处理
                //    //    //await next();

                //    //    //// 不是 API 请求，重写请求到 index.html
                //    //    //if (!Path.HasExtension(context.Request.Path.Value))
                //    //    //{
                //    //    //    context.Request.Path = "/index.html";
                //    //    //    await next();
                //    //    //}
                //    //}
                //});

                app.UseStaticFiles();

                app.UseCors(builder =>
                {
                    builder.AllowAnyMethod().AllowAnyHeader().SetIsOriginAllowed(origin => true).AllowCredentials();
                });

                // 添加基础认证
                // 从环境变量获取配置，如果 appsettings.json 中的配置为空
                var userFromConfig = builder.Configuration["BasicAuth:User"];
                var passwordFromConfig = builder.Configuration["BasicAuth:Password"];
                var user = string.IsNullOrEmpty(userFromConfig) ?
                               Environment.GetEnvironmentVariable("BASIC_AUTH_USER") : userFromConfig;
                var password = string.IsNullOrEmpty(passwordFromConfig) ?
                                  Environment.GetEnvironmentVariable("BASIC_AUTH_PASSWORD") : passwordFromConfig;

                // 如果账号和密码
                if (!string.IsNullOrWhiteSpace(user) && !string.IsNullOrWhiteSpace(password))
                {
                    var basicAuth = new BasicAuthAuthorizationUser()
                    {
                        Login = user,
                        PasswordClear = password
                    };
                    var filter = new BasicAuthAuthorizationFilterOptions
                    {
                        RequireSsl = false,
                        SslRedirect = false,
                        LoginCaseSensitive = true,
                        Users = new[] { basicAuth }
                    };
                    var options = new DashboardOptions
                    {
                        Authorization = new[] { new BasicAuthAuthorizationFilter(filter) }
                    };

                    // 全局
                    app.UseMiddleware<AspNetCoreDashboardMiddleware>(options);

                    //// 部分
                    //app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), appBuilder =>
                    //{
                    //    appBuilder.UseMiddleware<AspNetCoreDashboardMiddleware>(options);
                    //});
                }

                // 从配置或环境变量中获取是否开启只读模式
                var isReadOnlyMode = builder.Configuration.GetSection("ReadOnly").Get<bool?>();
                if (isReadOnlyMode != true)
                {
                    if (bool.TryParse(Environment.GetEnvironmentVariable("READ_ONLY"), out var ro) && ro)
                    {
                        isReadOnlyMode = ro;
                    }
                }
                if (isReadOnlyMode == true)
                {
                    app.UseMiddleware<ReadOnlyMiddleware>(isReadOnlyMode);
                }

                app.MapControllers();

                //app.MapGet("/", () =>
                //{
                //    return "ok";
                //});

                //app.UseEndpoints(endpoints =>
                //{
                //    //endpoints.MapControllers();

                //    // 配置回退路由
                //    endpoints.MapFallbackToFile("index.html");
                //});

                // 配置回退路由
                app.MapFallbackToFile("/", "index.html");

                //app.Use(async (context, next) =>
                //{
                //    await next();

                //    if (context.Response.StatusCode == 404 && !Path.HasExtension(context.Request.Path.Value))
                //    {
                //        context.Request.Path = "/index.html"; // 将请求重定向到首页
                //        await next();
                //    }
                //});

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "应用启动失败");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }
    }
}