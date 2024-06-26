using Microsoft.Extensions.Configuration;
using Microsoft.Web.WebView2.WinForms;
using System.Reflection;

namespace MDriveSync.Client.WinFormAPI
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

            // �������ļ��ж�ȡ URL�����û����ʹ��Ĭ��ֵ
            var configuration = Program.Configuration;
            apiUrl = configuration.GetValue<string>("urls")?.Replace("*", "localhost") ?? "http://localhost:8080";

            // Initialize WebView2
            webView = new WebView2
            {
                Dock = DockStyle.Fill,
                Source = new Uri($"{apiUrl}") // ָ�� Web API �� URL
            };
            this.Controls.Add(webView);

            // Initialize NotifyIcon
            notifyIcon = new NotifyIcon
            {
                Icon = this.Icon,
                Text = "MDrive",
                Visible = true
            };

            // ʹ����Դ�е� PNG ͼ��
            this.Icon = LoadIconFromResource("MDriveSync.Client.WinFormAPI.Resources.logo.png", 64, 64);

            notifyIcon.DoubleClick += NotifyIcon_DoubleClick;

            // Initialize ContextMenuStrip
            contextMenuStrip = new ContextMenuStrip();
            exitMenuItem = new ToolStripMenuItem("�˳�", null, ExitMenuItem_Click);
            contextMenuStrip.Items.Add(exitMenuItem);
            notifyIcon.ContextMenuStrip = contextMenuStrip;

            this.Resize += MainForm_Resize;
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
                e.Cancel = true; // ȡ���رղ���
                this.WindowState = FormWindowState.Minimized; // ��С������
                this.Hide(); // ���ش���
                notifyIcon.Visible = true; // ��ʾ NotifyIcon
            }
        }

        private void ExitMenuItem_Click(object sender, EventArgs e)
        {
            notifyIcon.Visible = false;
            notifyIcon.Dispose(); // ȷ��ͼ�걻�ͷ�
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
