namespace MDriveSync.Client.WinForm
{
    partial class Form1
    {
        /// <summary>
        ///  Required designer variable.
        /// </summary>
        private System.ComponentModel.IContainer components = null;

        /// <summary>
        ///  Clean up any resources being used.
        /// </summary>
        /// <param name="disposing">true if managed resources should be disposed; otherwise, false.</param>
        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        #region Windows Form Designer generated code

        /// <summary>
        ///  Required method for Designer support - do not modify
        ///  the contents of this method with the code editor.
        /// </summary>
        private void InitializeComponent()
        {
            btnGetToken = new Button();
            btnPlan = new Button();
            btnLocalDirectory = new Button();
            btnCloudDirectory = new Button();
            label1 = new Label();
            btnBegin = new Button();
            btnStop = new Button();
            tbMessage = new TextBox();
            btnLogin = new Button();
            panel1 = new Panel();
            lbLogin = new Label();
            lbPlan = new Label();
            lbLocal = new Label();
            lbCloud = new Label();
            SuspendLayout();
            // 
            // btnGetToken
            // 
            btnGetToken.Location = new Point(49, 63);
            btnGetToken.Name = "btnGetToken";
            btnGetToken.Size = new Size(109, 33);
            btnGetToken.TabIndex = 0;
            btnGetToken.Text = "扫码登录";
            btnGetToken.UseVisualStyleBackColor = true;
            btnGetToken.Click += btnGetToken_Click;
            // 
            // btnPlan
            // 
            btnPlan.Location = new Point(49, 131);
            btnPlan.Name = "btnPlan";
            btnPlan.Size = new Size(109, 33);
            btnPlan.TabIndex = 0;
            btnPlan.Text = "备份计划";
            btnPlan.UseVisualStyleBackColor = true;
            btnPlan.Click += btnPlan_Click;
            // 
            // btnLocalDirectory
            // 
            btnLocalDirectory.Location = new Point(49, 198);
            btnLocalDirectory.Name = "btnLocalDirectory";
            btnLocalDirectory.Size = new Size(109, 33);
            btnLocalDirectory.TabIndex = 0;
            btnLocalDirectory.Text = "本地目录";
            btnLocalDirectory.UseVisualStyleBackColor = true;
            btnLocalDirectory.Click += btnLocalDirectory_Click;
            // 
            // btnCloudDirectory
            // 
            btnCloudDirectory.Location = new Point(49, 268);
            btnCloudDirectory.Name = "btnCloudDirectory";
            btnCloudDirectory.Size = new Size(109, 33);
            btnCloudDirectory.TabIndex = 0;
            btnCloudDirectory.Text = "云盘目录";
            btnCloudDirectory.UseVisualStyleBackColor = true;
            btnCloudDirectory.Click += btnCloudDirectory_Click;
            // 
            // label1
            // 
            label1.AutoSize = true;
            label1.Location = new Point(22, 19);
            label1.Name = "label1";
            label1.Size = new Size(134, 17);
            label1.TabIndex = 1;
            label1.Text = "阿里云盘：使用测试api";
            // 
            // btnBegin
            // 
            btnBegin.Location = new Point(49, 338);
            btnBegin.Name = "btnBegin";
            btnBegin.Size = new Size(109, 33);
            btnBegin.TabIndex = 0;
            btnBegin.Text = "开始备份";
            btnBegin.UseVisualStyleBackColor = true;
            btnBegin.Click += btnBegin_Click;
            // 
            // btnStop
            // 
            btnStop.Location = new Point(180, 338);
            btnStop.Name = "btnStop";
            btnStop.Size = new Size(109, 33);
            btnStop.TabIndex = 0;
            btnStop.Text = "停止备份";
            btnStop.UseVisualStyleBackColor = true;
            btnStop.Click += btnStop_Click;
            // 
            // tbMessage
            // 
            tbMessage.Location = new Point(455, 63);
            tbMessage.Multiline = true;
            tbMessage.Name = "tbMessage";
            tbMessage.ReadOnly = true;
            tbMessage.ScrollBars = ScrollBars.Both;
            tbMessage.Size = new Size(314, 308);
            tbMessage.TabIndex = 2;
            // 
            // btnLogin
            // 
            btnLogin.Location = new Point(180, 63);
            btnLogin.Name = "btnLogin";
            btnLogin.Size = new Size(109, 33);
            btnLogin.TabIndex = 0;
            btnLogin.Text = "手动登陆";
            btnLogin.UseVisualStyleBackColor = true;
            btnLogin.Click += btnLogin_Click;
            // 
            // panel1
            // 
            panel1.BorderStyle = BorderStyle.FixedSingle;
            panel1.Location = new Point(230, 131);
            panel1.Name = "panel1";
            panel1.Size = new Size(380, 110);
            panel1.TabIndex = 3;
            panel1.Visible = false;
            // 
            // lbLogin
            // 
            lbLogin.AutoSize = true;
            lbLogin.Location = new Point(313, 71);
            lbLogin.Name = "lbLogin";
            lbLogin.Size = new Size(104, 17);
            lbLogin.TabIndex = 4;
            lbLogin.Text = "登陆状态：未登陆";
            // 
            // lbPlan
            // 
            lbPlan.AutoSize = true;
            lbPlan.Location = new Point(180, 139);
            lbPlan.Name = "lbPlan";
            lbPlan.Size = new Size(121, 17);
            lbPlan.TabIndex = 4;
            lbPlan.Text = "备份计划：0 * * * * ?";
            // 
            // lbLocal
            // 
            lbLocal.AutoSize = true;
            lbLocal.Location = new Point(180, 206);
            lbLocal.Name = "lbLocal";
            lbLocal.Size = new Size(104, 17);
            lbLocal.TabIndex = 4;
            lbLocal.Text = "本地目录：未配置";
            // 
            // lbCloud
            // 
            lbCloud.AutoSize = true;
            lbCloud.Location = new Point(180, 276);
            lbCloud.Name = "lbCloud";
            lbCloud.Size = new Size(104, 17);
            lbCloud.TabIndex = 4;
            lbCloud.Text = "云盘目录：未配置";
            // 
            // Form1
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(800, 415);
            Controls.Add(panel1);
            Controls.Add(lbCloud);
            Controls.Add(lbLocal);
            Controls.Add(lbPlan);
            Controls.Add(lbLogin);
            Controls.Add(tbMessage);
            Controls.Add(label1);
            Controls.Add(btnStop);
            Controls.Add(btnBegin);
            Controls.Add(btnCloudDirectory);
            Controls.Add(btnLocalDirectory);
            Controls.Add(btnPlan);
            Controls.Add(btnLogin);
            Controls.Add(btnGetToken);
            Name = "Form1";
            Text = "测试版本";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private Button btnGetToken;
        private Button btnPlan;
        private Button btnLocalDirectory;
        private Button btnCloudDirectory;
        private Label label1;
        private Button btnBegin;
        private Button btnStop;
        private TextBox tbMessage;
        private Button btnLogin;
        private Panel panel1;
        private Label lbLogin;
        private Label lbPlan;
        private Label lbLocal;
        private Label lbCloud;
    }
}
