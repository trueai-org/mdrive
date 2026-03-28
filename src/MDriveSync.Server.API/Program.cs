using MDriveSync.Core;
using MDriveSync.Infrastructure;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Quartz.Logging;
using Serilog;
using Serilog.Debugging;

namespace MDriveSync.Server.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            // 配置 Serilog
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration);

            if (builder.Environment.IsDevelopment())
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
                // 使用 Serilog
                builder.Host.UseSerilog();

                // 阿里云盘服务商配置
                builder.Services.Configure<AliyunDriveProviderOptions>(builder.Configuration.GetSection("AliyunDriveProvider"));

                // 百度网盘服务商配置
                builder.Services.Configure<BaiduNetDiskProviderOptions>(builder.Configuration.GetSection("BaiduNetDiskProvider"));

                var consulOpt = builder.Configuration.GetSection(nameof(ConsulOptions));
                builder.Services.Configure<ConsulOptions>(consulOpt);

                var consulValue = new ConsulOptions();
                consulOpt.Bind(consulValue);

                // 注册 Consul
                builder.Services.AddSingleton<ConsulService>();

                // 添加健康检查
                builder.Services.AddHealthChecks();

                builder.Services.TryAddSingleton<IHttpContextAccessor, HttpContextAccessor>();
                builder.Services.AddMemoryCache();

                builder.Services.AddControllers();

                var app = builder.Build();

                app.UseCors(builder =>
                {
                    builder.AllowAnyMethod().AllowAnyHeader().SetIsOriginAllowed(origin => true).AllowCredentials();
                });

                app.UseForwardedHeaders(new ForwardedHeadersOptions { ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto });

                app.MapControllers();
                app.MapHealthChecks("/health");

                app.MapGet("/", () =>
                {
                    return "ok";
                });

                var applicationLifetime = app.Services.GetRequiredService<IHostApplicationLifetime>();
                applicationLifetime.ApplicationStarted.Register(async () =>
                {
                    Log.Information("应用程序已启动...");
                    try
                    {
                        if (consulValue?.Enable == true && consulValue.IsValid)
                        {
                            var consulService = app.Services.GetRequiredService<ConsulService>();
                            await consulService.RegisterServiceAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "注册服务到 Consul 失败");
                    }
                });
                // 自动注销 Consul
                applicationLifetime.ApplicationStopping.Register(async () =>
                {
                    Log.Information("应用程序正在停止...");
                    try
                    {
                        if (consulValue?.Enable == true && consulValue.IsValid)
                        {
                            var consulService = app.Services.GetRequiredService<ConsulService>();
                            await consulService.DeregisterServiceAsync();
                        }
                    }
                    catch (Exception ex)
                    {
                        Log.Error(ex, "从 Consul 注销服务失败");
                    }
                });

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