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
//   SmokeTest.exe <outDir> [--input] [--e2e] [--actions] [--process <fake release dir>]
static partial class SmokeTest
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
    [DllImport("user32.dll")] static extern uint GetGuiResources(IntPtr h, uint flags);

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
        string realSettings = RealSettingsFingerprint();
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
        TestV12();
        TestV12Render();
        TestFinalQa();
        TestV13();
        if (args.Contains("--actions")) TestRealActions();
        if (args.Contains("--input")) TestInput();
        if (args.Contains("--e2e")) TestEndToEnd();
        int proc = Array.IndexOf(args, "--process");
        if (proc >= 0 && proc + 1 < args.Length) TestProcesses(args[proc + 1]);
        Check(RealSettingsFingerprint() == realSettings, "user's real settings (%APPDATA%\\WinDeck) untouched by the tests");

        Console.WriteLine(fails == 0 ? "ALL PASSED" : fails + " FAILED");
        return fails;
    }

    // ------------------------------------------------------------------ config

    static void TestConfig()
    {
        AppConfig c = ConfigStore.Load();
        Check(ConfigStore.IsNew, "first load creates new config");
        Check(File.Exists(ConfigStore.ConfigPath), "config.json written");
        Check(c.Pages.Count == 2 && c.ButtonCount == 8 && c.Pages.All(p => p.Buttons.Count == AppConfig.MaxKeyCount),
            "default config: 2 pages, 8 visible buttons, 16 slots stored");

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
        Check(n.Pages.Count == 2 && n.Pages.All(p => p.Buttons.Count == AppConfig.MaxKeyCount), "normalize pads pages to 16 slots");
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

    // ------------------------------------------------------------------ 1.2: 설정 경량화, 색, 배경, 버튼 수

    static void TestV12()
    {
        AppConfig d = AppConfig.CreateDefault();
        string json = ConfigStore.ToJson(d);
        Check(json.Length < 30 * 1024, "default config is small with icon references", (json.Length / 1024) + "KB");
        Check(d.Pages[0].Buttons.Take(8).All(b => b.IconData.StartsWith(IconLibrary.RefPrefix)), "default buttons reference library icons");
        Check(IconCache.FromData("lib:folder") != null && IconCache.FromData("lib:no-such-icon") == null, "lib: reference renders, unknown falls back");

        AppConfig legacy = AppConfig.CreateDefault();
        legacy.Pages[0].Buttons[0].IconData = IconLibrary.Find("downloads").Base64;
        legacy.Pages[0].Buttons[1].IconData = IconCache.LoadIconData(Path.Combine(outDir, "icon_src.png"));
        AppConfig migrated = ConfigStore.Parse(ConfigStore.ToJson(legacy));
        Check(migrated.Pages[0].Buttons[0].IconData == "lib:downloads", "1.1 embedded library icon migrates to a reference");
        Check(migrated.Pages[0].Buttons[1].IconData.Length > 100, "user's own image icon is kept as image");

        Check(ColorChooser.ParseHex("#a1b2c3") == "#A1B2C3" && ColorChooser.ParseHex("A1B2C3") == "#A1B2C3" && ColorChooser.ParseHex("#abc") == "#AABBCC",
            "hex color input accepts #RRGGBB, RRGGBB, #RGB");
        Check(ColorChooser.ParseHex("#12345") == null && ColorChooser.ParseHex("zz0000") == null, "invalid hex rejected");
        AppConfig rc = AppConfig.CreateDefault();
        for (int i = 0; i < 10; i++) ColorChooser.AddRecent(rc, "#00000" + i);
        ColorChooser.AddRecent(rc, "#000003");
        Check(rc.RecentColors.Count == 8 && rc.RecentColors[0] == "#000003" && rc.RecentColors.Count(x => x == "#000003") == 1,
            "recent colors: newest first, no duplicates, max 8");

        AppConfig oldCfg = ConfigStore.Parse("{\"Pages\":[{\"Name\":\"A\",\"Buttons\":[{\"Title\":\"x\",\"Action\":\"url\",\"Target\":\"a.com\"}]}]}");
        Check(oldCfg.ButtonCount == 8 && oldCfg.BackgroundH.Mode == PanelBackground.ModeDefault && oldCfg.BackgroundVSameAsH
            && oldCfg.CustomColors.Count == 0 && oldCfg.RecentColors.Count == 0, "1.1 config gets 1.2 defaults");
        AppConfig bad = AppConfig.CreateDefault();
        bad.BackgroundH.Dim = 300;
        bad.BackgroundH.Mode = PanelBackground.ModeImage; // 이미지 없이 이미지 모드
        bad.ButtonCount = 13;
        bad.Normalize();
        Check(bad.BackgroundH.Dim == 80 && bad.BackgroundH.Mode == PanelBackground.ModeDefault && bad.ButtonCount == 12, "background and button count normalized");

        string big = Path.Combine(outDir, "bg_4k.png");
        using (Bitmap b = new Bitmap(3840, 2160))
        {
            using (Graphics g = Graphics.FromImage(b))
            using (var br = new System.Drawing.Drawing2D.LinearGradientBrush(new Rectangle(0, 0, 3840, 2160), Color.SteelBlue, Color.Orange, 30f))
                g.FillRectangle(br, 0, 0, 3840, 2160);
            b.Save(big, ImageFormat.Png);
        }
        string data = PanelBackgroundRenderer.LoadImageData(big);
        Size sz = PanelBackgroundRenderer.ImageSize(data);
        Check(Math.Max(sz.Width, sz.Height) == PanelBackgroundRenderer.MaxImageSide && data.Length < 300 * 1024,
            "4K background image is reduced to 1280px JPEG", sz + ", " + (data.Length * 3 / 4 / 1024) + "KB");

        AppConfig bgc = AppConfig.CreateDefault();
        bgc.BackgroundH = new PanelBackground { Mode = PanelBackground.ModeImage, ImageData = data };
        bgc.BackgroundV = new PanelBackground { Mode = PanelBackground.ModeColor, Color = "#123456" };
        bgc.Vertical = true;
        Check(bgc.ActiveBackground == bgc.BackgroundH, "vertical uses horizontal background when 'same' is on");
        bgc.BackgroundVSameAsH = false;
        Check(bgc.ActiveBackground == bgc.BackgroundV, "vertical uses its own background when 'same' is off");
        AppConfig bgrt = ConfigStore.Parse(ConfigStore.ToJson(bgc));
        Check(bgrt.BackgroundH.ImageData == data && bgrt.BackgroundV.Color == "#123456" && !bgrt.BackgroundVSameAsH, "backgrounds survive save/load");

        var grid = new System.Collections.Generic.List<string>();
        foreach (int n in AppConfig.ButtonCounts)
            foreach (bool v in new[] { false, true })
            {
                var g2 = new AppConfig { ButtonCount = n, Vertical = v };
                grid.Add(g2.Columns + "x" + g2.Rows);
            }
        Check(string.Join(",", grid.ToArray()) == "4x2,2x4,4x3,3x4,4x4,4x4", "layouts: 4 columns horizontal, 4 rows vertical", string.Join(",", grid.ToArray()));

        // 16 → 8 줄이기
        AppConfig sh = AppConfig.CreateDefault();
        sh.ButtonCount = 16;
        for (int i = 0; i < 16; i++) sh.Pages[0].Buttons[i] = new DeckButton { Title = "A" + i, Action = ActionTypes.Url, Target = "a" + i + ".com" };
        for (int i = 0; i < 9; i++) sh.Pages[1].Buttons[i] = new DeckButton { Title = "B" + i, Action = ActionTypes.Url, Target = "b.com" };
        sh.Pages[0].Buttons[12].Hotkey = "Ctrl+Alt+F12";
        sh.CurrentPage = 1;
        sh.ButtonCount = 8;
        Check(!HotkeyMap.Collect(sh).Any(e => e.Combo == "Ctrl+Alt+F12") && HotkeyMap.CollectAll(sh).Any(e => e.Combo == "Ctrl+Alt+F12" && e.Hidden),
            "hidden button's hotkey is paused but still listed");
        Check(HotkeyMap.Validate(sh, "Ctrl+Alt+F12", HotkeyKind.Button, 1, 0) != null, "hidden button's hotkey still blocks duplicates");
        ButtonLayout.HiddenInfo info = ButtonLayout.WouldHide(sh, 8);
        Check(info.Buttons == 9 && info.Hotkeys == 1 && info.Lines.Count == 2, "shrink preview lists hidden buttons per page", info.Buttons + "/" + info.Hotkeys + "/" + info.Lines.Count);
        AppConfig hidden = ConfigStore.Parse(ConfigStore.ToJson(sh));
        Check(hidden.Pages[0].Buttons[15].Title == "A15", "hide mode keeps buttons 9-16 in the config");
        hidden.ButtonCount = 16;
        Check(hidden.Pages[0].Buttons[12].Title == "A12", "growing back to 16 shows them in place");

        int created = ButtonLayout.Compact(sh, 8);
        Check(created == 2 && sh.Pages.Count == 4 && sh.Pages[1].Name == "기본 (이어서)" && sh.Pages[1].Buttons[0].Title == "A8"
            && sh.Pages[3].Name == "페이지 2 (이어서)" && sh.Pages[3].Buttons[0].Title == "B8",
            "compact moves overflow to '(이어서)' pages right after each page", string.Join(" | ", sh.Pages.Select(x => x.Name).ToArray()));
        Check(sh.Pages.All(pg => pg.Buttons.Skip(8).All(b => b.IsEmpty)), "after compact nothing is hidden");
        Check(sh.Pages[1].Buttons[4].Hotkey == "Ctrl+Alt+F12", "moved button keeps its hotkey");
        Check(sh.CurrentPage == 2 && sh.Pages[sh.CurrentPage].Name == "페이지 2", "current page follows its page after compact");

        AppConfig fill = AppConfig.CreateDefault();
        fill.Pages[1].Buttons[2] = new DeckButton { Title = "visible", Action = ActionTypes.Url, Target = "x.com" };
        fill.Pages[1].Buttons[13] = new DeckButton { Title = "far", Action = ActionTypes.Url, Target = "y.com" };
        ButtonLayout.Compact(fill, 8);
        Check(fill.Pages.Count == 2 && fill.Pages[1].Buttons[0].Title == "far" && fill.Pages[1].Buttons[2].Title == "visible",
            "compact first fills empty visible slots of the same page");
    }

    static void TestV12Render()
    {
        string bgData = PanelBackgroundRenderer.LoadImageData(Path.Combine(outDir, "bg_4k.png"));
        var shots = new[]
        {
            new { Name = "deck_12.png", Count = 12, Vertical = false, Bg = (PanelBackground)null },
            new { Name = "deck_16_vertical.png", Count = 16, Vertical = true, Bg = (PanelBackground)null },
            new { Name = "deck_color_bg.png", Count = 8, Vertical = false, Bg = new PanelBackground { Mode = PanelBackground.ModeColor, Color = "#1F3A5F" } },
            new { Name = "deck_image_bg.png", Count = 12, Vertical = false, Bg = new PanelBackground { Mode = PanelBackground.ModeImage, ImageData = bgData, Dim = 30 } },
            new { Name = "deck_light_bg.png", Count = 8, Vertical = false, Bg = new PanelBackground { Mode = PanelBackground.ModeColor, Color = "#F2F2F2" } }
        };
        foreach (var shot in shots)
        {
            AppConfig c = AppConfig.CreateDefault();
            c.ButtonCount = shot.Count;
            c.Vertical = shot.Vertical;
            if (shot.Bg != null) c.BackgroundH = shot.Bg;
            c.HasPosition = true;
            c.WindowX = 60;
            c.WindowY = 60;
            var deck = new DeckForm(c, false);
            deck.Show();
            Pump(300);
            int visible = deck.Controls.OfType<DeckKey>().Count(k => k.Visible);
            Check(visible == shot.Count, shot.Name + ": shows " + shot.Count + " keys", visible.ToString());
            Save(deck, shot.Name);
            deck.Close();
            deck.Dispose();
        }

        AppConfig sc = AppConfig.CreateDefault();
        sc.BackgroundH = new PanelBackground { Mode = PanelBackground.ModeImage, ImageData = bgData, Dim = 30 };
        sc.RecentColors.AddRange(new[] { "#C42B1C", "#0063B1", "#FFB900" });
        var s = new SettingsForm(ConfigStore.Clone(sc), false);
        s.InitialTab = 1;
        s.StartPosition = FormStartPosition.Manual;
        s.Location = new Point(-6000, 0);
        s.Show();
        Pump(400);
        Save(s, "settings_background.png");
        s.Close();
        s.Dispose();

        AppConfig shrink = AppConfig.CreateDefault();
        for (int i = 8; i < 14; i++) shrink.Pages[0].Buttons[i] = new DeckButton { Title = "버튼" + (i + 1), Action = ActionTypes.Url, Target = "x.com", Hotkey = i == 9 ? "Ctrl+Alt+9" : "" };
        var dlg = new ShrinkDialog(16, 8, ButtonLayout.WouldHide(shrink, 8));
        dlg.StartPosition = FormStartPosition.Manual;
        dlg.Location = new Point(-6000, 0);
        dlg.Show();
        Pump(300);
        Save(dlg, "shrink_dialog.png");
        dlg.Close();
        dlg.Dispose();

        var ed = new ButtonEditForm(sc.Pages[0].Buttons[4].Clone(), sc);
        ed.StartPosition = FormStartPosition.Manual;
        ed.Location = new Point(-6000, 0);
        ed.Show();
        Pump(300);
        Save(ed, "edit_colors.png");
        ed.Close();
        ed.Dispose();
    }

    // ------------------------------------------------------------------ 최종 검수

    static void TestFinalQa()
    {
        // 실행 중 버튼 수 변경
        AppConfig c = AppConfig.CreateDefault();
        c.HasPosition = true;
        c.WindowX = 60;
        c.WindowY = 60;
        var deck = new DeckForm(c, false);
        deck.Show();
        Pump(200);
        Size size8 = deck.ClientSize;
        foreach (int n in new[] { 16, 12, 8 })
        {
            deck.Config.ButtonCount = n;
            deck.ApplyConfig(false);
            Pump(100);
            int visible = deck.Controls.OfType<DeckKey>().Count(k => k.Visible);
            Check(visible == n, "runtime switch to " + n + " keys", visible.ToString());
        }
        Check(deck.ClientSize == size8, "panel returns to the same size after 16→12→8", deck.ClientSize.ToString());
        deck.Config.ButtonCount = 16;
        deck.Config.Vertical = true;
        deck.ApplyConfig(false);
        Pump(100);
        Rectangle wa = Screen.FromControl(deck).WorkingArea;
        Check(wa.Contains(deck.Bounds), "16 keys vertical panel stays on screen", deck.Bounds + " in " + wa);

        // 배경 다시 만들기를 반복해도 GDI 개체·메모리가 늘지 않는지
        deck.Config.BackgroundH = new PanelBackground
        {
            Mode = PanelBackground.ModeImage,
            ImageData = PanelBackgroundRenderer.LoadImageData(Path.Combine(outDir, "bg_4k.png")),
            Dim = 30
        };
        deck.Config.Vertical = false;
        deck.ApplyConfig(false);
        Pump(200);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        IntPtr me = Process.GetCurrentProcess().Handle;
        uint gdi0 = GetGuiResources(me, 0);
        long mem0 = GC.GetTotalMemory(true);
        for (int i = 0; i < 40; i++)
        {
            deck.Config.Vertical = i % 2 == 0;
            deck.Config.ButtonCount = AppConfig.ButtonCounts[i % 3];
            deck.ApplyConfig(false);
            Application.DoEvents();
        }
        Pump(200);
        GC.Collect();
        GC.WaitForPendingFinalizers();
        uint gdi1 = GetGuiResources(me, 0);
        long mem1 = GC.GetTotalMemory(true);
        Check(gdi1 <= gdi0 + 6, "40 background/layout rebuilds leak no GDI objects", gdi0 + " → " + gdi1);
        Check(mem1 - mem0 < 4 * 1024 * 1024, "40 rebuilds keep managed memory flat", ((mem1 - mem0) / 1024) + "KB");
        deck.Close();
        deck.Dispose();

        // 설정 창에서 16 → 8 로 줄일 때: 확인 창 취소 → 그대로, 당기기 선택 → 정리되어 저장
        foreach (bool compact in new[] { false, true })
        {
            AppConfig w = AppConfig.CreateDefault();
            w.ButtonCount = 16;
            for (int i = 8; i < 12; i++) w.Pages[0].Buttons[i] = new DeckButton { Title = "X" + i, Action = ActionTypes.Url, Target = "x.com" };
            w.Normalize();
            var sf = new SettingsForm(w, false);
            sf.StartPosition = FormStartPosition.Manual;
            sf.Location = new Point(-6000, 0);
            sf.Show();
            Pump(200);
            var cbo = (ComboBox)typeof(SettingsForm).GetField("cboCount", System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Instance).GetValue(sf);
            cbo.SelectedIndex = 0; // 8개
            bool sawDialog = false;
            var t = new System.Windows.Forms.Timer { Interval = 150 };
            t.Tick += delegate
            {
                ShrinkDialog d = Application.OpenForms.OfType<ShrinkDialog>().FirstOrDefault();
                if (d == null) return;
                t.Stop();
                sawDialog = true;
                if (compact)
                {
                    d.Controls.OfType<RadioButton>().First(r => r.Text.StartsWith("빈 칸")).Checked = true;
                    d.Controls.OfType<Button>().First(b => b.Text == "확인").PerformClick();
                }
                else d.Controls.OfType<Button>().First(b => b.Text == "취소").PerformClick();
            };
            t.Start();
            sf.Controls.OfType<Button>().First(b => b.Text == "확인").PerformClick();
            Pump(300);
            t.Stop();
            if (!compact)
                Check(sawDialog && sf.Visible && sf.Result == null, "settings: cancelling the shrink dialog keeps the settings window open");
            else
                Check(sawDialog && sf.Result != null && sf.Result.ButtonCount == 8 && sf.Result.Pages[0].Buttons.Skip(8).All(b => b.IsEmpty)
                    && sf.Result.Pages.Count == 3 && sf.Result.Pages[0].Buttons.Count(b => !b.IsEmpty) == 8,
                    "settings: choosing 'compact' saves 8 keys with overflow moved to a new page",
                    sf.Result == null ? "no result" : sf.Result.Pages.Count + " pages");
            if (sf.Visible) sf.Close();
            sf.Dispose();
        }
    }

    // 폴더 열기·프로그램 실행을 실제로 실행 (탐색기 창과 명령 창이 잠깐 열린다)
    static void TestRealActions()
    {
        string folder = Path.Combine(outDir, "open-me");
        Directory.CreateDirectory(folder);
        ActionRunner.Run(new DeckButton { Action = ActionTypes.Folder, Target = folder }, null);
        bool found = false;
        Type shellType = Type.GetTypeFromProgID("Shell.Application");
        object shell = Activator.CreateInstance(shellType);
        for (int i = 0; i < 40 && !found; i++)
        {
            Pump(150);
            object windows = shellType.InvokeMember("Windows", System.Reflection.BindingFlags.InvokeMethod, null, shell, null);
            foreach (object w in (System.Collections.IEnumerable)windows)
            {
                try
                {
                    object doc = w.GetType().InvokeMember("Document", System.Reflection.BindingFlags.GetProperty, null, w, null);
                    object fld = doc.GetType().InvokeMember("Folder", System.Reflection.BindingFlags.GetProperty, null, doc, null);
                    object self = fld.GetType().InvokeMember("Self", System.Reflection.BindingFlags.GetProperty, null, fld, null);
                    string path = (string)self.GetType().InvokeMember("Path", System.Reflection.BindingFlags.GetProperty, null, self, null);
                    if (string.Equals(path.TrimEnd('\\'), folder.TrimEnd('\\'), StringComparison.OrdinalIgnoreCase))
                    {
                        found = true;
                        w.GetType().InvokeMember("Quit", System.Reflection.BindingFlags.InvokeMethod, null, w, null);
                        break;
                    }
                }
                catch (Exception) { }
            }
        }
        Check(found, "folder action opens that folder in Explorer (window closed again)");

        string marker = Path.Combine(outDir, "run-marker.txt");
        if (File.Exists(marker)) File.Delete(marker);
        ActionRunner.Run(new DeckButton { Action = ActionTypes.Run, Target = "cmd.exe", Args = "/c echo ok> \"" + marker + "\"" }, null);
        for (int i = 0; i < 40 && !File.Exists(marker); i++) Pump(100);
        Check(File.Exists(marker), "run action starts a program with arguments");

        bool threw = false;
        try { ActionRunner.Run(new DeckButton { Action = ActionTypes.Folder, Target = Path.Combine(outDir, "no-such-folder") }, null); }
        catch (ActionException) { threw = true; }
        Check(threw, "missing folder gives a friendly error");
        threw = false;
        try { ActionRunner.Run(new DeckButton { Action = ActionTypes.Run, Target = Path.Combine(outDir, "no-such.exe") }, null); }
        catch (ActionException) { threw = true; }
        Check(threw, "missing program gives a friendly error");
        threw = false;
        try { ActionRunner.Run(new DeckButton { Action = ActionTypes.Hotkey, Target = "Ctrl+Nope" }, null); }
        catch (ActionException) { threw = true; }
        Check(threw, "invalid hotkey action gives a friendly error");
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
        for (int tab = 0; tab < 5; tab++)
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
