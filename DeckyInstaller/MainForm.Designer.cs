namespace DeckyInstaller
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

        #region Windows Form Designer generated code

        protected override void Dispose(bool disposing)
        {
            if (disposing && (components != null))
            {
                components.Dispose();
            }
            base.Dispose(disposing);
        }

        private void InitializeComponent()
        {
            components = new System.ComponentModel.Container();
            btnInstall = new Button();
            progressBar = new ProgressBar();
            lblStatus = new Label();
            txtOutput = new TextBox();
            cmbVersions = new ComboBox();
            lblVersion = new Label();
            SuspendLayout();

            // Form-wide styles
            this.BackColor = Color.FromArgb(24, 24, 24);
            this.ForeColor = Color.FromArgb(240, 240, 240);
            this.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);

            // 
            // btnInstall
            // 
            btnInstall.BackColor = Color.FromArgb(0, 120, 215);
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Font = new Font("Segoe UI", 10.2F, FontStyle.Bold, GraphicsUnit.Point);
            btnInstall.ForeColor = Color.White;
            btnInstall.Location = new Point(20, 60);
            btnInstall.Name = "btnInstall";
            btnInstall.Size = new Size(460, 45);
            btnInstall.TabIndex = 0;
            btnInstall.Text = "Install Decky Loader";
            btnInstall.UseVisualStyleBackColor = false;
            btnInstall.Cursor = Cursors.Hand;
            btnInstall.Click += btnInstall_Click;

            // 
            // progressBar
            // 
            progressBar.Location = new Point(20, 115);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(460, 5);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.TabIndex = 1;
            progressBar.ForeColor = Color.FromArgb(0, 120, 215);
            progressBar.BackColor = Color.FromArgb(45, 45, 45);

            // 
            // lblVersion
            // 
            lblVersion.AutoSize = true;
            lblVersion.Font = new Font("Segoe UI", 9.75F, FontStyle.Bold, GraphicsUnit.Point);
            lblVersion.Location = new Point(20, 22);
            lblVersion.Name = "lblVersion";
            lblVersion.Size = new Size(63, 17);
            lblVersion.TabIndex = 2;
            lblVersion.Text = "Version:";
            lblVersion.ForeColor = Color.FromArgb(240, 240, 240);

            // 
            // cmbVersions
            // 
            cmbVersions.DropDownStyle = ComboBoxStyle.DropDownList;
            cmbVersions.FlatStyle = FlatStyle.Flat;
            cmbVersions.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);
            cmbVersions.Location = new Point(90, 20);
            cmbVersions.Name = "cmbVersions";
            cmbVersions.Size = new Size(390, 25);
            cmbVersions.TabIndex = 3;
            cmbVersions.BackColor = Color.FromArgb(45, 45, 45);
            cmbVersions.ForeColor = Color.FromArgb(240, 240, 240);
            cmbVersions.SelectedIndexChanged += cmbVersions_SelectedIndexChanged;

            // 
            // lblStatus
            // 
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(20, 130);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(89, 17);
            lblStatus.TabIndex = 4;
            lblStatus.Text = "Ready to install...";
            lblStatus.ForeColor = Color.FromArgb(200, 200, 200);

            // 
            // txtOutput
            // 
            txtOutput.BackColor = Color.FromArgb(30, 30, 30);
            txtOutput.ForeColor = Color.FromArgb(220, 220, 220);
            txtOutput.Font = new Font("Cascadia Code", 9F, FontStyle.Regular, GraphicsUnit.Point);
            txtOutput.Location = new Point(20, 155);
            txtOutput.Multiline = true;
            txtOutput.Name = "txtOutput";
            txtOutput.ReadOnly = true;
            txtOutput.ScrollBars = ScrollBars.Both;
            txtOutput.Size = new Size(460, 215);
            txtOutput.TabIndex = 5;
            txtOutput.WordWrap = false;
            txtOutput.BorderStyle = BorderStyle.None;
            txtOutput.Padding = new Padding(5);

            // 
            // MainForm
            // 
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(500, 390);
            Controls.Add(txtOutput);
            Controls.Add(lblStatus);
            Controls.Add(cmbVersions);
            Controls.Add(lblVersion);
            Controls.Add(progressBar);
            Controls.Add(btnInstall);
            FormBorderStyle = FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "MainForm";
            StartPosition = FormStartPosition.CenterScreen;
            Text = "Decky Loader Installer";
            Load += MainForm_Load;
            Padding = new Padding(20);
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        protected Button btnInstall;
        protected ProgressBar progressBar;
        protected Label lblStatus;
        protected TextBox txtOutput;
        protected ComboBox cmbVersions;
        protected Label lblVersion;
    }
}
