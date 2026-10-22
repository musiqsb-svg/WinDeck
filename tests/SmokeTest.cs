using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using WinDeck;

// WinDeck 자동 점검. 실제 사용자 설정은 건드리지 않도록 ConfigStore.Dir 를 출력 폴더로 돌린다.
//   SmokeTest.exe <outDir> [--input] [--e2e]
static class SmokeTest
{
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("user32.dll")] static extern IntPtr GetForegroundWindow();
    [DllImport("user32.dll")] static extern bool SetForegroundWindow(IntPtr h);
    [DllImport("user32.dll")] static extern bool BringWindowToTop(IntPtr h);
    [DllImport("user32.dll")] static extern uint GetWindowThreadProcessId(IntPtr h, out uint pid);
    [DllImport("user32.dll")] static extern bool AttachThreadInput(uint a, uint b, bool attach);
    [DllImport("kernel32.dll")] static extern uint GetCurrentThreadId();
    [DllImport("user32.dll")] static extern IntPtr WindowFromPoint(Point p);
    [DllImport("user32.dll", SetLastError = true)] static extern uint SendInput(uint n, MINPUT[] inputs, int size);

    [StructLayout(LayoutKind.Sequential)]
    struct MINPUT { public uint type; public MOUSEINPUT mi; }
    [StructLayout(LayoutKind.Sequential)]
    struct MOUSEINPUT { public int dx, dy; public uint mouseData, dwFlags, time; public IntPtr extra; }

    static int fails;
    static string outDir;

    static void Check(bool ok, string name, string detail = null)
    {
        Console.WriteLine((ok ? "PASS  " : "FAIL  ") + name + (ok || detail == null ? "" : "   -> " + detail));
        if (!ok) fails++;
    }

    static void Pump(int ms)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < ms)
        {
            Application.DoEvents();
            Thread.Sleep(10);
        }
    }

    [STAThread]
    static int Main(string[] args)
    {
        SetProcessDPIAware();
        Console.OutputEncoding = Encoding.UTF8;
        Application.EnableVisualStyles();
        Application.SetCompatibleTextRenderingDefault(false);

        if (args.Length == 2 && args[0] == "--target") return RunTarget(args[1]);

        outDir = args[0];
        Directory.CreateDirectory(outDir);
        ConfigStore.Dir = Path.Combine(outDir, "cfg");
        if (Directory.Exists(ConfigStore.Dir)) Directory.Delete(ConfigStore.Dir, true);

        using (Graphics g = Graphics.FromHwnd(IntPtr.Zero))
            Console.WriteLine("Screen DPI: " + g.DpiX + "  (scale " + (g.DpiX / 96f) + ")");

        TestConfig();
        TestDistribution();
        TestKeyCombo();
        TestHotkeys();
        TestIconLibrary();
        TestRender();
        TestHotkeyDispatch();
        if (args.Contains("--input")) TestInput();
        if (args.Contains("--e2e")) TestEndToEnd();

        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails;
    }

    // ------------------------------------------------------------------ config

    static void TestConfig()
    {
        AppConfig c = ConfigStore.Load();
        Check(ConfigStore.IsNew, "first load creates new config");
        Check(File.Exists(ConfigStore.ConfigPath), "config.json written");
        Check(c.Pages.Count == 2 && c.Pages.All(p => p.Buttons.Count == 8), "default config has 2 pages x 8 buttons");

        AppConfig c2 = ConfigStore.Load();
        Check(!ConfigStore.IsNew, "second load reads existing file");
        Check(c2.Pages[0].Buttons[4].Target == "안녕하세요. 확인 후 회신드리겠습니다.", "Korean text survives save/load", c2.Pages[0].Buttons[4].Target);
        string json = File.ReadAllText(ConfigStore.ConfigPath, Encoding.UTF8);
        Console.WriteLine("      (json is " + (json.Contains("인사말") ? "readable Korean" : "unicode-escaped") + ", " + json.Length + " chars)");

        // icon + export/import roundtrip
        string png = Path.Combine(outDir, "icon_src.png");
        using (Bitmap b = AppIcon.Render(200)) b.Save(png, ImageFormat.Png);
        c2.Pages[0].Buttons[2].IconData = IconCache.LoadIconData(png);
        string export = Path.Combine(outDir, "export.json");
        ConfigStore.Export(c2, export);
        AppConfig imported = ConfigStore.Import(export);
        Check(imported.Pages[0].Buttons[2].IconData == c2.Pages[0].Buttons[2].IconData && imported.Pages[0].Buttons[2].IconData.Length > 100,
            "icon data survives export/import");
        Image decoded = IconCache.FromData(imported.Pages[0].Buttons[2].IconData);
        Check(decoded != null && decoded.Width == IconCache.StoredIconSize, "stored icon decodes at 128px");

        // normalize short / broken pages
        AppConfig n = ConfigStore.Parse("{\"Pages\":[{\"Name\":\"X\",\"Buttons\":[{\"Title\":\"a\",\"Action\":\"bogus\"}]},{\"Id\":\"\",\"Buttons\":null}]}");
        Check(n.Pages.Count == 2 && n.Pages.All(p => p.Buttons.Count == 8), "normalize pads pages to 8 buttons");
        Check(n.Pages[0].Buttons[0].Action == ActionTypes.None, "unknown action becomes none");
        Check(n.Pages[1].Name.Length > 0 && n.Pages[1].Id.Length > 0, "missing page name/id filled in");
        Check(n.Hotkey == AppConfig.DefaultHotkey && n.KeySize == 88 && n.Opacity == 100, "missing settings get defaults");

        // corrupted file
        File.WriteAllText(ConfigStore.ConfigPath, "{ this is not json");
        AppConfig recovered = ConfigStore.Load();
        Check(ConfigStore.LoadWarning != null && recovered.Pages.Count == 2, "corrupted config falls back to defaults with warning");
        Check(Directory.GetFiles(ConfigStore.Dir, "config.json.broken-*").Length == 1, "corrupted config kept as .broken file");

        bool threw = false;
        try { ConfigStore.Parse("[1,2,3]"); }
        catch (InvalidDataException) { threw = true; }
        Check(threw, "import of non-WinDeck json is rejected");
    }

    // ------------------------------------------------------------------ portable mode & team defaults

    static void TestDistribution()
    {
        string app = Path.Combine(outDir, "fakeapp");
        if (Directory.Exists(app)) Directory.Delete(app, true);
        Directory.CreateDirectory(app);
        string savedDir = ConfigStore.Dir;
        try
        {
            ConfigStore.AppDir = app;
            ConfigStore.Dir = null;
            string appData = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinDeck");
            Check(ConfigStore.Dir == appData && !ConfigStore.IsPortable, "without WinDeckData folder settings go to %APPDATA%", ConfigStore.Dir);

            string data = Path.Combine(app, ConfigStore.PortableFolderName);
            Directory.CreateDirectory(data);
            ConfigStore.Dir = null;
            Check(ConfigStore.Dir == data && ConfigStore.IsPortable, "WinDeckData folder next to exe enables portable mode", ConfigStore.Dir);

            AppConfig team = AppConfig.CreateDefault();
            team.Pages[0].Name = "팀 공용";
            team.Pages[0].Buttons[0].Title = "공유 드라이브";
            team.HasPosition = true;
            team.WindowX = 5;
            team.CurrentPage = 1;
            ConfigStore.Export(team, Path.Combine(app, ConfigStore.TeamDefaultsFileName));

            AppConfig first = ConfigStore.Load();
            Check(ConfigStore.IsNew && first.Pages[0].Name == "팀 공용" && first.Pages[0].Buttons[0].Title == "공유 드라이브",
                "first run starts from team defaults file", first.Pages[0].Name);
            Check(!first.HasPosition && first.CurrentPage == 0, "team defaults do not carry window position or page");
            Check(File.Exists(Path.Combine(data, "config.json")), "portable config saved next to exe");

            first.Pages[0].Name = "내 설정";
            ConfigStore.Save(first);
            AppConfig again = ConfigStore.Load();
            Check(!ConfigStore.IsNew && again.Pages[0].Name == "내 설정", "later runs use the user's own config, not team defaults");

            File.WriteAllText(Path.Combine(app, ConfigStore.TeamDefaultsFileName), "{ broken");
            File.Delete(Path.Combine(data, "config.json"));
            AppConfig fallback = ConfigStore.Load();
            Check(fallback.Pages[0].Name == "기본", "broken team defaults file falls back to built-in defaults");
        }
        finally
        {
            ConfigStore.AppDir = null;
            ConfigStore.Dir = savedDir;
        }
    }

    // ------------------------------------------------------------------ hotkeys (model / validation)

    static void TestHotkeys()
    {
        AppConfig c = AppConfig.CreateDefault();
        Check(c.PrevPageHotkey == "Ctrl+Alt+Left" && c.NextPageHotkey == "Ctrl+Alt+Right" && !c.ShowAtCursor, "default page hotkeys, cursor mode off");
        List<HotkeyEntry> entries = HotkeyMap.Collect(c);
        Check(entries.Count == 3 && entries[0].Kind == HotkeyKind.TogglePanel, "default config has 3 global hotkeys", entries.Count.ToString());

        AppConfig old = ConfigStore.Parse("{\"Hotkey\":\"Ctrl+`\",\"Pages\":[{\"Name\":\"A\",\"Buttons\":[{\"Title\":\"x\",\"Action\":\"url\",\"Target\":\"a.com\"}]}]}");
        Check(old.PrevPageHotkey == AppConfig.DefaultPrevPageHotkey && old.Pages[0].Hotkey == "" && old.Pages[0].Buttons[0].Hotkey == "" && !old.ShowAtCursor,
            "1.0 config without hotkey fields loads with defaults");

        c.Pages[1].Hotkey = "ctrl+alt+2";
        c.Pages[0].Buttons[4].Hotkey = "Ctrl+Alt+H";
        entries = HotkeyMap.Collect(c);
        Check(entries.Any(e => e.Kind == HotkeyKind.Page && e.PageIndex == 1 && e.Combo == "Ctrl+Alt+2"), "page hotkey collected and normalized");
        Check(entries.Any(e => e.Kind == HotkeyKind.Button && e.PageIndex == 0 && e.ButtonIndex == 4 && e.Label.Contains("인사말")), "button hotkey collected with label");
        Check(HotkeyMap.Validate(c, "Ctrl+Alt+2", HotkeyKind.Button, 0, 0) != null, "conflict with a page hotkey detected");
        Check(HotkeyMap.Validate(c, "Ctrl+Alt+H", HotkeyKind.Button, 0, 4) == null, "a button's own hotkey is not a conflict");
        Check(HotkeyMap.Validate(c, "ctrl+alt+left", HotkeyKind.Page, 0, -1) != null, "conflict with prev-page hotkey detected (case-insensitive)");
        Check(HotkeyMap.Validate(c, "Q", HotkeyKind.Button, 0, 0) != null, "single letter hotkey rejected");
        Check(HotkeyMap.Validate(c, "F9", HotkeyKind.Button, 0, 0) == null, "lone F-key allowed");
        Check(HotkeyMap.Validate(c, "Ctrl+A, Ctrl+C", HotkeyKind.Button, 0, 0) != null, "key sequence rejected as global hotkey");
        Check(HotkeyMap.Validate(c, "", HotkeyKind.Button, 0, 0) == null, "empty hotkey means none");

        AppConfig rt = ConfigStore.Parse(ConfigStore.ToJson(c));
        Check(rt.Pages[1].Hotkey == "ctrl+alt+2" && rt.Pages[0].Buttons[4].Hotkey == "Ctrl+Alt+H", "hotkeys survive save/load");
    }

    static void TestHotkeyDispatch()
    {
        AppConfig c = AppConfig.CreateDefault();
        c.Pages.Add(DeckPage.CreateEmpty("세번째"));
        c.HasPosition = true;
        c.WindowX = 60;
        c.WindowY = 60;
        var deck = new DeckForm(c, false);
        deck.Show();
        Pump(200);
        Point oldCursor = Cursor.Position;
        try
        {
            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.NextPage });
            Check(deck.Config.CurrentPage == 1, "next-page hotkey");
            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.PrevPage });
            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.PrevPage });
            Check(deck.Config.CurrentPage == 2, "prev-page hotkey wraps around");
            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.Page, PageIndex = 0 });
            Check(deck.Config.CurrentPage == 0, "page shortcut jumps to that page");

            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.TogglePanel });
            Check(!deck.Visible, "toggle hotkey hides the panel");
            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.NextPage });
            Check(deck.Visible && deck.Location == new Point(60, 60) && deck.Config.CurrentPage == 1,
                "page hotkey reveals the hidden panel at its saved position", deck.Location.ToString());

            Rectangle wa = Screen.PrimaryScreen.WorkingArea;
            Cursor.Position = new Point(wa.Left + wa.Width / 2, wa.Top + wa.Height / 2);
            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.TogglePanel });
            deck.Config.ShowAtCursor = true;
            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.TogglePanel });
            Point cur = Cursor.Position;
            Check(deck.Visible && Math.Abs(deck.Left + deck.Width / 2 - cur.X) <= 2 && cur.Y > deck.Top && cur.Y < deck.Top + deck.Height / 3,
                "cursor mode opens the panel under the mouse", deck.Bounds + " cursor " + cur);

            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.TogglePanel });
            deck.Config.ShowAtCursor = false;
            deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.TogglePanel });
            Check(deck.Visible && deck.Location == new Point(60, 60), "cursor mode off returns to the saved position", deck.Location.ToString());
            Check(deck.Config.WindowX == 60 && deck.Config.WindowY == 60, "showing at the cursor does not overwrite the saved position");
        }
        finally
        {
            Cursor.Position = oldCursor;
            deck.Close();
            deck.Dispose();
        }
    }

    static void TestIconLibrary()
    {
        int expected = File.ReadAllLines(Path.Combine(RepoRoot, "assets", "icons", "manifest.tsv"))
            .Count(l => l.Trim().Length > 0 && !l.StartsWith("#"));
        List<IconAsset> icons = IconLibrary.All;
        Check(icons.Count == expected, "all manifest icons are embedded in the exe", icons.Count + "/" + expected);
        Check(icons.All(i => i.Image.Width == IconCache.StoredIconSize && i.Image.Height == IconCache.StoredIconSize), "embedded icons are 128px");
        Check(icons.Select(i => i.Name).Distinct().Count() == icons.Count, "icon names are unique");
        IconAsset folder = IconLibrary.Find("folder");
        Check(folder != null && IconCache.FromData(folder.Base64) != null, "library icon converts to button IconData");
    }

    static string RepoRoot
    {
        get { return Path.GetFullPath(Path.Combine(Path.GetDirectoryName(Application.ExecutablePath), "..", "..")); }
    }

    // ------------------------------------------------------------------ key combos

    static void TestKeyCombo()
    {
        KeyCombo k = KeyCombo.Parse("Ctrl+Shift+Esc");
        Check(k.Ctrl && k.Shift && !k.Alt && k.Key == 0x1B && k.ToString() == "Ctrl+Shift+Esc", "parse Ctrl+Shift+Esc", k.ToString());
        Check(KeyCombo.Parse("win+d").ToString() == "Win+D", "parse win+d (case-insensitive)");
        Check(KeyCombo.Parse("Win+Shift+S").ToString() == "Win+Shift+S", "parse Win+Shift+S");
        KeyCombo w = KeyCombo.Parse("Win");
        Check(w.Key == 0x5B && !w.HasModifier && w.ToString() == "Win", "lone Win key", w.ToString());
        Check(KeyCombo.Parse("Alt+F4").Key == 0x73, "F4 vk");
        Check(KeyCombo.Parse("VolumeUp").Key == 0xAF, "VolumeUp vk");
        Check(KeyCombo.Parse("Ctrl+Comma").Key == 0xBC, "Comma key");
        Check(KeyCombo.Parse("한영").Key == 0x15, "Korean key name");
        Check(KeyCombo.ParseSequence("Ctrl+A, Ctrl+C").Count == 2, "sequence with comma");
        Check(KeyCombo.NormalizeSequence("ctrl+shift+s,  alt+tab") == "Ctrl+Shift+S, Alt+Tab", "normalize sequence", KeyCombo.NormalizeSequence("ctrl+shift+s,  alt+tab"));
        KeyCombo bad;
        Check(!KeyCombo.TryParse("Ctrl+Foo", out bad), "unknown key rejected");
        Check(!KeyCombo.TryParse("Ctrl+A+B", out bad), "two main keys rejected");
        Check(!KeyCombo.TryParse("12", out bad), "numeric junk rejected");
        var cap = new KeyCombo { Ctrl = true, Alt = true, Key = (ushort)Keys.Space };
        Check(cap.ToString() == "Ctrl+Alt+Space" && KeyCombo.Parse(cap.ToString()).Key == 0x20, "captured combo formats and re-parses");
        Check(cap.HotkeyModifiers == 0x3, "hotkey modifier flags");
        KeyCombo def = KeyCombo.Parse(AppConfig.DefaultHotkey);
        Check(def.Ctrl && !def.Alt && def.Key == 0xC0 && def.ToString() == AppConfig.DefaultHotkey, "default hotkey Ctrl+` parses and round-trips", def.ToString());
        Check(ActionRunner.NormalizeUrl("naver.com") == "https://naver.com", "url without scheme gets https");
        Check(ActionRunner.NormalizeUrl("localhost:3000") == "https://localhost:3000", "host:port is not a scheme");
        Check(ActionRunner.NormalizeUrl("ms-settings:display") == "ms-settings:display", "app scheme kept");
    }

    // ------------------------------------------------------------------ rendering

    static void Save(Control c, string name)
    {
        using (var bmp = new Bitmap(c.Width, c.Height))
        {
            c.DrawToBitmap(bmp, new Rectangle(Point.Empty, c.Size));
            bmp.Save(Path.Combine(outDir, name), ImageFormat.Png);
        }
    }

    static void TestRender()
    {
        IntPtr fgBefore = GetForegroundWindow();

        // README 용: 내장 기본 버튼 그대로의 패널
        AppConfig plain = AppConfig.CreateDefault();
        plain.HasPosition = true;
        plain.WindowX = 60;
        plain.WindowY = 60;
        var plainDeck = new DeckForm(plain, false);
        plainDeck.Show();
        Pump(300);
        Save(plainDeck, "deck_default.png");
        plainDeck.Close();
        plainDeck.Dispose();

        AppConfig cfg = AppConfig.CreateDefault();
        cfg.Pages[0].Buttons[2].IconData = IconCache.LoadIconData(Path.Combine(outDir, "icon_src.png"));
        cfg.HasPosition = true;
        cfg.WindowX = 60;
        cfg.WindowY = 60;
        var deck = new DeckForm(cfg, false);
        deck.Show();
        Pump(400);
        Check(GetForegroundWindow() != deck.Handle, "showing the panel does not activate it");
        Check(deck.Visible && deck.TopMost, "panel visible and top-most");
        Console.WriteLine("      panel size " + deck.Width + "x" + deck.Height);
        Save(deck, "deck.png");
        using (var bmp = new Bitmap(deck.Width + 40, deck.Height + 40))
        {
            using (Graphics g = Graphics.FromImage(bmp)) g.CopyFromScreen(deck.Left - 20, deck.Top - 20, 0, 0, bmp.Size);
            bmp.Save(Path.Combine(outDir, "deck_screen.png"), ImageFormat.Png);
        }
        deck.Close();
        deck.Dispose();

        var v = AppConfig.CreateDefault();
        v.Vertical = true;
        v.KeySize = 72;
        v.CurrentPage = 1;
        v.HasPosition = true;
        v.WindowX = 60;
        v.WindowY = 60;
        var deck2 = new DeckForm(v, false);
        deck2.Show();
        Pump(300);
        Save(deck2, "deck_vertical_page2.png");
        deck2.Close();
        deck2.Dispose();

        RenderEditor(cfg, 4, "edit_text.png");
        RenderEditor(cfg, 5, "edit_hotkey.png");
        RenderEditor(cfg, 7, "edit_run.png");
        RenderEditor(cfg, 0, "edit_folder.png");

        AppConfig withKeys = ConfigStore.Clone(cfg);
        withKeys.Pages[1].Hotkey = "Ctrl+Alt+2";
        withKeys.Pages[0].Buttons[4].Hotkey = "Ctrl+Alt+H";
        for (int tab = 0; tab < 4; tab++)
        {
            var s = new SettingsForm(ConfigStore.Clone(withKeys), false);
            s.InitialTab = tab;
            s.StartPosition = FormStartPosition.Manual;
            s.Location = new Point(-6000, 0);
            s.Show();
            Pump(300);
            Save(s, tab == 0 ? "settings.png" : "settings_tab" + tab + ".png");
            s.Close();
            s.Dispose();
        }
        RenderEditor(withKeys, 4, "edit_text_hotkey.png");

        var picker = new IconPickerForm(Theme.ParseColor("#0063B1", Theme.KeyBack));
        picker.StartPosition = FormStartPosition.Manual;
        picker.Location = new Point(-6000, 0);
        picker.Show();
        Pump(400);
        Save(picker, "icon_picker.png");
        picker.Close();
        picker.Dispose();
        Pump(100);
        if (fgBefore != IntPtr.Zero) SetForegroundWindow(fgBefore);
    }

    static void RenderEditor(AppConfig cfg, int index, string file)
    {
        var f = new ButtonEditForm(cfg.Pages[0].Buttons[index].Clone(), cfg);
        f.StartPosition = FormStartPosition.Manual;
        f.Location = new Point(-6000, 0);
        f.Show();
        Pump(250);
        Save(f, file);
        f.Close();
        f.Dispose();
    }

    // ------------------------------------------------------------------ input (same process, direct SendInput)

    static bool ForceForeground(Form f)
    {
        for (int attempt = 0; attempt < 5 && GetForegroundWindow() != f.Handle; attempt++)
        {
            uint pid;
            IntPtr fg = GetForegroundWindow();
            uint fgThread = GetWindowThreadProcessId(fg, out pid);
            uint me = GetCurrentThreadId();
            if (fgThread != 0 && fgThread != me) AttachThreadInput(me, fgThread, true);
            BringWindowToTop(f.Handle);
            SetForegroundWindow(f.Handle);
            f.Activate();
            if (fgThread != 0 && fgThread != me) AttachThreadInput(me, fgThread, false);
            Pump(150);
        }
        return GetForegroundWindow() == f.Handle;
    }

    static void TestInput()
    {
        string userClipboard = Clipboard.ContainsText() ? Clipboard.GetText() : null;
        var f = new Form { Text = "WinDeck input test", StartPosition = FormStartPosition.Manual, Location = new Point(120, 120), Size = new Size(520, 260), TopMost = true };
        var tb = new TextBox { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 12f) };
        f.Controls.Add(tb);
        f.Show();
        if (!ForceForeground(f))
        {
            Console.WriteLine("SKIP  input tests (could not bring test window to front; nothing was typed)");
            f.Close();
            return;
        }
        tb.Focus();
        Pump(200);

        Func<bool> safe = () => GetForegroundWindow() == f.Handle;

        if (safe()) InputSender.TypeText("안녕하세요 Hello 123!\n둘째 줄", false);
        Pump(400);
        Check(tb.Text == "안녕하세요 Hello 123!\r\n둘째 줄", "TypeText: Korean + English + newline", tb.Text);

        tb.Clear();
        if (safe()) InputSender.TypeText("abc", false);
        Pump(200);
        if (safe()) InputSender.SendSequence(KeyCombo.ParseSequence("Ctrl+A"));
        Pump(200);
        if (safe()) InputSender.TypeText("X", true);
        Pump(300);
        Check(tb.Text == "X\r\n", "Ctrl+A combo then text with Enter", tb.Text);

        tb.Clear();
        Clipboard.SetText("ORIGINAL-CLIP");
        if (safe()) InputSender.PasteText("붙여넣기 테스트 ✓", false);
        Pump(400);
        Check(tb.Text == "붙여넣기 테스트 ✓", "PasteText inserts text", tb.Text);
        Pump(1200);
        Check(Clipboard.ContainsText() && Clipboard.GetText() == "ORIGINAL-CLIP", "clipboard restored after paste",
            Clipboard.ContainsText() ? Clipboard.GetText() : "(no text)");

        f.Close();
        if (userClipboard != null) Clipboard.SetText(userClipboard);
    }

    // ------------------------------------------------------------------ end-to-end (separate target process, real mouse click)

    static int RunTarget(string stateFile)
    {
        var f = new Form { Text = "WinDeck E2E target", StartPosition = FormStartPosition.Manual, Location = new Point(150, 150), Size = new Size(560, 260), TopMost = true };
        var tb = new TextBox { Multiline = true, AcceptsReturn = true, Dock = DockStyle.Fill, Font = new Font("Malgun Gothic", 12f) };
        f.Controls.Add(tb);
        var t = new System.Windows.Forms.Timer { Interval = 100 };
        t.Tick += delegate
        {
            try { File.WriteAllText(stateFile, (GetForegroundWindow() == f.Handle ? "FG" : "BG") + "|" + tb.Text, Encoding.UTF8); }
            catch (IOException) { }
            if (File.Exists(stateFile + ".quit")) f.Close();
        };
        f.Shown += delegate
        {
            ForceForeground(f);
            tb.Focus();
            t.Start();
        };
        Application.Run(f);
        return 0;
    }

    static string ReadState(string file)
    {
        for (int i = 0; i < 10; i++)
        {
            try { return File.ReadAllText(file, Encoding.UTF8); }
            catch (IOException) { Thread.Sleep(20); }
        }
        return "";
    }

    static void ClickAt(Point p)
    {
        Rectangle vs = SystemInformation.VirtualScreen;
        int ax = (int)Math.Round((p.X - vs.Left) * 65535.0 / (vs.Width - 1));
        int ay = (int)Math.Round((p.Y - vs.Top) * 65535.0 / (vs.Height - 1));
        const uint MOVE = 0x1, DOWN = 0x2, UP = 0x4, ABS = 0x8000, VDESK = 0x4000;
        var inputs = new[]
        {
            new MINPUT { type = 0, mi = new MOUSEINPUT { dx = ax, dy = ay, dwFlags = MOVE | ABS | VDESK | DOWN } },
            new MINPUT { type = 0, mi = new MOUSEINPUT { dx = ax, dy = ay, dwFlags = MOVE | ABS | VDESK | UP } }
        };
        SendInput(1, new[] { inputs[0] }, Marshal.SizeOf(typeof(MINPUT)));
        Pump(70);
        SendInput(1, new[] { inputs[1] }, Marshal.SizeOf(typeof(MINPUT)));
    }

    static bool ClickKey(DeckForm deck, int index, string stateFile)
    {
        DeckKey key = deck.Controls.OfType<DeckKey>().First(k => k.Index == index);
        Point center = key.PointToScreen(new Point(key.Width / 2, key.Height / 2));
        if (WindowFromPoint(center) != key.Handle)
        {
            Console.WriteLine("SKIP  click on key " + index + " (something else is covering the panel)");
            return false;
        }
        if (!ReadState(stateFile).StartsWith("FG"))
        {
            Console.WriteLine("SKIP  click on key " + index + " (target window lost focus)");
            return false;
        }
        ClickAt(center);
        Pump(700);
        return true;
    }

    static void TestEndToEnd()
    {
        string state = Path.Combine(outDir, "target_state.txt");
        foreach (string f in new[] { state, state + ".quit" }) if (File.Exists(f)) File.Delete(f);
        string userClipboard = Clipboard.ContainsText() ? Clipboard.GetText() : null;

        Process target = Process.Start(new ProcessStartInfo(Application.ExecutablePath, "--target \"" + state + "\"") { UseShellExecute = false });
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < 6000 && !ReadStateSafe(state).StartsWith("FG")) Pump(100);
        if (!ReadStateSafe(state).StartsWith("FG"))
        {
            Console.WriteLine("SKIP  end-to-end (target window could not take focus)");
            File.WriteAllText(state + ".quit", "");
            return;
        }

        AppConfig cfg = AppConfig.CreateDefault();
        DeckPage page = cfg.Pages[0];
        page.Buttons[0] = new DeckButton { Title = "E2E", Action = ActionTypes.Text, Target = "클릭 입력 OK" };
        page.Buttons[1] = new DeckButton { Title = "SelAll", Action = ActionTypes.Hotkey, Target = "Ctrl+A" };
        page.Buttons[2] = new DeckButton { Title = "Replace", Action = ActionTypes.Text, Target = "교체됨" };
        page.Buttons[3] = new DeckButton { Title = "Paste", Action = ActionTypes.Text, Target = "+붙여넣기", UsePaste = true, PressEnter = true };
        page.Buttons[4] = new DeckButton { Title = "Page2", Action = ActionTypes.Page, Target = cfg.Pages[1].Id };
        cfg.HasPosition = true;
        cfg.WindowX = 900;
        cfg.WindowY = 180;
        Clipboard.SetText("ORIGINAL-CLIP");

        var deck = new DeckForm(cfg, false);
        deck.Show();
        Pump(500);
        Check(ReadState(state).StartsWith("FG"), "target keeps focus when panel appears");
        Point oldCursor = Cursor.Position;
        try
        {
            if (ClickKey(deck, 0, state))
            {
                string s = ReadState(state);
                Check(s == "FG|클릭 입력 OK", "click on text key types into other app and keeps its focus", s);
            }
            if (ClickKey(deck, 1, state) && ClickKey(deck, 2, state))
            {
                string s = ReadState(state);
                Check(s == "FG|교체됨", "hotkey key (Ctrl+A) then text key replaces text", s);
            }
            if (ClickKey(deck, 3, state))
            {
                Pump(1200);
                string s = ReadState(state);
                Check(s == "FG|교체됨+붙여넣기\r\n", "paste-mode key with Enter", s);
                Check(Clipboard.ContainsText() && Clipboard.GetText() == "ORIGINAL-CLIP", "clipboard restored after paste key");
            }
            if (ClickKey(deck, 4, state))
            {
                Check(deck.Config.CurrentPage == 1, "page key switches to page 2");
                // 마우스 버튼을 누르지 않은 채 DoDragDrop 하면 커서 위치에 즉시 드롭된다.
                DropOnKey(deck, 0, new DataObject(DataFormats.FileDrop, new[] { outDir }));
                DeckButton dropped = deck.Config.Pages[1].Buttons[0];
                Check(dropped.Action == ActionTypes.Folder && dropped.Target == outDir && dropped.Title == Path.GetFileName(outDir),
                    "dropping a folder on an empty key creates a folder button", dropped.Action + " " + dropped.Title);
                DropOnKey(deck, 1, new DataObject(DataFormats.UnicodeText, "https://www.naver.com/news"));
                dropped = deck.Config.Pages[1].Buttons[1];
                Check(dropped.Action == ActionTypes.Url && dropped.Title == "naver.com", "dropping a link creates a link button", dropped.Action + " " + dropped.Title);

                // 버튼 단축키: 다른 페이지(0)에 있는 문장 버튼이 패널을 건드리지 않고 앞 창에 입력되는지
                if (ReadState(state).StartsWith("FG"))
                {
                    deck.OnHotkey(new HotkeyEntry { Kind = HotkeyKind.Button, PageIndex = 0, ButtonIndex = 0 });
                    Pump(900);
                    string s = ReadState(state);
                    Check(s == "FG|교체됨+붙여넣기\r\n클릭 입력 OK", "button hotkey types into the focused app", s);
                    Check(deck.Config.CurrentPage == 1, "button hotkey does not switch the visible page");
                }
            }
        }
        finally
        {
            Cursor.Position = oldCursor;
            File.WriteAllText(state + ".quit", "");
            target.WaitForExit(3000);
            deck.Close();
            if (userClipboard != null) Clipboard.SetText(userClipboard);
        }
    }

    // OLE 드래그 루프는 합성 입력으로 끝내기 어려워서, 키의 DragEnter/DragDrop 처리기를 직접 호출해 검증한다.
    static void DropOnKey(DeckForm deck, int index, DataObject data)
    {
        DeckKey key = deck.Controls.OfType<DeckKey>().First(k => k.Index == index);
        Check(key.AllowDrop, "key " + index + " accepts drops");
        var args = new DragEventArgs(data, 0, 0, 0, DragDropEffects.Copy | DragDropEffects.Link, DragDropEffects.None);
        const System.Reflection.BindingFlags flags = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic;
        typeof(Control).GetMethod("OnDragEnter", flags).Invoke(key, new object[] { args });
        Check(args.Effect == DragDropEffects.Copy, "drag-enter shows copy cursor", args.Effect.ToString());
        typeof(Control).GetMethod("OnDragDrop", flags).Invoke(key, new object[] { args });
        Pump(300);
    }

    static string ReadStateSafe(string file)
    {
        return File.Exists(file) ? ReadState(file) : "";
    }
}
