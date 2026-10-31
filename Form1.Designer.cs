namespace GameCaro
{
    partial class Form1
    {
        private System.ComponentModel.IContainer? components = null;

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
            pnlBoard = new System.Windows.Forms.Panel();
            lblIP = new System.Windows.Forms.Label();
            txtIP = new System.Windows.Forms.TextBox();
            btnHost = new System.Windows.Forms.Button();
            btnJoin = new System.Windows.Forms.Button();
            rtbChat = new System.Windows.Forms.RichTextBox();
            txtMessage = new System.Windows.Forms.TextBox();
            btnSend = new System.Windows.Forms.Button();
            btnRestart = new System.Windows.Forms.Button();
            lblStatus = new System.Windows.Forms.Label();
            SuspendLayout();
            //
            // pnlBoard
            //
            pnlBoard.Location = new System.Drawing.Point(12, 12);
            pnlBoard.Name = "pnlBoard";
            pnlBoard.Size = new System.Drawing.Size(450, 450);
            pnlBoard.TabIndex = 0;
            //
            // lblIP
            //
            lblIP.AutoSize = true;
            lblIP.Location = new System.Drawing.Point(480, 16);
            lblIP.Name = "lblIP";
            lblIP.Size = new System.Drawing.Size(150, 15);
            lblIP.TabIndex = 1;
            lblIP.Text = "IP của người tạo phòng:";
            //
            // txtIP
            //
            txtIP.Location = new System.Drawing.Point(480, 38);
            txtIP.Name = "txtIP";
            txtIP.Size = new System.Drawing.Size(290, 23);
            txtIP.TabIndex = 2;
            txtIP.Text = "127.0.0.1";
            //
            // btnHost
            //
            btnHost.Location = new System.Drawing.Point(480, 70);
            btnHost.Name = "btnHost";
            btnHost.Size = new System.Drawing.Size(140, 32);
            btnHost.TabIndex = 3;
            btnHost.Text = "Tạo phòng";
            btnHost.UseVisualStyleBackColor = true;
            btnHost.Click += btnHost_Click;
            //
            // btnJoin
            //
            btnJoin.Location = new System.Drawing.Point(630, 70);
            btnJoin.Name = "btnJoin";
            btnJoin.Size = new System.Drawing.Size(140, 32);
            btnJoin.TabIndex = 4;
            btnJoin.Text = "Vào phòng";
            btnJoin.UseVisualStyleBackColor = true;
            btnJoin.Click += btnJoin_Click;
            //
            // rtbChat
            //
            rtbChat.Location = new System.Drawing.Point(480, 114);
            rtbChat.Name = "rtbChat";
            rtbChat.ReadOnly = true;
            rtbChat.Size = new System.Drawing.Size(290, 280);
            rtbChat.TabIndex = 5;
            rtbChat.Text = "";
            //
            // txtMessage
            //
            txtMessage.Location = new System.Drawing.Point(480, 402);
            txtMessage.Name = "txtMessage";
            txtMessage.Size = new System.Drawing.Size(210, 23);
            txtMessage.TabIndex = 6;
            //
            // btnSend
            //
            btnSend.Enabled = false;
            btnSend.Location = new System.Drawing.Point(700, 400);
            btnSend.Name = "btnSend";
            btnSend.Size = new System.Drawing.Size(70, 27);
            btnSend.TabIndex = 7;
            btnSend.Text = "Gửi";
            btnSend.UseVisualStyleBackColor = true;
            btnSend.Click += btnSend_Click;
            //
            // btnRestart
            //
            btnRestart.Enabled = false;
            btnRestart.Location = new System.Drawing.Point(480, 438);
            btnRestart.Name = "btnRestart";
            btnRestart.Size = new System.Drawing.Size(290, 32);
            btnRestart.TabIndex = 8;
            btnRestart.Text = "Chơi lại";
            btnRestart.UseVisualStyleBackColor = true;
            btnRestart.Click += btnRestart_Click;
            //
            // lblStatus
            //
            lblStatus.AutoSize = true;
            lblStatus.Location = new System.Drawing.Point(12, 478);
            lblStatus.Name = "lblStatus";
            lblStatus.Size = new System.Drawing.Size(80, 15);
            lblStatus.TabIndex = 9;
            lblStatus.Text = "Chưa kết nối";
            //
            // Form1
            //
            AutoScaleDimensions = new System.Drawing.SizeF(7F, 15F);
            AutoScaleMode = System.Windows.Forms.AutoScaleMode.Font;
            ClientSize = new System.Drawing.Size(790, 510);
            Controls.Add(pnlBoard);
            Controls.Add(lblIP);
            Controls.Add(txtIP);
            Controls.Add(btnHost);
            Controls.Add(btnJoin);
            Controls.Add(rtbChat);
            Controls.Add(txtMessage);
            Controls.Add(btnSend);
            Controls.Add(btnRestart);
            Controls.Add(lblStatus);
            FormBorderStyle = System.Windows.Forms.FormBorderStyle.FixedSingle;
            MaximizeBox = false;
            Name = "Form1";
            StartPosition = System.Windows.Forms.FormStartPosition.CenterScreen;
            Text = "Game Caro";
            ResumeLayout(false);
            PerformLayout();
        }

        #endregion

        private System.Windows.Forms.Panel pnlBoard;
        private System.Windows.Forms.Label lblIP;
        private System.Windows.Forms.TextBox txtIP;
        private System.Windows.Forms.Button btnHost;
        private System.Windows.Forms.Button btnJoin;
        private System.Windows.Forms.RichTextBox rtbChat;
        private System.Windows.Forms.TextBox txtMessage;
        private System.Windows.Forms.Button btnSend;
        private System.Windows.Forms.Button btnRestart;
        private System.Windows.Forms.Label lblStatus;
    }
}
