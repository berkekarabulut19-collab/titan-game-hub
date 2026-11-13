using System;
using System.Drawing;
using System.Windows.Forms;

namespace TitanGameHub
{
    public enum NotificationType
    {
        Info = 0,
        Success = 1,
        Warning = 2,
        Error = 3
    }

    internal class SteamPurpleToastForm : Form
    {
        private readonly System.Windows.Forms.Timer _closeTimer;

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= 0x08000000; // WS_EX_NOACTIVATE
                cp.ExStyle |= 0x00000008; // WS_EX_TOPMOST
                return cp;
            }
        }

        public SteamPurpleToastForm(string title, string message, NotificationType type)
        {
            this.FormBorderStyle = FormBorderStyle.None;
            this.StartPosition = FormStartPosition.Manual;
            this.ShowInTaskbar = false;
            this.TopMost = true;
            this.Size = new Size(340, 76);
            this.BackColor = Color.FromArgb(26, 21, 38); // Steam Purple Dark

            Color accent = Color.FromArgb(147, 51, 234); // Purple
            if (type == NotificationType.Success) accent = Color.FromArgb(93, 186, 47);
            else if (type == NotificationType.Warning) accent = Color.FromArgb(217, 119, 6);
            else if (type == NotificationType.Error) accent = Color.FromArgb(220, 38, 38);

            Panel leftBar = new Panel
            {
                Dock = DockStyle.Left,
                Width = 5,
                BackColor = accent
            };
            this.Controls.Add(leftBar);

            Label lblTitle = new Label
            {
                Text = string.IsNullOrEmpty(title) ? "TITAN GAME HUB" : title,
                Font = new Font("Segoe UI", 9.5f, FontStyle.Bold),
                ForeColor = Color.FromArgb(192, 132, 252),
                Location = new Point(14, 10),
                Size = new Size(314, 20),
                AutoEllipsis = true
            };
            this.Controls.Add(lblTitle);

            Label lblBody = new Label
            {
                Text = message ?? "",
                Font = new Font("Segoe UI", 8.5f, FontStyle.Regular),
                ForeColor = Color.FromArgb(228, 223, 240),
                Location = new Point(14, 32),
                Size = new Size(314, 36),
                AutoEllipsis = true
            };
            this.Controls.Add(lblBody);

            this.Paint += delegate(object s, PaintEventArgs e)
            {
                using (Pen p = new Pen(Color.FromArgb(83, 62, 125), 1))
                {
                    e.Graphics.DrawRectangle(p, 0, 0, this.Width - 1, this.Height - 1);
                }
            };

            Rectangle area = Screen.PrimaryScreen.WorkingArea;
            this.Location = new Point(area.Right - this.Width - 16, area.Bottom - this.Height - 16);

            this.Click += delegate { this.Close(); };
            lblTitle.Click += delegate { this.Close(); };
            lblBody.Click += delegate { this.Close(); };

            _closeTimer = new System.Windows.Forms.Timer { Interval = 3800 };
            _closeTimer.Tick += delegate
            {
                _closeTimer.Stop();
                this.Close();
            };
            _closeTimer.Start();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing && _closeTimer != null)
            {
                _closeTimer.Dispose();
            }
            base.Dispose(disposing);
        }
    }

    public partial class WpfMainWindow
    {
        private void ShowToast(string message, NotificationType type)
        {
            try
            {
                if (_txtBottomStatus != null)
                    _txtBottomStatus.Text = message ?? string.Empty;

                if (type == NotificationType.Success || type == NotificationType.Warning)
                    ShowSteamToast("TITAN GAME HUB", message, type);
            }
            catch { }
        }

        private void ShowSteamToast(string title, string message, NotificationType type)
        {
            try
            {
                SteamPurpleToastForm toast = new SteamPurpleToastForm(title, message, type);
                toast.Show();
            }
            catch { }
        }
    }
}
