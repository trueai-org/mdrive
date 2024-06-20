using MDriveSync.Core;
using MDriveSync.Core.BaseAuth;
using MDriveSync.Core.Dashboard;
using MDriveSync.Core.Filters;
using MDriveSync.Core.Middlewares;
using MDriveSync.Infrastructure;
using Microsoft.AspNetCore.Mvc;
using Quartz.Logging;
using Serilog;
using Serilog.Debugging;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace MDriveSync.Client.API
{
    public class Program
    {
        public static void Main(string[] args)
        {
            var builder = WebApplication.CreateBuilder(args);

            var env = builder.Environment;

            //// ��������ļ�
            //var configuration = builder.Configuration
            //    .AddJsonFile($"appsettings.json", optional: true, reloadOnChange: true)

            //    // ���������е�
            //    .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

            //    //// ����Ĭ�ϵ�
            //    //.AddJsonFile($"{ClientSettings.ClientSettingsPath}", optional: true, reloadOnChange: true);

            //// ����Զ��������ļ�
            //builder.Configuration.AddJsonFile($"{ClientSettings.ClientSettingsPath}", optional: true, reloadOnChange: true);

            //// �����Ϊ���� exe ����ʱ��ʹ�ô�����ʽ���� Serilog����������ȫ�����������ļ���
            //// ����ܵ��� Serilog �ڳ����Զ����ֺͼ�������չ���򼯣��� Sinks��ʱ��������
            //// ��ʽ���� Serilog���ڳ���� Main �����У�ʹ�ô�����ʽ���� Serilog����������ȫ�����������ļ���
            //// ����Ҫô�ֶ��г� Sinks ����ͨ���������ַ�ʽ
            //var logOptions = new ConfigurationReaderOptions(
            //    typeof(ConsoleLoggerConfigurationExtensions).Assembly);

            // ���� Serilog
            var logger = new LoggerConfiguration()
                .ReadFrom.Configuration(builder.Configuration);

            //// ���Ϊ���� exe �ļ����޷�д��־���������������д��
            //.WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day);

            if (env.IsDevelopment())
            {
                logger.MinimumLevel.Debug()
                      .Enrich.FromLogContext();

                //.WriteTo.Console();

                // ʹ�� Serilog.Debugging.SelfLog.Enable(Console.Error) ������ Serilog ��������ϣ��⽫��������������⡣
                SelfLog.Enable(Console.Error);
            }

            Log.Logger = logger.CreateLogger();

            // Quartz Log
            var loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);
            LogProvider.SetLogProvider(loggerFactory);

            // ȷ����Ӧ�ó������ʱ�رղ�ˢ����־
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();

            try
            {
                Log.Information($"Current: {Directory.GetCurrentDirectory()}");

                // ʹ�� Serilog
                builder.Host.UseSerilog();

                // ��ҵ�ͻ�������
                builder.Services.Configure<ClientOptions>(builder.Configuration.GetSection("Client"));

                //// API ��ͼģ����֤ 400 ������
                //builder.Services.Configure<ApiBehaviorOptions>(options =>
                //{
                //    options.SuppressModelStateInvalidFilter = true;
                //});

                // API �쳣������
                // API ����/ģ�͹�����
                builder.Services.AddControllers(options =>
                {
                    options.Filters.Add<CustomLogicExceptionFilterAttribute>();
                    options.Filters.Add<CustomActionFilterAttribute>();
                });

                // �Զ������� API ��Ϊѡ��
                // ���� api ��ͼģ����֤ 400 ��������Ҫ�� AddControllers ֮������
                builder.Services.Configure<ApiBehaviorOptions>(options =>
                {
                    // ����1������ ModelState �Զ� 400 ������ʹ���Զ��巽����֤
                    // ��Ҫ�ڿ������������ֶ����� ModelState.IsValid�������ڿ�����������ʹ�� [ApiController] ���ԣ�����ʹ�� ActionFilterAttribute ������
                    //options.SuppressModelStateInvalidFilter = true;

                    // ����2���Զ���ģ����֤������
                    options.InvalidModelStateResponseFactory = (context) =>
                    {
                        var error = context.ModelState.Values.FirstOrDefault()?.Errors?.FirstOrDefault()?.ErrorMessage ?? "�����쳣";
                        Log.Logger.Warning("�����쳣 {@0} - {@1}", context.HttpContext?.Request?.GetUrl() ?? "", error);
                        return new JsonResult(Result.Fail(error));
                    };
                });

                // ��̨����
                //builder.Services.AddHostedService<TimedHostedService>();

                // ʹ�õ���ģʽ

                // �������̺�̨����
                builder.Services.AddSingleton<AliyunDriveHostedService>();
                builder.Services.AddHostedService(provider => provider.GetRequiredService<AliyunDriveHostedService>());

                // ���ش洢��̨����
                builder.Services.AddSingleton<LocalStorageHostedService>();
                builder.Services.AddHostedService(provider => provider.GetRequiredService<LocalStorageHostedService>());

                var app = builder.Build();

                //// ���� API �;�̬/�ͻ���·��
                //app.Use(async (context, next) =>
                //{
                //    if (context.Request.Path.StartsWithSegments("/api"))
                //    {
                //        // ����� API ���󣬼���ִ�к����м��
                //        await next();
                //    }
                //    //else
                //    //{
                //    //    app.MapFallbackToFile("index.html");

                //    //    //// �� API ���󣬳�����Ϊ��̬�ļ�����
                //    //    //await next();

                //    //    //// ���� API ������д���� index.html
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

                // ��ӻ�����֤
                // �ӻ���������ȡ���ã���� appsettings.json �е�����Ϊ��
                var userFromConfig = builder.Configuration["BasicAuth:User"];
                var passwordFromConfig = builder.Configuration["BasicAuth:Password"];
                var user = string.IsNullOrEmpty(userFromConfig) ?
                               Environment.GetEnvironmentVariable("BASIC_AUTH_USER") : userFromConfig;

                var password = string.IsNullOrEmpty(passwordFromConfig) ?
                                  Environment.GetEnvironmentVariable("BASIC_AUTH_PASSWORD") : passwordFromConfig;

                // ����˺ź�����
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

                    // ȫ��
                    app.UseMiddleware<AspNetCoreDashboardMiddleware>(options);

                    //// ����
                    //app.UseWhen(context => context.Request.Path.StartsWithSegments("/api"), appBuilder =>
                    //{
                    //    appBuilder.UseMiddleware<AspNetCoreDashboardMiddleware>(options);
                    //});
                }

                // �����û򻷾������л�ȡ�Ƿ���ֻ��ģʽ
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

                // ��ʾģʽ����
                var isDemoMode = builder.Configuration.GetSection("Demo").Get<bool?>();
                if (isDemoMode != true)
                {
                    if (bool.TryParse(Environment.GetEnvironmentVariable("DEMO"), out var demo) && demo)
                    {
                        isDemoMode = demo;
                    }
                }
                GlobalConfiguration.IsDemoMode = isDemoMode;

                app.MapControllers();

                //app.MapGet("/", () =>
                //{
                //    return "ok";
                //});

                //app.UseEndpoints(endpoints =>
                //{
                //    //endpoints.MapControllers();

                //    // ���û���·��
                //    endpoints.MapFallbackToFile("index.html");
                //});

                // ���û���·��
                app.MapFallbackToFile("/", "index.html");

                //app.Use(async (context, next) =>
                //{
                //    await next();

                //    if (context.Response.StatusCode == 404 && !Path.HasExtension(context.Request.Path.Value))
                //    {
                //        context.Request.Path = "/index.html"; // �������ض�����ҳ
                //        await next();
                //    }
                //});

                // �ڴ˴����������
                // �������ļ��ж�ȡ URL
                // �滻 * �Ա������������
                var url = builder.Configuration.GetSection("urls")?.Get<string>()?.Replace("*", "localhost");
                OpenBrowser(url);

                // ���� Windows Service ֧��
                // install Microsoft.Extensions.Hosting.WindowsServices
                //builder.Host.UseWindowsService();

                app.Run();
            }
            catch (Exception ex)
            {
                Log.Fatal(ex, "Ӧ������ʧ��");
            }
            finally
            {
                Log.CloseAndFlush();
            }
        }

        /// <summary>
        /// ��Ĭ��������ķ���
        /// </summary>
        /// <param name="url"></param>
        private static void OpenBrowser(string url)
        {
            try
            {
                if (string.IsNullOrWhiteSpace(url))
                {
                    return;
                }

                // ���ݲ�ͬ�Ĳ���ϵͳʹ�ò�ͬ������
                if (GlobalConfiguration.IsWindows())
                {
                    Process.Start(new ProcessStartInfo("cmd", $"/c start {url}")); // Windows
                }
                else if (GlobalConfiguration.IsLinux())
                {
                    // Process.Start("xdg-open", url); // Linux
                }
                else if (GlobalConfiguration.IsMacOS())
                {
                    Process.Start("open", url); // MacOS
                }
            }
            catch (Exception ex)
            {
                // �������������ʱ���ܳ��ֵ��쳣
                // �������¼��־��������������

                Log.Error(ex, "��Ĭ��������쳣 {@0}", url);
            }
        }
    }
}