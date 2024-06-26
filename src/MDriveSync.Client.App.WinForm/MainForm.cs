using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.WinForms;
using Serilog;
using System.Reflection;

namespace MDriveSync.Client.App.WinForm
{
    public partial class MainForm : Form
    {
        private WebView2 webView;
        private NotifyIcon notifyIcon;
        private string apiUrl;
        private ContextMenuStrip contextMenuStrip;
        private ToolStripMenuItem exitMenuItem;

        public MainForm()
        {
            InitializeComponent();

            try
            {
                // 从配置文件中读取 URL，如果没有则使用默认值
                var configuration = Program.Configuration;
                apiUrl = configuration.GetValue<string>("urls")?.Replace("*", "localhost") ?? "http://localhost:8080";

                // Initialize WebView2
                webView = new WebView2
                {
                    Dock = DockStyle.Fill,
                    Source = new Uri($"{apiUrl}") // 指向 Web API 的 URL
                };
                this.Controls.Add(webView);

                // Initialize NotifyIcon
                notifyIcon = new NotifyIcon
                {
                    Icon = this.Icon,
                    Text = "MDrive",
                    Visible = true
                };

                // 使用资源中的 PNG 图像
                // 获取当前程序集名称，而不是写死
                var assemblyName = Assembly.GetExecutingAssembly().GetName().Name;
                this.Icon = LoadIconFromResource($"{assemblyName}.Resources.logo.png", 64, 64);

                notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

                // Initialize ContextMenuStrip
                contextMenuStrip = new ContextMenuStrip();
                exitMenuItem = new ToolStripMenuItem("退出", null, ExitMenuItem_Click);
                contextMenuStrip.Items.Add(exitMenuItem);
                notifyIcon.ContextMenuStrip = contextMenuStrip;

                this.Resize += MainForm_Resize;
            }
            catch (Exception ex)
            {
                Log.Logger.Error(ex, "初始化失败，请重试");

                MessageBox.Show("初始化失败，请重试");
            }
        }

        private void MainForm_Resize(object sender, EventArgs e)
        {
            if (this.WindowState == FormWindowState.Minimized)
            {
                this.Hide();
                notifyIcon.Visible = true;
            }
        }

        private void NotifyIcon_DoubleClick(object sender, EventArgs e)
        {
            this.Show();
            this.WindowState = FormWindowState.Normal;
            notifyIcon.Visible = false;
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true; // 取消关闭操作
                this.WindowState = FormWindowState.Minimized; // 最小化窗口
                this.Hide(); // 隐藏窗口
                notifyIcon.Visible = true; // 显示 NotifyIcon
            }
        }

        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose(); // 确保图标被释放
            Application.Exit();
        }

        private Icon LoadIconFromResource(string resourceName, int width, int height)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var bitmap = new Bitmap(stream))
                    {
                        var resizedBitmap = new Bitmap(bitmap, new Size(width, height));
                        return Icon.FromHandle(resizedBitmap.GetHicon());
                    }
                }
                else
                {
                    throw new ArgumentException("Resource not found: " + resourceName);
                }
            }
        }

        private Icon LoadIconFromResource(string resourceName)
        {
            using (var stream = Assembly.GetExecutingAssembly().GetManifestResourceStream(resourceName))
            {
                if (stream != null)
                {
                    using (var bitmap = new Bitmap(stream))
                    {
                        return Icon.FromHandle(bitmap.GetHicon());
                    }
                }
                else
                {
                    throw new ArgumentException("Resource not found: " + resourceName);
                }
            }
        }
    }
}