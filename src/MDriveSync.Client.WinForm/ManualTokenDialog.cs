using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MDriveSync.Client.WinForm
{
    public partial class ManualTokenDialog : UserControl
    {
        public event EventHandler<string> TextEntered;

        public event EventHandler CloseDialog;
        public ManualTokenDialog()
        {
            InitializeComponent();
        }

        private void btnLocalConfirm_Click(object sender, EventArgs e)
        {
            // 获取文本框的内容
            string text = textBox1.Text;

            // 触发 TextEntered 事件，并传递文本框的内容
            TextEntered?.Invoke(this, text);
            this.Visible = false;
        }

        private void button1_Click(object sender, EventArgs e)
        {
            string url = "https://openapi.alipan.com/oauth/authorize?client_id=12561ebaf6504bea8a611932684c86f6&redirect_uri=https://api.duplicati.net/api/open/aliyundrive&scope=user:base,file:all:read,file:all:write&relogin=true";
            Process.Start(new ProcessStartInfo
            {
                FileName = url,
                UseShellExecute = true
            });
        }

        private void btnLocalCancel_Click(object sender, EventArgs e)
        {
            this.Visible = false;
            CloseDialog?.Invoke(this, e);
        }

        public void LoadValue(string text)
        {
            textBox1.Text = text;
        }
    }
}
