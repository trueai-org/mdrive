namespace MDriveSync.Client.WinForm
{
    partial class CloudDirectoryDialog
    {
        /// <summary> 
        /// 必需的设计器变量。
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary> 
        /// 清理所有正在使用的资源。
        /// </summary>
        /// <param name="disposing">如果应释放托管资源，为 true；否则为 false。</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region 组件设计器生成的代码

        /// <summary> 
        /// 设计器支持所需的方法 - 不要修改
        /// 使用代码编辑器修改此方法的内容。
        /// </summary>
        private void InitializeComponent()
        {
            label1 = new Label();
            textBox1 = new TextBox();
            btnLocalCancel = new Button();
            btnCloudConfirm = new Button();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(36, 25);
            label1.Name = "label1";
            label1.Size = new Size(56, 17);
            label1.TabIndex = 10;
            label1.Text = "云盘目录";
            // 
            // textBox1
            // 
            textBox1.Location = new Point(98, 22);
            textBox1.Name = "textBox1";
            textBox1.PlaceholderText = "云盘备份目录，例如：backups/test";
            textBox1.Size = new Size(247, 23);
            textBox1.TabIndex = 9;
            // 
            // btnLocalCancel
            // 
            btnLocalCancel.Location = new Point(228, 66);
            btnLocalCancel.Name = "btnLocalCancel";
            btnLocalCancel.Size = new Size(75, 23);
            btnLocalCancel.TabIndex = 7;
            btnLocalCancel.Text = "取消";
            btnLocalCancel.UseVisualStyleBackColor = true;
            btnLocalCancel.Click += btnLocalCancel_Click;
            // 
            // btnCloudConfirm
            // 
            btnCloudConfirm.Location = new Point(88, 66);
            btnCloudConfirm.Name = "btnCloudConfirm";
            btnCloudConfirm.Size = new Size(75, 23);
            btnCloudConfirm.TabIndex = 8;
            btnCloudConfirm.Text = "确认";
            btnCloudConfirm.UseVisualStyleBackColor = true;
            btnCloudConfirm.Click += btnCloudConfirm_Click;
            // 
            // CloudDirectoryDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(label1);
            Controls.Add(textBox1);
            Controls.Add(btnLocalCancel);
            Controls.Add(btnCloudConfirm);
            Name = "CloudDirectoryDialog";
            Size = new Size(380, 110);
            Load += CloudDirectoryDialog_Load;
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private TextBox textBox1;
        private Button btnLocalCancel;
        private Button btnCloudConfirm;
    }
}
