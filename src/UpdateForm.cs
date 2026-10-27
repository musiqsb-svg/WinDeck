using System;
using System.Diagnostics;
using System.Drawing;
using System.Threading;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>새 버전 안내: 변경 내용을 보여주고, [지금 업데이트]를 누르면 받아서 검사까지 한다.</summary>
    public class UpdateForm : Form
    {
        readonly UpdateInfo info;
        readonly Button btnUpdate, btnLater, btnSkip;
        readonly LinkLabel lnkPage;
        readonly ProgressBar bar;
        readonly Label lblStatus;
        volatile bool cancelled;
        bool working;
        int lastPercent = -2;
        DateTime lastReport;

        public PreparedUpdate Prepared { get; private set; }
        public bool SkipRequested { get; private set; }

        public UpdateForm(UpdateInfo info)
        {
            this.info = info;
            SuspendLayout();
            Text = "WinDeck 업데이트";
            Font = new Font(Theme.UiFontName, 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            TopMost = true;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(520, 384);

            Controls.Add(new Label
            {
                Text = "새 버전 v" + info.Version + " 이 나왔습니다",
                Font = new Font(Theme.UiFontName, 11f, FontStyle.Bold),
                AutoSize = true,
                Location = new Point(16, 14)
            });
            Controls.Add(new Label
            {
                Text = "지금 쓰는 버전 v" + AppInfo.Version + "  ·  "
                    + (Updater.IsInstalled ? "설치 파일로 업데이트합니다" : "포터블: 프로그램 파일만 바꾸고 설정은 그대로 둡니다"),
                AutoSize = false,
                AutoEllipsis = true,
                Size = new Size(488, 20),
                Location = new Point(16, 42),
                ForeColor = SystemColors.GrayText
            });
            var notes = new TextBox
            {
                Multiline = true,
                ReadOnly = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(16, 66),
                Size = new Size(488, 150),
                BackColor = SystemColors.Window,
                Text = NotesText(info.Notes)
            };
            Controls.Add(notes);
            Controls.Add(new Label
            {
                Text = "업데이트 전에 지금 설정을 저장·백업하고, 설치가 끝나면 WinDeck 을 다시 실행합니다.\n"
                    + "받은 파일은 SHA-256 검사를 통과해야만 설치합니다.",
                AutoSize = false,
                Size = new Size(488, 40),
                Location = new Point(16, 226),
                ForeColor = SystemColors.GrayText
            });
            lnkPage = new LinkLabel { Text = "릴리스 페이지에서 직접 받기", AutoSize = true, Location = new Point(16, 276) };
            lnkPage.LinkClicked += delegate
            {
                try { Process.Start(new ProcessStartInfo(info.PageUrl ?? Updater.ReleasesPage) { UseShellExecute = true }); }
                catch (Exception) { }
            };
            Controls.Add(lnkPage);
            bar = new ProgressBar { Location = new Point(16, 276), Size = new Size(488, 14), Visible = false };
            Controls.Add(bar);
            lblStatus = new Label { AutoSize = false, AutoEllipsis = true, Location = new Point(16, 296), Size = new Size(488, 18) };
            Controls.Add(lblStatus);

            btnSkip = new Button { Text = "이 버전 건너뛰기", Location = new Point(16, 340), Size = new Size(130, 30) };
            btnLater = new Button { Text = "나중에", Location = new Point(318, 340), Size = new Size(88, 30), DialogResult = DialogResult.Cancel };
            btnUpdate = new Button { Text = "지금 업데이트", Location = new Point(412, 340), Size = new Size(92, 30) };
            btnUpdate.Font = new Font(Font, FontStyle.Bold);
            btnSkip.Click += delegate
            {
                SkipRequested = true;
                DialogResult = DialogResult.Cancel;
                Close();
            };
            btnUpdate.Click += delegate { StartPrepare(); };
            Controls.Add(btnSkip);
            Controls.Add(btnLater);
            Controls.Add(btnUpdate);
            AcceptButton = btnUpdate;
            CancelButton = btnLater;

            // DPI 배율은 모든 컨트롤을 추가한 뒤에 지정해야 크기가 두 번 커지지 않는다.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        /// <summary>
        /// GitHub 릴리스 설명(마크다운)을 읽기 쉬운 글로: 제목 기호·굵게·코드·인용 표시를 걷어내고 목록은 점으로,
        /// 경고 상자 표시([!WARNING])와 그림 표는 뺀다.
        /// </summary>
        public static string NotesText(string notes)
        {
            if (string.IsNullOrWhiteSpace(notes)) return "변경 내용은 릴리스 페이지에서 볼 수 있습니다.";
            var lines = new System.Collections.Generic.List<string>();
            foreach (string raw in notes.Replace("\r\n", "\n").Split('\n'))
            {
                string line = raw.TrimEnd();
                string trimmed = line.TrimStart();
                if (trimmed.StartsWith(">")) trimmed = line = trimmed.TrimStart('>').Trim();
                if (trimmed.StartsWith("[!") || trimmed.StartsWith("|") || trimmed.Contains("<img")) continue;
                if (trimmed.StartsWith("#")) line = trimmed.TrimStart('#').Trim();
                else if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                    line = new string(' ', Math.Min(4, line.Length - trimmed.Length)) + "• " + trimmed.Substring(2);
                line = line.Replace("**", "").Replace("`", "");
                if (line.Length == 0 && (lines.Count == 0 || lines[lines.Count - 1].Length == 0)) continue;
                lines.Add(line);
            }
            return string.Join("\r\n", lines.ToArray()).Trim();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            Activate();
            btnUpdate.Focus();
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (working) cancelled = true; // 받는 중에 닫으면 받기를 멈춘다
            base.OnFormClosing(e);
        }

        void StartPrepare()
        {
            working = true;
            cancelled = false;
            btnUpdate.Enabled = false;
            btnSkip.Enabled = false;
            lnkPage.Visible = false;
            bar.Visible = true;
            bar.Style = ProgressBarStyle.Marquee;
            lblStatus.ForeColor = SystemColors.ControlText;
            lblStatus.Text = "준비 중…";
            var t = new Thread(() =>
            {
                try
                {
                    PreparedUpdate p = Updater.Prepare(info, Report, () => cancelled);
                    Ui(delegate
                    {
                        working = false;
                        Prepared = p;
                        DialogResult = DialogResult.OK;
                        Close();
                    });
                }
                catch (Exception ex)
                {
                    if (cancelled) return;
                    if (!(ex is UpdateException)) ErrorLog.Write(ex);
                    Ui(delegate { Failed(ex); });
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        void Report(string status, int percent)
        {
            DateTime now = DateTime.Now;
            if (percent == lastPercent && (now - lastReport).TotalMilliseconds < 200) return;
            lastPercent = percent;
            lastReport = now;
            Ui(delegate
            {
                lblStatus.Text = status;
                if (percent < 0)
                {
                    bar.Style = ProgressBarStyle.Marquee;
                }
                else
                {
                    bar.Style = ProgressBarStyle.Continuous;
                    bar.Value = Math.Max(0, Math.Min(100, percent));
                }
            });
        }

        void Failed(Exception ex)
        {
            working = false;
            bar.Visible = false;
            lnkPage.Visible = true;
            lblStatus.ForeColor = Color.Firebrick;
            lblStatus.Text = "업데이트하지 못했습니다: " + Updater.Describe(ex).Replace("\n", " ");
            btnUpdate.Text = "다시 시도";
            btnUpdate.Enabled = true;
            btnSkip.Enabled = true;
        }

        void Ui(MethodInvoker action)
        {
            try
            {
                if (!IsDisposed && IsHandleCreated) BeginInvoke(action);
            }
            catch (InvalidOperationException) { }
        }
    }
}
