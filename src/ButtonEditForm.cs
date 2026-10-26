using System;
using System.Collections.Generic;
using System.Drawing;
using System.IO;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>버튼 하나의 이름·설명·동작·모양을 편집한다.</summary>
    public class ButtonEditForm : Form
    {
        static readonly string[] HotkeyPresets =
        {
            "자주 쓰는 단축키에서 고르기…",
            "Win+D  —  바탕화면 보기",
            "Win+E  —  파일 탐색기",
            "Win+Shift+S  —  화면 캡처",
            "Win+V  —  클립보드 기록",
            "Win+L  —  화면 잠금",
            "Win+R  —  실행 창",
            "Ctrl+Shift+Esc  —  작업 관리자",
            "Alt+Tab  —  창 전환",
            "Alt+F4  —  창 닫기",
            "Ctrl+C  —  복사",
            "Ctrl+V  —  붙여넣기",
            "Ctrl+Z  —  실행 취소",
            "Ctrl+S  —  저장",
            "Ctrl+Shift+T  —  닫은 탭 다시 열기",
            "VolumeMute  —  음소거",
            "VolumeUp  —  볼륨 크게",
            "VolumeDown  —  볼륨 작게",
            "MediaPlayPause  —  재생/일시정지",
            "MediaNext  —  다음 곡"
        };

        readonly AppConfig cfg;
        readonly DeckButton original;
        readonly int pageIndex, buttonIndex;
        string iconData, backColor, foreColor;

        TextBox txtTitle, txtDesc, txtFolder, txtUrl, txtText, txtRun, txtArgs;
        KeyCaptureBox txtHotkey, txtKeyHotkey;
        Button btnRecord, btnKeyRecord, btnLibrary, btnTest, btnOk, btnCancel, btnIcon, btnIconClear, btnBackCustom, btnFore, btnForeAuto;
        ComboBox cboAction, cboPreset, cboPage;
        CheckBox chkEnter, chkPaste;
        readonly Dictionary<string, Panel> panels = new Dictionary<string, Panel>();
        readonly List<Button> swatchButtons = new List<Button>();
        readonly List<Button> recentButtons = new List<Button>();
        TextBox txtBackHex, txtForeHex;
        KeyPreview preview;
        ToolTip tips;

        public DeckButton Result { get; private set; }

        public ButtonEditForm(DeckButton button, AppConfig cfg) : this(button, cfg, -1, -1) { }

        /// <param name="pageIndex">편집 중인 버튼의 페이지 위치 (단축키 겹침 검사에서 자기 자신을 빼기 위함)</param>
        public ButtonEditForm(DeckButton button, AppConfig cfg, int pageIndex, int buttonIndex)
        {
            this.cfg = cfg;
            this.pageIndex = pageIndex;
            this.buttonIndex = buttonIndex;
            original = button ?? new DeckButton();
            iconData = original.IconData;
            backColor = original.BackColor;
            foreColor = original.ForeColor;
            BuildUi();
            LoadValues();
        }

        // ------------------------------------------------------------------ UI

        void BuildUi()
        {
            SuspendLayout();
            Text = "버튼 편집";
            Font = new Font(Theme.UiFontName, 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterScreen;
            ClientSize = new Size(624, 596);
            tips = new ToolTip();

            AddLabel(this, "버튼 이름", 16, 19);
            txtTitle = new TextBox { Location = new Point(120, 16), Size = new Size(330, 23), MaxLength = 40 };
            Controls.Add(txtTitle);

            AddLabel(this, "설명 (툴팁)", 16, 51);
            txtDesc = new TextBox { Multiline = true, Location = new Point(120, 48), Size = new Size(330, 52), MaxLength = 300 };
            Controls.Add(txtDesc);
            tips.SetToolTip(txtDesc, "패널에서 버튼 위에 마우스를 올리면 보이는 설명입니다.");

            AddLabel(this, "동작", 16, 115);
            cboAction = new ComboBox { Location = new Point(120, 112), Size = new Size(330, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboAction.Items.AddRange(ActionTypes.Names);
            Controls.Add(cboAction);

            BuildActionPanels();

            Controls.Add(new Label { Location = new Point(16, 340), Size = new Size(592, 2), BorderStyle = BorderStyle.Fixed3D });
            Controls.Add(new Label { Text = "모양", AutoSize = true, Location = new Point(16, 352), Font = new Font(Theme.UiFontName, 9f, FontStyle.Bold) });

            AddLabel(this, "아이콘", 16, 385);
            btnLibrary = new Button { Text = "아이콘 모음…", Location = new Point(120, 380), Size = new Size(110, 28) };
            btnIcon = new Button { Text = "내 이미지…", Location = new Point(234, 380), Size = new Size(110, 28) };
            btnIconClear = new Button { Text = "기본 아이콘", Location = new Point(348, 380), Size = new Size(102, 28) };
            Controls.Add(btnLibrary);
            tips.SetToolTip(btnLibrary, "WinDeck 에 들어 있는 아이콘 중에서 고릅니다.");
            Controls.Add(btnIcon);
            Controls.Add(btnIconClear);
            tips.SetToolTip(btnIcon, "PNG·JPG·ICO 이미지나 프로그램(.exe)의 아이콘을 버튼에 표시합니다.");

            AddLabel(this, "배경색", 16, 421);
            for (int i = 0; i < Theme.Swatches.Length; i++)
            {
                string hex = Theme.Swatches[i];
                var sw = new Button
                {
                    Location = new Point(120 + i * 28, 417),
                    Size = new Size(24, 24),
                    FlatStyle = FlatStyle.Flat,
                    BackColor = Theme.ParseColor(hex, Theme.KeyBack),
                    Tag = hex,
                    TabStop = false
                };
                sw.Click += delegate { backColor = hex; UpdateAppearance(); };
                tips.SetToolTip(sw, hex.Length == 0 ? "기본색" : hex);
                swatchButtons.Add(sw);
                Controls.Add(sw);
            }
            btnBackCustom = new Button { Text = "직접…", Location = new Point(376, 415), Size = new Size(74, 28) };
            Controls.Add(btnBackCustom);

            // 최근 고른 색 (클릭: 배경색, 오른쪽 클릭: 글자색) + 배경색 코드 입력
            Controls.Add(new Label { Text = "최근 색", AutoSize = true, Location = new Point(16, 453), ForeColor = SystemColors.GrayText });
            for (int i = 0; i < ColorChooser.MaxRecent; i++)
            {
                var rb = new Button { Location = new Point(120 + i * 26, 450), Size = new Size(22, 22), FlatStyle = FlatStyle.Flat, TabStop = false, Visible = false };
                rb.FlatAppearance.BorderColor = Color.FromArgb(160, 160, 160);
                rb.MouseUp += (sender, e) =>
                {
                    string hex = (string)((Button)sender).Tag;
                    if (e.Button == MouseButtons.Right) foreColor = hex;
                    else if (e.Button == MouseButtons.Left) backColor = hex;
                    else return;
                    ColorChooser.AddRecent(cfg, hex);
                    UpdateAppearance();
                };
                recentButtons.Add(rb);
                Controls.Add(rb);
            }
            txtBackHex = new TextBox { Location = new Point(376, 450), Size = new Size(74, 23), MaxLength = 7 };
            Controls.Add(txtBackHex);
            tips.SetToolTip(txtBackHex, "배경색 코드 (#RRGGBB) 입력 후 Enter. 비우면 기본색");

            AddLabel(this, "글자색", 16, 493);
            btnFore = new Button { Text = "색 선택…", Location = new Point(120, 488), Size = new Size(120, 28) };
            btnForeAuto = new Button { Text = "자동", Location = new Point(246, 488), Size = new Size(110, 28) };
            txtForeHex = new TextBox { Location = new Point(376, 490), Size = new Size(74, 23), MaxLength = 7 };
            Controls.Add(txtForeHex);
            tips.SetToolTip(txtForeHex, "글자색 코드 (#RRGGBB) 입력 후 Enter. 비우면 자동");
            Controls.Add(btnFore);
            Controls.Add(btnForeAuto);

            var hint = new Label
            {
                Text = "팁: 패널의 빈 칸에 파일·폴더·링크를 끌어다 놓아도 버튼이 바로 만들어집니다.",
                AutoSize = true,
                Location = new Point(16, 530),
                ForeColor = SystemColors.GrayText
            };
            Controls.Add(hint);

            btnTest = new Button { Text = "테스트 실행", Location = new Point(16, 552), Size = new Size(110, 32) };
            btnOk = new Button { Text = "확인", Location = new Point(412, 552), Size = new Size(96, 32) };
            btnCancel = new Button { Text = "취소", Location = new Point(514, 552), Size = new Size(96, 32), DialogResult = DialogResult.Cancel };
            Controls.Add(btnTest);
            Controls.Add(btnOk);
            Controls.Add(btnCancel);
            CancelButton = btnCancel;
            tips.SetToolTip(btnTest, "폴더·링크·프로그램 동작을 지금 바로 실행해 봅니다.");

            Controls.Add(new Label { Text = "미리보기", AutoSize = true, Location = new Point(474, 19) });
            preview = new KeyPreview { Location = new Point(474, 42), Size = new Size(136, 136) };

            Controls.Add(new Label { Text = "바로 실행 단축키", AutoSize = true, Location = new Point(474, 194) });
            txtKeyHotkey = new KeyCaptureBox { Location = new Point(474, 216), Size = new Size(136, 23) };
            btnKeyRecord = new Button { Text = "단축키 바꾸기", Location = new Point(474, 244), Size = new Size(136, 26) };
            btnKeyRecord.Click += delegate { if (txtKeyHotkey.Capturing) txtKeyHotkey.EndCapture(); else txtKeyHotkey.BeginCapture(); };
            txtKeyHotkey.CaptureChanged += delegate { btnKeyRecord.Text = txtKeyHotkey.Capturing ? "새 키를 누르세요" : "단축키 바꾸기"; };
            Controls.Add(txtKeyHotkey);
            Controls.Add(btnKeyRecord);
            Controls.Add(new Label
            {
                Text = "패널을 열지 않아도\n이 키로 바로 실행됩니다.\n(비우면 없음)",
                AutoSize = true,
                Location = new Point(474, 276),
                ForeColor = SystemColors.GrayText
            });
            tips.SetToolTip(txtKeyHotkey, "예: Ctrl+Alt+1 — 어느 프로그램에서든 이 키를 누르면 버튼이 실행됩니다.");
            Controls.Add(preview);

            // events
            txtTitle.TextChanged += delegate { UpdatePreview(); };
            cboAction.SelectedIndexChanged += delegate { ShowActionPanel(); UpdatePreview(); };
            txtRun.TextChanged += delegate { UpdatePreview(); };
            btnIcon.Click += delegate { PickIcon(); };
            btnLibrary.Click += delegate { PickFromLibrary(); };
            btnIconClear.Click += delegate { iconData = ""; UpdateAppearance(); };
            btnBackCustom.Click += delegate { string c = PickColor(backColor, Theme.KeyBack); if (c != null) { backColor = c; UpdateAppearance(); } };
            btnFore.Click += delegate { string c = PickColor(foreColor, Color.White); if (c != null) { foreColor = c; UpdateAppearance(); } };
            btnForeAuto.Click += delegate { foreColor = ""; UpdateAppearance(); };
            WireHexBox(txtBackHex, hex => backColor = hex);
            WireHexBox(txtForeHex, hex => foreColor = hex);
            btnTest.Click += delegate { TestRun(); };
            btnOk.Click += delegate { Accept(); };

            // DPI 배율은 모든 컨트롤을 추가한 뒤에 지정해야 크기가 두 번 커지지 않는다.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        void BuildActionPanels()
        {
            var p = NewPanel(ActionTypes.None);
            AddHint(p, "누르면 아무 동작도 하지 않는 버튼입니다.\n이름표나 구분선 용도로 쓸 수 있습니다.", 0, 4);

            p = NewPanel(ActionTypes.Folder);
            AddLabel(p, "폴더 경로", 0, 3);
            txtFolder = new TextBox { Location = new Point(104, 0), Size = new Size(232, 23) };
            p.Controls.Add(txtFolder);
            var b = new Button { Text = "찾아보기…", Location = new Point(342, -1), Size = new Size(92, 26) };
            b.Click += delegate { BrowseFolder(); };
            p.Controls.Add(b);
            AddHint(p, "예: C:\\업무\\자료    %USERPROFILE%\\Documents\n파일 경로를 넣으면 그 파일이 있는 폴더를 열고\n파일을 선택해 줍니다.", 104, 32);

            p = NewPanel(ActionTypes.Url);
            AddLabel(p, "링크 주소", 0, 3);
            txtUrl = new TextBox { Location = new Point(104, 0), Size = new Size(330, 23) };
            p.Controls.Add(txtUrl);
            AddHint(p, "예: https://www.naver.com   (https:// 는 생략 가능)\nmailto: · ms-settings: 같은 링크도 됩니다.", 104, 32);

            p = NewPanel(ActionTypes.Text);
            AddLabel(p, "입력할 문장", 0, 3);
            txtText = new TextBox
            {
                Multiline = true,
                AcceptsReturn = true,
                ScrollBars = ScrollBars.Vertical,
                Location = new Point(104, 0),
                Size = new Size(330, 96)
            };
            p.Controls.Add(txtText);
            chkEnter = new CheckBox { Text = "입력한 뒤 Enter 누르기", AutoSize = true, Location = new Point(104, 102) };
            chkPaste = new CheckBox { Text = "붙여넣기 방식으로 입력 (긴 글이나 입력이 안 될 때)", AutoSize = true, Location = new Point(104, 126) };
            p.Controls.Add(chkEnter);
            p.Controls.Add(chkPaste);
            AddHint(p, "마지막으로 쓰던 창의 커서 위치에 입력됩니다.\n줄바꿈은 Enter 키로 입력됩니다.", 104, 150);

            p = NewPanel(ActionTypes.Hotkey);
            AddLabel(p, "단축키", 0, 3);
            txtHotkey = new KeyCaptureBox { Location = new Point(104, 0), Size = new Size(224, 23), AppendMode = false };
            p.Controls.Add(txtHotkey);
            btnRecord = new Button { Text = "단축키 바꾸기", Location = new Point(334, -1), Size = new Size(100, 26) };
            btnRecord.Click += delegate { if (txtHotkey.Capturing) txtHotkey.EndCapture(); else txtHotkey.BeginCapture(); };
            txtHotkey.CaptureChanged += delegate { btnRecord.Text = txtHotkey.Capturing ? "새 키를 누르세요" : "단축키 바꾸기"; };
            p.Controls.Add(btnRecord);
            cboPreset = new ComboBox { Location = new Point(104, 34), Size = new Size(330, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboPreset.Items.AddRange(HotkeyPresets);
            cboPreset.SelectedIndex = 0;
            cboPreset.SelectedIndexChanged += delegate { ApplyPreset(); };
            p.Controls.Add(cboPreset);
            AddHint(p, "직접 입력도 됩니다: Ctrl+Shift+N, Win+E, F5\n여러 키를 차례로 누르려면 쉼표로 구분: Ctrl+A, Ctrl+C\n'단축키 바꾸기'를 누르고 원하는 키를 누르면\n자동으로 적힙니다.", 104, 66);

            p = NewPanel(ActionTypes.Run);
            AddLabel(p, "파일 경로", 0, 3);
            txtRun = new TextBox { Location = new Point(104, 0), Size = new Size(232, 23) };
            p.Controls.Add(txtRun);
            b = new Button { Text = "찾아보기…", Location = new Point(342, -1), Size = new Size(92, 26) };
            b.Click += delegate { BrowseFile(); };
            p.Controls.Add(b);
            AddLabel(p, "실행 옵션", 0, 37);
            txtArgs = new TextBox { Location = new Point(104, 34), Size = new Size(330, 23) };
            p.Controls.Add(txtArgs);
            AddHint(p, "프로그램(.exe) · 바로가기(.lnk) · 문서 · 이미지 등\n어떤 파일이든 실행합니다.\n실행 옵션은 필요할 때만 적으세요. (예: --incognito)", 104, 66);

            p = NewPanel(ActionTypes.Page);
            AddLabel(p, "이동할 페이지", 0, 3);
            cboPage = new ComboBox { Location = new Point(104, 0), Size = new Size(330, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            foreach (DeckPage page in cfg.Pages) cboPage.Items.Add(page.Name);
            p.Controls.Add(cboPage);
            AddHint(p, "누르면 선택한 페이지의 버튼들로 바뀝니다.\n(패널 위에서 마우스 휠이나 ◀ ▶ 로도 넘길 수 있습니다)", 104, 32);
        }

        Panel NewPanel(string action)
        {
            var p = new Panel { Location = new Point(16, 148), Size = new Size(436, 184), Visible = false };
            panels[action] = p;
            Controls.Add(p);
            return p;
        }

        static void AddLabel(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label { Text = text, AutoSize = true, Location = new Point(x, y) });
        }

        static void AddHint(Control parent, string text, int x, int y)
        {
            parent.Controls.Add(new Label
            {
                Text = text,
                AutoSize = true,
                Location = new Point(x, y),
                ForeColor = SystemColors.GrayText
            });
        }

        // ------------------------------------------------------------------ values

        void LoadValues()
        {
            txtTitle.Text = original.Title;
            txtDesc.Text = original.Description;
            txtKeyHotkey.Text = original.Hotkey ?? "";
            int idx = Array.IndexOf(ActionTypes.All, original.Action);
            string target = original.Target ?? "";
            switch (original.Action)
            {
                case ActionTypes.Folder: txtFolder.Text = target; break;
                case ActionTypes.Url: txtUrl.Text = target; break;
                case ActionTypes.Text:
                    txtText.Text = target.Replace("\r\n", "\n").Replace("\n", "\r\n");
                    chkEnter.Checked = original.PressEnter;
                    chkPaste.Checked = original.UsePaste;
                    break;
                case ActionTypes.Hotkey: txtHotkey.Text = target; break;
                case ActionTypes.Run:
                    txtRun.Text = target;
                    txtArgs.Text = original.Args;
                    break;
                case ActionTypes.Page:
                    cboPage.SelectedIndex = cfg.Pages.FindIndex(p => p.Id == target);
                    break;
            }
            if (cboPage.SelectedIndex < 0 && cboPage.Items.Count > 0) cboPage.SelectedIndex = 0;
            cboAction.SelectedIndex = idx < 0 ? 0 : idx;
            ShowActionPanel();
            UpdateAppearance();
        }

        string SelectedAction
        {
            get { return ActionTypes.All[Math.Max(0, cboAction.SelectedIndex)]; }
        }

        DeckButton BuildButton()
        {
            var b = new DeckButton
            {
                Title = txtTitle.Text.Trim(),
                Description = txtDesc.Text.Trim(),
                Action = SelectedAction,
                IconData = iconData ?? "",
                BackColor = backColor ?? "",
                ForeColor = foreColor ?? ""
            };
            switch (b.Action)
            {
                case ActionTypes.Folder: b.Target = txtFolder.Text.Trim(); break;
                case ActionTypes.Url: b.Target = txtUrl.Text.Trim(); break;
                case ActionTypes.Text:
                    b.Target = txtText.Text;
                    b.PressEnter = chkEnter.Checked;
                    b.UsePaste = chkPaste.Checked;
                    break;
                case ActionTypes.Hotkey: b.Target = txtHotkey.Text.Trim(); break;
                case ActionTypes.Run:
                    b.Target = txtRun.Text.Trim().Trim('"');
                    b.Args = txtArgs.Text.Trim();
                    break;
                case ActionTypes.Page:
                    b.Target = cboPage.SelectedIndex >= 0 ? cfg.Pages[cboPage.SelectedIndex].Id : "";
                    break;
            }
            return b;
        }

        void ShowActionPanel()
        {
            string a = SelectedAction;
            foreach (var kv in panels) kv.Value.Visible = kv.Key == a;
            btnTest.Enabled = a == ActionTypes.Folder || a == ActionTypes.Url || a == ActionTypes.Run;
        }

        /// <summary>#RRGGBB 입력칸: Enter 또는 칸을 벗어나면 적용, 비우면 기본/자동, 잘못된 값은 되돌린다.</summary>
        void WireHexBox(TextBox box, Action<string> apply)
        {
            Action commit = delegate
            {
                string text = box.Text.Trim();
                if (text.Length == 0) apply("");
                else
                {
                    string hex = ColorChooser.ParseHex(text);
                    if (hex == null)
                    {
                        System.Media.SystemSounds.Beep.Play();
                        UpdateAppearance();
                        return;
                    }
                    apply(hex);
                    ColorChooser.AddRecent(cfg, hex);
                }
                UpdateAppearance();
            };
            box.KeyDown += (sender, e) => { if (e.KeyCode == Keys.Enter) { commit(); e.SuppressKeyPress = true; } };
            box.Leave += delegate { commit(); };
        }

        void UpdateAppearance()
        {
            for (int i = 0; i < recentButtons.Count; i++)
            {
                Button rb = recentButtons[i];
                bool has = i < cfg.RecentColors.Count;
                rb.Visible = has;
                if (!has) continue;
                rb.Tag = cfg.RecentColors[i];
                rb.BackColor = Theme.ParseColor(cfg.RecentColors[i], Theme.KeyBack);
                tips.SetToolTip(rb, cfg.RecentColors[i] + "  ·  클릭: 배경색  ·  오른쪽 클릭: 글자색");
            }
            if (!txtBackHex.Focused) txtBackHex.Text = backColor ?? "";
            if (!txtForeHex.Focused) txtForeHex.Text = foreColor ?? "";
            foreach (Button sw in swatchButtons)
            {
                bool selected = string.Equals((string)sw.Tag, backColor ?? "", StringComparison.OrdinalIgnoreCase);
                sw.FlatAppearance.BorderColor = selected ? Theme.Accent : Color.FromArgb(160, 160, 160);
                sw.FlatAppearance.BorderSize = selected ? 3 : 1;
            }
            btnIconClear.Enabled = !string.IsNullOrEmpty(iconData);
            btnForeAuto.Enabled = !string.IsNullOrEmpty(foreColor);
            UpdatePreview();
        }

        void UpdatePreview()
        {
            if (preview == null) return;
            preview.Data = BuildButton();
            preview.Invalidate();
        }

        // ------------------------------------------------------------------ actions

        void BrowseFolder()
        {
            string initial = ActionRunner.ExpandPath(txtFolder.Text);
            string path = FolderPicker.Pick(this, initial, "열 폴더 선택");
            if (path == null) return;
            txtFolder.Text = path;
            if (txtTitle.Text.Trim().Length == 0) txtTitle.Text = new DirectoryInfo(path).Name.TrimEnd('\\');
        }

        void BrowseFile()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "실행할 파일 선택";
                ofd.Filter = "프로그램 (*.exe;*.lnk;*.bat;*.cmd)|*.exe;*.lnk;*.bat;*.cmd|모든 파일 (*.*)|*.*";
                ofd.DereferenceLinks = false;
                string current = ActionRunner.ExpandPath(txtRun.Text);
                try
                {
                    if (current.Length > 0 && Path.IsPathRooted(current) && Directory.Exists(Path.GetDirectoryName(current)))
                        ofd.InitialDirectory = Path.GetDirectoryName(current);
                }
                catch (ArgumentException) { }
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                txtRun.Text = ofd.FileName;
                if (txtTitle.Text.Trim().Length == 0) txtTitle.Text = Path.GetFileNameWithoutExtension(ofd.FileName);
            }
        }

        void ApplyPreset()
        {
            if (cboPreset.SelectedIndex <= 0) return;
            string item = (string)cboPreset.SelectedItem;
            int sep = item.IndexOf("  —  ", StringComparison.Ordinal);
            txtHotkey.Text = item.Substring(0, sep);
            if (txtTitle.Text.Trim().Length == 0) txtTitle.Text = item.Substring(sep + 5);
            cboPreset.SelectedIndex = 0;
        }

        void PickFromLibrary()
        {
            string data = IconPickerForm.Pick(this, Theme.ParseColor(backColor, Theme.KeyBack));
            if (data == null) return;
            iconData = data;
            UpdateAppearance();
        }

        void PickIcon()
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "버튼 이미지 선택";
                ofd.Filter = "이미지 (*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico)|*.png;*.jpg;*.jpeg;*.bmp;*.gif;*.ico|프로그램 아이콘 (*.exe)|*.exe|모든 파일 (*.*)|*.*";
                if (ofd.ShowDialog(this) != DialogResult.OK) return;
                try
                {
                    iconData = IconCache.LoadIconData(ofd.FileName);
                    UpdateAppearance();
                }
                catch (Exception ex)
                {
                    MessageBox.Show(this, "이미지를 읽지 못했습니다.\n" + ex.Message, "버튼 편집", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        string PickColor(string current, Color fallback)
        {
            return ColorChooser.Pick(this, cfg, current, fallback);
        }

        void TestRun()
        {
            DeckButton b = BuildButton();
            string error = CheckButton(b);
            if (error != null)
            {
                MessageBox.Show(this, error, "버튼 편집", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            try
            {
                ActionRunner.Run(b, null);
            }
            catch (Exception ex)
            {
                MessageBox.Show(this, ex.Message, "테스트 실행", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }
        }

        static string CheckButton(DeckButton b)
        {
            switch (b.Action)
            {
                case ActionTypes.Folder:
                    if (b.Target.Length == 0) return "열 폴더 경로를 입력하세요.";
                    break;
                case ActionTypes.Url:
                    if (b.Target.Length == 0) return "링크 주소를 입력하세요.";
                    break;
                case ActionTypes.Text:
                    if (b.Target.Length == 0) return "입력할 문장을 적어 주세요.";
                    break;
                case ActionTypes.Hotkey:
                    if (b.Target.Length == 0) return "단축키를 입력하세요. ('단축키 바꾸기'를 누르고 원하는 키를 눌러도 됩니다)";
                    try { KeyCombo.ParseSequence(b.Target); }
                    catch (FormatException ex) { return "단축키 형식이 올바르지 않습니다.\n" + ex.Message; }
                    break;
                case ActionTypes.Run:
                    if (b.Target.Length == 0) return "실행할 파일 경로를 입력하세요.";
                    break;
                case ActionTypes.Page:
                    if (b.Target.Length == 0) return "이동할 페이지를 선택하세요.";
                    break;
            }
            return null;
        }

        void Accept()
        {
            DeckButton b = BuildButton();
            string error = CheckButton(b);
            if (error != null)
            {
                MessageBox.Show(this, error, "버튼 편집", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            if (b.Action == ActionTypes.Folder)
            {
                string p = ActionRunner.ExpandPath(b.Target);
                if (!p.StartsWith("shell:", StringComparison.OrdinalIgnoreCase) && !Directory.Exists(p) && !File.Exists(p)
                    && MessageBox.Show(this, "폴더가 지금은 없습니다:\n" + p + "\n\n그래도 저장할까요?", "버튼 편집",
                        MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                    return;
            }
            if (b.Action == ActionTypes.Hotkey) b.Target = KeyCombo.NormalizeSequence(b.Target);
            string keyError = HotkeyMap.Validate(cfg, txtKeyHotkey.Text.Trim(), HotkeyKind.Button, pageIndex, buttonIndex);
            if (keyError != null)
            {
                txtKeyHotkey.Focus();
                MessageBox.Show(this, "[바로 실행 단축키] " + keyError, "버튼 편집", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return;
            }
            b.Hotkey = HotkeyMap.Normalize(txtKeyHotkey.Text) ?? "";
            Result = b;
            DialogResult = DialogResult.OK;
            Close();
        }
    }
}
