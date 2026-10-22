using System;
using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>패널 전체 설정: 일반(크기·배치·투명도·위치), 단축키, 페이지 관리, 백업.</summary>
    public class SettingsForm : Form
    {
        static readonly int[] KeySizes = { 72, 88, 110, 132 };
        static readonly string[] KeySizeNames = { "작게", "보통", "크게", "아주 크게" };

        AppConfig working;
        TabControl tabs;
        TabPage tabGeneral, tabHotkeys, tabPages, tabBackup;
        KeyCaptureBox txtHotkey, txtPrev, txtNext;
        ComboBox cboSize, cboLayout;
        TrackBar trkOpacity;
        Label lblOpacity;
        CheckBox chkTopMost, chkShowAtCursor, chkAutoStart;
        ListBox lstPages, lstHotkeys;

        public AppConfig Result { get; private set; }
        public bool AutoStartEnabled { get; private set; }

        public SettingsForm(AppConfig workingCopy, bool autoStart)
        {
            working = workingCopy;
            AutoStartEnabled = autoStart;
            BuildUi();
            LoadValues();
        }

        /// <summary>처음 열 탭 (0 일반, 1 단축키, 2 페이지, 3 백업).</summary>
        public int InitialTab
        {
            set { tabs.SelectedIndex = Math.Max(0, Math.Min(tabs.TabCount - 1, value)); }
        }

        void BuildUi()
        {
            SuspendLayout();
            Text = "WinDeck 설정  (v" + AppInfo.Version + (ConfigStore.IsPortable ? ", 포터블" : "") + ")";
            Font = new Font(Theme.UiFontName, 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(456, 420);
            var tips = new ToolTip();

            tabs = new TabControl { Location = new Point(12, 12), Size = new Size(432, 356) };
            tabGeneral = NewTab("일반");
            tabHotkeys = NewTab("단축키");
            tabPages = NewTab("페이지");
            tabBackup = NewTab("백업");
            Controls.Add(tabs);

            // ---- 일반
            AddLabel(tabGeneral, "버튼 크기", 12, 19);
            cboSize = new ComboBox { Location = new Point(150, 16), Size = new Size(170, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboSize.Items.AddRange(KeySizeNames);
            tabGeneral.Controls.Add(cboSize);

            AddLabel(tabGeneral, "배치", 12, 53);
            cboLayout = new ComboBox { Location = new Point(150, 50), Size = new Size(170, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboLayout.Items.AddRange(new object[] { "가로 (4 × 2)", "세로 (2 × 4)" });
            tabGeneral.Controls.Add(cboLayout);

            AddLabel(tabGeneral, "투명도", 12, 94);
            trkOpacity = new TrackBar
            {
                Location = new Point(144, 86),
                Size = new Size(200, 45),
                Minimum = 30,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10
            };
            lblOpacity = new Label { AutoSize = true, Location = new Point(350, 94) };
            trkOpacity.ValueChanged += delegate { lblOpacity.Text = trkOpacity.Value + "%"; };
            tabGeneral.Controls.Add(trkOpacity);
            tabGeneral.Controls.Add(lblOpacity);

            chkTopMost = new CheckBox { Text = "항상 다른 창 위에 표시", AutoSize = true, Location = new Point(12, 136) };
            chkShowAtCursor = new CheckBox { Text = "단축키로 열 때 마우스 커서 위치에 표시", AutoSize = true, Location = new Point(12, 164) };
            chkAutoStart = new CheckBox { Text = "윈도우 시작 시 자동 실행", AutoSize = true, Location = new Point(12, 220) };
            tabGeneral.Controls.Add(chkTopMost);
            tabGeneral.Controls.Add(chkShowAtCursor);
            AddHint(tabGeneral, "끄면 마지막으로 옮겨 둔 자리에 나타납니다.", 30, 188);
            tabGeneral.Controls.Add(chkAutoStart);
            tips.SetToolTip(chkShowAtCursor, "보이기/숨기기·페이지 단축키로 패널을 열 때 마우스가 있는 곳에 띄웁니다.");

            // ---- 단축키
            txtHotkey = AddHotkeyRow(tabHotkeys, "보이기/숨기기", 16);
            txtPrev = AddHotkeyRow(tabHotkeys, "이전 페이지", 50);
            txtNext = AddHotkeyRow(tabHotkeys, "다음 페이지", 84);
            AddHint(tabHotkeys, "어느 프로그램에서든 누를 수 있습니다. 칸을 비우면 사용하지 않습니다.\n페이지별 단축키는 [페이지] 탭, 버튼 단축키는 버튼 편집 창에서 지정합니다.", 12, 118);
            AddLabel(tabHotkeys, "지정된 단축키 전체", 12, 162);
            lstHotkeys = new ListBox { IntegralHeight = false, Location = new Point(12, 182), Size = new Size(398, 136), SelectionMode = SelectionMode.None };
            tabHotkeys.Controls.Add(lstHotkeys);
            tabs.SelectedIndexChanged += delegate { if (tabs.SelectedTab == tabHotkeys) RefreshHotkeyOverview(); };
            foreach (KeyCaptureBox box in new[] { txtHotkey, txtPrev, txtNext })
                box.TextChanged += delegate { if (tabs.SelectedTab == tabHotkeys) RefreshHotkeyOverview(); };

            // ---- 페이지
            lstPages = new ListBox { IntegralHeight = false, Location = new Point(12, 12), Size = new Size(290, 304) };
            lstPages.DoubleClick += delegate { RenamePage(); };
            tabPages.Controls.Add(lstPages);
            AddSideButton("추가…", 12, delegate { AddPage(); });
            AddSideButton("이름 변경…", 46, delegate { RenamePage(); });
            AddSideButton("단축키…", 80, delegate { SetPageHotkey(); });
            AddSideButton("삭제", 114, delegate { DeletePage(); });
            AddSideButton("위로 ▲", 148, delegate { MovePage(-1); });
            AddSideButton("아래로 ▼", 182, delegate { MovePage(1); });

            // ---- 백업
            AddHint(tabBackup, "모든 페이지·버튼·아이콘·단축키를 .json 파일 하나로 저장하거나 불러옵니다.\n다른 PC로 옮기거나 동료와 버튼 구성을 나눌 때 씁니다.", 12, 14);
            var btnExport = new Button { Text = "설정 내보내기 (백업)…", Location = new Point(12, 58), Size = new Size(398, 32) };
            var btnImport = new Button { Text = "설정 불러오기…", Location = new Point(12, 98), Size = new Size(398, 32) };
            btnExport.Click += delegate { CollectValues(); BackupUi.Export(this, working); };
            btnImport.Click += delegate { ImportConfig(); };
            tabBackup.Controls.Add(btnExport);
            tabBackup.Controls.Add(btnImport);
            var link = new LinkLabel { Text = "설정 파일 폴더 열기", AutoSize = true, Location = new Point(12, 146) };
            link.LinkClicked += delegate
            {
                try { Process.Start("explorer.exe", "\"" + ConfigStore.Dir + "\""); }
                catch (Exception) { }
            };
            tabBackup.Controls.Add(link);
            AddHint(tabBackup, ConfigStore.Dir, 12, 170);

            var ok = new Button { Text = "확인", Location = new Point(256, 378), Size = new Size(92, 30) };
            var cancel = new Button { Text = "취소", Location = new Point(352, 378), Size = new Size(92, 30), DialogResult = DialogResult.Cancel };
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            CancelButton = cancel;

            // DPI 배율은 모든 컨트롤을 추가한 뒤에 지정해야 크기가 두 번 커지지 않는다.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        TabPage NewTab(string text)
        {
            var t = new TabPage(text) { UseVisualStyleBackColor = true };
            tabs.TabPages.Add(t);
            return t;
        }

        static void AddLabel(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) });
        }

        static void AddHint(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y), ForeColor = SystemColors.GrayText });
        }

        static KeyCaptureBox AddHotkeyRow(Control parent, string label, int y)
        {
            AddLabel(parent, label, 12, y + 3);
            var box = new KeyCaptureBox { Location = new Point(130, y), Size = new Size(170, 23) };
            var btn = new Button { Text = "단축키 바꾸기", Location = new Point(306, y - 1), Size = new Size(104, 26) };
            btn.Click += delegate { if (box.Capturing) box.EndCapture(); else box.BeginCapture(); };
            box.CaptureChanged += delegate { btn.Text = box.Capturing ? "새 키를 누르세요" : "단축키 바꾸기"; };
            parent.Controls.Add(box);
            parent.Controls.Add(btn);
            return box;
        }

        void AddSideButton(string text, int y, EventHandler onClick)
        {
            var b = new Button { Text = text, Location = new Point(310, y), Size = new Size(100, 28) };
            b.Click += onClick;
            tabPages.Controls.Add(b);
        }

        void LoadValues()
        {
            txtHotkey.Text = working.Hotkey;
            txtPrev.Text = working.PrevPageHotkey;
            txtNext.Text = working.NextPageHotkey;
            int sizeIdx = 0;
            for (int i = 0; i < KeySizes.Length; i++)
                if (Math.Abs(KeySizes[i] - working.KeySize) < Math.Abs(KeySizes[sizeIdx] - working.KeySize)) sizeIdx = i;
            cboSize.SelectedIndex = sizeIdx;
            cboLayout.SelectedIndex = working.Vertical ? 1 : 0;
            trkOpacity.Value = Math.Max(trkOpacity.Minimum, Math.Min(trkOpacity.Maximum, working.Opacity));
            lblOpacity.Text = trkOpacity.Value + "%";
            chkTopMost.Checked = working.TopMost;
            chkShowAtCursor.Checked = working.ShowAtCursor;
            chkAutoStart.Checked = AutoStartEnabled;
            RefreshPages(working.CurrentPage);
            RefreshHotkeyOverview();
        }

        // ------------------------------------------------------------------ hotkeys

        void ApplyHotkeyBoxes()
        {
            working.Hotkey = txtHotkey.Text.Trim();
            working.PrevPageHotkey = txtPrev.Text.Trim();
            working.NextPageHotkey = txtNext.Text.Trim();
        }

        void RefreshHotkeyOverview()
        {
            ApplyHotkeyBoxes();
            lstHotkeys.BeginUpdate();
            lstHotkeys.Items.Clear();
            var entries = HotkeyMap.Collect(working);
            foreach (HotkeyEntry e in entries)
            {
                bool duplicate = entries.Count(x => string.Equals(x.Combo, e.Combo, StringComparison.OrdinalIgnoreCase)) > 1;
                lstHotkeys.Items.Add(e.Combo + "   ·   " + e.Label + (duplicate ? "   ← 겹침" : ""));
            }
            if (entries.Count == 0) lstHotkeys.Items.Add("(지정된 단축키가 없습니다)");
            lstHotkeys.EndUpdate();
        }

        // ------------------------------------------------------------------ pages

        void RefreshPages(int select)
        {
            lstPages.BeginUpdate();
            lstPages.Items.Clear();
            for (int i = 0; i < working.Pages.Count; i++)
            {
                DeckPage p = working.Pages[i];
                int used = p.Buttons.Count(b => !b.IsEmpty);
                string key = string.IsNullOrWhiteSpace(p.Hotkey) ? "" : "   [" + p.Hotkey + "]";
                lstPages.Items.Add((i + 1) + ". " + p.Name + "   (" + used + "/" + AppConfig.KeyCount + ")" + key);
            }
            lstPages.EndUpdate();
            if (lstPages.Items.Count > 0) lstPages.SelectedIndex = Math.Max(0, Math.Min(lstPages.Items.Count - 1, select));
        }

        void AddPage()
        {
            string name = InputDialog.Ask(this, "페이지 추가", "새 페이지 이름", "페이지 " + (working.Pages.Count + 1));
            if (name == null) return;
            working.Pages.Add(DeckPage.CreateEmpty(name));
            RefreshPages(working.Pages.Count - 1);
        }

        void RenamePage()
        {
            int i = lstPages.SelectedIndex;
            if (i < 0) return;
            string name = InputDialog.Ask(this, "페이지 이름 변경", "페이지 이름", working.Pages[i].Name);
            if (name == null) return;
            working.Pages[i].Name = name;
            RefreshPages(i);
        }

        void SetPageHotkey()
        {
            int i = lstPages.SelectedIndex;
            if (i < 0) return;
            ApplyHotkeyBoxes();
            DeckPage page = working.Pages[i];
            string result = HotkeyDialog.Ask(this, "페이지 단축키", "'" + page.Name + "' 페이지로 바로 이동하는 단축키", page.Hotkey,
                combo => HotkeyMap.Validate(working, combo, HotkeyKind.Page, i, -1));
            if (result == null) return;
            page.Hotkey = result;
            RefreshPages(i);
        }

        void DeletePage()
        {
            int i = lstPages.SelectedIndex;
            if (i < 0) return;
            if (working.Pages.Count <= 1)
            {
                MessageBox.Show(this, "페이지는 최소 한 개가 있어야 합니다.", "WinDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            DeckPage p = working.Pages[i];
            int used = p.Buttons.Count(b => !b.IsEmpty);
            string msg = "'" + p.Name + "' 페이지를 삭제할까요?" + (used > 0 ? "\n이 페이지의 버튼 " + used + "개도 함께 삭제됩니다." : "");
            if (MessageBox.Show(this, msg, "페이지 삭제", MessageBoxButtons.YesNo, MessageBoxIcon.Warning) != DialogResult.Yes) return;
            working.Pages.RemoveAt(i);
            if (working.CurrentPage >= working.Pages.Count) working.CurrentPage = working.Pages.Count - 1;
            RefreshPages(Math.Min(i, working.Pages.Count - 1));
        }

        void MovePage(int delta)
        {
            int i = lstPages.SelectedIndex;
            int j = i + delta;
            if (i < 0 || j < 0 || j >= working.Pages.Count) return;
            DeckPage current = working.Pages[working.CurrentPage];
            DeckPage p = working.Pages[i];
            working.Pages.RemoveAt(i);
            working.Pages.Insert(j, p);
            working.CurrentPage = working.Pages.IndexOf(current);
            RefreshPages(j);
        }

        // ------------------------------------------------------------------ backup / accept

        void ImportConfig()
        {
            AppConfig imported = BackupUi.Import(this, working);
            if (imported == null) return;
            imported.HasPosition = working.HasPosition;
            imported.WindowX = working.WindowX;
            imported.WindowY = working.WindowY;
            working = imported;
            AutoStartEnabled = chkAutoStart.Checked;
            Result = working;
            DialogResult = DialogResult.OK;
            Close();
        }

        void CollectValues()
        {
            ApplyHotkeyBoxes();
            working.KeySize = KeySizes[Math.Max(0, cboSize.SelectedIndex)];
            working.Vertical = cboLayout.SelectedIndex == 1;
            working.Opacity = trkOpacity.Value;
            working.TopMost = chkTopMost.Checked;
            working.ShowAtCursor = chkShowAtCursor.Checked;
            AutoStartEnabled = chkAutoStart.Checked;
        }

        void Accept()
        {
            CollectValues();
            var checks = new[]
            {
                Tuple.Create(txtHotkey, HotkeyKind.TogglePanel, "보이기/숨기기"),
                Tuple.Create(txtPrev, HotkeyKind.PrevPage, "이전 페이지"),
                Tuple.Create(txtNext, HotkeyKind.NextPage, "다음 페이지")
            };
            foreach (var c in checks)
            {
                string error = HotkeyMap.Validate(working, c.Item1.Text.Trim(), c.Item2, -1, -1);
                if (error != null)
                {
                    tabs.SelectedTab = tabHotkeys;
                    c.Item1.Focus();
                    MessageBox.Show(this, "[" + c.Item3 + "] " + error, "WinDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return;
                }
                string normalized = HotkeyMap.Normalize(c.Item1.Text);
                c.Item1.Text = normalized ?? "";
            }
            CollectValues();
            working.Normalize();
            Result = working;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>단축키 하나를 지정하는 작은 대화상자 (페이지 단축키 등).</summary>
    public class HotkeyDialog : Form
    {
        readonly KeyCaptureBox box;
        readonly Func<string, string> validate;

        public string Value { get; private set; }

        public HotkeyDialog(string title, string prompt, string initial, Func<string, string> validate)
        {
            this.validate = validate;
            SuspendLayout();
            Text = title;
            Font = new Font(Theme.UiFontName, 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(420, 150);

            Controls.Add(new Label { Text = prompt, AutoSize = true, Location = new Point(16, 16) });
            box = new KeyCaptureBox { Location = new Point(16, 42), Size = new Size(200, 23), Text = initial ?? "" };
            var record = new Button { Text = "단축키 바꾸기", Location = new Point(222, 41), Size = new Size(104, 26) };
            record.Click += delegate { if (box.Capturing) box.EndCapture(); else box.BeginCapture(); };
            box.CaptureChanged += delegate { record.Text = box.Capturing ? "새 키를 누르세요" : "단축키 바꾸기"; };
            var clear = new Button { Text = "지우기", Location = new Point(330, 41), Size = new Size(74, 26) };
            clear.Click += delegate { box.Text = ""; };
            Controls.Add(box);
            Controls.Add(record);
            Controls.Add(new Label { Text = "예: Ctrl+Alt+1   (비우면 단축키 없음)", AutoSize = true, Location = new Point(16, 74), ForeColor = SystemColors.GrayText });
            Controls.Add(clear);

            var ok = new Button { Text = "확인", Location = new Point(218, 106), Size = new Size(90, 30) };
            var cancel = new Button { Text = "취소", Location = new Point(314, 106), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
            ok.Click += delegate { Accept(); };
            Controls.Add(ok);
            Controls.Add(cancel);
            CancelButton = cancel;

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        void Accept()
        {
            string text = box.Text.Trim();
            string error = validate == null ? null : validate(text);
            if (error != null)
            {
                MessageBox.Show(this, error, Text, MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            Value = HotkeyMap.Normalize(text) ?? "";
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>확인하면 정리된 단축키("" 는 없음), 취소하면 null.</summary>
        public static string Ask(IWin32Window owner, string title, string prompt, string initial, Func<string, string> validate)
        {
            using (var d = new HotkeyDialog(title, prompt, initial, validate))
                return d.ShowDialog(owner) == DialogResult.OK ? d.Value : null;
        }
    }
}
