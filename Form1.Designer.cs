namespace PerfAudit
{
    partial class Form1
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

        #region Windows Form Designer generated code

        private void InitializeComponent()
        {
            System.ComponentModel.ComponentResourceManager resources = new System.ComponentModel.ComponentResourceManager(typeof(Form1));
            this.comboBoxProcesses = new System.Windows.Forms.ComboBox();
            this.btnToggle = new System.Windows.Forms.Button();
            this.btnRefresh = new System.Windows.Forms.Button();
            this.lblStatus = new System.Windows.Forms.Label();
            this.lblLiveUsage = new System.Windows.Forms.Label();
            this.label1 = new System.Windows.Forms.Label();
            this.chkAlwaysOnTop = new System.Windows.Forms.CheckBox();
            this.txtThreshold = new System.Windows.Forms.TextBox();
            this.label2 = new System.Windows.Forms.Label();
            this.label3 = new System.Windows.Forms.Label();
            this.SuspendLayout();

            this.comboBoxProcesses.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.comboBoxProcesses.DropDownStyle = System.Windows.Forms.ComboBoxStyle.DropDownList;
            this.comboBoxProcesses.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.comboBoxProcesses.ForeColor = System.Drawing.Color.White;
            this.comboBoxProcesses.FormattingEnabled = true;
            this.comboBoxProcesses.Location = new System.Drawing.Point(319, 110);
            this.comboBoxProcesses.Name = "comboBoxProcesses";
            this.comboBoxProcesses.Size = new System.Drawing.Size(331, 24);
            this.comboBoxProcesses.TabIndex = 0;

            this.btnToggle.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnToggle.Font = new System.Drawing.Font("Segoe UI", 9F, System.Drawing.FontStyle.Bold);
            this.btnToggle.ForeColor = System.Drawing.Color.Orange;
            this.btnToggle.Location = new System.Drawing.Point(97, 193);
            this.btnToggle.Name = "btnToggle";
            this.btnToggle.Size = new System.Drawing.Size(152, 51);
            this.btnToggle.TabIndex = 1;
            this.btnToggle.Text = "Start Monitoring";
            this.btnToggle.UseVisualStyleBackColor = true;
            this.btnToggle.Click += new System.EventHandler(this.btnToggle_Click);

            this.btnRefresh.FlatStyle = System.Windows.Forms.FlatStyle.Flat;
            this.btnRefresh.ForeColor = System.Drawing.Color.Orange;
            this.btnRefresh.Location = new System.Drawing.Point(97, 291);
            this.btnRefresh.Name = "btnRefresh";
            this.btnRefresh.Size = new System.Drawing.Size(152, 49);
            this.btnRefresh.TabIndex = 2;
            this.btnRefresh.Text = "Refresh List";
            this.btnRefresh.UseVisualStyleBackColor = true;
            // FIXED: Added event handler mapping
            this.btnRefresh.Click += new System.EventHandler(this.btnRefresh_Click);

            this.lblStatus.AutoSize = true;
            this.lblStatus.ForeColor = System.Drawing.Color.White;
            this.lblStatus.Location = new System.Drawing.Point(94, 387);
            this.lblStatus.Name = "lblStatus";
            this.lblStatus.Size = new System.Drawing.Size(79, 16);
            this.lblStatus.TabIndex = 3;
            this.lblStatus.Text = "Status: IDLE";

            this.lblLiveUsage.AutoSize = true;
            this.lblLiveUsage.Font = new System.Drawing.Font("Segoe UI", 14F, System.Drawing.FontStyle.Bold);
            this.lblLiveUsage.ForeColor = System.Drawing.Color.LightGreen;
            this.lblLiveUsage.Location = new System.Drawing.Point(600, 380);
            this.lblLiveUsage.Name = "lblLiveUsage";
            this.lblLiveUsage.Size = new System.Drawing.Size(131, 32);
            this.lblLiveUsage.TabIndex = 4;
            this.lblLiveUsage.Text = "CPU: 0.0%";

            this.label1.AutoSize = true;
            this.label1.Font = new System.Drawing.Font("Segoe UI", 18F, System.Drawing.FontStyle.Bold);
            this.label1.ForeColor = System.Drawing.Color.Turquoise;
            this.label1.Location = new System.Drawing.Point(375, 51);
            this.label1.Name = "label1";
            this.label1.Size = new System.Drawing.Size(214, 41);
            this.label1.TabIndex = 5;
            this.label1.Text = "Select Process";

            this.chkAlwaysOnTop.AutoSize = true;
            this.chkAlwaysOnTop.ForeColor = System.Drawing.Color.Coral;
            this.chkAlwaysOnTop.Location = new System.Drawing.Point(13, 25);
            this.chkAlwaysOnTop.Name = "chkAlwaysOnTop";
            this.chkAlwaysOnTop.Size = new System.Drawing.Size(119, 20);
            this.chkAlwaysOnTop.TabIndex = 6;
            this.chkAlwaysOnTop.Text = "Always on top?";
            this.chkAlwaysOnTop.UseVisualStyleBackColor = true;
            this.chkAlwaysOnTop.CheckedChanged += new System.EventHandler(this.chkAlwaysOnTop_CheckedChanged);

            this.txtThreshold.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(45)))), ((int)(((byte)(45)))), ((int)(((byte)(48)))));
            this.txtThreshold.BorderStyle = System.Windows.Forms.BorderStyle.FixedSingle;
            this.txtThreshold.ForeColor = System.Drawing.Color.White;
            this.txtThreshold.Location = new System.Drawing.Point(97, 110);
            this.txtThreshold.Name = "txtThreshold";
            this.txtThreshold.Size = new System.Drawing.Size(152, 22);
            this.txtThreshold.TabIndex = 7;
            this.txtThreshold.Text = "15.0";

            this.label2.AutoSize = true;
            this.label2.ForeColor = System.Drawing.Color.Coral;
            this.label2.Location = new System.Drawing.Point(5, 112);
            this.label2.Name = "label2";
            this.label2.Size = new System.Drawing.Size(86, 16);
            this.label2.TabIndex = 8;
            this.label2.Text = "Threshold %:";

            this.label3.AutoSize = true;
            this.label3.Font = new System.Drawing.Font("Segoe UI", 7.8F, System.Drawing.FontStyle.Italic);
            this.label3.ForeColor = System.Drawing.Color.Gray;
            this.label3.Location = new System.Drawing.Point(105, 147);
            this.label3.Name = "label3";
            this.label3.Size = new System.Drawing.Size(121, 17);
            this.label3.TabIndex = 9;
            this.label3.Text = "Default set to 15.0%";

            this.AutoScaleDimensions = new System.Drawing.SizeF(8F, 16F);
            this.AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            this.BackColor = System.Drawing.Color.FromArgb(((int)(((byte)(32)))), ((int)(((byte)(32)))), ((int)(((byte)(32)))));
            this.ClientSize = new System.Drawing.Size(800, 450);
            this.Controls.Add(this.label3);
            this.Controls.Add(this.label2);
            this.Controls.Add(this.txtThreshold);
            this.Controls.Add(this.chkAlwaysOnTop);
            this.Controls.Add(this.label1);
            this.Controls.Add(this.lblLiveUsage);
            this.Controls.Add(this.lblStatus);
            this.Controls.Add(this.btnRefresh);
            this.Controls.Add(this.btnToggle);
            this.Controls.Add(this.comboBoxProcesses);
            this.FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            this.Icon = ((System.Drawing.Icon)(resources.GetObject("$this.Icon")));
            this.MaximizeBox = false;
            this.Name = "Form1";
            this.Text = "PerfAudit v1.7";
            this.ResumeLayout(false);
            this.PerformLayout();
        }

        #endregion

        private System.Windows.Forms.ComboBox comboBoxProcesses;
        private System.Windows.Forms.Button btnToggle;
        private System.Windows.Forms.Button btnRefresh;
        private System.Windows.Forms.Label lblStatus;
        private System.Windows.Forms.Label lblLiveUsage;
        private System.Windows.Forms.Label label1;
        private System.Windows.Forms.CheckBox chkAlwaysOnTop;
        private System.Windows.Forms.TextBox txtThreshold;
        private System.Windows.Forms.Label label2;
        private System.Windows.Forms.Label label3;
    }
}