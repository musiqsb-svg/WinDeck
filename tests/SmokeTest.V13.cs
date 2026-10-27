using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Windows.Forms;
using Microsoft.Win32;
using WinDeck;

// 1.3: 설정 되돌아감 방지(다른 곳에서 바뀐 파일 감지, 자동 백업, 이전 설정 찾기, 저장 후 종료)와 자동 업데이트 점검.
static partial class SmokeTest
{
    // ------------------------------------------------------------------ in-process

    static void TestV13()
    {
        string savedDir = ConfigStore.Dir;
        string testId = "smoke" + Process.GetCurrentProcess().Id;
        Environment.SetEnvironmentVariable("WINDECK_INSTANCE", testId);
        try
        {
            TestDeepPortable();
            TestStaleDetection();
            TestBackups();
            try { TestV13Render(); }
            catch (Exception ex) { Check(false, "v13 render", ex.GetType().Name + ": " + ex.Message + "\n" + ex.StackTrace); }
            TestUpgradeBackup();
            TestDiscovery();
            TestDeckSync();
            TestPathRemap();
            TestUpdaterUnits();
        }
        finally
        {
            Environment.SetEnvironmentVariable("WINDECK_INSTANCE", null);
            DeleteTestRegistry(testId);
            ConfigStore.AppDir = null;
            ConfigStore.Dir = savedDir;
        }
    }

    /// <summary>실제 사용자 설정 폴더의 파일 목록·크기·시간 (점검 전후 비교용).</summary>
    static string RealSettingsFingerprint()
    {
        string dir = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinDeck");
        if (!Directory.Exists(dir)) return "(none)";
        return string.Join("\n", Directory.GetFiles(dir, "*", SearchOption.AllDirectories).OrderBy(f => f)
            .Select(f => f + "|" + new FileInfo(f).Length + "|" + File.GetLastWriteTimeUtc(f).Ticks).ToArray());
    }

    static void TestDeepPortable()
    {
        // WinDeckData\config.json.tmp 까지 260자 안에 드는 깊은 폴더 (예전의 긴 확인용 파일 이름은 넘쳐서 %APPDATA% 로 빠졌음)
        string app = Path.Combine(outDir, "v13_deep");
        if (Directory.Exists(app)) Directory.Delete(app, true);
        while (Path.Combine(app, "WinDeckData", "config.json.tmp").Length < 250) app = Path.Combine(app, "d");
        Directory.CreateDirectory(Path.Combine(app, "WinDeckData"));
        ConfigStore.AppDir = app;
        ConfigStore.Dir = null;
        Check(ConfigStore.IsPortable, "portable mode works in a deep folder (" + Path.Combine(app, "WinDeckData", "config.json").Length + " chars)", ConfigStore.Dir);
        ConfigStore.AppDir = null;
        ConfigStore.Dir = null;
    }

    static string FreshDir(string name)
    {
        string d = Path.Combine(outDir, name);
        if (Directory.Exists(d)) Directory.Delete(d, true);
        Directory.CreateDirectory(d);
        return d;
    }

    static void DeleteTestRegistry(string id)
    {
        try
        {
            using (RegistryKey k = Registry.CurrentUser.OpenSubKey(@"Software\WinDeck-test", true))
            {
                if (k == null) return;
                k.DeleteSubKeyTree(id, false);
                if (k.SubKeyCount == 0 && k.ValueCount == 0)
                {
                    k.Close();
                    Registry.CurrentUser.DeleteSubKey(@"Software\WinDeck-test", false);
                }
            }
        }
        catch (Exception) { }
    }

    static string[] BackupReasons()
    {
        return ConfigStore.ListBackups().Select(b => b.Reason).ToArray();
    }

    static void TestStaleDetection()
    {
        ConfigStore.Dir = FreshDir("v13_stale");
        AppConfig c = ConfigStore.Load();
        Check(!ConfigStore.ChangedOnDisk(), "fresh config is not reported as changed");
        c.Pages[0].Name = "내 변경";
        ConfigStore.Save(c);
        Check(!ConfigStore.ChangedOnDisk(), "own save is not reported as an external change");

        string json = File.ReadAllText(ConfigStore.ConfigPath, Encoding.UTF8).Replace("내 변경", "외부 변경");
        Thread.Sleep(20);
        File.WriteAllText(ConfigStore.ConfigPath, json, new UTF8Encoding(false));
        Check(ConfigStore.ChangedOnDisk(), "file edited by someone else is detected");
        AppConfig reloaded = ConfigStore.ReloadFromDisk();
        Check(reloaded.Pages[0].Name == "외부 변경" && !ConfigStore.ChangedOnDisk(), "reload reads the new file and clears the flag");

        File.SetLastWriteTime(ConfigStore.ConfigPath, DateTime.Now.AddMinutes(3));
        Check(!ConfigStore.ChangedOnDisk(), "touching the file without changing it is not a change");

        // 감지하지 못한 채 덮어쓰게 되더라도 바뀐 파일은 백업에 남는다.
        File.WriteAllText(ConfigStore.ConfigPath, json.Replace("외부 변경", "또 다른 변경"), new UTF8Encoding(false));
        ConfigStore.Save(c);
        BackupInfo ext = ConfigStore.ListBackups().FirstOrDefault(b => b.Reason == "external_change");
        Check(ext != null && File.ReadAllText(ext.Path, Encoding.UTF8).Contains("또 다른 변경"),
            "overwriting an externally changed file backs it up first");
        Check(ConfigStore.Load().Pages[0].Name == "내 변경", "save after backup writes own settings");

        // 백신·동기화 프로그램이 파일을 잠깐 열고 있어도 저장이 실패하지 않는다.
        var hold = new FileStream(ConfigStore.ConfigPath, FileMode.Open, FileAccess.Read, FileShare.Read);
        var release = new Thread(() => { Thread.Sleep(300); hold.Dispose(); });
        release.Start();
        bool saved = true;
        c.Pages[0].Name = "잠긴 뒤 저장";
        try { ConfigStore.Save(c); }
        catch (IOException) { saved = false; }
        release.Join();
        Check(saved && ConfigStore.Load().Pages[0].Name == "잠긴 뒤 저장", "save waits for a file briefly held open by another program");
    }

    static void TestBackups()
    {
        ConfigStore.Dir = FreshDir("v13_backups");
        AppConfig c = ConfigStore.Load();
        for (int i = 0; i < ConfigStore.MaxBackups + 5; i++) ConfigStore.AutoBackup(c, "before_settings");
        List<BackupInfo> list = ConfigStore.ListBackups();
        Check(list.Count == ConfigStore.MaxBackups, "only the newest " + ConfigStore.MaxBackups + " backups are kept", list.Count.ToString());
        Check(list.Select(b => b.Path).Distinct().Count() == list.Count, "backups made in the same second get distinct names");
        Check(list[0].ReasonText == "설정 바꾸기 전" && list[0].Time > DateTime.Now.AddMinutes(-1), "backup list shows reason and time", list[0].ReasonText);

        ConfigStore.Dir = FreshDir("v13_daily");
        c = ConfigStore.Load();
        ConfigStore.BackupIfDue(c);
        ConfigStore.BackupIfDue(c);
        Check(BackupReasons().Count(r => r == "daily") == 1, "daily backup is made once a day");
        Check(ConfigStore.Import(ConfigStore.ListBackups()[0].Path).Pages.Count == c.Pages.Count, "backup file can be restored");
    }

    /// <summary>새 화면 그림: 업데이트 창, 설정의 백업 탭(백업 목록), 일반 탭(업데이트 확인).</summary>
    static void TestV13Render()
    {
        ConfigStore.Dir = Path.Combine(outDir, "v13_backups");
        ConfigStore.AutoBackup(ConfigStore.Load(), "before_import");
        ConfigStore.AutoBackup(ConfigStore.Load(), "daily");
        for (int tab = 0; tab < 5; tab += 4)
        {
            var s = new SettingsForm(ConfigStore.Load(), false);
            s.InitialTab = tab;
            s.StartPosition = FormStartPosition.Manual;
            s.Location = new System.Drawing.Point(-6000, 0);
            s.Show();
            Pump(300);
            if (tab == 4)
            {
                ListView list = s.Controls.OfType<TabControl>().First().TabPages[4].Controls.OfType<ListView>().First();
                Check(list.Items.Count == ConfigStore.MaxBackups && list.Items[0].SubItems[1].Text == "하루 자동 백업",
                    "backup tab lists the automatic backups, newest first", list.Items.Count + " " + list.Items[0].SubItems[1].Text);
                // 문서용 그림에 점검 폴더 경로(사용자 이름 포함)가 나오지 않게
                foreach (Control c in list.Parent.Controls)
                    if (c.Text.StartsWith("설정 위치:")) c.Text = @"설정 위치: %APPDATA%\WinDeck";
            }
            Save(s, tab == 0 ? "v13_settings_general.png" : "v13_settings_backup.png");
            s.Close();
            s.Dispose();
        }
        var uf = new UpdateForm(new UpdateInfo
        {
            Version = "1.3.1",
            Notes = "## 1.3.1\n- 업데이트 뒤 최근 설정이 되돌아가던 문제 수정\n- 새 버전 알림과 한 번 눌러 설치",
            PageUrl = Updater.ReleasesPage
        });
        uf.StartPosition = FormStartPosition.Manual;
        uf.Location = new System.Drawing.Point(-6000, 0);
        uf.Show();
        Pump(300);
        Save(uf, "v13_update_dialog.png");
        uf.Close();
        uf.Dispose();
    }

    static void TestUpgradeBackup()
    {
        ConfigStore.Dir = FreshDir("v13_upgrade");
        AppConfig c = AppConfig.CreateDefault();
        c.Pages[0].Name = "1.2 에서 쓰던 설정";
        string json = ConfigStore.ToJson(c).Replace("\"SavedByVersion\": null", "\"SavedByVersion\": \"1.2.0\"");
        File.WriteAllText(ConfigStore.ConfigPath, json, new UTF8Encoding(false));

        AppConfig loaded = ConfigStore.Load();
        Check(ConfigStore.UpgradedFrom == "1.2.0" && loaded.Pages[0].Name == "1.2 에서 쓰던 설정", "upgrade keeps settings and notes the old version");
        BackupInfo b = ConfigStore.ListBackups().FirstOrDefault(x => x.Reason == "before_update");
        Check(b != null && File.ReadAllText(b.Path, Encoding.UTF8).Contains("\"1.2.0\""), "upgrade backs up the untouched old file");
        Check(ConfigStore.Load().SavedByVersion == AppInfo.Version && ConfigStore.UpgradedFrom == null, "after upgrade the file is marked with the new version");

        File.WriteAllText(ConfigStore.ConfigPath, ConfigStore.ToJson(c).Replace("\"SavedByVersion\": null,", ""), new UTF8Encoding(false));
        ConfigStore.Load();
        Check(ConfigStore.UpgradedFrom == "", "config from 1.2 or older (no version field) counts as an upgrade");
    }

    static void TestDiscovery()
    {
        string root = FreshDir("v13_discovery");
        // 다운로드 폴더에 1.2 포터블을 풀어 쓰다가, 1.3 포터블을 새 폴더에 푼 경우
        string oldData = Path.Combine(root, @"WinDeck-Portable-1.2.0\WinDeck\WinDeckData");
        string newApp = Path.Combine(root, @"WinDeck-Portable-1.3.0\WinDeck");
        Directory.CreateDirectory(oldData);
        Directory.CreateDirectory(Path.Combine(newApp, "WinDeckData"));
        AppConfig old = AppConfig.CreateDefault();
        old.Pages[0].Name = "옛 포터블 설정";
        ConfigStore.Export(old, Path.Combine(oldData, "config.json"));
        File.SetLastWriteTime(Path.Combine(oldData, "config.json"), DateTime.Now.AddMinutes(5)); // 실제 %APPDATA% 설정보다 최근으로

        ConfigStore.AppDir = newApp;
        ConfigStore.Dir = null;
        ConfigStore.Load();
        FoundConfig f = ConfigStore.FindPreviousConfig();
        Check(ConfigStore.IsNew && f != null && f.IsNewLocation && f.Config.Pages[0].Name == "옛 포터블 설정",
            "first run in a new portable folder finds the previous portable settings", f == null ? "none" : f.Path);
        ConfigStore.RememberLocation();

        // 이미 설정이 있는 곳: 마지막으로 쓴 다른 위치가 더 최근일 때만 알려준다.
        string otherDir = Path.Combine(root, "other");
        ConfigStore.AppDir = root;
        ConfigStore.Dir = otherDir;
        AppConfig mine = AppConfig.CreateDefault();
        mine.Pages[0].Name = "오래된 다른 설정";
        ConfigStore.Save(mine);
        File.SetLastWriteTime(ConfigStore.ConfigPath, DateTime.Now.AddMinutes(-30));
        string newerPath = Path.Combine(newApp, @"WinDeckData\config.json");
        File.SetLastWriteTime(newerPath, DateTime.Now);
        ConfigStore.Load();
        f = ConfigStore.FindPreviousConfig();
        Check(!ConfigStore.IsNew && f != null && !f.IsNewLocation && f.Path.Equals(Path.GetFullPath(newerPath), StringComparison.OrdinalIgnoreCase),
            "newer settings at the last used location are offered", f == null ? "none" : f.Path);

        File.SetLastWriteTime(newerPath, DateTime.Now.AddHours(-2));
        ConfigStore.Load();
        Check(ConfigStore.FindPreviousConfig() == null, "older settings elsewhere are not offered");

        // 바탕 화면·다운로드에 받아 둔 설정 파일(.json)은 저절로 쓰지 않는다.
        File.WriteAllText(Path.Combine(root, "WinDeck_백업.json"), ConfigStore.ToJson(old), new UTF8Encoding(false));
        ConfigStore.AppDir = newApp;
        ConfigStore.Dir = FreshDir(@"v13_discovery\WinDeck-Portable-1.3.0\WinDeck\WinDeckData");
        ConfigStore.Load();
        f = ConfigStore.FindPreviousConfig();
        Check(f == null || !f.Path.EndsWith("WinDeck_백업.json"), "loose .json files are never picked up automatically");
        ConfigStore.AppDir = null;
    }

    static void TestDeckSync()
    {
        ConfigStore.Dir = FreshDir("v13_deck");
        AppConfig cfg = ConfigStore.Load();
        cfg.HasPosition = true;
        cfg.WindowX = 321;
        cfg.WindowY = 123;
        ConfigStore.Save(cfg);
        var deck = new DeckForm(cfg, false);
        try
        {
            Check(!deck.CheckExternalChange(), "no reload when nothing changed");

            AppConfig other = ConfigStore.Clone(cfg);
            other.Pages[0].Name = "다른 PC 에서 고친 설정";
            other.WindowX = 5;
            File.WriteAllText(ConfigStore.ConfigPath, ConfigStore.ToJson(other), new UTF8Encoding(false));
            Check(deck.CheckExternalChange() && deck.Config.Pages[0].Name == "다른 PC 에서 고친 설정",
                "panel reloads a config file changed while running");
            Check(deck.Config.WindowX == 321, "reload keeps this PC's panel position");
            Check(BackupReasons().Contains("before_reload"), "state before reload is backed up");

            // 설정 창을 연 사이 단축키로 페이지를 넘겨도, 확인을 누르면 지금 페이지가 유지된다.
            AppConfig dialogCopy = ConfigStore.Clone(deck.Config);
            deck.GoToPage(deck.Config.Pages[1].Id);
            dialogCopy.Opacity = 80;
            int before = ConfigStore.ListBackups().Count;
            deck.ApplySettingsResult(dialogCopy, "before_settings");
            Check(deck.Config.CurrentPage == 1 && deck.Config.Opacity == 80, "settings OK keeps the page chosen meanwhile", deck.Config.CurrentPage.ToString());
            Check(ConfigStore.ListBackups().Count == before + 1 && BackupReasons()[0] == "before_settings", "settings change is backed up");
            deck.ApplySettingsResult(ConfigStore.Clone(deck.Config), "before_settings");
            Check(ConfigStore.ListBackups().Count == before + 1, "unchanged settings make no extra backup");
            Check(ConfigStore.Load().Opacity == 80, "settings result saved");
        }
        finally
        {
            deck.Dispose();
        }

        string export = Path.Combine(outDir, "v13_shared.json");
        ConfigStore.Export(cfg, export);
        string notConfig = Path.Combine(outDir, "v13_other.json");
        File.WriteAllText(notConfig, "[1, 2, 3]");
        Check(ConfigStore.LooksLikeConfigFile(export) && !ConfigStore.LooksLikeConfigFile(notConfig)
            && !ConfigStore.LooksLikeConfigFile(Path.Combine(outDir, "nothing.json")), "dropped .json is recognised only if it is a WinDeck config");

        AppConfig a = AppConfig.CreateDefault(), b = AppConfig.CreateDefault();
        a.HasPosition = true; a.WindowX = 77; a.LastUpdateCheck = "x"; a.SkippedUpdateVersion = "9.0.0";
        b.Hotkey = "Ctrl+F12";
        ConfigStore.KeepLocalState(a, b);
        Check(b.WindowX == 77 && b.LastUpdateCheck == "x" && b.SkippedUpdateVersion == "9.0.0" && b.Hotkey == "Ctrl+F12",
            "imported settings keep this PC's position and update state");
    }

    static void TestPathRemap()
    {
        string desktop = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
        string docs = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
        string downloads = KnownFolders.Downloads;
        Check(ActionRunner.RemapUserPath(@"C:\Users\teammate\Desktop") == desktop, "teammate's Desktop path opens my Desktop", ActionRunner.RemapUserPath(@"C:\Users\teammate\Desktop"));
        Check(ActionRunner.RemapUserPath(@"C:\Users\teammate\OneDrive\바탕 화면") == desktop, "OneDrive Korean desktop path is remapped");
        Check(ActionRunner.RemapUserPath(@"C:\Users\teammate\OneDrive - 회사\Documents") == docs, "OneDrive for Business documents path is remapped");
        if (downloads != null)
            Check(ActionRunner.RemapUserPath(@"C:\Users\teammate\Downloads") == downloads, "teammate's Downloads path opens my Downloads");
        Check(ActionRunner.RemapUserPath(@"C:\Users\teammate\Desktop\없는폴더-xyz") == @"C:\Users\teammate\Desktop\없는폴더-xyz", "missing file stays as is (shows a clear error)");
        Check(ActionRunner.RemapUserPath(@"Q:\공유\자료") == @"Q:\공유\자료", "non-profile paths are not touched");
        Check(ActionRunner.RemapUserPath(outDir) == outDir, "existing paths are not touched");
    }

    static void TestUpdaterUnits()
    {
        Check(Updater.VersionFromTag("v1.3.1") == "1.3.1" && Updater.VersionFromTag("1.4") == "1.4.0" && Updater.VersionFromTag("latest") == null, "release tag parsing");
        Check(Updater.IsNewer("1.3.1", "1.3.0") && Updater.IsNewer("1.10.0", "1.9.9") && !Updater.IsNewer("1.3.0", "1.3.0") && !Updater.IsNewer("1.2.9", "1.3.0"),
            "version comparison");

        string json = "{\"tag_name\":\"v1.3.1\",\"html_url\":\"https://github.com/musiqsb-svg/WinDeck/releases/tag/v1.3.1\",\"body\":\"- 고침\","
            + "\"assets\":[{\"name\":\"WinDeck-Setup-1.3.1.exe\",\"browser_download_url\":\"https://x/s.exe\"},"
            + "{\"name\":\"WinDeck-Portable-1.3.1.zip\",\"browser_download_url\":\"https://x/p.zip\"},"
            + "{\"name\":\"WinDeck-SHA256-1.3.1.txt\",\"browser_download_url\":\"https://x/h.txt\"}]}";
        UpdateInfo info = Updater.ParseRelease(json);
        Check(info.Version == "1.3.1" && info.SetupUrl == "https://x/s.exe" && info.PortableUrl == "https://x/p.zip" && info.ChecksumUrl == "https://x/h.txt"
            && info.Notes == "- 고침", "GitHub release JSON parsing");

        Check(UpdateForm.NotesText("## 1.3.1\n### 추가\n- **굵게** `코드`\n  - 하위") == "1.3.1\r\n추가\r\n• 굵게 코드\r\n  • 하위",
            "release notes shown without markdown symbols", UpdateForm.NotesText("## 1.3.1\n### 추가\n- **굵게** `코드`\n  - 하위"));

        string notes = UpdateForm.NotesText("> [!WARNING]\n> **Edge 로는 설치되지 않습니다.**\n\n### 받을 파일\n| ① | ② |\n|---|---|\n| <img src=\"a.png\"> | <img src=\"b.png\"> |\n\n\n### 고침\n- 설정 유지");
        Check(notes == "Edge 로는 설치되지 않습니다.\r\n\r\n받을 파일\r\n\r\n고침\r\n• 설정 유지", "release notes drop alert markers and image tables", notes);

        Dictionary<string, string> sums = Updater.ParseChecksums("ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef0123456789  WinDeck-Setup-1.3.1.exe\n"
            + "0000000000000000000000000000000000000000000000000000000000000000 *WinDeck-Portable-1.3.1.zip\n");
        Check(sums.Count == 2 && sums["windeck-setup-1.3.1.exe"].StartsWith("abcdef") && sums["WinDeck-Portable-1.3.1.zip"].StartsWith("0000"), "SHA-256 list parsing");

        string dir = FreshDir("v13_update");
        string abc = Path.Combine(dir, "abc.txt");
        File.WriteAllText(abc, "abc", Encoding.ASCII);
        Check(Updater.Sha256(abc) == "ba7816bf8f01cfea414140de5dae2223b00361a396177a9cb410ff61f20015ad", "SHA-256 of a file");

        string winDeckExe = typeof(AppInfo).Assembly.Location;
        Updater.CheckFileVersion(winDeckExe, AppInfo.Version);
        bool threw = false;
        try { Updater.CheckFileVersion(winDeckExe, "99.0.0"); }
        catch (UpdateException) { threw = true; }
        Check(threw, "file version mismatch is refused");

        // 포터블 업데이트 준비: file:// 로 받아서 검사하고 exe 만 꺼낸다.
        string zip = Path.Combine(dir, "WinDeck-Portable-" + AppInfo.Version + ".zip");
        MakePortableZip(zip, winDeckExe, "안내 v13");
        string sumsFile = Path.Combine(dir, "WinDeck-SHA256-" + AppInfo.Version + ".txt");
        File.WriteAllText(sumsFile, Updater.Sha256(zip) + "  " + Path.GetFileName(zip) + "\n");
        string app = Path.Combine(dir, "app");
        Directory.CreateDirectory(app);
        ConfigStore.AppDir = app;
        var local = new UpdateInfo
        {
            Version = AppInfo.Version,
            PortableName = Path.GetFileName(zip),
            PortableUrl = new Uri(zip).AbsoluteUri,
            ChecksumUrl = new Uri(sumsFile).AbsoluteUri
        };
        var steps = new List<string>();
        PreparedUpdate p = Updater.Prepare(local, (s, n) => steps.Add(s), null);
        Check(!p.Installer && File.Exists(Path.Combine(p.Path, "WinDeck.exe")) && File.Exists(Path.Combine(p.Path, "WinDeck 사용 안내.txt"))
            && !Directory.Exists(Path.Combine(p.Path, "WinDeckData")) && !File.Exists(Path.Combine(p.Path, "WinDeck.defaults.json")),
            "portable update unpacks only the exe and guide (settings folder untouched)");
        Check(steps.Any(s => s.StartsWith("받는 중")) && steps.Last().Contains("검사"), "download progress is reported");

        File.WriteAllText(sumsFile, new string('0', 64) + "  " + Path.GetFileName(zip) + "\n");
        threw = false;
        try { Updater.Prepare(local, (s, n) => { }, null); }
        catch (UpdateException ex) { threw = ex.Message.Contains("SHA-256"); }
        Check(threw, "download whose SHA-256 does not match is refused");

        local.ChecksumUrl = null;
        threw = false;
        try { Updater.Prepare(local, (s, n) => { }, null); }
        catch (UpdateException) { threw = true; }
        Check(threw, "release without a SHA-256 list is not installed automatically");
        ConfigStore.AppDir = null;
    }

    /// <summary>release.ps1 과 같은 모양의 포터블 zip (WinDeck/ 아래 exe·안내·WinDeckData·팀 기본 설정).</summary>
    static void MakePortableZip(string zip, string exe, string guideText)
    {
        if (File.Exists(zip)) File.Delete(zip);
        using (FileStream fs = File.Create(zip))
        using (var za = new ZipArchive(fs, ZipArchiveMode.Create, false, Encoding.UTF8))
        {
            za.CreateEntry("WinDeck/");
            za.CreateEntryFromFile(exe, "WinDeck/WinDeck.exe");
            using (var w = new StreamWriter(za.CreateEntry("WinDeck/WinDeck 사용 안내.txt").Open(), new UTF8Encoding(true))) w.Write(guideText);
            using (var w = new StreamWriter(za.CreateEntry("WinDeck/WinDeck.defaults.json").Open())) w.Write("{}");
            za.CreateEntry("WinDeck/WinDeckData/");
        }
    }

    // ------------------------------------------------------------------ real processes (--process <fake release dir>)

    [DllImport("user32.dll")] static extern bool EnumWindows(EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll")] static extern bool EnumChildWindows(IntPtr parent, EnumProc cb, IntPtr lParam);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int GetWindowText(IntPtr h, StringBuilder sb, int max);
    [DllImport("user32.dll")] static extern bool IsWindowVisible(IntPtr h);
    [DllImport("user32.dll")] static extern IntPtr SendMessageTimeout(IntPtr h, uint msg, IntPtr w, IntPtr l, uint flags, uint timeout, out IntPtr result);
    [DllImport("user32.dll")] static extern int GetDlgCtrlID(IntPtr h);
    delegate bool EnumProc(IntPtr h, IntPtr lParam);
    const uint BM_CLICK = 0x00F5;

    static string WindowText(IntPtr h)
    {
        var sb = new StringBuilder(512);
        GetWindowText(h, sb, sb.Capacity);
        return sb.ToString();
    }

    static IntPtr FindProcessWindow(int pid, string title, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            IntPtr found = IntPtr.Zero;
            EnumWindows((h, l) =>
            {
                uint p;
                GetWindowThreadProcessId(h, out p);
                if (p == pid && IsWindowVisible(h) && WindowText(h) == title) { found = h; return false; }
                return true;
            }, IntPtr.Zero);
            if (found != IntPtr.Zero) return found;
            Thread.Sleep(150);
        }
        return IntPtr.Zero;
    }

    static IntPtr FindChild(IntPtr parent, Func<string, bool> match)
    {
        IntPtr found = IntPtr.Zero;
        EnumChildWindows(parent, (h, l) =>
        {
            if (match(WindowText(h))) { found = h; return false; }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    static bool WaitFor(Func<bool> condition, int timeoutMs)
    {
        var sw = Stopwatch.StartNew();
        while (sw.ElapsedMilliseconds < timeoutMs)
        {
            if (condition()) return true;
            Thread.Sleep(150);
        }
        return condition();
    }

    /// <summary>실행 중인 WinDeck 의 저장을 막지 않도록 공유 모드로 설정 파일을 읽는다.</summary>
    static AppConfig ReadShared(string path)
    {
        using (var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
        using (var reader = new StreamReader(fs, Encoding.UTF8))
            return ConfigStore.Parse(reader.ReadToEnd());
    }

    static Process StartApp(string exe, string args, string updateUrl)
    {
        var psi = new ProcessStartInfo(exe, args) { UseShellExecute = false, WorkingDirectory = Path.GetDirectoryName(exe) };
        psi.EnvironmentVariables["WINDECK_INSTANCE"] = Environment.GetEnvironmentVariable("WINDECK_INSTANCE");
        if (updateUrl != null) psi.EnvironmentVariables["WINDECK_UPDATE_URL"] = updateUrl;
        return Process.Start(psi);
    }

    static int RunApp(string exe, string args, int timeoutMs)
    {
        using (Process p = StartApp(exe, args, null))
            return p.WaitForExit(timeoutMs) ? p.ExitCode : -1;
    }

    /// <summary>시험용 포터블 WinDeck 폴더 (exe + 설정 2페이지).</summary>
    static string MakeAppCopy(string name, string pageName)
    {
        string app = FreshDir(name);
        File.Copy(typeof(AppInfo).Assembly.Location, Path.Combine(app, "WinDeck.exe"));
        string data = Path.Combine(app, "WinDeckData");
        Directory.CreateDirectory(data);
        ConfigStore.Dir = data;
        AppConfig c = AppConfig.CreateDefault();
        c.Pages[0].Name = pageName;
        c.AutoUpdateCheck = false;
        c.HasPosition = true;
        c.WindowX = 40;
        c.WindowY = 40;
        ConfigStore.Save(c);
        return app;
    }

    static void TestProcesses(string fakeRelease)
    {
        string savedDir = ConfigStore.Dir;
        string testId = "proc" + Process.GetCurrentProcess().Id;
        Environment.SetEnvironmentVariable("WINDECK_INSTANCE", testId);
        var started = new List<Process>();
        try
        {
            // 0) 새 폴더에 푼 포터블: 처음 실행할 때 예전 포터블 설정을 찾아 묻고, [예]면 그 설정으로 시작 (1.2 → 1.3 되돌아감 원인)
            FreshDir("v13_proc_portable");
            string oldApp = MakeAppCopy(@"v13_proc_portable\WinDeck-Portable-1.2.0\WinDeck", "예전 포터블 설정");
            File.SetLastWriteTime(Path.Combine(oldApp, @"WinDeckData\config.json"), DateTime.Now.AddMinutes(10));
            string newApp = FreshDir(@"v13_proc_portable\WinDeck-Portable-1.3.0\WinDeck");
            Directory.CreateDirectory(Path.Combine(newApp, "WinDeckData"));
            string newExe = Path.Combine(newApp, "WinDeck.exe");
            File.Copy(typeof(AppInfo).Assembly.Location, newExe);
            Process first = StartApp(newExe, "", null);
            started.Add(first);
            IntPtr ask = FindProcessWindow(first.Id, "WinDeck - 이전 설정", 15000);
            Check(ask != IntPtr.Zero, "new portable folder asks to load the previous portable settings");
            if (ask != IntPtr.Zero)
            {
                // 메시지 상자는 활성 창이 아니면 BM_CLICK 을 무시하므로, 단추 ID 로 WM_COMMAND 를 보낸다.
                IntPtr yes = FindChild(ask, t => t.StartsWith("예"));
                Check(yes != IntPtr.Zero, "yes button on the previous-settings question");
                IntPtr r;
                SendMessageTimeout(ask, 0x0111 /* WM_COMMAND */, (IntPtr)GetDlgCtrlID(yes), yes, 0x0002, 5000, out r);
                Check(WaitFor(() =>
                {
                    try { return ReadShared(Path.Combine(newApp, @"WinDeckData\config.json")).Pages[0].Name == "예전 포터블 설정"; }
                    catch (Exception) { return false; }
                }, 10000), "answering yes loads the previous settings into the new folder");
            }
            Check(RunApp(newExe, "--exit", 15000) == 0 && first.WaitForExit(5000), "new portable exits on request");

            // 1) --exit: 저장하고 스스로 끝난다
            string app = MakeAppCopy("v13_proc_exit", "종료 점검");
            string exe = Path.Combine(app, "WinDeck.exe");
            string config = Path.Combine(app, @"WinDeckData\config.json");
            Check(RunApp(exe, "--exit", 5000) == 0, "--exit with nothing running returns at once");
            Process p = StartApp(exe, "", null);
            started.Add(p);
            Check(WaitFor(AppInstance.IsRunning, 10000), "test instance starts");
            Thread.Sleep(1500);
            DateTime before = File.GetLastWriteTimeUtc(config);
            var sw = Stopwatch.StartNew();
            Check(RunApp(exe, "--exit", 15000) == 0 && p.WaitForExit(5000), "--exit closes the running WinDeck", sw.ElapsedMilliseconds + "ms");
            Check(p.HasExited && p.ExitCode == 0 && File.GetLastWriteTimeUtc(config) > before, "WinDeck saved its settings before exiting");
            Check(ReadShared(config).Pages[0].Name == "종료 점검", "settings intact after graceful exit");
            Check(Directory.GetFiles(Path.Combine(app, @"WinDeckData\backups"), "daily_*.json").Length == 1, "daily backup made at start-up");

            // 2) 업데이트 창이 열려 있어도 --exit 로 끝난다
            string releaseJson = Path.Combine(fakeRelease, "release.json");
            string url = new Uri(releaseJson).AbsoluteUri;
            p = StartApp(exe, "", url);
            started.Add(p);
            WaitFor(AppInstance.IsRunning, 10000);
            RunApp(exe, "--check-update", 5000);
            IntPtr dlg = FindProcessWindow(p.Id, "WinDeck 업데이트", 15000);
            Check(dlg != IntPtr.Zero, "--check-update opens the update window for a newer release");
            if (dlg != IntPtr.Zero)
                Check(FindChild(dlg, t => t.StartsWith("새 버전 v99.0.0")) != IntPtr.Zero, "update window names the new version");
            Check(RunApp(exe, "--exit", 15000) == 0 && p.WaitForExit(5000), "--exit works while a dialog is open");

            // 3) SHA-256 이 맞지 않으면 설치하지 않는다
            p = StartApp(exe, "", new Uri(Path.Combine(fakeRelease, "release-bad.json")).AbsoluteUri);
            started.Add(p);
            WaitFor(AppInstance.IsRunning, 10000);
            RunApp(exe, "--check-update", 5000);
            dlg = FindProcessWindow(p.Id, "WinDeck 업데이트", 15000);
            if (dlg != IntPtr.Zero)
            {
                Click(dlg, "지금 업데이트");
                bool failed = WaitFor(() => FindChild(dlg, t => t.StartsWith("업데이트하지 못했습니다")) != IntPtr.Zero, 15000);
                Check(failed && !p.HasExited, "corrupted download is refused and WinDeck keeps running");
                Check(Updater.VersionFromTag(FileVersionInfo.GetVersionInfo(exe).FileVersion) == AppInfo.Version, "exe unchanged after refused update");
            }
            else Check(false, "update window for bad release");
            RunApp(exe, "--exit", 15000);
            p.WaitForExit(5000);

            // 4) 포터블 업데이트: 받기 → 검사 → exe 교체 → 다시 실행, 설정은 그대로
            app = MakeAppCopy("v13_proc_update", "업데이트 점검");
            exe = Path.Combine(app, "WinDeck.exe");
            config = Path.Combine(app, @"WinDeckData\config.json");
            string pagesBefore = ConfigStore.Describe(ConfigStore.Import(config));
            p = StartApp(exe, "", url);
            started.Add(p);
            WaitFor(AppInstance.IsRunning, 10000);
            RunApp(exe, "--check-update", 5000);
            dlg = FindProcessWindow(p.Id, "WinDeck 업데이트", 15000);
            if (dlg == IntPtr.Zero)
            {
                Check(false, "update window for portable update");
                return;
            }
            Click(dlg, "지금 업데이트");
            Check(p.WaitForExit(20000), "old WinDeck exits after starting the update");
            bool swapped = WaitFor(() => Updater.VersionFromTag(FileVersionInfo.GetVersionInfo(exe).FileVersion) == "99.0.0", 10000);
            Check(swapped, "exe replaced by the new version");
            Check(WaitFor(AppInstance.IsRunning, 15000), "new version restarted by itself");
            Check(WaitFor(() => !File.Exists(exe + ".old"), 20000), "old exe cleaned up after restart");
            Check(File.ReadAllText(Path.Combine(app, "WinDeck 사용 안내.txt"), Encoding.UTF8).Contains("fake-guide-99"), "guide text updated too");
            Check(!File.Exists(Path.Combine(app, "WinDeck.defaults.json")), "team defaults file in the zip is not copied over");
            Thread.Sleep(1500);
            AppConfig after = ReadShared(config);
            Check(after.Pages[0].Name == "업데이트 점검" && ConfigStore.Describe(after) == pagesBefore, "settings unchanged by the update", ConfigStore.Describe(after));
            Check(after.SavedByVersion == "99.0.0", "new version took over the settings file", after.SavedByVersion);
            string[] backups = Directory.GetFiles(Path.Combine(app, @"WinDeckData\backups"), "before_update_*.json");
            Check(backups.Length >= 1, "settings backed up before the update", backups.Length.ToString());
            Check(RunApp(exe, "--exit", 15000) == 0 && WaitFor(() => !AppInstance.IsRunning(), 5000), "updated WinDeck exits on request");
        }
        finally
        {
            foreach (Process p in started)
                try { if (!p.HasExited) p.Kill(); }
                catch (Exception) { }
            foreach (Process p in Process.GetProcessesByName("WinDeck"))
            {
                try { if (p.MainModule.FileName.StartsWith(outDir, StringComparison.OrdinalIgnoreCase)) p.Kill(); }
                catch (Exception) { }
            }
            Environment.SetEnvironmentVariable("WINDECK_INSTANCE", null);
            DeleteTestRegistry(testId);
            ConfigStore.Dir = savedDir;
        }
    }

    static void Click(IntPtr dialog, string buttonText)
    {
        ClickWhere(dialog, t => t == buttonText);
    }

    static void ClickWhere(IntPtr dialog, Func<string, bool> match)
    {
        IntPtr button = FindChild(dialog, match);
        Check(button != IntPtr.Zero, "button '" + (button == IntPtr.Zero ? "?" : WindowText(button)) + "' found");
        IntPtr result;
        SendMessageTimeout(button, BM_CLICK, IntPtr.Zero, IntPtr.Zero, 0x0002, 5000, out result);
    }
}
