using Microsoft.AspNetCore.Hosting;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;
using Serilog.Debugging;

namespace MDriveSync.Client.WinFormAPI
{
    internal static class Program
    {
        public static IConfiguration Configuration { get; private set; }

        [STAThread]
        private static void Main()
        {
            var host = CreateHostBuilder().Build();
            Configuration = host.Services.GetRequiredService<IConfiguration>();

            Task.Run(() => host.Run());

            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);
            Application.Run(new MainForm());
        }

        public static IHostBuilder CreateHostBuilder() =>
            Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, config) =>
                {
                    var env = context.HostingEnvironment;

                    // ��� appsettings.json �����ļ�
                    config.AddJsonFile("appsettings.json", optional: true, reloadOnChange: true)
                          .AddJsonFile($"appsettings.{env.EnvironmentName}.json", optional: true, reloadOnChange: true);

                    // ��ӻ�������
                    config.AddEnvironmentVariables();
                })
                .ConfigureWebHostDefaults(webBuilder =>
                {
                    webBuilder.UseStartup<MDriveSync.Client.API.Startup>();
                })
                .UseSerilog((context, services, configuration) =>
                {
                    configuration.ReadFrom.Configuration(context.Configuration);

                    if (context.HostingEnvironment.IsDevelopment())
                    {
                        configuration.MinimumLevel.Debug()
                                     .Enrich.FromLogContext();

                        SelfLog.Enable(Console.Error);
                    }
                });
    }
}