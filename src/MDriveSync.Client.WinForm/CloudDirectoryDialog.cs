using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Data;
using System.Drawing;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using System.Windows.Forms;

namespace MDriveSync.Client.WinForm
{
    public partial class CloudDirectoryDialog : UserControl
    {
        public event EventHandler<string> TextEntered;

        public event EventHandler CloseDialog;
        public CloudDirectoryDialog()
        {
            InitializeComponent();
        }

        private void btnCloudConfirm_Click(object sender, EventArgs e)
        {
            // 获取文本框的内容
            string text = textBox1.Text;

            // 触发 TextEntered 事件，并传递文本框的内容
            TextEntered?.Invoke(this, text);
            this.Visible = false;
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

        private void CloudDirectoryDialog_Load(object sender, EventArgs e)
        {

        }
    }
}
