using MDriveSync.Core;
using MDriveSync.Core.BaseAuth;
using MDriveSync.Core.Dashboard;
using MDriveSync.Core.Filters;
using MDriveSync.Core.Middlewares;
using MDriveSync.Infrastructure;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace MDriveSync.Client.App
{
    public class Startup
    {
        public Startup(IConfiguration configuration)
        {
            Configuration = configuration;
        }

        public IConfiguration Configuration { get; }

        public void ConfigureServices(IServiceCollection services)
        {
            services.Configure<ClientOptions>(Configuration.GetSection("Client"));

            // API 异常过滤器
            // API 方法/模型过滤器
            services.AddControllers(options =>
            {
                options.Filters.Add<CustomLogicExceptionFilterAttribute>();
                options.Filters.Add<CustomActionFilterAttribute>();
            });

            // 自定义配置 API 行为选项
            // 配置 api 视图模型验证 400 错误处理，需要在 AddControllers 之后配置
            services.Configure<ApiBehaviorOptions>(options =>
            {
                options.InvalidModelStateResponseFactory = (context) =>
                {
                    var error = context.ModelState.Values.FirstOrDefault()?.Errors?.FirstOrDefault()?.ErrorMessage ?? "参数异常";
                    Log.Logger.Warning("参数异常 {@0} - {@1}", context.HttpContext?.Request?.GetUrl() ?? "", error);
                    return new JsonResult(Result.Fail(error));
                };
            });

            services.AddSingleton<AliyunDriveHostedService>();
            services.AddHostedService(provider => provider.GetRequiredService<AliyunDriveHostedService>());

            services.AddSingleton<LocalStorageHostedService>();
            services.AddHostedService(provider => provider.GetRequiredService<LocalStorageHostedService>());
        }

        public void Configure(IApplicationBuilder app, IHostEnvironment env)
        {
            if (env.IsDevelopment())
            {
                app.UseDeveloperExceptionPage();
            }

            app.UseStaticFiles();

            app.UseCors(builder =>
            {
                builder.AllowAnyMethod().AllowAnyHeader().SetIsOriginAllowed(origin => true).AllowCredentials();
            });

            var userFromConfig = Configuration["BasicAuth:User"];
            var passwordFromConfig = Configuration["BasicAuth:Password"];
            var user = string.IsNullOrEmpty(userFromConfig) ? Environment.GetEnvironmentVariable("BASIC_AUTH_USER") : userFromConfig;
            var password = string.IsNullOrEmpty(passwordFromConfig) ? Environment.GetEnvironmentVariable("BASIC_AUTH_PASSWORD") : passwordFromConfig;

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

                app.UseMiddleware<AspNetCoreDashboardMiddleware>(options);
            }

            var isReadOnlyMode = Configuration.GetSection("ReadOnly").Get<bool?>();
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

            var isDemoMode = Configuration.GetSection("Demo").Get<bool?>();
            if (isDemoMode != true)
            {
                if (bool.TryParse(Environment.GetEnvironmentVariable("DEMO"), out var demo) && demo)
                {
                    isDemoMode = demo;
                }
            }
            GlobalConfiguration.IsDemoMode = isDemoMode;

            app.UseRouting();

            app.UseEndpoints(endpoints =>
            {
                endpoints.MapControllers();
                endpoints.MapFallbackToFile("index.html");
            });
        }
    }
}