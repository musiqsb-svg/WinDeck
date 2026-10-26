using System;
using System.Collections.Generic;
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
        TabPage tabGeneral, tabBackground, tabHotkeys, tabPages, tabBackup;
        KeyCaptureBox txtHotkey, txtPrev, txtNext;
        ComboBox cboSize, cboLayout, cboCount, cboBgTarget, cboFit;
        readonly int originalCount;
        CheckBox chkBgSame;
        RadioButton rbDefault, rbColor, rbImage;
        Panel pnlBgSwatch;
        TextBox txtBgHex;
        Button btnBgColor, btnBgImage, btnBgClear;
        Label lblBgInfo, lblDim;
        TrackBar trkDim;
        BackgroundPreview bgPreview;
        bool loadingBg;
        TrackBar trkOpacity;
        Label lblOpacity;
        CheckBox chkTopMost, chkShowAtCursor, chkAutoStart;
        ListBox lstPages, lstHotkeys;

        public AppConfig Result { get; private set; }
        public bool AutoStartEnabled { get; private set; }

        public SettingsForm(AppConfig workingCopy, bool autoStart)
        {
            working = workingCopy;
            originalCount = workingCopy.ButtonCount;
            AutoStartEnabled = autoStart;
            BuildUi();
            LoadValues();
        }

        /// <summary>처음 열 탭 (0 일반, 1 배경, 2 단축키, 3 페이지, 4 백업).</summary>
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
            tabBackground = NewTab("배경");
            tabHotkeys = NewTab("단축키");
            tabPages = NewTab("페이지");
            tabBackup = NewTab("백업");
            Controls.Add(tabs);

            // ---- 일반
            AddLabel(tabGeneral, "버튼 크기", 12, 19);
            cboSize = new ComboBox { Location = new Point(150, 16), Size = new Size(170, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboSize.Items.AddRange(KeySizeNames);
            tabGeneral.Controls.Add(cboSize);

            AddLabel(tabGeneral, "버튼 수", 12, 53);
            cboCount = new ComboBox { Location = new Point(150, 50), Size = new Size(170, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboCount.Items.AddRange(new object[] { "8개", "12개", "16개" });
            tabGeneral.Controls.Add(cboCount);
            AddHint(tabGeneral, "모든 페이지 공통", 326, 53);

            AddLabel(tabGeneral, "배치", 12, 87);
            cboLayout = new ComboBox { Location = new Point(150, 84), Size = new Size(170, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboLayout.Items.AddRange(new object[] { "가로 (4열)", "세로 (4줄)" });
            tabGeneral.Controls.Add(cboLayout);
            cboCount.SelectedIndexChanged += delegate { UpdateLayoutNames(); UpdateBgPreview(); };
            cboLayout.SelectedIndexChanged += delegate { UpdateBgPreview(); };

            AddLabel(tabGeneral, "투명도", 12, 128);
            trkOpacity = new TrackBar
            {
                Location = new Point(144, 120),
                Size = new Size(200, 45),
                Minimum = 30,
                Maximum = 100,
                TickFrequency = 10,
                SmallChange = 5,
                LargeChange = 10
            };
            lblOpacity = new Label { AutoSize = true, Location = new Point(350, 128) };
            trkOpacity.ValueChanged += delegate { lblOpacity.Text = trkOpacity.Value + "%"; };
            tabGeneral.Controls.Add(trkOpacity);
            tabGeneral.Controls.Add(lblOpacity);

            chkTopMost = new CheckBox { Text = "항상 다른 창 위에 표시", AutoSize = true, Location = new Point(12, 170) };
            chkShowAtCursor = new CheckBox { Text = "단축키로 열 때 마우스 커서 위치에 표시", AutoSize = true, Location = new Point(12, 198) };
            chkAutoStart = new CheckBox { Text = "윈도우 시작 시 자동 실행", AutoSize = true, Location = new Point(12, 254) };
            tabGeneral.Controls.Add(chkTopMost);
            tabGeneral.Controls.Add(chkShowAtCursor);
            AddHint(tabGeneral, "끄면 마지막으로 옮겨 둔 자리에 나타납니다.", 30, 222);
            tabGeneral.Controls.Add(chkAutoStart);
            tips.SetToolTip(chkShowAtCursor, "보이기/숨기기·페이지 단축키로 패널을 열 때 마우스가 있는 곳에 띄웁니다.");
            tips.SetToolTip(cboCount, "줄여도 뒤쪽 버튼은 지우지 않고 숨겨서 보관합니다. 다시 늘리면 제자리에 나타납니다.");

            BuildBackgroundTab(tips);

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
            cboCount.SelectedIndex = Math.Max(0, Array.IndexOf(AppConfig.ButtonCounts, working.ButtonCount));
            UpdateLayoutNames();
            chkBgSame.Checked = working.BackgroundVSameAsH;
            cboBgTarget.SelectedIndex = working.Vertical ? 1 : 0;
            LoadBgControls();
            chkAutoStart.Checked = AutoStartEnabled;
            RefreshPages(working.CurrentPage);
            RefreshHotkeyOverview();
        }

        int SelectedCount
        {
            get { return cboCount == null || cboCount.SelectedIndex < 0 ? working.ButtonCount : AppConfig.ButtonCounts[cboCount.SelectedIndex]; }
        }

        void UpdateLayoutNames()
        {
            int n = SelectedCount / 4;
            int sel = cboLayout.SelectedIndex;
            cboLayout.Items[0] = "가로 (4 × " + n + ")";
            cboLayout.Items[1] = "세로 (" + n + " × 4)";
            cboLayout.SelectedIndex = sel;
            if (lstPages != null && lstPages.Items.Count > 0) RefreshPages(lstPages.SelectedIndex);
        }

        // ------------------------------------------------------------------ background

        void BuildBackgroundTab(ToolTip tips)
        {
            TabPage t = tabBackground;
            AddLabel(t, "배치", 12, 17);
            cboBgTarget = new ComboBox { Location = new Point(60, 14), Size = new Size(150, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboBgTarget.Items.AddRange(new object[] { "가로 배치", "세로 배치" });
            t.Controls.Add(cboBgTarget);
            chkBgSame = new CheckBox { Text = "세로에도 가로 배경 사용", AutoSize = true, Location = new Point(12, 46) };
            t.Controls.Add(chkBgSame);
            tips.SetToolTip(chkBgSame, "켜 두면 세로 배치에서도 가로 배치 배경을 씁니다. 이미지는 세로 모양에 맞게 자동으로 잘립니다.");

            AddLabel(t, "종류", 12, 79);
            rbDefault = new RadioButton { Text = "기본", AutoSize = true, Location = new Point(60, 77) };
            rbColor = new RadioButton { Text = "단색", AutoSize = true, Location = new Point(118, 77) };
            rbImage = new RadioButton { Text = "이미지", AutoSize = true, Location = new Point(176, 77) };
            t.Controls.Add(rbDefault);
            t.Controls.Add(rbColor);
            t.Controls.Add(rbImage);

            AddLabel(t, "바탕색", 12, 111);
            pnlBgSwatch = new Panel { Location = new Point(60, 107), Size = new Size(24, 24), BorderStyle = BorderStyle.FixedSingle };
            btnBgColor = new Button { Text = "색 선택…", Location = new Point(90, 105), Size = new Size(80, 27) };
            txtBgHex = new TextBox { Location = new Point(176, 107), Size = new Size(70, 23), MaxLength = 7 };
            t.Controls.Add(pnlBgSwatch);
            t.Controls.Add(btnBgColor);
            t.Controls.Add(txtBgHex);
            tips.SetToolTip(txtBgHex, "#RRGGBB 형식으로 입력하고 Enter");

            AddLabel(t, "이미지", 12, 146);
            btnBgImage = new Button { Text = "이미지 선택…", Location = new Point(60, 140), Size = new Size(110, 27) };
            btnBgClear = new Button { Text = "지우기", Location = new Point(176, 140), Size = new Size(70, 27) };
            lblBgInfo = new Label { AutoSize = true, Location = new Point(60, 172), ForeColor = SystemColors.GrayText };
            t.Controls.Add(btnBgImage);
            t.Controls.Add(btnBgClear);
            t.Controls.Add(lblBgInfo);

            AddLabel(t, "맞춤", 12, 199);
            cboFit = new ComboBox { Location = new Point(60, 196), Size = new Size(186, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboFit.Items.AddRange(new object[] { "채우기 (잘라서 꽉 채움)", "맞추기 (전체 보이기)", "늘이기" });
            t.Controls.Add(cboFit);

            AddLabel(t, "어둡게", 12, 238);
            trkDim = new TrackBar { Location = new Point(54, 230), Size = new Size(160, 45), Minimum = 0, Maximum = 80, TickFrequency = 10, SmallChange = 5, LargeChange = 10 };
            lblDim = new Label { AutoSize = true, Location = new Point(214, 238) };
            t.Controls.Add(trkDim);
            t.Controls.Add(lblDim);
            tips.SetToolTip(trkDim, "밝은 사진 위에서도 페이지 이름과 버튼이 잘 보이도록 배경을 어둡게 합니다.");

            bgPreview = new BackgroundPreview { Location = new Point(262, 14), Size = new Size(150, 226) };
            t.Controls.Add(bgPreview);
            AddHint(t, "이미지는 넣을 때 패널 크기로 줄여 저장합니다 (원본 파일은 그대로).", 12, 290);

            cboBgTarget.SelectedIndexChanged += delegate { LoadBgControls(); };
            chkBgSame.CheckedChanged += delegate { if (!loadingBg) { working.BackgroundVSameAsH = chkBgSame.Checked; LoadBgControls(); } };
            rbDefault.CheckedChanged += delegate { if (rbDefault.Checked) SetMode(PanelBackground.ModeDefault); };
            rbColor.CheckedChanged += delegate { if (rbColor.Checked) SetMode(PanelBackground.ModeColor); };
            rbImage.CheckedChanged += delegate { if (rbImage.Checked) SetMode(PanelBackground.ModeImage); };
            btnBgColor.Click += delegate { PickBgColor(); };
            txtBgHex.KeyDown += (s, e) => { if (e.KeyCode == Keys.Enter) { ApplyBgHex(); e.SuppressKeyPress = true; } };
            txtBgHex.Leave += delegate { ApplyBgHex(); };
            btnBgImage.Click += delegate { PickBgImage(); };
            btnBgClear.Click += delegate
            {
                PanelBackground bg = EditingBg;
                bg.ImageData = "";
                if (bg.Mode == PanelBackground.ModeImage) bg.Mode = PanelBackground.ModeDefault;
                LoadBgControls();
            };
            cboFit.SelectedIndexChanged += delegate
            {
                if (loadingBg) return;
                EditingBg.Fit = new[] { PanelBackground.FitFill, PanelBackground.FitFit, PanelBackground.FitStretch }[Math.Max(0, cboFit.SelectedIndex)];
                UpdateBgPreview();
            };
            trkDim.ValueChanged += delegate
            {
                lblDim.Text = trkDim.Value + "%";
                if (loadingBg) return;
                EditingBg.Dim = trkDim.Value;
                UpdateBgPreview();
            };
        }

        bool EditingVertical
        {
            get { return cboBgTarget.SelectedIndex == 1; }
        }

        /// <summary>지금 편집 중인 배경 (세로 + '가로 배경 사용' 이면 가로 배경을 보여주기만 한다).</summary>
        PanelBackground EditingBg
        {
            get { return EditingVertical ? working.BackgroundV : working.BackgroundH; }
        }

        PanelBackground ShownBg
        {
            get { return EditingVertical && chkBgSame.Checked ? working.BackgroundH : EditingBg; }
        }

        void LoadBgControls()
        {
            loadingBg = true;
            try
            {
                bool editable = !(EditingVertical && chkBgSame.Checked);
                PanelBackground bg = ShownBg;
                rbDefault.Checked = bg.Mode == PanelBackground.ModeDefault;
                rbColor.Checked = bg.Mode == PanelBackground.ModeColor;
                rbImage.Checked = bg.Mode == PanelBackground.ModeImage;
                Color baseColor = PanelBackgroundRenderer.BaseColor(bg);
                pnlBgSwatch.BackColor = bg.Mode == PanelBackground.ModeDefault ? Theme.PanelBack : baseColor;
                txtBgHex.Text = bg.Mode == PanelBackground.ModeDefault ? "" : Theme.ToHex(baseColor);
                bool hasImage = bg.ImageData.Length > 0;
                Size sz = hasImage ? PanelBackgroundRenderer.ImageSize(bg.ImageData) : Size.Empty;
                lblBgInfo.Text = hasImage ? sz.Width + " × " + sz.Height + " · " + (bg.ImageData.Length * 3 / 4 / 1024) + "KB" : "이미지 없음";
                cboFit.SelectedIndex = Math.Max(0, Array.IndexOf(new[] { PanelBackground.FitFill, PanelBackground.FitFit, PanelBackground.FitStretch }, bg.Fit));
                trkDim.Value = Math.Max(0, Math.Min(80, bg.Dim));
                lblDim.Text = trkDim.Value + "%";
                foreach (Control c in new Control[] { rbDefault, rbColor, rbImage, btnBgColor, txtBgHex, btnBgImage })
                    c.Enabled = editable;
                btnBgClear.Enabled = editable && hasImage;
                cboFit.Enabled = editable && hasImage;
                trkDim.Enabled = editable && hasImage;
            }
            finally
            {
                loadingBg = false;
            }
            UpdateBgPreview();
        }

        void SetMode(string mode)
        {
            if (loadingBg) return;
            PanelBackground bg = EditingBg;
            if (mode == PanelBackground.ModeImage && bg.ImageData.Length == 0)
            {
                if (!PickBgImage()) { LoadBgControls(); return; }
                return;
            }
            if (mode == PanelBackground.ModeColor && bg.Color.Length == 0)
            {
                string c = ColorChooser.Pick(this, working, Theme.ToHex(Theme.PanelBack), Theme.PanelBack);
                if (c == null) { LoadBgControls(); return; }
                bg.Color = c;
            }
            bg.Mode = mode;
            LoadBgControls();
        }

        void PickBgColor()
        {
            PanelBackground bg = EditingBg;
            string c = ColorChooser.Pick(this, working, bg.Color, Theme.PanelBack);
            if (c == null) return;
            bg.Color = c;
            if (bg.Mode == PanelBackground.ModeDefault) bg.Mode = PanelBackground.ModeColor;
            LoadBgControls();
        }

        void ApplyBgHex()
        {
            if (loadingBg || !txtBgHex.Enabled) return;
            PanelBackground bg = EditingBg;
            if (txtBgHex.Text.Trim().Length == 0) return;
            string hex = ColorChooser.ParseHex(txtBgHex.Text);
            if (hex == null)
            {
                System.Media.SystemSounds.Beep.Play();
                LoadBgControls();
                return;
            }
            if (hex == bg.Color && bg.Mode != PanelBackground.ModeDefault) return;
            bg.Color = hex;
            ColorChooser.AddRecent(working, hex);
            if (bg.Mode == PanelBackground.ModeDefault) bg.Mode = PanelBackground.ModeColor;
            LoadBgControls();
        }

        bool PickBgImage()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = (EditingVertical ? "세로" : "가로") + " 배치 배경 이미지 선택";
                ofd.Filter = "이미지 (*.png;*.jpg;*.jpeg;*.bmp;*.gif)|*.png;*.jpg;*.jpeg;*.bmp;*.gif|모든 파일 (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return false;
                try
                {
                    PanelBackground bg = EditingBg;
                    bg.ImageData = PanelBackgroundRenderer.LoadImageData(ofd.FileName);
                    bg.Mode = PanelBackground.ModeImage;
                    LoadBgControls();
                    return true;
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "이미지를 읽지 못했습니다.\n" + ex.Message, "WinDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                    return false;
                }
            }
        }

        void UpdateBgPreview()
        {
            if (bgPreview == null || cboBgTarget == null) return;
            bgPreview.Background = ShownBg;
            bgPreview.VerticalShape = EditingVertical;
            bgPreview.Count = SelectedCount;
            bgPreview.Invalidate();
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
            var entries = HotkeyMap.CollectAll(working);
            foreach (HotkeyEntry e in entries)
            {
                bool duplicate = entries.Count(x => string.Equals(x.Combo, e.Combo, StringComparison.OrdinalIgnoreCase)) > 1;
                lstHotkeys.Items.Add(e.Combo + "   ·   " + e.Label + (e.Hidden ? "   (숨겨진 버튼 — 쉬는 중)" : "") + (duplicate ? "   ← 겹침" : ""));
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
                int count = SelectedCount;
                int used = p.Buttons.Take(count).Count(b => !b.IsEmpty);
                int hidden = p.Buttons.Skip(count).Count(b => !b.IsEmpty);
                string key = string.IsNullOrWhiteSpace(p.Hotkey) ? "" : "   [" + p.Hotkey + "]";
                lstPages.Items.Add((i + 1) + ". " + p.Name + "   (" + used + "/" + count + (hidden > 0 ? ", 숨김 " + hidden : "") + ")" + key);
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
            working.ButtonCount = SelectedCount;
            working.BackgroundVSameAsH = chkBgSame.Checked;
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
            if (working.ButtonCount < originalCount)
            {
                ButtonLayout.HiddenInfo info = ButtonLayout.WouldHide(working, working.ButtonCount);
                if (info.Buttons > 0)
                {
                    ShrinkChoice choice = ShrinkDialog.Ask(this, originalCount, working.ButtonCount, info);
                    if (choice == ShrinkChoice.Cancel)
                    {
                        tabs.SelectedTab = tabGeneral;
                        return;
                    }
                    if (choice == ShrinkChoice.Compact) ButtonLayout.Compact(working, working.ButtonCount);
                }
            }
            working.Normalize();
            Result = working;
            DialogResult = DialogResult.OK;
            Close();
        }
    }

    /// <summary>배경 탭의 미니 패널 미리보기.</summary>
    public class BackgroundPreview : Control
    {
        public PanelBackground Background;
        public bool VerticalShape;
        public int Count = 8;

        public BackgroundPreview()
        {
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            g.Clear(SystemColors.Control);
            int cols = VerticalShape ? Count / 4 : 4, rows = VerticalShape ? 4 : Count / 4;
            const float key = 20f, gap = 4f, pad = 6f, bar = 14f;
            float pw = pad * 2 + cols * key + (cols - 1) * gap;
            float ph = pad + bar + rows * key + (rows - 1) * gap + pad;
            float s = Math.Min((Width - 4) / pw, (Height - 4) / ph);
            var panel = new Rectangle((int)((Width - pw * s) / 2), (int)((Height - ph * s) / 2), (int)(pw * s), (int)(ph * s));
            PanelBackgroundRenderer.Paint(g, panel, Background, (int)((pad + bar) * s));
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            using (var keyBrush = new SolidBrush(Color.FromArgb(235, Theme.KeyBack)))
                for (int i = 0; i < cols * rows; i++)
                {
                    int c = i % cols, r = i / cols;
                    var kr = new RectangleF(panel.X + (pad + c * (key + gap)) * s, panel.Y + (pad + bar + r * (key + gap)) * s, key * s, key * s);
                    using (var path = Theme.RoundRect(kr, key * s * 0.18f)) g.FillPath(keyBrush, path);
                }
            using (var pen = new Pen(Theme.PanelBorder)) g.DrawRectangle(pen, panel);
        }
    }

    public enum ShrinkChoice
    {
        Cancel,
        Hide,
        Compact
    }

    /// <summary>버튼 수를 줄일 때 숨겨질 버튼을 보여주고 처리 방법을 고르게 한다.</summary>
    public class ShrinkDialog : Form
    {
        readonly RadioButton rbHide, rbCompact;
        public ShrinkChoice Choice { get; private set; }

        public ShrinkDialog(int from, int to, ButtonLayout.HiddenInfo info)
        {
            SuspendLayout();
            Text = "버튼 수 줄이기";
            Font = new Font(Theme.UiFontName, 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(460, 346);

            Controls.Add(new Label
            {
                Text = "버튼 수를 " + from + "개에서 " + to + "개로 줄이면 아래 버튼 " + info.Buttons + "개가 화면에서 빠집니다."
                    + (info.Hotkeys > 0 ? "\n이 버튼들의 바로 실행 단축키 " + info.Hotkeys + "개는 숨겨진 동안 쉽니다." : ""),
                AutoSize = true,
                Location = new Point(16, 14)
            });
            var list = new ListBox { IntegralHeight = false, Location = new Point(16, 54), Size = new Size(428, 110), SelectionMode = SelectionMode.None };
            foreach (string line in info.Lines) list.Items.Add(line);
            Controls.Add(list);

            rbHide = new RadioButton { Text = "숨겨서 보관 (추천)", AutoSize = true, Location = new Point(16, 176), Checked = true };
            rbCompact = new RadioButton { Text = "빈 칸으로 당기고, 남으면 새 페이지로 옮기기", AutoSize = true, Location = new Point(16, 228) };
            Controls.Add(rbHide);
            Controls.Add(new Label { Text = "지우지 않습니다. 다시 " + from + "개로 늘리면 원래 자리에 그대로 나타납니다.", AutoSize = true, Location = new Point(34, 199), ForeColor = SystemColors.GrayText });
            Controls.Add(rbCompact);
            Controls.Add(new Label { Text = "모든 버튼이 계속 보입니다. 대신 버튼 위치가 바뀌고,\n남는 버튼은 '(이어서)' 페이지로 옮겨집니다.", AutoSize = true, Location = new Point(34, 251), ForeColor = SystemColors.GrayText });

            var ok = new Button { Text = "확인", Location = new Point(258, 302), Size = new Size(90, 30) };
            var cancel = new Button { Text = "취소", Location = new Point(354, 302), Size = new Size(90, 30), DialogResult = DialogResult.Cancel };
            ok.Click += delegate
            {
                Choice = rbCompact.Checked ? ShrinkChoice.Compact : ShrinkChoice.Hide;
                DialogResult = DialogResult.OK;
                Close();
            };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        public static ShrinkChoice Ask(IWin32Window owner, int from, int to, ButtonLayout.HiddenInfo info)
        {
            using (var d = new ShrinkDialog(from, to, info))
                return d.ShowDialog(owner) == DialogResult.OK ? d.Choice : ShrinkChoice.Cancel;
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
