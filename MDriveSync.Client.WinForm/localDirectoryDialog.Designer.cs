namespace MDriveSync.Client.WinForm
{
    partial class LocalDirectoryDialog
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
            btnLocalConfirm = new Button();
            SuspendLayout();
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(30, 30);
            label1.Name = "label1";
            label1.Size = new Size(56, 17);
            label1.TabIndex = 6;
            label1.Text = "本地目录";
            // 
            // textBox1
            // 
            textBox1.Location = new Point(92, 27);
            textBox1.Name = "textBox1";
            textBox1.PlaceholderText = "备份的文件夹，例如：E:\\\\test";
            textBox1.Size = new Size(247, 23);
            textBox1.TabIndex = 5;
            // 
            // btnLocalCancel
            // 
            btnLocalCancel.Location = new Point(222, 71);
            btnLocalCancel.Name = "btnLocalCancel";
            btnLocalCancel.Size = new Size(75, 23);
            btnLocalCancel.TabIndex = 3;
            btnLocalCancel.Text = "取消";
            btnLocalCancel.UseVisualStyleBackColor = true;
            btnLocalCancel.Click += btnLocalCancel_Click;
            // 
            // btnLocalConfirm
            // 
            btnLocalConfirm.Location = new Point(82, 71);
            btnLocalConfirm.Name = "btnLocalConfirm";
            btnLocalConfirm.Size = new Size(75, 23);
            btnLocalConfirm.TabIndex = 4;
            btnLocalConfirm.Text = "确认";
            btnLocalConfirm.UseVisualStyleBackColor = true;
            btnLocalConfirm.Click += btnLocalConfirm_Click;
            // 
            // LocalDirectoryDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(label1);
            Controls.Add(textBox1);
            Controls.Add(btnLocalCancel);
            Controls.Add(btnLocalConfirm);
            Name = "LocalDirectoryDialog";
            Size = new Size(380, 110);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Label label1;
        private TextBox textBox1;
        private Button btnLocalCancel;
        private Button btnLocalConfirm;
    }
}
