using System;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace WinDeck.Setup
{
    /// <summary>설치 창: 설치 위치와 선택(바로가기·자동 실행·실행)을 보여 주고 [설치]를 누르면 설치한다.</summary>
    public class InstallForm : Form
    {
        readonly string sourceAppDir, uninstaller, version;
        readonly CheckBox chkDesktop, chkAutoStart, chkLaunch;
        readonly Button btnInstall, btnCancel;
        readonly Label lblStatus;
        readonly ProgressBar bar;
        bool done, working;

        public bool Installed { get; private set; }

        public InstallForm(string sourceAppDir, string uninstaller, string version)
        {
            this.sourceAppDir = sourceAppDir;
            this.uninstaller = uninstaller;
            this.version = version;
            InstallOptions current = SetupCore.CurrentOptions();
            string installed = SetupCore.InstalledVersion();

            SuspendLayout();
            Text = "WinDeck 설치";
            Font = new Font("Malgun Gothic", 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(460, 300);
            try { Icon = Icon.ExtractAssociatedIcon(Application.ExecutablePath); }
            catch (Exception) { }

            Controls.Add(new Label
            {
                Text = "WinDeck " + version + " 설치",
                Font = new Font("Malgun Gothic", 12f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(18, 16)
            });
            Controls.Add(new Label
            {
                Text = installed == null
                    ? "관리자 권한 없이 지금 사용자에게만 설치합니다."
                    : "지금 설치된 " + installed + " 을(를) " + version + " (으)로 바꿉니다. 버튼 설정은 그대로입니다.",
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(424, 20),
                Location = new Point(18, 50)
            });
            Controls.Add(new Label { Text = "설치 위치", AutoSize = true, Location = new Point(18, 80), ForeColor = SystemColors.GrayText });
            Controls.Add(new TextBox
            {
                Text = InstallInfo.InstallDir,
                ReadOnly = true,
                Location = new Point(18, 100),
                Size = new Size(424, 23),
                BackColor = SystemColors.Control
            });
            chkDesktop = new CheckBox { Text = "바탕 화면에 바로가기 만들기", AutoSize = true, Location = new Point(18, 136), Checked = current.DesktopShortcut };
            chkAutoStart = new CheckBox { Text = "Windows 시작 시 자동 실행", AutoSize = true, Location = new Point(18, 160), Checked = current.AutoStart };
            chkLaunch = new CheckBox { Text = "설치가 끝나면 WinDeck 실행", AutoSize = true, Location = new Point(18, 184), Checked = true };
            Controls.Add(chkDesktop);
            Controls.Add(chkAutoStart);
            Controls.Add(chkLaunch);

            bar = new ProgressBar { Location = new Point(18, 218), Size = new Size(424, 12), Style = ProgressBarStyle.Marquee, Visible = false };
            Controls.Add(bar);
            lblStatus = new Label { AutoSize = false, AutoEllipsis = true, Location = new Point(18, 234), Size = new Size(424, 20) };
            Controls.Add(lblStatus);

            btnCancel = new Button { Text = "취소", Location = new Point(354, 258), Size = new Size(88, 30), DialogResult = DialogResult.Cancel };
            btnInstall = new Button { Text = "설치", Location = new Point(260, 258), Size = new Size(88, 30) };
            btnInstall.Font = new Font(Font, FontStyle.Bold);
            btnInstall.Click += delegate
            {
                if (done) Close();
                else StartInstall();
            };
            Controls.Add(btnInstall);
            Controls.Add(btnCancel);
            AcceptButton = btnInstall;
            CancelButton = btnCancel;

            // DPI 배율은 모든 컨트롤을 추가한 뒤에 지정해야 크기가 두 번 커지지 않는다.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            btnInstall.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (working) e.Cancel = true; // 복사하는 도중에는 닫지 않는다
            base.OnFormClosing(e);
        }

        void StartInstall()
        {
            var o = new InstallOptions { DesktopShortcut = chkDesktop.Checked, AutoStart = chkAutoStart.Checked, Launch = chkLaunch.Checked };
            working = true;
            btnInstall.Enabled = false;
            btnCancel.Enabled = false;
            chkDesktop.Enabled = chkAutoStart.Enabled = chkLaunch.Enabled = false;
            bar.Visible = true;
            // 바로가기(.lnk)는 COM 으로 만들므로 STA 스레드에서 작업한다.
            var t = new Thread(() =>
            {
                Exception error = null;
                try
                {
                    SetupCore.Install(sourceAppDir, uninstaller, o, s => BeginInvoke(new Action(() => lblStatus.Text = s)));
                    if (o.Launch) SetupCore.LaunchInstalled();
                }
                catch (Exception ex)
                {
                    error = ex;
                    SetupCore.Log("install failed: " + ex);
                }
                BeginInvoke(new Action(() => Finish(error)));
            });
            t.SetApartmentState(ApartmentState.STA);
            t.IsBackground = true;
            t.Start();
        }

        void Finish(Exception error)
        {
            working = false;
            bar.Visible = false;
            if (error != null)
            {
                lblStatus.Text = "설치하지 못했습니다.";
                MessageBox.Show(this, "설치하지 못했습니다.\n\n" + error.Message + "\n\n기록: " + SetupCore.LogPath,
                    "WinDeck 설치", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                btnInstall.Enabled = true;
                btnCancel.Enabled = true;
                chkDesktop.Enabled = chkAutoStart.Enabled = chkLaunch.Enabled = true;
                return;
            }
            Installed = true;
            done = true;
            lblStatus.Text = "설치를 마쳤습니다. 시작 메뉴에서 WinDeck 을 찾을 수 있습니다.";
            btnInstall.Text = "닫기";
            btnInstall.Enabled = true;
            btnCancel.Visible = false;
            btnInstall.Focus();
        }
    }
}
