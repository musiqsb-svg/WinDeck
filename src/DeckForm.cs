using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>
    /// 항상 위에 떠 있는 버튼 패널.
    /// WS_EX_NOACTIVATE 창이라 클릭해도 활성화되지 않으므로, 문장·단축키가 원래 쓰던 창에 그대로 들어간다.
    /// </summary>
    public class DeckForm : Form, IBackgroundHost
    {
        const int HotkeyIdBase = 0x5D01;

        AppConfig cfg;
        readonly bool systemHooks;
        readonly float scale;
        readonly List<DeckKey> keys = new List<DeckKey>();
        readonly GlyphButton btnPrev, btnNext, btnSettings, btnHide;
        readonly ToolTip tip;
        readonly ContextMenuStrip keyMenu, panelMenu;
        readonly Timer foregroundTimer;
        readonly Timer modifierTimer;
        Bitmap bgCache;
        bool bgHasImage;
        Color barText = Theme.Text, barTextDim = Theme.TextDim;
        readonly Dictionary<int, HotkeyEntry> hotkeys = new Dictionary<int, HotkeyEntry>();
        string lastHotkeyFailure = "";
        int pendingPage = -1, pendingButton = -1;
        DateTime pendingDeadline;
        readonly Font titleFont, countFont, tipTitleFont, tipBodyFont, tipDetailFont;
        NotifyIcon tray;
        ContextMenuStrip trayMenu;
        ToolStripMenuItem trayToggleItem, trayAutoStartItem;
        ToolStripMenuItem miEdit, miRun, miCopy, miPaste, miClear;
        DeckButton copiedButton;
        Rectangle titleRect;
        bool dragging;
        Point dragCursor, dragOrigin;
        int wheelAccum;
        bool exiting, hideHintShown, dialogOpen, dwmBorder;
        FileSystemWatcher watcher;
        readonly Timer reloadTimer, maintenanceTimer;
        ToolStripMenuItem trayUpdateItem;
        UpdateInfo pendingUpdate;
        bool updateChecking, balloonIsUpdate;
        int exitAttempts;

        public DeckForm(AppConfig config, bool systemHooks)
        {
            cfg = config;
            this.systemHooks = systemHooks;
            using (Graphics g = Graphics.FromHwnd(IntPtr.Zero)) scale = g.DpiX / 96f;

            Text = "WinDeck";
            FormBorderStyle = FormBorderStyle.None;
            ShowInTaskbar = false;
            StartPosition = FormStartPosition.Manual;
            BackColor = Theme.PanelBack;
            DoubleBuffered = true;
            Font = new Font(Theme.UiFontName, 9f);
            Icon = AppIcon.CreateIcon(32);
            TopMost = cfg.TopMost;

            titleFont = new Font(Theme.UiFontName, 9f, FontStyle.Bold);
            countFont = new Font(Theme.UiFontName, 8.25f);
            tipTitleFont = new Font(Theme.UiFontName, 9.5f, FontStyle.Bold);
            tipBodyFont = new Font(Theme.UiFontName, 9f);
            tipDetailFont = new Font(Theme.UiFontName, 8.25f);

            tip = new ToolTip { ShowAlways = true, InitialDelay = 450, ReshowDelay = 120, AutoPopDelay = 20000, OwnerDraw = true };
            tip.Popup += Tip_Popup;
            tip.Draw += Tip_Draw;

            keyMenu = BuildKeyMenu();
            panelMenu = BuildPanelMenu();
            ContextMenuStrip = panelMenu;
            tip.SetToolTip(this, "·");

            for (int i = 0; i < AppConfig.MaxKeyCount; i++)
            {
                var k = new DeckKey(i);
                k.KeyClicked += Key_Clicked;
                k.Wheel += HandleWheel;
                k.DragEnter += Key_DragEnter;
                k.DragDrop += Key_DragDrop;
                k.ContextMenuStrip = keyMenu;
                keys.Add(k);
                Controls.Add(k);
            }

            btnPrev = AddBarButton(Glyphs.ChevronLeft, "이전 페이지", delegate { ChangePage(-1); });
            btnNext = AddBarButton(Glyphs.ChevronRight, "다음 페이지", delegate { ChangePage(1); });
            btnSettings = AddBarButton(Glyphs.Settings, "설정", delegate { OpenSettings(); });
            btnHide = AddBarButton(Glyphs.Minimize, "숨기기", delegate { HidePanel(); });

            if (systemHooks) BuildTray();

            foregroundTimer = new Timer { Interval = 250 };
            foregroundTimer.Tick += delegate { ActionRunner.TrackForeground(); };
            foregroundTimer.Start();

            // 버튼 단축키: 사용자가 Ctrl/Alt 등을 뗀 뒤에 실행해야 문장이 Ctrl+글자로 입력되지 않는다.
            modifierTimer = new Timer { Interval = 25 };
            modifierTimer.Tick += delegate
            {
                if (InputSender.ModifiersDown() && DateTime.Now < pendingDeadline) return;
                modifierTimer.Stop();
                RunPendingButton();
            };

            // 다른 곳(사용자, 다른 WinDeck)에서 config.json 이 바뀌면 잠시 뒤 다시 읽는다.
            reloadTimer = new Timer { Interval = 500 };
            reloadTimer.Tick += delegate
            {
                if (dialogOpen) return; // 대화상자를 닫은 뒤 다시 확인
                reloadTimer.Stop();
                SyncFromDisk();
            };

            // 하루 백업, 하루 한 번 업데이트 확인 (시작 1분 뒤, 그 뒤로 한 시간마다 점검)
            maintenanceTimer = new Timer { Interval = 60 * 1000 };
            maintenanceTimer.Tick += delegate
            {
                maintenanceTimer.Interval = 60 * 60 * 1000;
                DoMaintenance();
            };

            ApplyConfig(true);
            if (systemHooks)
            {
                StartConfigWatcher();
                maintenanceTimer.Start();
            }
        }

        public AppConfig Config
        {
            get { return cfg; }
        }

        DeckPage CurrentPage
        {
            get { return cfg.Pages[Math.Max(0, Math.Min(cfg.Pages.Count - 1, cfg.CurrentPage))]; }
        }

        int S(float v)
        {
            return (int)Math.Round(v * scale);
        }

        // ------------------------------------------------------------------ window behaviour

        protected override CreateParams CreateParams
        {
            get
            {
                CreateParams cp = base.CreateParams;
                cp.ExStyle |= Native.WS_EX_NOACTIVATE | Native.WS_EX_TOOLWINDOW;
                return cp;
            }
        }

        protected override bool ShowWithoutActivation
        {
            get { return true; }
        }

        protected override void WndProc(ref Message m)
        {
            if (m.Msg == Native.WM_MOUSEACTIVATE)
            {
                m.Result = (IntPtr)Native.MA_NOACTIVATE;
                return;
            }
            if (m.Msg == Native.WM_HOTKEY)
            {
                HotkeyEntry entry;
                if (hotkeys.TryGetValue(m.WParam.ToInt32(), out entry)) OnHotkey(entry);
                return;
            }
            base.WndProc(ref m);
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            try
            {
                int pref = Native.DWMWCP_ROUND;
                bool round = Native.DwmSetWindowAttribute(Handle, Native.DWMWA_WINDOW_CORNER_PREFERENCE, ref pref, 4) == 0;
                int color = Theme.PanelBorder.R | (Theme.PanelBorder.G << 8) | (Theme.PanelBorder.B << 16);
                dwmBorder = round && Native.DwmSetWindowAttribute(Handle, Native.DWMWA_BORDER_COLOR, ref color, 4) == 0;
            }
            catch (Exception)
            {
                dwmBorder = false;
            }
            RegisterHotkeys();
        }

        protected override void OnHandleDestroyed(EventArgs e)
        {
            UnregisterHotkeys();
            base.OnHandleDestroyed(e);
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            if (tray == null) return;
            if (ConfigStore.LoadWarning != null)
            {
                ShowWarning(ConfigStore.LoadWarning);
            }
            else if (ConfigStore.Previous != null)
            {
                OfferPreviousConfig(ConfigStore.Previous);
            }
            else if (ConfigStore.IsNew)
            {
                Balloon(8000, "WinDeck 이 실행되었습니다",
                    "버튼 클릭: 실행 · 우클릭: 편집 · 빈 칸에 파일/폴더/링크 끌어놓기\n"
                    + HotkeyText + " 로 패널을 숨기거나 다시 열 수 있습니다.", ToolTipIcon.Info);
            }
            else if (ConfigStore.UpgradedFrom != null)
            {
                Balloon(6000, "WinDeck v" + AppInfo.Version + " 로 업데이트되었습니다",
                    "버튼과 설정은 그대로입니다. 업데이트 전 설정은 [설정 > 백업]에 보관해 두었습니다.", ToolTipIcon.Info);
            }
        }

        protected override void OnFormClosing(FormClosingEventArgs e)
        {
            if (!exiting && e.CloseReason == CloseReason.UserClosing)
            {
                e.Cancel = true;
                HidePanel();
                return;
            }
            base.OnFormClosing(e);
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                foregroundTimer.Dispose();
                modifierTimer.Dispose();
                reloadTimer.Dispose();
                maintenanceTimer.Dispose();
                if (watcher != null) watcher.Dispose();
                if (bgCache != null) bgCache.Dispose();
                if (tray != null) tray.Dispose();
            }
            base.Dispose(disposing);
        }

        // ------------------------------------------------------------------ layout & paint

        public void ApplyConfig(bool first)
        {
            if (Math.Abs(Opacity - cfg.Opacity / 100.0) > 0.001) Opacity = cfg.Opacity / 100.0;
            if (TopMost != cfg.TopMost) TopMost = cfg.TopMost;
            DoLayoutPanel();
            if (first)
            {
                Rectangle wa = Screen.PrimaryScreen.WorkingArea;
                Location = cfg.HasPosition
                    ? new Point(cfg.WindowX, cfg.WindowY)
                    : new Point(wa.Right - Width - S(24), wa.Bottom - Height - S(24));
            }
            ClampToScreen();
            RefreshKeys();
            RegisterHotkeys();
        }

        void DoLayoutPanel()
        {
            int pad = S(10), gap = S(8), k = S(cfg.KeySize);
            int barTop = S(7), barH = S(26);
            int cols = cfg.Columns;
            int rows = cfg.Rows;
            int gridTop = barTop + barH + S(7);
            int width = pad * 2 + cols * k + (cols - 1) * gap;
            int height = gridTop + rows * k + (rows - 1) * gap + pad;
            ClientSize = new Size(width, height);

            for (int i = 0; i < keys.Count; i++)
            {
                int c = i % cols, r = i / cols;
                keys[i].Visible = i < cfg.ButtonCount; // 줄인 뒤쪽 칸은 숨기기만 하고 데이터는 보관
                keys[i].Bounds = new Rectangle(pad + c * (k + gap), gridTop + r * (k + gap), k, k);
            }

            int bw = S(26);
            btnPrev.Bounds = new Rectangle(pad - S(3), barTop, bw, barH);
            btnNext.Bounds = new Rectangle(btnPrev.Right, barTop, bw, barH);
            btnHide.Bounds = new Rectangle(width - pad + S(3) - bw, barTop, bw, barH);
            btnSettings.Bounds = new Rectangle(btnHide.Left - bw, barTop, bw, barH);
            titleRect = new Rectangle(btnNext.Right + S(6), barTop, Math.Max(0, btnSettings.Left - btnNext.Right - S(10)), barH);
            RebuildBackground();
        }

        /// <summary>
        /// 배경 비트맵을 패널 크기로 한 번 만들어 둔다 (이미지일 때만). 버튼·막대는 이 비트맵의 일부를 잘라 그린다.
        /// 배경 밝기에 따라 페이지 이름 글자색도 정한다.
        /// </summary>
        void RebuildBackground()
        {
            PanelBackground bg = cfg.ActiveBackground;
            BackColor = PanelBackgroundRenderer.BaseColor(bg);
            if (bgCache != null)
            {
                bgCache.Dispose();
                bgCache = null;
            }
            bgHasImage = PanelBackgroundRenderer.HasImage(bg);
            if (bgHasImage) bgCache = PanelBackgroundRenderer.Build(bg, ClientSize, S(40));
            bool light = bgCache != null
                ? PanelBackgroundRenderer.IsLight(bgCache, new Rectangle(0, 0, ClientSize.Width, S(40)))
                : Theme.Luminance(BackColor) > 0.6;
            barText = light ? Color.FromArgb(28, 28, 30) : Theme.Text;
            barTextDim = light ? Color.FromArgb(84, 84, 90) : Theme.TextDim;
            foreach (Control c in Controls) c.Invalidate();
            Invalidate();
        }

        protected override void OnPaintBackground(PaintEventArgs e)
        {
            if (bgCache != null) e.Graphics.DrawImage(bgCache, e.ClipRectangle, e.ClipRectangle, GraphicsUnit.Pixel);
            else base.OnPaintBackground(e);
        }

        public void PaintChildBackground(Graphics g, Control child)
        {
            if (bgCache != null) g.DrawImage(bgCache, new Rectangle(0, 0, child.Width, child.Height), child.Bounds, GraphicsUnit.Pixel);
            else g.Clear(BackColor);
        }

        public Color BarForeColor
        {
            get { return barText; }
        }

        public Color BarDimColor
        {
            get { return barTextDim; }
        }

        void ClampToScreen()
        {
            Rectangle wa = Screen.FromRectangle(Bounds).WorkingArea;
            int x = Math.Max(wa.Left, Math.Min(Left, wa.Right - Width));
            int y = Math.Max(wa.Top, Math.Min(Top, wa.Bottom - Height));
            if (x != Left || y != Top) Location = new Point(x, y);
        }

        void RefreshKeys()
        {
            DeckPage page = CurrentPage;
            for (int i = 0; i < keys.Count; i++)
            {
                keys[i].Data = page.Buttons[i];
                tip.SetToolTip(keys[i], "·");
                keys[i].Invalidate();
            }
            bool multi = cfg.Pages.Count > 1;
            btnPrev.Enabled = multi;
            btnNext.Enabled = multi;
            Invalidate();
            UpdateTrayText();
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            Graphics g = e.Graphics;
            if (!dwmBorder)
                using (var pen = new Pen(Theme.PanelBorder))
                    g.DrawRectangle(pen, 0, 0, ClientSize.Width - 1, ClientSize.Height - 1);

            if (titleRect.Width <= 0) return;
            string name = CurrentPage.Name;
            string count = (cfg.CurrentPage + 1) + "/" + cfg.Pages.Count;
            const TextFormatFlags flags = TextFormatFlags.Left | TextFormatFlags.VerticalCenter | TextFormatFlags.SingleLine
                | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;
            Size cs = TextRenderer.MeasureText(g, count, countFont, Size.Empty, flags);
            Size ns = TextRenderer.MeasureText(g, name, titleFont, Size.Empty, flags);
            // 좁은 세로 배치에서는 페이지 번호보다 이름을 우선해서 보여준다.
            bool showCount = ns.Width + S(6) + cs.Width <= titleRect.Width;
            int nameWidth = showCount ? ns.Width : titleRect.Width;
            var nameRect = new Rectangle(titleRect.X, titleRect.Y, nameWidth, titleRect.Height);
            if (bgHasImage) // 사진 위에서도 읽히도록 그림자
            {
                Rectangle shadow = nameRect;
                shadow.Offset(1, 1);
                TextRenderer.DrawText(g, name, titleFont, shadow, barText == Theme.Text ? Color.Black : Color.White, flags | TextFormatFlags.EndEllipsis);
            }
            TextRenderer.DrawText(g, name, titleFont, nameRect, barText, flags | TextFormatFlags.EndEllipsis);
            if (showCount)
                TextRenderer.DrawText(g, count, countFont,
                    new Rectangle(titleRect.X + nameWidth + S(6), titleRect.Y, cs.Width + S(2), titleRect.Height),
                    barTextDim, flags);
        }

        GlyphButton AddBarButton(string glyph, string tipTitle, EventHandler onClick)
        {
            var b = new GlyphButton(glyph, tipTitle);
            b.Clicked += onClick;
            tip.SetToolTip(b, "·");
            Controls.Add(b);
            return b;
        }

        // ------------------------------------------------------------------ dragging & wheel

        protected override void OnMouseDown(MouseEventArgs e)
        {
            base.OnMouseDown(e);
            if (e.Button != MouseButtons.Left) return;
            dragging = true;
            dragCursor = Cursor.Position;
            dragOrigin = Location;
        }

        protected override void OnMouseMove(MouseEventArgs e)
        {
            base.OnMouseMove(e);
            if (!dragging)
            {
                Cursor = titleRect.Contains(e.Location) ? Cursors.SizeAll : Cursors.Default;
                return;
            }
            Point c = Cursor.Position;
            int x = dragOrigin.X + c.X - dragCursor.X;
            int y = dragOrigin.Y + c.Y - dragCursor.Y;
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int snap = S(16);
            if (Math.Abs(x - wa.Left) < snap) x = wa.Left;
            else if (Math.Abs(x + Width - wa.Right) < snap) x = wa.Right - Width;
            if (Math.Abs(y - wa.Top) < snap) y = wa.Top;
            else if (Math.Abs(y + Height - wa.Bottom) < snap) y = wa.Bottom - Height;
            Location = new Point(x, y);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            base.OnMouseUp(e);
            if (!dragging) return;
            dragging = false;
            SavePosition();
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            base.OnMouseWheel(e);
            HandleWheel(e.Delta);
        }

        void HandleWheel(int delta)
        {
            wheelAccum += delta;
            while (wheelAccum >= 120) { wheelAccum -= 120; ChangePage(-1); }
            while (wheelAccum <= -120) { wheelAccum += 120; ChangePage(1); }
        }

        void SavePosition()
        {
            SyncFromDisk();
            cfg.WindowX = Left;
            cfg.WindowY = Top;
            cfg.HasPosition = true;
            SaveConfig();
        }

        // ------------------------------------------------------------------ pages

        public bool GoToPage(string pageId)
        {
            SyncFromDisk();
            int idx = cfg.Pages.FindIndex(p => p.Id == pageId);
            if (idx < 0) return false;
            cfg.CurrentPage = idx;
            SaveConfig();
            RefreshKeys();
            return true;
        }

        void ChangePage(int delta)
        {
            SyncFromDisk();
            int n = cfg.Pages.Count;
            if (n <= 1) return;
            cfg.CurrentPage = ((cfg.CurrentPage + delta) % n + n) % n;
            SaveConfig();
            RefreshKeys();
        }

        void AddPage()
        {
            string name = AskText("페이지 추가", "새 페이지 이름", "페이지 " + (cfg.Pages.Count + 1));
            if (name == null) return;
            SyncFromDisk();
            cfg.Pages.Add(DeckPage.CreateEmpty(name));
            cfg.CurrentPage = cfg.Pages.Count - 1;
            ConfigChanged();
        }

        void RenamePage()
        {
            SyncFromDisk();
            string pageId = CurrentPage.Id;
            string name = AskText("페이지 이름 변경", "페이지 이름", CurrentPage.Name);
            if (name == null) return;
            SyncFromDisk();
            DeckPage page = cfg.Pages.Find(p => p.Id == pageId);
            if (page == null) return;
            page.Name = name;
            ConfigChanged();
        }

        string AskText(string title, string prompt, string initial)
        {
            if (dialogOpen) return null;
            dialogOpen = true;
            try { return InputDialog.Ask(this, title, prompt, initial); }
            finally { dialogOpen = false; }
        }

        // ------------------------------------------------------------------ keys

        void Key_Clicked(object sender, EventArgs e)
        {
            var k = (DeckKey)sender;
            if (k.Data == null || k.Data.IsEmpty)
            {
                EditKey(k.Index);
                return;
            }
            Execute(k);
        }

        void Execute(DeckKey k)
        {
            RunButton(k.Data, k);
        }

        /// <summary>버튼 동작 실행. flashKey 가 있으면 성공/실패를 테두리 색으로 보여준다.</summary>
        void RunButton(DeckButton b, DeckKey flashKey)
        {
            try
            {
                ActionRunner.Run(b, this);
                if (flashKey != null) flashKey.Flash(Theme.Success);
                if (b.Action == ActionTypes.Page && !Visible) RevealPanel();
            }
            catch (ActionException ex)
            {
                if (flashKey != null) flashKey.Flash(Theme.Error);
                ShowWarning(ex.Message);
            }
            catch (Exception ex)
            {
                if (flashKey != null) flashKey.Flash(Theme.Error);
                ErrorLog.Write(ex);
                ShowWarning("실행 중 오류가 발생했습니다.\n" + ex.Message);
            }
        }

        public void EditKey(int index)
        {
            if (dialogOpen) return;
            SyncFromDisk();
            dialogOpen = true;
            try
            {
                DeckPage page = CurrentPage;
                string pageId = page.Id;
                using (var f = new ButtonEditForm(page.Buttons[index].Clone(), cfg, cfg.Pages.IndexOf(page), index))
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return;
                    // 편집하는 사이 설정 파일이 바뀌었으면 다시 읽은 설정에 이 버튼만 넣는다.
                    SyncFromDisk();
                    DeckPage target = cfg.Pages.Find(p => p.Id == pageId);
                    if (target == null)
                    {
                        ShowWarning("편집하던 페이지가 다른 곳에서 삭제되어 버튼을 저장하지 못했습니다.");
                        return;
                    }
                    target.Buttons[index] = f.Result;
                    ConfigChanged();
                    keys[index].Flash(Theme.Accent);
                }
            }
            finally
            {
                dialogOpen = false;
            }
        }

        ContextMenuStrip BuildKeyMenu()
        {
            var m = new ContextMenuStrip();
            miEdit = new ToolStripMenuItem("버튼 편집…", null, delegate { if (MenuKey != null) EditKey(MenuKey.Index); });
            miEdit.Font = new Font(m.Font, FontStyle.Bold);
            miRun = new ToolStripMenuItem("지금 실행", null, delegate { if (MenuKey != null) Execute(MenuKey); });
            miCopy = new ToolStripMenuItem("복사", null, delegate { if (MenuKey != null) copiedButton = MenuKey.Data.Clone(); });
            miPaste = new ToolStripMenuItem("붙여넣기", null, delegate { PasteInto(MenuKey); });
            miClear = new ToolStripMenuItem("비우기", null, delegate { ClearKey(MenuKey); });
            m.Items.AddRange(new ToolStripItem[]
            {
                miEdit, miRun, new ToolStripSeparator(), miCopy, miPaste, miClear, new ToolStripSeparator(),
                new ToolStripMenuItem("페이지 이름 변경…", null, delegate { RenamePage(); }),
                new ToolStripMenuItem("페이지 추가…", null, delegate { AddPage(); }),
                new ToolStripMenuItem("설정…", null, delegate { OpenSettings(); })
            });
            m.Opening += delegate
            {
                menuKey = m.SourceControl as DeckKey;
                DeckKey k = menuKey;
                bool has = k != null && k.Data != null && !k.Data.IsEmpty;
                miRun.Enabled = has && k.Data.Action != ActionTypes.None;
                miCopy.Enabled = has;
                miClear.Enabled = has;
                miPaste.Enabled = copiedButton != null;
            };
            return m;
        }

        DeckKey menuKey;

        DeckKey MenuKey
        {
            get { return menuKey; }
        }

        ContextMenuStrip BuildPanelMenu()
        {
            var m = new ContextMenuStrip();
            m.Items.AddRange(new ToolStripItem[]
            {
                new ToolStripMenuItem("설정…", null, delegate { OpenSettings(); }),
                new ToolStripMenuItem("페이지 이름 변경…", null, delegate { RenamePage(); }),
                new ToolStripMenuItem("페이지 추가…", null, delegate { AddPage(); }),
                new ToolStripSeparator(),
                new ToolStripMenuItem("숨기기", null, delegate { HidePanel(); }),
                new ToolStripMenuItem("종료", null, delegate { ExitApp(); })
            });
            return m;
        }

        void PasteInto(DeckKey k)
        {
            if (k == null || copiedButton == null) return;
            if (!ConfirmReplace(k.Data, "붙여넣은 버튼")) return;
            SyncFromDisk();
            DeckButton pasted = copiedButton.Clone();
            pasted.Hotkey = ""; // 같은 단축키가 두 버튼에 걸리지 않도록 복사본에는 단축키를 넣지 않는다
            CurrentPage.Buttons[k.Index] = pasted;
            ConfigChanged();
            k.Flash(Theme.Accent);
        }

        void ClearKey(DeckKey k)
        {
            if (k == null || k.Data == null || k.Data.IsEmpty) return;
            string name = string.IsNullOrWhiteSpace(k.Data.Title) ? "이" : "'" + k.Data.Title + "'";
            if (MessageBox.Show(this, name + " 버튼을 비울까요?", "WinDeck", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return;
            SyncFromDisk();
            CurrentPage.Buttons[k.Index] = new DeckButton();
            ConfigChanged();
        }

        bool ConfirmReplace(DeckButton existing, string what)
        {
            if (existing == null || existing.IsEmpty) return true;
            string name = string.IsNullOrWhiteSpace(existing.Title) ? "기존" : "'" + existing.Title + "'";
            return MessageBox.Show(this, name + " 버튼을 " + what + "(으)로 바꿀까요?", "WinDeck",
                MessageBoxButtons.YesNo, MessageBoxIcon.Question) == DialogResult.Yes;
        }

        // ------------------------------------------------------------------ drag & drop onto a key

        void Key_DragEnter(object sender, DragEventArgs e)
        {
            bool ok = e.Data.GetDataPresent(DataFormats.FileDrop)
                || e.Data.GetDataPresent(DataFormats.UnicodeText)
                || e.Data.GetDataPresent(DataFormats.Text);
            if (!ok) e.Effect = DragDropEffects.None;
            else if ((e.AllowedEffect & DragDropEffects.Copy) != 0) e.Effect = DragDropEffects.Copy;
            else if ((e.AllowedEffect & DragDropEffects.Link) != 0) e.Effect = DragDropEffects.Link;
            else e.Effect = DragDropEffects.None;
        }

        void Key_DragDrop(object sender, DragEventArgs e)
        {
            var k = (DeckKey)sender;
            DeckButton b = ButtonFromDrop(e.Data);
            if (b == null) return;
            var files = e.Data.GetData(DataFormats.FileDrop) as string[];
            string json = files != null && files.Length == 1 && files[0].EndsWith(".json", StringComparison.OrdinalIgnoreCase) ? files[0] : null;
            // 끌어온 프로그램이 응답을 기다리지 않도록 확인 창은 드롭이 끝난 뒤에 띄운다.
            BeginInvoke(new Action(delegate
            {
                // 받은 설정 파일(.json)을 패널에 끌어놓으면 버튼 대신 설정 불러오기를 권한다.
                if (json != null && ConfigStore.LooksLikeConfigFile(json))
                {
                    DialogResult r = MessageBox.Show(this, "'" + Path.GetFileName(json) + "' 은(는) WinDeck 설정 파일입니다.\n\n"
                        + "[예]  이 설정 불러오기 (지금 설정은 자동으로 백업)\n[아니요]  이 파일을 여는 버튼 만들기",
                        "WinDeck", MessageBoxButtons.YesNoCancel, MessageBoxIcon.Question);
                    if (r == DialogResult.Yes)
                    {
                        ImportFile(json, false);
                        return;
                    }
                    if (r != DialogResult.No) return;
                }
                if (!ConfirmReplace(k.Data, "'" + b.Title + "'")) return;
                SyncFromDisk();
                CurrentPage.Buttons[k.Index] = b;
                ConfigChanged();
                k.Flash(Theme.Accent);
            }));
        }

        static DeckButton ButtonFromDrop(IDataObject data)
        {
            var files = data.GetData(DataFormats.FileDrop) as string[];
            if (files != null && files.Length > 0)
            {
                string path = files[0];
                if (Directory.Exists(path))
                {
                    string name = new DirectoryInfo(path).Name.TrimEnd('\\');
                    return new DeckButton { Title = name, Action = ActionTypes.Folder, Target = path };
                }
                string title = Path.GetFileNameWithoutExtension(path);
                if (Path.GetExtension(path).Equals(".url", StringComparison.OrdinalIgnoreCase))
                {
                    string url = ReadInternetShortcut(path);
                    if (url != null) return new DeckButton { Title = title, Action = ActionTypes.Url, Target = url };
                }
                return new DeckButton { Title = title, Action = ActionTypes.Run, Target = path };
            }

            string text = (data.GetData(DataFormats.UnicodeText) as string) ?? (data.GetData(DataFormats.Text) as string);
            if (string.IsNullOrWhiteSpace(text)) return null;
            text = text.Trim();
            Uri uri;
            if ((text.StartsWith("http://", StringComparison.OrdinalIgnoreCase) || text.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
                || text.StartsWith("www.", StringComparison.OrdinalIgnoreCase))
                && !text.Contains("\n")
                && Uri.TryCreate(ActionRunner.NormalizeUrl(text), UriKind.Absolute, out uri))
            {
                string host = uri.Host.StartsWith("www.") ? uri.Host.Substring(4) : uri.Host;
                return new DeckButton { Title = host, Action = ActionTypes.Url, Target = text };
            }
            string firstLine = text.Split('\n')[0].Trim();
            string shortTitle = firstLine.Length > 10 ? firstLine.Substring(0, 10) + "…" : firstLine;
            return new DeckButton { Title = shortTitle, Action = ActionTypes.Text, Target = text };
        }

        static string ReadInternetShortcut(string path)
        {
            try
            {
                foreach (string line in File.ReadAllLines(path))
                    if (line.StartsWith("URL=", StringComparison.OrdinalIgnoreCase)) return line.Substring(4).Trim();
            }
            catch (Exception) { }
            return null;
        }

        // ------------------------------------------------------------------ tooltips

        class TipContent
        {
            public string Title, Body, Detail;
        }

        TipContent GetTip(Control c)
        {
            var k = c as DeckKey;
            if (k != null)
            {
                DeckButton b = k.Data;
                if (b == null) return null;
                if (b.IsEmpty)
                    return new TipContent
                    {
                        Title = "빈 버튼",
                        Body = "클릭해서 설정하거나, 파일·폴더·링크를 이 칸에 끌어다 놓으세요.",
                        Detail = "우클릭: 메뉴"
                    };
                string title = string.IsNullOrWhiteSpace(b.Title) ? ActionTypes.DisplayName(b.Action) : b.Title;
                string detail = ActionDetail(b);
                if (!string.IsNullOrWhiteSpace(b.Hotkey)) detail += "\n바로 실행 단축키 · " + b.Hotkey;
                return new TipContent { Title = title, Body = b.Description, Detail = detail };
            }
            var gb = c as GlyphButton;
            if (gb != null)
            {
                if (gb == btnHide) return new TipContent { Title = gb.TipTitle, Detail = "다시 열기: " + HotkeyText + " 또는 트레이 아이콘" };
                if (gb == btnPrev || gb == btnNext)
                {
                    string key = gb == btnPrev ? cfg.PrevPageHotkey : cfg.NextPageHotkey;
                    return new TipContent
                    {
                        Title = gb.TipTitle,
                        Detail = (string.IsNullOrWhiteSpace(key) ? "" : "단축키 · " + key + "\n") + "패널 위에서 마우스 휠로도 넘길 수 있습니다."
                    };
                }
                return new TipContent { Title = gb.TipTitle };
            }
            if (c == this)
                return new TipContent
                {
                    Title = CurrentPage.Name,
                    Body = string.IsNullOrWhiteSpace(CurrentPage.Hotkey) ? null : "이 페이지 바로가기 · " + CurrentPage.Hotkey,
                    Detail = "끌어서 이동 · 휠: 페이지 넘기기 · 우클릭: 메뉴"
                };
            return null;
        }

        string ActionDetail(DeckButton b)
        {
            switch (b.Action)
            {
                case ActionTypes.Folder: return "폴더 열기 · " + Shorten(b.Target, 60);
                case ActionTypes.Url: return "링크 열기 · " + Shorten(b.Target, 60);
                case ActionTypes.Text:
                    return "문장 입력 · " + Shorten(b.Target.Replace("\r", "").Replace("\n", " / "), 50)
                        + (b.PressEnter ? "  (+Enter)" : "");
                case ActionTypes.Hotkey: return "단축키 · " + b.Target;
                case ActionTypes.Run:
                    return "실행 · " + Shorten(b.Target, 60) + (string.IsNullOrWhiteSpace(b.Args) ? "" : " " + Shorten(b.Args, 30));
                case ActionTypes.Page:
                    DeckPage p = cfg.Pages.FirstOrDefault(x => x.Id == b.Target);
                    return "페이지 이동 · " + (p == null ? "(없는 페이지)" : p.Name);
            }
            return "동작 없음";
        }

        static string Shorten(string s, int max)
        {
            s = s ?? "";
            return s.Length <= max ? s : s.Substring(0, max - 1) + "…";
        }

        const TextFormatFlags TipFlags = TextFormatFlags.WordBreak | TextFormatFlags.NoPrefix | TextFormatFlags.Left | TextFormatFlags.Top;

        void Tip_Popup(object sender, PopupEventArgs e)
        {
            TipContent t = GetTip(e.AssociatedControl);
            if (t == null)
            {
                e.Cancel = true;
                return;
            }
            Rectangle[] parts;
            e.ToolTipSize = MeasureTip(t, out parts);
        }

        Size MeasureTip(TipContent t, out Rectangle[] parts)
        {
            int pad = S(10), maxW = S(320);
            var proposed = new Size(maxW, int.MaxValue);
            Size ts = TextRenderer.MeasureText(t.Title ?? "", tipTitleFont, proposed, TipFlags);
            Size bs = string.IsNullOrWhiteSpace(t.Body) ? Size.Empty : TextRenderer.MeasureText(t.Body, tipBodyFont, proposed, TipFlags);
            Size ds = string.IsNullOrWhiteSpace(t.Detail) ? Size.Empty : TextRenderer.MeasureText(t.Detail, tipDetailFont, proposed, TipFlags);
            int w = Math.Max(ts.Width, Math.Max(bs.Width, ds.Width));
            int y = pad;
            var tr = new Rectangle(pad, y, w, ts.Height);
            y = tr.Bottom;
            Rectangle br = Rectangle.Empty, dr = Rectangle.Empty;
            if (bs != Size.Empty)
            {
                br = new Rectangle(pad, y + S(4), w, bs.Height);
                y = br.Bottom;
            }
            if (ds != Size.Empty)
            {
                dr = new Rectangle(pad, y + S(6), w, ds.Height);
                y = dr.Bottom;
            }
            parts = new[] { tr, br, dr };
            return new Size(w + pad * 2, y + pad);
        }

        void Tip_Draw(object sender, DrawToolTipEventArgs e)
        {
            TipContent t = GetTip(e.AssociatedControl) ?? new TipContent { Title = "" };
            Rectangle[] parts;
            MeasureTip(t, out parts);
            Graphics g = e.Graphics;
            using (var bg = new SolidBrush(Theme.TipBack)) g.FillRectangle(bg, e.Bounds);
            using (var pen = new Pen(Theme.PanelBorder)) g.DrawRectangle(pen, 0, 0, e.Bounds.Width - 1, e.Bounds.Height - 1);
            TextRenderer.DrawText(g, t.Title ?? "", tipTitleFont, parts[0], Theme.Text, TipFlags);
            if (parts[1] != Rectangle.Empty) TextRenderer.DrawText(g, t.Body, tipBodyFont, parts[1], Color.FromArgb(214, 214, 220), TipFlags);
            if (parts[2] != Rectangle.Empty) TextRenderer.DrawText(g, t.Detail, tipDetailFont, parts[2], Theme.TextDim, TipFlags);
        }

        // ------------------------------------------------------------------ show / hide / hotkey

        string HotkeyText
        {
            get { return string.IsNullOrWhiteSpace(cfg.Hotkey) ? "트레이 아이콘" : cfg.Hotkey; }
        }

        public void TogglePanel()
        {
            if (Visible) HidePanel();
            else ShowPanel();
        }

        public void ShowPanel()
        {
            ShowPanel(false);
        }

        /// <summary>atCursor: 마우스 커서 아래에 표시, 아니면 저장된 위치에 표시.</summary>
        public void ShowPanel(bool atCursor)
        {
            if (atCursor) MoveNearCursor();
            else if (!Visible && cfg.HasPosition) Location = new Point(cfg.WindowX, cfg.WindowY);
            ClampToScreen();
            if (!Visible) Show();
            Native.SetWindowPos(Handle, cfg.TopMost ? Native.HWND_TOPMOST : Native.HWND_TOP, 0, 0, 0, 0,
                Native.SWP_NOMOVE | Native.SWP_NOSIZE | Native.SWP_NOACTIVATE | Native.SWP_SHOWWINDOW);
        }

        /// <summary>상단 막대가 커서 바로 아래 오도록 가운데 맞춤 (화면 밖으로 나가지 않게).</summary>
        void MoveNearCursor()
        {
            Point c = Cursor.Position;
            Rectangle wa = Screen.FromPoint(c).WorkingArea;
            int x = c.X - Width / 2;
            int y = c.Y - S(20);
            x = Math.Max(wa.Left, Math.Min(x, wa.Right - Width));
            y = Math.Max(wa.Top, Math.Min(y, wa.Bottom - Height));
            Location = new Point(x, y);
        }

        void RevealPanel()
        {
            if (!Visible) ShowPanel(cfg.ShowAtCursor);
        }

        /// <summary>전역 단축키가 눌렸을 때.</summary>
        public void OnHotkey(HotkeyEntry entry)
        {
            // Alt·Win 을 누른 채로 단축키를 쓰면, 키를 뗄 때 앞 창의 메뉴나 시작 메뉴가 열린다. 빈 키 입력으로 막는다.
            if (InputSender.AltOrWinDown()) InputSender.SendMaskKey();
            switch (entry.Kind)
            {
                case HotkeyKind.TogglePanel:
                    if (Visible) HidePanel();
                    else ShowPanel(cfg.ShowAtCursor);
                    break;
                case HotkeyKind.PrevPage:
                    ChangePage(-1);
                    RevealPanel();
                    break;
                case HotkeyKind.NextPage:
                    ChangePage(1);
                    RevealPanel();
                    break;
                case HotkeyKind.Page:
                    SyncFromDisk();
                    if (entry.PageIndex >= 0 && entry.PageIndex < cfg.Pages.Count && entry.PageIndex != cfg.CurrentPage)
                    {
                        cfg.CurrentPage = entry.PageIndex;
                        SaveConfig();
                        RefreshKeys();
                    }
                    RevealPanel();
                    break;
                case HotkeyKind.Button:
                    pendingPage = entry.PageIndex;
                    pendingButton = entry.ButtonIndex;
                    pendingDeadline = DateTime.Now.AddMilliseconds(1500);
                    modifierTimer.Stop();
                    modifierTimer.Start();
                    break;
            }
        }

        void RunPendingButton()
        {
            int p = pendingPage, b = pendingButton;
            pendingPage = -1;
            if (p < 0 || p >= cfg.Pages.Count || b < 0 || b >= cfg.Pages[p].Buttons.Count) return;
            DeckButton button = cfg.Pages[p].Buttons[b];
            if (button.IsEmpty || button.Action == ActionTypes.None) return;
            RunButton(button, Visible && p == cfg.CurrentPage ? keys[b] : null);
        }

        public void HidePanel()
        {
            Hide();
            if (!hideHintShown && tray != null)
            {
                hideHintShown = true;
                Balloon(4000, "WinDeck 은 계속 실행 중입니다",
                    HotkeyText + " 를 누르거나 트레이 아이콘을 클릭하면 다시 열립니다.", ToolTipIcon.Info);
            }
        }

        /// <summary>설정의 모든 전역 단축키를 다시 등록한다. 등록 실패 목록이 바뀌었을 때만 알린다.</summary>
        void RegisterHotkeys()
        {
            if (!systemHooks || !IsHandleCreated) return;
            UnregisterHotkeys();
            var failed = new List<string>();
            var used = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
            int id = HotkeyIdBase;
            foreach (HotkeyEntry entry in HotkeyMap.Collect(cfg))
            {
                KeyCombo combo;
                if (!KeyCombo.TryParse(entry.Combo, out combo)) continue;
                if (!used.Add(entry.Combo))
                {
                    failed.Add(entry.Combo + " — " + entry.Label + " (다른 항목과 겹침)");
                    continue;
                }
                if (Native.RegisterHotKey(Handle, id, combo.HotkeyModifiers | Native.MOD_NOREPEAT, combo.Key))
                    hotkeys[id++] = entry;
                else
                    failed.Add(entry.Combo + " — " + entry.Label);
            }
            string failure = string.Join("\n", failed.ToArray());
            if (failure.Length > 0 && failure != lastHotkeyFailure && tray != null)
            {
                string text = failure + "\n다른 프로그램이 쓰고 있을 수 있습니다. 설정에서 바꿔 주세요.";
                Balloon(8000, "일부 단축키를 등록하지 못했습니다", text.Length > 250 ? text.Substring(0, 249) + "…" : text, ToolTipIcon.Warning);
            }
            lastHotkeyFailure = failure;
        }

        void UnregisterHotkeys()
        {
            if (IsHandleCreated)
                foreach (int id in hotkeys.Keys) Native.UnregisterHotKey(Handle, id);
            hotkeys.Clear();
        }

        /// <summary>현재 등록된 단축키 (점검용).</summary>
        public List<HotkeyEntry> RegisteredHotkeys
        {
            get { return new List<HotkeyEntry>(hotkeys.Values); }
        }

        // ------------------------------------------------------------------ tray

        void BuildTray()
        {
            trayToggleItem = new ToolStripMenuItem("패널 숨기기", null, delegate { TogglePanel(); });
            trayToggleItem.Font = new Font(trayToggleItem.Font, FontStyle.Bold);
            trayAutoStartItem = new ToolStripMenuItem("윈도우 시작 시 자동 실행", null, delegate { ToggleAutoStart(); });
            trayUpdateItem = new ToolStripMenuItem("업데이트 확인…", null, delegate
            {
                if (pendingUpdate != null) ShowUpdateDialog(pendingUpdate);
                else CheckForUpdatesNow();
            });
            trayMenu = new ContextMenuStrip();
            trayMenu.Items.AddRange(new ToolStripItem[]
            {
                trayToggleItem,
                new ToolStripMenuItem("설정…", null, delegate { ShowPanel(); OpenSettings(); }),
                new ToolStripSeparator(),
                new ToolStripMenuItem("설정 내보내기 (백업)…", null, delegate { ExportConfig(); }),
                new ToolStripMenuItem("설정 불러오기…", null, delegate { ImportConfig(); }),
                new ToolStripSeparator(),
                trayAutoStartItem,
                trayUpdateItem,
                new ToolStripSeparator(),
                new ToolStripMenuItem("종료", null, delegate { ExitApp(); })
            });
            trayMenu.Opening += delegate
            {
                trayToggleItem.Text = Visible ? "패널 숨기기" : "패널 보이기";
                trayAutoStartItem.Checked = AutoStart.IsEnabled();
            };

            tray = new NotifyIcon
            {
                Icon = AppIcon.CreateIcon(SystemInformation.SmallIconSize.Width),
                Text = "WinDeck",
                ContextMenuStrip = trayMenu,
                Visible = true
            };
            tray.MouseClick += (s, e) => { if (e.Button == MouseButtons.Left) TogglePanel(); };
            tray.BalloonTipClicked += delegate { if (balloonIsUpdate && pendingUpdate != null) ShowUpdateDialog(pendingUpdate); };
        }

        void Balloon(int ms, string title, string text, ToolTipIcon icon)
        {
            Balloon(ms, title, text, icon, false);
        }

        /// <summary>트레이 알림. update 가 true 면 알림을 눌렀을 때 업데이트 창을 연다.</summary>
        void Balloon(int ms, string title, string text, ToolTipIcon icon, bool update)
        {
            if (tray == null) return;
            balloonIsUpdate = update;
            tray.ShowBalloonTip(ms, title, text, icon);
        }

        void UpdateTrayText()
        {
            if (tray == null) return;
            string t = "WinDeck · " + CurrentPage.Name;
            tray.Text = t.Length > 63 ? t.Substring(0, 63) : t;
        }

        void ToggleAutoStart()
        {
            try
            {
                AutoStart.Set(!AutoStart.IsEnabled());
            }
            catch (Exception ex)
            {
                ShowWarning("자동 실행 설정을 바꾸지 못했습니다.\n" + ex.Message);
            }
        }

        public void ExitApp()
        {
            exiting = true;
            SaveBeforeExit();
            if (watcher != null) watcher.EnableRaisingEvents = false;
            if (tray != null) tray.Visible = false;
            Close();
        }

        void SaveBeforeExit()
        {
            try
            {
                // 다른 곳에서 더 최근에 바꾼 파일은 덮어쓰지 않는다 (지금 설정은 바뀔 때마다 이미 저장되어 있다).
                if (!ConfigStore.ChangedOnDisk()) ConfigStore.Save(cfg);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }

        /// <summary>
        /// 설치 파일·빌드 스크립트가 보낸 종료 요청(WinDeck.exe --exit): 열린 편집 창은 취소하고, 설정을 저장한 뒤 끝낸다.
        /// 메시지 상자처럼 닫을 수 없는 창이 3초 넘게 남아 있으면 저장만 하고 바로 끝낸다.
        /// </summary>
        public void RequestExit()
        {
            var modals = Application.OpenForms.Cast<Form>().Where(f => f != this && f.Visible && f.Modal).ToList();
            if (modals.Count == 0 && !dialogOpen)
            {
                ExitApp();
                return;
            }
            if (++exitAttempts > 15)
            {
                exiting = true;
                SaveBeforeExit();
                if (tray != null) tray.Visible = false;
                Environment.Exit(0);
            }
            foreach (Form f in modals) f.DialogResult = DialogResult.Cancel;
            var retry = new Timer { Interval = 200 };
            retry.Tick += delegate
            {
                retry.Dispose();
                RequestExit();
            };
            retry.Start();
        }

        // ------------------------------------------------------------------ settings & backup

        public void OpenSettings()
        {
            if (dialogOpen) return;
            SyncFromDisk();
            dialogOpen = true;
            UpdateInfo requested = null;
            try
            {
                using (var f = new SettingsForm(ConfigStore.Clone(cfg), AutoStart.IsEnabled()))
                {
                    if (f.ShowDialog(this) != DialogResult.OK) return;
                    try
                    {
                        if (f.AutoStartEnabled != AutoStart.IsEnabled()) AutoStart.Set(f.AutoStartEnabled);
                    }
                    catch (Exception ex)
                    {
                        ShowWarning("자동 실행 설정을 바꾸지 못했습니다.\n" + ex.Message);
                    }
                    ApplySettingsResult(f.Result, f.BackupReason);
                    requested = f.UpdateRequested;
                }
            }
            finally
            {
                dialogOpen = false;
            }
            if (requested != null) ShowUpdateDialog(requested);
        }

        /// <summary>
        /// 설정 창에서 확인을 눌렀을 때. 창을 열어 둔 사이 단축키로 바꾼 현재 페이지와 패널 위치는 지금 값을 지키고,
        /// 바뀌는 내용이 있으면 적용 전 설정을 백업한다.
        /// </summary>
        public void ApplySettingsResult(AppConfig result, string backupReason)
        {
            SyncFromDisk();
            result.HasPosition = cfg.HasPosition;
            result.WindowX = cfg.WindowX;
            result.WindowY = cfg.WindowY;
            if (backupReason == "before_settings")
            {
                string livePage = CurrentPage.Id;
                int idx = result.Pages.FindIndex(p => p.Id == livePage);
                if (idx >= 0) result.CurrentPage = idx;
            }
            if (ConfigStore.ToJson(result) != ConfigStore.ToJson(cfg))
            {
                try { ConfigStore.AutoBackup(cfg, backupReason); }
                catch (Exception ex) { ErrorLog.Write(ex); }
            }
            cfg = result;
            SaveConfig();
            ApplyConfig(false);
        }

        void ExportConfig()
        {
            if (dialogOpen) return;
            dialogOpen = true;
            try { BackupUi.Export(this, cfg); }
            finally { dialogOpen = false; }
        }

        void ImportConfig()
        {
            if (dialogOpen) return;
            dialogOpen = true;
            string path;
            try { path = BackupUi.PickImportFile(this, cfg.LastImportFolder); }
            finally { dialogOpen = false; }
            if (path != null) ImportFile(path, true);
        }

        /// <summary>
        /// 설정 파일을 불러온다. 내용은 WinDeck 설정 폴더에 저장되므로, 받은 파일은 바탕 화면·다운로드 어디에 있든
        /// 불러온 뒤 지우거나 옮겨도 된다.
        /// </summary>
        void ImportFile(string path, bool confirm)
        {
            if (dialogOpen) return;
            dialogOpen = true;
            AppConfig n;
            try { n = BackupUi.ReadImport(this, path, confirm); }
            finally { dialogOpen = false; }
            if (n == null) return;
            SyncFromDisk();
            try { ConfigStore.AutoBackup(cfg, "before_import"); }
            catch (Exception ex) { ErrorLog.Write(ex); }
            ConfigStore.KeepLocalState(cfg, n);
            n.LastImportFolder = Path.GetDirectoryName(path);
            cfg = n;
            SaveConfig();
            ApplyConfig(false);
            ShowPanel();
            Balloon(6000, "설정을 불러왔습니다",
                "내용은 WinDeck 에 저장되었으므로 받은 파일(" + Path.GetFileName(path) + ")은 지우거나 옮겨도 됩니다.", ToolTipIcon.Info);
        }

        /// <summary>다른 위치(이전 포터블 폴더, 설치본 등)에서 찾은 설정을 불러올지 묻는다.</summary>
        void OfferPreviousConfig(FoundConfig p)
        {
            ConfigStore.Previous = null;
            string head = p.IsNewLocation
                ? "이 위치에서 WinDeck 을 처음 실행했습니다.\n전에 쓰던 설정을 찾았습니다."
                : "다른 위치의 WinDeck 에 더 최근에 저장된 설정이 있습니다.";
            string msg = head + "\n\n" + p.Path + "\n마지막 저장 " + p.Time.ToString("yyyy-MM-dd HH:mm") + " · " + ConfigStore.Describe(p.Config)
                + "\n\n이 설정을 불러올까요?" + (p.IsNewLocation ? "" : "\n(지금 설정은 백업해 둡니다)");
            DialogResult r;
            dialogOpen = true;
            try { r = MessageBox.Show(this, msg, "WinDeck - 이전 설정", MessageBoxButtons.YesNo, MessageBoxIcon.Question); }
            finally { dialogOpen = false; }
            if (r != DialogResult.Yes) return;
            if (!p.IsNewLocation)
            {
                try { ConfigStore.AutoBackup(cfg, "before_previous"); }
                catch (Exception ex) { ErrorLog.Write(ex); }
            }
            cfg = p.Config;
            SaveConfig();
            ApplyConfig(false);
            if (cfg.HasPosition)
            {
                Location = new Point(cfg.WindowX, cfg.WindowY);
                ClampToScreen();
            }
            Balloon(5000, "이전 설정을 불러왔습니다", ConfigStore.Describe(cfg), ToolTipIcon.Info);
        }

        // ------------------------------------------------------------------ 다른 곳에서 바뀐 설정 파일

        void StartConfigWatcher()
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.Dir);
                watcher = new FileSystemWatcher(ConfigStore.Dir, "config.json");
                watcher.NotifyFilter = NotifyFilters.LastWrite | NotifyFilters.FileName | NotifyFilters.Size | NotifyFilters.CreationTime;
                watcher.Changed += delegate { QueueReloadCheck(); };
                watcher.Created += delegate { QueueReloadCheck(); };
                watcher.Renamed += delegate { QueueReloadCheck(); };
                watcher.EnableRaisingEvents = true;
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }

        void QueueReloadCheck()
        {
            try
            {
                BeginInvoke(new Action(delegate
                {
                    reloadTimer.Stop();
                    reloadTimer.Start();
                }));
            }
            catch (InvalidOperationException) { }
        }

        /// <summary>
        /// 마지막으로 읽거나 저장한 뒤 다른 곳에서 config.json 이 바뀌었으면 다시 읽는다.
        /// 메모리에 있던 예전 설정으로 새 파일을 덮어써서 최근 설정이 되돌아가는 일을 막는다. 다시 읽었으면 true.
        /// </summary>
        bool SyncFromDisk()
        {
            if (!ConfigStore.ChangedOnDisk()) return false;
            AppConfig n;
            try
            {
                n = ConfigStore.ReloadFromDisk();
            }
            catch (Exception)
            {
                return false; // 쓰는 중이거나 잘못된 파일: 지금 설정을 유지 (다음 저장 때 그 파일은 백업된다)
            }
            try { ConfigStore.AutoBackup(cfg, "before_reload"); }
            catch (Exception ex) { ErrorLog.Write(ex); }
            ConfigStore.KeepLocalState(cfg, n);
            cfg = n;
            ApplyConfig(false);
            Balloon(5000, "설정을 다시 불러왔습니다", "다른 곳에서 바뀐 설정 파일을 읽었습니다. 바뀌기 전 상태는 백업해 두었습니다.", ToolTipIcon.Info);
            return true;
        }

        /// <summary>점검용: 바뀐 설정 파일을 바로 확인한다.</summary>
        public bool CheckExternalChange()
        {
            return SyncFromDisk();
        }

        // ------------------------------------------------------------------ backup & update

        void DoMaintenance()
        {
            if (dialogOpen) return;
            ConfigStore.BackupIfDue(cfg);
            if (AppInstance.IsTest && Updater.TestUrl == null) return;
            if (cfg.AutoUpdateCheck && UpdateCheckDue()) CheckForUpdates(false);
        }

        bool UpdateCheckDue()
        {
            DateTime last;
            if (!DateTime.TryParse(cfg.LastUpdateCheck, CultureInfo.InvariantCulture, DateTimeStyles.RoundtripKind, out last)) return true;
            return DateTime.UtcNow - last.ToUniversalTime() > TimeSpan.FromHours(20);
        }

        /// <summary>트레이 메뉴·명령줄(--check-update)에서: 바로 확인하고 결과를 알려준다.</summary>
        public void CheckForUpdatesNow()
        {
            CheckForUpdates(true);
        }

        void CheckForUpdates(bool manual)
        {
            if (updateChecking) return;
            updateChecking = true;
            System.Threading.ThreadPool.QueueUserWorkItem(delegate
            {
                UpdateInfo info = null;
                Exception error = null;
                try { info = Updater.FetchLatest(); }
                catch (Exception ex) { error = ex; }
                try { BeginInvoke(new Action(delegate { OnUpdateChecked(info, error, manual); })); }
                catch (InvalidOperationException) { }
            });
        }

        void OnUpdateChecked(UpdateInfo info, Exception error, bool manual)
        {
            updateChecking = false;
            if (error != null)
            {
                if (manual) ShowWarning("업데이트를 확인하지 못했습니다.\n" + Updater.Describe(error));
                return;
            }
            if (!dialogOpen)
            {
                SyncFromDisk();
                cfg.LastUpdateCheck = DateTime.UtcNow.ToString("o", CultureInfo.InvariantCulture);
                SaveConfig();
            }
            if (!Updater.IsNewer(info.Version, AppInfo.Version))
            {
                pendingUpdate = null;
                UpdateTrayUpdateItem();
                if (manual)
                    MessageBox.Show(this, "지금 최신 버전(v" + AppInfo.Version + ")을 쓰고 있습니다.", "WinDeck 업데이트",
                        MessageBoxButtons.OK, MessageBoxIcon.Information);
                return;
            }
            pendingUpdate = info;
            UpdateTrayUpdateItem();
            if (manual && !dialogOpen)
            {
                ShowUpdateDialog(info);
                return;
            }
            // 다른 창이 열려 있으면 알림만 띄운다 (알림이나 트레이 메뉴에서 설치).
            if (!manual && info.Version == cfg.SkippedUpdateVersion) return;
            Balloon(15000, "WinDeck v" + info.Version + " 이 나왔습니다",
                "여기를 누르면 바뀐 내용을 보고 바로 설치할 수 있습니다.", ToolTipIcon.Info, true);
        }

        void UpdateTrayUpdateItem()
        {
            if (trayUpdateItem == null) return;
            bool has = pendingUpdate != null;
            trayUpdateItem.Text = has ? "새 버전 v" + pendingUpdate.Version + " 설치…" : "업데이트 확인…";
            trayUpdateItem.Font = new Font(trayMenu.Font, has ? FontStyle.Bold : FontStyle.Regular);
        }

        void ShowUpdateDialog(UpdateInfo info)
        {
            if (dialogOpen) return;
            dialogOpen = true;
            PreparedUpdate prepared = null;
            try
            {
                using (var f = new UpdateForm(info))
                {
                    if (f.ShowDialog(this) == DialogResult.OK) prepared = f.Prepared;
                    if (f.SkipRequested)
                    {
                        SyncFromDisk();
                        cfg.SkippedUpdateVersion = info.Version;
                        SaveConfig();
                    }
                }
            }
            finally
            {
                dialogOpen = false;
            }
            if (prepared != null) ApplyPreparedUpdate(prepared);
        }

        /// <summary>받아서 검사한 업데이트를 설치한다: 설정을 저장·백업하고, 설치를 시작한 뒤 이 WinDeck 은 끝낸다.</summary>
        void ApplyPreparedUpdate(PreparedUpdate p)
        {
            SyncFromDisk();
            SaveConfig();
            try { ConfigStore.AutoBackup(cfg, "before_update"); }
            catch (Exception ex) { ErrorLog.Write(ex); }
            try
            {
                Updater.Launch(p);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                ShowWarning("업데이트를 시작하지 못했습니다.\n" + ex.Message + "\n\n릴리스 페이지에서 직접 받아 설치해 주세요.");
                return;
            }
            ExitApp();
        }

        /// <summary>버튼·페이지 구성이 바뀐 뒤: 저장, 화면 갱신, 단축키 재등록.</summary>
        void ConfigChanged()
        {
            SaveConfig();
            RefreshKeys();
            RegisterHotkeys();
        }

        void SaveConfig()
        {
            try
            {
                ConfigStore.Save(cfg);
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                ShowWarning("설정을 저장하지 못했습니다.\n" + ex.Message);
            }
        }

        void ShowWarning(string message)
        {
            MessageBox.Show(this, message, "WinDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
        }
    }
}
