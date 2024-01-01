namespace MDriveSync.Client.WinForm
{
    partial class PlanDialog
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
            btnPlanConfirm = new Button();
            btnPlanCancel = new Button();
            textBox1 = new TextBox();
            label1 = new Label();
            SuspendLayout();
            // 
            // btnPlanConfirm
            // 
            btnPlanConfirm.Location = new Point(79, 65);
            btnPlanConfirm.Name = "btnPlanConfirm";
            btnPlanConfirm.Size = new Size(75, 23);
            btnPlanConfirm.TabIndex = 0;
            btnPlanConfirm.Text = "确认";
            btnPlanConfirm.UseVisualStyleBackColor = true;
            btnPlanConfirm.Click += btnPlanConfirm_Click;
            // 
            // btnPlanCancel
            // 
            btnPlanCancel.Location = new Point(219, 65);
            btnPlanCancel.Name = "btnPlanCancel";
            btnPlanCancel.Size = new Size(75, 23);
            btnPlanCancel.TabIndex = 0;
            btnPlanCancel.Text = "取消";
            btnPlanCancel.UseVisualStyleBackColor = true;
            btnPlanCancel.Click += btnPlanCancel_Click;
            // 
            // textBox1
            // 
            textBox1.Location = new Point(89, 21);
            textBox1.Name = "textBox1";
            textBox1.PlaceholderText = "备份计划时间，例如每分钟执行：0 * * * * ?";
            textBox1.Size = new Size(247, 23);
            textBox1.TabIndex = 1;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(27, 24);
            label1.Name = "label1";
            label1.Size = new Size(56, 17);
            label1.TabIndex = 2;
            label1.Text = "备份计划";
            // 
            // PlanDialog
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            Controls.Add(label1);
            Controls.Add(textBox1);
            Controls.Add(btnPlanCancel);
            Controls.Add(btnPlanConfirm);
            Name = "PlanDialog";
            Size = new Size(380, 110);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnPlanConfirm;
        private Button btnPlanCancel;
        private TextBox textBox1;
        private Label label1;
    }
}
