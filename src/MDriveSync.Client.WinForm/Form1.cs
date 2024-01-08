using MDriveSync.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using OpenQA.Selenium;
using OpenQA.Selenium.Chrome;
using Quartz.Logging;
using SeleniumService;
using Serilog;
using Serilog.Debugging;
using Serilog.Settings.Configuration;
using System;
using System.Diagnostics;
using System.Windows.Forms;

namespace MDriveSync.Client.WinForm
{
    public partial class Form1 : Form
    {
        private readonly IHost _host;
        private PlanDialog planForm;
        private LocalDirectoryDialog localDirectoryForm;
        private CloudDirectoryDialog cloudDirectoryForm;
        private ManualTokenDialog manualTokenForm;
        public static TimedHostedService TimedHostedService { get; private set; }
        public Form1()
        {
            InitializeComponent();
            planForm = new PlanDialog();
            panel1.Controls.Add(planForm);
            planForm.CloseDialog += closeClientForm;
            planForm.TextEntered += savePlan;
            planForm.Visible = false;
            localDirectoryForm = new LocalDirectoryDialog();
            panel1.Controls.Add(localDirectoryForm);
            localDirectoryForm.CloseDialog += closeClientForm;
            localDirectoryForm.TextEntered += saveLocalDirectory;
            localDirectoryForm.Visible = false;
            cloudDirectoryForm = new CloudDirectoryDialog();
            panel1.Controls.Add(cloudDirectoryForm);
            cloudDirectoryForm.CloseDialog += closeClientForm;
            cloudDirectoryForm.TextEntered += saveCloudlDirectory;
            cloudDirectoryForm.Visible = false;
            manualTokenForm = new ManualTokenDialog();
            panel1.Controls.Add(manualTokenForm);
            manualTokenForm.CloseDialog += closeClientForm;
            manualTokenForm.TextEntered += saveManualToken;
            manualTokenForm.Visible = false;
            ReloadStatus();
            btnStop.Enabled = false;
            //加载状态
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
                    //.AddJsonFile($"{ClientSettings.ClientSettingsPath}", optional: true, reloadOnChange: true)
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
                    //// 注册服务
                    //services.Configure<ClientOptions>(context.Configuration.GetSection("Client"));

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
                    .AddProvider(new FormLoggerProvider(WriteToWindow)); ;
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

            Invoke(new Action(() =>
            {
                tbMessage.AppendText(message + Environment.NewLine);
            }
                    ));
        }

        /// <summary>
        /// 重新加载状态
        /// </summary>
        private void ReloadStatus()
        {
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            ////判断
            //{
            //    //后面换成接口查询，每分钟查询
            //    if (string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].RefreshToken))
            //    {
            //        lbLogin.Text = "登陆状态：未登陆";
            //    }
            //    else
            //    {
            //        lbLogin.Text = "登陆状态：已登陆";
            //    }
            //    lbLogin.Refresh();
            //}
            //{

            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Schedules?.Count() > 0)
            //    {
            //        lbPlan.Text = $"备份计划：{aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Schedules?[0].ToString()}";
            //    }
            //    else
            //    {
            //        lbPlan.Text = "备份计划：未设置";
            //    }
            //    lbPlan.Refresh();
            //}
            //{

            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Sources?.Count() > 0)
            //    {
            //        //本地目录：未配置
            //        lbLocal.Text = $"本地目录：{aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Sources?[0].ToString()}";
            //    }
            //    else
            //    {
            //        lbLocal.Text = "本地目录：未配置";
            //    }
            //    lbLocal.Refresh();
            //}
            //{

            //    if (!string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Target))
            //    {
            //        //本地目录：未配置
            //        lbCloud.Text = $"云盘目录：{aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Target.ToString()}";
            //    }
            //    else
            //    {
            //        lbCloud.Text = "云盘目录：未配置";
            //    }
            //    lbCloud.Refresh();
            //}
        }

        private void LoadValue()
        {
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);

            //{
            //    if (!string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].RefreshToken))
            //    {
            //        manualTokenForm.LoadValue(aliyunDriveConfig?.Client?.AliyunDrives?[0].RefreshToken.ToString());
            //    }

            //}
            //{
            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Schedules?.Count() > 0)
            //    {
            //        planForm.LoadValue(aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Schedules?[0].ToString());
            //    }
            //}
            //{

            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Sources?.Count() > 0)
            //    {

            //        localDirectoryForm.LoadValue(aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Sources?[0].ToString());
            //    }

            //}
            //{

            //    if (!string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Target))
            //    {
            //        cloudDirectoryForm.LoadValue(aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Target);
            //    }

            //}
        }

        private async void btnGetToken_Click(object sender, EventArgs e)
        {
            var url = "https://openapi.alipan.com/oauth/authorize?client_id=12561ebaf6504bea8a611932684c86f6&redirect_uri=https://api.duplicati.net/api/open/aliyundrive&scope=user:base,file:all:read,file:all:write&relogin=true";
            ChromeBrower driver = new ChromeBrower();
            try
            {
                driver.Open();
                driver.Navigate(url);
                var waitUrl = "https://api.duplicati.net/api/open/aliyundrive?code=";
                driver.WaitForUrl(waitUrl);
                var token = driver.GetElementText("/html/body/pre");
                //Thread.Sleep(100000);
                driver.Close();
                saveToken(sender, token);
                Invoke(new Action(() =>
                {
                    tbMessage.AppendText("登陆成功" + Environment.NewLine);
                }
                    ));

            }
            catch (Exception ex)
            {
                Console.WriteLine(ex);
                driver.Close();
                throw;
            }
            ReloadStatus();
        }



        private async void btnBegin_Click(object sender, EventArgs e)
        {
            if (CheckConfig())
            {
                await _host.StartAsync();
                LockBtn();
                TimedHostedService = _host.Services.GetRequiredService<TimedHostedService>();
            }

        }

        /// <summary>
        /// 判断
        /// </summary>
        private bool CheckConfig()
        {
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //{
            //    if (string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].RefreshToken))
            //    {
            //        MessageBox.Show(this, "请先登陆");
            //        return false;
            //    }
            //}
            //{
            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Schedules?.Count() == 0)
            //    {
            //        MessageBox.Show(this, "请先填写备份计划");
            //        return false;
            //    }
            //}
            //{
            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Sources?.Count() == 0)
            //    {
            //        MessageBox.Show(this, "请先填写本地目录");
            //        return false;
            //    }
            //}
            //{
            //    if (string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Target))
            //    {
            //        MessageBox.Show(this, "请先填写云盘目录");
            //        return false;
            //    }
            //}
            return true;
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            Invoke(new Action(() =>
            {
                tbMessage.AppendText("停止中，请稍后…………" + Environment.NewLine);
            }));
            using (_host)
            {
                _host.StopAsync().Wait();
            }
            UnlockBtn();
        }

        private void LockBtn()
        {
            btnBegin.Enabled = false;
            btnLogin.Enabled = false;
            btnGetToken.Enabled = false;
            btnPlan.Enabled = false;
            btnLocalDirectory.Enabled = false;
            btnCloudDirectory.Enabled = false;
            btnStop.Enabled = true;
        }

        private void UnlockBtn()
        {
            btnStop.Enabled = false;
            btnBegin.Enabled = true;
            btnLogin.Enabled = true;
            btnGetToken.Enabled = true;
            btnPlan.Enabled = true;
            btnLocalDirectory.Enabled = true;
            btnCloudDirectory.Enabled = true;
        }

        private void btnLogin_Click(object sender, EventArgs e)
        {
            try
            {
                //planForm.Show();
                LoadValue();
                openClientForm(manualTokenForm);
                //planForm.Location = new Point(10, 10);
                //planForm.Size = new Size(200, 100);
            }
            catch (Exception ex)
            {
                // 处理异常
                Console.WriteLine("无法打开浏览器：" + ex.Message);
            }
        }

        /// <summary>
        /// 打开用户控件
        /// </summary>
        /// <param name="userControl"></param>
        private void openClientForm(UserControl userControl)
        {
            userControl.Visible = true;

            panel1.Visible = true;
            panel1.Left = (this.ClientSize.Width - userControl.Width) / 2;
            panel1.Top = (this.ClientSize.Height - userControl.Height) / 2;
            panel1.Size = new Size(userControl.Width, userControl.Height);
        }

        /// <summary>
        /// 关闭用户控件
        /// </summary>
        /// <param name="userControl"></param>
        private void closeClientForm(object sender, EventArgs e)
        {
            panel1.Visible = false;
        }

        /// <summary>
        /// 备份计划
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void btnPlan_Click(object sender, EventArgs e)
        {
            LoadValue();
            openClientForm(planForm);
        }

        private void savePlan(object sender, string text)
        {
            //// 将文本保存到主窗体的字段中
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //aliyunDriveConfig.Client.AliyunDrives[0].Jobs[0].Schedules = new List<string>() { text };
            //saveConfig(aliyunDriveConfig);
            //panel1.Visible = false;
            //ReloadStatus();
        }


        private void saveLocalDirectory(object sender, string text)
        {
            //// 将文本保存到主窗体的字段中
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //aliyunDriveConfig.Client.AliyunDrives[0].Jobs[0].Sources = new List<string>() { text };
            //saveConfig(aliyunDriveConfig);
            //panel1.Visible = false;
            //ReloadStatus();
        }

        private void saveCloudlDirectory(object sender, string text)
        {
            //// 将文本保存到主窗体的字段中
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //aliyunDriveConfig.Client.AliyunDrives[0].Jobs[0].Target = text;
            //saveConfig(aliyunDriveConfig);
            //panel1.Visible = false;
            //ReloadStatus();
        }

        private void saveManualToken(object sender, string text)
        {
            //// 将文本保存到主窗体的字段中
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //aliyunDriveConfig.Client.AliyunDrives[0].RefreshToken = text;
            //saveConfig(aliyunDriveConfig);
            //panel1.Visible = false;
            //ReloadStatus();
        }



        private void saveToken(object sender, string text)
        {
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //aliyunDriveConfig.Client.AliyunDrives[0].RefreshToken = text;
            //saveConfig(aliyunDriveConfig);
            //panel1.Visible = false;
            //ReloadStatus();
        }



        //private void saveConfig(ClientSettings settings)
        //{
        //    //string json = JsonConvert.SerializeObject(settings, Formatting.Indented);
        //    //File.WriteAllText(ClientSettings.ClientSettingsPath, json);

        //}

        private void btnLocalDirectory_Click(object sender, EventArgs e)
        {
            LoadValue();
            openClientForm(localDirectoryForm);
        }

        private void btnCloudDirectory_Click(object sender, EventArgs e)
        {
            LoadValue();
            openClientForm(cloudDirectoryForm);
        }
    }
}
