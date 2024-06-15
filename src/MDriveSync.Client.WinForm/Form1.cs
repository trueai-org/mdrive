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
        public static AliyunDriveHostedService TimedHostedService { get; private set; }
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
            //����״̬
            _host = Host.CreateDefaultBuilder()
                .ConfigureAppConfiguration((context, builder) =>
                {
                    var env = context.HostingEnvironment;

                    // �� ASP.NET Core Ӧ���У����� SetBasePath ͨ�����Ǳ���ģ���Ϊ ASP.NET Core Ĭ�ϻὫӦ�ó���ĸ�Ŀ¼��Ϊ��·����
                    // ����ζ�ţ�������������ļ����� appsettings.json �򻷾��ض��������ļ���ʱ��ϵͳĬ�ϻ��Ӧ�ó���ĸ�Ŀ¼��ʼ������Щ�ļ���

                    // ����������£���������Ҫ��ʽ���û�·����
                    // 1.�Ǳ�׼Ŀ¼�ṹ��������������ļ����ڱ�׼����Ŀ��Ŀ¼�У���������һ�����ӵ�Ŀ¼�ṹ����������Ҫʹ�� SetBasePath ����ȷָ�������ļ���λ�á�
                    // 2.�ڲ�ͬ�������������У����磬�ڵ�Ԫ���Ի�ĳЩ���͵�Ӧ�ó����У���ǰ����Ŀ¼��������Ŀ��Ŀ¼��ͬ������������£�ʹ�� SetBasePath ָ����ȷ��·�����Ǳ�Ҫ�ġ�
                    // 3.����Ŀ���������ļ���������ж����Ŀ����ͬһ�������ļ����Ҹ��ļ�λ����Щ��Ŀ�Ĺ�ͬ��Ŀ¼�У����û�·������ָ���������λ�á�

                    // �������ѭ ASP.NET Core �ı�׼Լ����������Ŀ��Ŀ¼���� appsettings.json ����Բ�ͬ������ appsettings.{Environment}.json �ļ�����ô��Щ�ļ����Զ����ݵ�ǰ���������ء�
                    // ����������£�������Ҫ�ֶ���Ӵ�����������Щ�ļ���

                    // ��������������ļ�
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

                        // ʹ�� Serilog.Debugging.SelfLog.Enable(Console.Error) ������ Serilog ��������ϣ��⽫��������������⡣
                        SelfLog.Enable(Console.Error);
                    }

                    // �����Ϊ���� exe ����ʱ��ʹ�ô�����ʽ���� Serilog����������ȫ�����������ļ���
                    // ����ܵ��� Serilog �ڳ����Զ����ֺͼ�������չ���򼯣��� Sinks��ʱ��������
                    // ��ʽ���� Serilog���ڳ���� Main �����У�ʹ�ô�����ʽ���� Serilog����������ȫ�����������ļ���
                    // ����Ҫô�ֶ��г� Sinks ����ͨ���������ַ�ʽ
                    var logOptions = new ConfigurationReaderOptions(
                        typeof(ConsoleLoggerConfigurationExtensions).Assembly);

                    Log.Logger = loggerConfig
                    .ReadFrom.Configuration(configuration, logOptions)
                    // ���Ϊ���� exe �ļ����޷�д��־���������������д��
                    .WriteTo.File("logs/log.txt", rollingInterval: RollingInterval.Day)
                    .CreateLogger();

                    // Quartz Log
                    var loggerFactory = new LoggerFactory().AddSerilog(Log.Logger);
                    LogProvider.SetLogProvider(loggerFactory);
                })
                .ConfigureServices((context, services) =>
                {
                    // ע�����
                    services.Configure<ClientOptions>(context.Configuration.GetSection("Client"));

                    // ע���̨����
                    //services.AddHostedService<TimedHostedService>();

                    // ʹ�õ���ģʽ
                    services.AddSingleton<AliyunDriveHostedService>();
                    services.AddHostedService(provider => provider.GetRequiredService<AliyunDriveHostedService>());

                    //// ע��WPF��������
                    //services.AddSingleton<MainWindow>();
                })
                .ConfigureLogging(logging =>
                {
                    // ���ַ�ʽ�������ȼ������ã�Ȼ�������Щ��������ʼ�� Serilog�����ṩ�˸��������ԡ�
                    logging.AddSerilog()
                    .AddProvider(new FormLoggerProvider(WriteToWindow)); ;
                })
                .Build();

            // ȷ����Ӧ�ó������ʱ�رղ�ˢ����־
            AppDomain.CurrentDomain.ProcessExit += (s, e) => Log.CloseAndFlush();
        }

        private void WriteToWindow(string message)
        {
            // ����ʵ�ֽ���Ϣд��WPF���ڵ��߼�

            // ��������һ����Ϊ LogMessages �� ObservableCollection<string>
            // ȷ����UI�߳���ִ�и���

            Invoke(new Action(() =>
            {
                tbMessage.AppendText(message + Environment.NewLine);
            }
                    ));
        }

        /// <summary>
        /// ���¼���״̬
        /// </summary>
        private void ReloadStatus()
        {
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            ////�ж�
            //{
            //    //���滻�ɽӿڲ�ѯ��ÿ���Ӳ�ѯ
            //    if (string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].RefreshToken))
            //    {
            //        lbLogin.Text = "��½״̬��δ��½";
            //    }
            //    else
            //    {
            //        lbLogin.Text = "��½״̬���ѵ�½";
            //    }
            //    lbLogin.Refresh();
            //}
            //{

            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Schedules?.Count() > 0)
            //    {
            //        lbPlan.Text = $"���ݼƻ���{aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Schedules?[0].ToString()}";
            //    }
            //    else
            //    {
            //        lbPlan.Text = "���ݼƻ���δ����";
            //    }
            //    lbPlan.Refresh();
            //}
            //{

            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Sources?.Count() > 0)
            //    {
            //        //����Ŀ¼��δ����
            //        lbLocal.Text = $"����Ŀ¼��{aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Sources?[0].ToString()}";
            //    }
            //    else
            //    {
            //        lbLocal.Text = "����Ŀ¼��δ����";
            //    }
            //    lbLocal.Refresh();
            //}
            //{

            //    if (!string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Target))
            //    {
            //        //����Ŀ¼��δ����
            //        lbCloud.Text = $"����Ŀ¼��{aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Target.ToString()}";
            //    }
            //    else
            //    {
            //        lbCloud.Text = "����Ŀ¼��δ����";
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
                    tbMessage.AppendText("��½�ɹ�" + Environment.NewLine);
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
                TimedHostedService = _host.Services.GetRequiredService<AliyunDriveHostedService>();
            }

        }

        /// <summary>
        /// �ж�
        /// </summary>
        private bool CheckConfig()
        {
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //{
            //    if (string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].RefreshToken))
            //    {
            //        MessageBox.Show(this, "���ȵ�½");
            //        return false;
            //    }
            //}
            //{
            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Schedules?.Count() == 0)
            //    {
            //        MessageBox.Show(this, "������д���ݼƻ�");
            //        return false;
            //    }
            //}
            //{
            //    if (aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Sources?.Count() == 0)
            //    {
            //        MessageBox.Show(this, "������д����Ŀ¼");
            //        return false;
            //    }
            //}
            //{
            //    if (string.IsNullOrWhiteSpace(aliyunDriveConfig?.Client?.AliyunDrives?[0].Jobs?[0].Target))
            //    {
            //        MessageBox.Show(this, "������д����Ŀ¼");
            //        return false;
            //    }
            //}
            return true;
        }

        private async void btnStop_Click(object sender, EventArgs e)
        {
            Invoke(new Action(() =>
            {
                tbMessage.AppendText("ֹͣ�У����Ժ󡭡�����" + Environment.NewLine);
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
                // �����쳣
                Console.WriteLine("�޷����������" + ex.Message);
            }
        }

        /// <summary>
        /// ���û��ؼ�
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
        /// �ر��û��ؼ�
        /// </summary>
        /// <param name="userControl"></param>
        private void closeClientForm(object sender, EventArgs e)
        {
            panel1.Visible = false;
        }

        /// <summary>
        /// ���ݼƻ�
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
            //// ���ı����浽��������ֶ���
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //aliyunDriveConfig.Client.AliyunDrives[0].Jobs[0].Schedules = new List<string>() { text };
            //saveConfig(aliyunDriveConfig);
            //panel1.Visible = false;
            //ReloadStatus();
        }


        private void saveLocalDirectory(object sender, string text)
        {
            //// ���ı����浽��������ֶ���
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //aliyunDriveConfig.Client.AliyunDrives[0].Jobs[0].Sources = new List<string>() { text };
            //saveConfig(aliyunDriveConfig);
            //panel1.Visible = false;
            //ReloadStatus();
        }

        private void saveCloudlDirectory(object sender, string text)
        {
            //// ���ı����浽��������ֶ���
            //var jsonString = File.ReadAllText(ClientSettings.ClientSettingsPath);
            //var aliyunDriveConfig = JsonConvert.DeserializeObject<ClientSettings>(jsonString);
            //aliyunDriveConfig.Client.AliyunDrives[0].Jobs[0].Target = text;
            //saveConfig(aliyunDriveConfig);
            //panel1.Visible = false;
            //ReloadStatus();
        }

        private void saveManualToken(object sender, string text)
        {
            //// ���ı����浽��������ֶ���
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
