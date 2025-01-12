namespace DeckyInstaller
{
    partial class MainForm
    {
        private System.ComponentModel.IContainer components = null;

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
            SuspendLayout();

            // Form-wide styles
            this.BackColor = Color.FromArgb(24, 24, 24);
            this.ForeColor = Color.FromArgb(240, 240, 240);
            this.Font = new Font("Segoe UI", 9.75F, FontStyle.Regular, GraphicsUnit.Point);

            // btnInstall
            btnInstall.BackColor = Color.FromArgb(0, 120, 215);
            btnInstall.FlatStyle = FlatStyle.Flat;
            btnInstall.FlatAppearance.BorderSize = 0;
            btnInstall.Font = new Font("Segoe UI", 10.2F, FontStyle.Bold, GraphicsUnit.Point);
            btnInstall.ForeColor = Color.White;
            btnInstall.Location = new Point(20, 20);
            btnInstall.Name = "btnInstall";
            btnInstall.Size = new Size(460, 45);
            btnInstall.TabIndex = 0;
            btnInstall.Text = "Install Decky Loader";
            btnInstall.UseVisualStyleBackColor = false;
            btnInstall.Cursor = Cursors.Hand;
            btnInstall.Click += btnInstall_Click;

            // progressBar
            progressBar.Location = new Point(20, 75);
            progressBar.Name = "progressBar";
            progressBar.Size = new Size(460, 5);
            progressBar.Style = ProgressBarStyle.Continuous;
            progressBar.TabIndex = 1;
            progressBar.ForeColor = Color.FromArgb(0, 120, 215);
            progressBar.BackColor = Color.FromArgb(45, 45, 45);

            // lblStatus
            lblStatus.AutoSize = true;
            lblStatus.Location = new Point(20, 90);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new Size(89, 17);
            lblStatus.TabIndex = 2;
            lblStatus.Text = "Ready to install...";
            lblStatus.ForeColor = Color.FromArgb(200, 200, 200);

            // txtOutput
            txtOutput.BackColor = Color.FromArgb(30, 30, 30);
            txtOutput.ForeColor = Color.FromArgb(220, 220, 220);
            txtOutput.Font = new Font("Cascadia Code", 9F, FontStyle.Regular, GraphicsUnit.Point);
            txtOutput.Location = new Point(20, 115);
            txtOutput.Multiline = true;
            txtOutput.Name = "txtOutput";
            txtOutput.ReadOnly = true;
            txtOutput.ScrollBars = ScrollBars.Both;
            txtOutput.Size = new Size(460, 215);
            txtOutput.TabIndex = 3;
            txtOutput.WordWrap = false;
            txtOutput.BorderStyle = BorderStyle.None;
            txtOutput.Padding = new Padding(5);

            // MainForm
            AutoScaleDimensions = new SizeF(7F, 17F);
            AutoScaleMode = AutoScaleMode.Font;
            ClientSize = new Size(500, 350);
            Controls.Add(txtOutput);
            Controls.Add(lblStatus);
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

        protected Button btnInstall;
        protected ProgressBar progressBar;
        protected Label lblStatus;
        protected TextBox txtOutput;
    }
}
