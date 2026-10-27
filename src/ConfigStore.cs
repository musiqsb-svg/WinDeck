using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Web.Script.Serialization;
using Microsoft.Win32;

namespace WinDeck
{
    /// <summary>
    /// 설정 파일 읽기·쓰기와 백업.
    /// 기본 위치는 %APPDATA%\WinDeck, exe 옆에 WinDeckData 폴더가 있으면 그곳(포터블 모드).
    /// </summary>
    public static class ConfigStore
    {
        public const string PortableFolderName = "WinDeckData";
        public const string TeamDefaultsFileName = "WinDeck.defaults.json";

        static string dir;
        static string appDir;

        /// <summary>WinDeck.exe 가 있는 폴더.</summary>
        public static string AppDir
        {
            get
            {
                if (appDir == null) appDir = Path.GetDirectoryName(System.Windows.Forms.Application.ExecutablePath);
                return appDir;
            }
            set { appDir = value; }
        }

        public static string Dir
        {
            get
            {
                if (dir == null) dir = ResolveDir();
                return dir;
            }
            set { dir = value; }
        }

        public static bool IsPortable
        {
            get { return string.Equals(Path.GetFullPath(Dir), Path.GetFullPath(Path.Combine(AppDir, PortableFolderName)), StringComparison.OrdinalIgnoreCase); }
        }

        static string ResolveDir()
        {
            string portable = Path.Combine(AppDir, PortableFolderName);
            if (Directory.Exists(portable) && IsWritable(portable)) return portable;
            // 점검용 실행(WINDECK_INSTANCE)은 실제 사용자 설정(%APPDATA%\WinDeck)을 절대 건드리지 않는다.
            if (AppInstance.IsTest) return Path.Combine(Path.GetTempPath(), "WinDeck-test", AppInstance.TestId);
            return AppDataDir;
        }

        static bool IsWritable(string folder)
        {
            try
            {
                // 이름이 길면 깊은 폴더에서 경로 길이 제한(260자)에 걸려 포터블 폴더를 못 쓰는 것으로 잘못 판단한다.
                string probe = Path.Combine(folder, ".wt" + Guid.NewGuid().ToString("N").Substring(0, 8));
                File.WriteAllText(probe, "");
                File.Delete(probe);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        /// <summary>
        /// 첫 실행 때 쓸 버튼 구성. exe 옆에 팀 기본 설정(WinDeck.defaults.json)이 있으면 그것을, 없으면 내장 기본값.
        /// </summary>
        public static AppConfig CreateInitialConfig()
        {
            string teamDefaults = Path.Combine(AppDir, TeamDefaultsFileName);
            if (File.Exists(teamDefaults))
            {
                try
                {
                    AppConfig c = Parse(File.ReadAllText(teamDefaults, Encoding.UTF8));
                    c.HasPosition = false;
                    c.CurrentPage = 0;
                    return c;
                }
                catch (Exception ex)
                {
                    ErrorLog.Write(ex);
                }
            }
            return AppConfig.CreateDefault();
        }

        public static string ConfigPath
        {
            get { return Path.Combine(Dir, "config.json"); }
        }

        /// <summary>이번 실행에서 설정 파일을 새로 만들었는지 (첫 실행 안내용).</summary>
        public static bool IsNew { get; private set; }

        /// <summary>설정 파일이 손상되어 기본값으로 시작했을 때의 안내 문구.</summary>
        public static string LoadWarning { get; private set; }

        static JavaScriptSerializer Serializer()
        {
            return new JavaScriptSerializer { MaxJsonLength = int.MaxValue, RecursionLimit = 64 };
        }

        /// <summary>이번 실행에서 버전이 바뀌었으면 이전 버전 ("" 은 버전 기록이 없던 1.2 이하). 아니면 null.</summary>
        public static string UpgradedFrom { get; private set; }

        public static AppConfig Load()
        {
            IsNew = false;
            LoadWarning = null;
            UpgradedFrom = null;
            Previous = null;
            if (File.Exists(ConfigPath))
            {
                byte[] data = null;
                try
                {
                    FileInfo stamp = new FileInfo(ConfigPath);
                    data = File.ReadAllBytes(ConfigPath);
                    AppConfig loaded = Parse(Encoding.UTF8.GetString(data));
                    Remember(data, stamp);
                    if (loaded.SavedByVersion != AppInfo.Version)
                    {
                        // 업데이트(또는 다른 버전 실행) 직후: 읽은 그대로의 파일을 먼저 백업하고 새 버전으로 저장해 둔다.
                        UpgradedFrom = loaded.SavedByVersion ?? "";
                        try { BackupFile(ConfigPath, "before_update"); }
                        catch (Exception ex) { ErrorLog.Write(ex); }
                        try { Save(loaded); }
                        catch (Exception ex) { ErrorLog.Write(ex); }
                    }
                    return loaded;
                }
                catch (Exception ex)
                {
                    string broken = ConfigPath + ".broken-" + DateTime.Now.ToString("yyyyMMdd-HHmmss");
                    try { File.Copy(ConfigPath, broken, true); }
                    catch (Exception) { }
                    if (data != null) Remember(data, new FileInfo(ConfigPath));
                    LoadWarning = "설정 파일을 읽지 못해 기본 설정으로 시작합니다.\n기존 파일은 다음 위치에 보관했습니다:\n"
                        + broken + "\n\n(" + ex.Message + ")";
                }
            }
            else
            {
                IsNew = true;
            }

            AppConfig c = IsNew ? CreateInitialConfig() : AppConfig.CreateDefault();
            try { Save(c); }
            catch (Exception) { }
            return c;
        }

        public static AppConfig Parse(string json)
        {
            AppConfig c;
            try
            {
                c = Serializer().Deserialize<AppConfig>(json);
            }
            catch (Exception ex)
            {
                throw new InvalidDataException("올바른 WinDeck 설정 파일이 아닙니다. (" + ex.Message + ")");
            }
            if (c == null || c.Pages == null)
                throw new InvalidDataException("올바른 WinDeck 설정 파일이 아닙니다.");
            c.Normalize();
            return c;
        }

        public static string ToJson(AppConfig c)
        {
            return PrettyJson(Serializer().Serialize(c));
        }

        public static AppConfig Clone(AppConfig c)
        {
            return Parse(ToJson(c));
        }

        /// <summary>
        /// 설정을 저장한다. 마지막으로 읽거나 저장한 뒤 다른 곳에서 파일이 바뀌었으면, 덮어쓰기 전에 그 파일을 백업한다.
        /// </summary>
        public static void Save(AppConfig c)
        {
            c.SavedByVersion = AppInfo.Version;
            byte[] data = new UTF8Encoding(false).GetBytes(ToJson(c));
            if (ChangedOnDisk())
            {
                try { BackupFile(ConfigPath, "external_change"); }
                catch (Exception ex) { ErrorLog.Write(ex); }
            }
            WriteAtomic(ConfigPath, data);
            Remember(data, new FileInfo(ConfigPath));
        }

        public static void Export(AppConfig c, string path)
        {
            File.WriteAllText(path, ToJson(c), new UTF8Encoding(false));
        }

        public static AppConfig Import(string path)
        {
            return Parse(File.ReadAllText(path, Encoding.UTF8));
        }

        /// <summary>
        /// 이 PC 에서만 의미 있는 값(패널 위치, 업데이트 확인 기록 등)을 다른 설정으로 옮긴다.
        /// 불러오기·되돌리기·다시 읽기를 해도 패널이 엉뚱한 곳으로 옮겨지지 않게.
        /// </summary>
        public static void KeepLocalState(AppConfig from, AppConfig to)
        {
            to.HasPosition = from.HasPosition;
            to.WindowX = from.WindowX;
            to.WindowY = from.WindowY;
            to.LastUpdateCheck = from.LastUpdateCheck;
            to.SkippedUpdateVersion = from.SkippedUpdateVersion;
            to.LastImportFolder = from.LastImportFolder;
        }

        /// <summary>"페이지 3개 · 버튼 14개" (안내 문구용).</summary>
        public static string Describe(AppConfig c)
        {
            int buttons = 0;
            foreach (DeckPage p in c.Pages)
                foreach (DeckButton b in p.Buttons)
                    if (b != null && !b.IsEmpty) buttons++;
            return "페이지 " + c.Pages.Count + "개 · 버튼 " + buttons + "개";
        }

        /// <summary>WinDeck 설정 파일처럼 보이는지 (끌어놓은 .json 을 버튼으로 만들지, 불러올지 정할 때).</summary>
        public static bool LooksLikeConfigFile(string path)
        {
            try
            {
                if (!path.EndsWith(".json", StringComparison.OrdinalIgnoreCase)) return false;
                var fi = new FileInfo(path);
                if (!fi.Exists || fi.Length > 64L * 1024 * 1024) return false;
                Import(path);
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // ------------------------------------------------------------------ 다른 곳에서 바뀐 파일 감지

        static string knownPath, knownHash;
        static DateTime knownTime;
        static long knownLength = -1;

        static void Remember(byte[] data, FileInfo stamp)
        {
            knownPath = ConfigPath;
            knownHash = Hash(data);
            stamp.Refresh();
            knownTime = stamp.Exists ? stamp.LastWriteTimeUtc : DateTime.MinValue;
            knownLength = stamp.Exists ? stamp.Length : -1;
        }

        static string Hash(byte[] data)
        {
            using (var sha = System.Security.Cryptography.SHA256.Create())
                return Convert.ToBase64String(sha.ComputeHash(data));
        }

        /// <summary>마지막으로 읽거나 저장한 뒤 다른 프로그램·사람이 config.json 을 바꿨는지.</summary>
        public static bool ChangedOnDisk()
        {
            if (knownHash == null || !string.Equals(knownPath, ConfigPath, StringComparison.OrdinalIgnoreCase)) return false;
            var fi = new FileInfo(ConfigPath);
            if (!fi.Exists) return false;
            if (fi.LastWriteTimeUtc == knownTime && fi.Length == knownLength) return false;
            byte[] data;
            try
            {
                data = File.ReadAllBytes(ConfigPath);
            }
            catch (IOException)
            {
                return true; // 다른 프로그램이 쓰는 중
            }
            if (Hash(data) != knownHash) return true;
            knownTime = fi.LastWriteTimeUtc; // 내용은 같고 시간만 바뀜
            knownLength = fi.Length;
            return false;
        }

        /// <summary>디스크의 config.json 을 다시 읽는다. 읽지 못하면 예외 (기억해 둔 상태는 그대로).</summary>
        public static AppConfig ReloadFromDisk()
        {
            var stamp = new FileInfo(ConfigPath);
            byte[] data = File.ReadAllBytes(ConfigPath);
            AppConfig c = Parse(Encoding.UTF8.GetString(data));
            Remember(data, stamp);
            return c;
        }

        // ------------------------------------------------------------------ 자동 백업

        public const int MaxBackups = 20;
        static readonly Regex BackupName = new Regex(@"^(?<reason>.+)_(?<date>\d{8})_(?<time>\d{6})(_\d+)?\.json$", RegexOptions.IgnoreCase);

        public static string BackupDir
        {
            get { return Path.Combine(Dir, "backups"); }
        }

        /// <summary>현재 설정을 backups 폴더에 자동 보관하고 그 경로를 돌려준다. 최근 20개만 남긴다.</summary>
        public static string AutoBackup(AppConfig c, string reason)
        {
            string path = NewBackupPath(reason);
            File.WriteAllText(path, ToJson(c), new UTF8Encoding(false));
            PruneBackups();
            return path;
        }

        /// <summary>파일을 그대로 backups 폴더에 복사한다 (읽지 못하는 파일도 보관할 수 있게).</summary>
        public static string BackupFile(string source, string reason)
        {
            string path = NewBackupPath(reason);
            File.Copy(source, path);
            File.SetLastWriteTime(path, DateTime.Now);
            PruneBackups();
            return path;
        }

        static string NewBackupPath(string reason)
        {
            Directory.CreateDirectory(BackupDir);
            string stem = Path.Combine(BackupDir, reason + "_" + DateTime.Now.ToString("yyyyMMdd_HHmmss"));
            string path = stem + ".json";
            for (int n = 2; File.Exists(path); n++) path = stem + "_" + n + ".json";
            return path;
        }

        static void PruneBackups()
        {
            List<BackupInfo> all = ListBackups();
            for (int i = MaxBackups; i < all.Count; i++)
            {
                try { File.Delete(all[i].Path); }
                catch (Exception) { }
            }
        }

        /// <summary>backups 폴더의 백업 목록 (최신순).</summary>
        public static List<BackupInfo> ListBackups()
        {
            var list = new List<BackupInfo>();
            if (!Directory.Exists(BackupDir)) return list;
            foreach (string file in Directory.GetFiles(BackupDir, "*.json"))
            {
                string name = Path.GetFileName(file);
                Match m = BackupName.Match(name);
                DateTime time;
                string reason = m.Success ? m.Groups["reason"].Value : "";
                if (!m.Success || !DateTime.TryParseExact(m.Groups["date"].Value + m.Groups["time"].Value, "yyyyMMddHHmmss",
                        CultureInfo.InvariantCulture, DateTimeStyles.None, out time))
                    time = File.GetLastWriteTime(file);
                list.Add(new BackupInfo { Path = file, Time = time, Reason = reason });
            }
            list.Sort((a, b) => b.Time != a.Time ? b.Time.CompareTo(a.Time) : string.Compare(b.Path, a.Path, StringComparison.OrdinalIgnoreCase));
            return list;
        }

        /// <summary>하루에 한 번 자동 백업 (프로그램을 켤 때와 켜 둔 동안 한 시간마다 확인).</summary>
        public static void BackupIfDue(AppConfig c)
        {
            try
            {
                foreach (BackupInfo b in ListBackups())
                    if (b.Reason == "daily" && DateTime.Now - b.Time < TimeSpan.FromHours(24)) return;
                AutoBackup(c, "daily");
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
            }
        }

        // ------------------------------------------------------------------ 다른 위치에 있는 이전 설정 찾기

        /// <summary>시작할 때 찾은, 불러올 만한 이전 설정 (없으면 null).</summary>
        public static FoundConfig Previous { get; set; }

        const string LastDirValue = "LastConfigDir";

        static string AppDataDir
        {
            get { return Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WinDeck"); }
        }

        /// <summary>마지막으로 쓴 설정 위치를 기록한다 (다음에 다른 위치에서 실행했을 때 찾을 수 있게).</summary>
        public static void RememberLocation()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.CreateSubKey(AppInstance.RegistryKey))
                    k.SetValue(LastDirValue, Path.GetFullPath(Dir));
            }
            catch (Exception) { }
        }

        static string LastLocation()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(AppInstance.RegistryKey, false))
                    return k == null ? null : k.GetValue(LastDirValue) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>
        /// 이 위치에서 처음 실행했거나(새 폴더에 포터블을 풀었을 때 등), 마지막으로 쓴 다른 위치의 설정이 더 최근이면
        /// 그 설정을 Previous 에 담는다. 바탕 화면·다운로드에 받아 둔 .json 은 찾지 않는다 (WinDeck 설정 폴더만).
        /// </summary>
        public static FoundConfig FindPreviousConfig()
        {
            Previous = null;
            try
            {
                string here = Path.GetFullPath(ConfigPath);
                string last = LastLocation();
                var candidates = new List<string>();
                if (!string.IsNullOrEmpty(last)) candidates.Add(Path.Combine(last, "config.json"));
                if (IsNew)
                {
                    if (!AppInstance.IsTest) candidates.Add(Path.Combine(AppDataDir, "config.json"));
                    candidates.AddRange(NearbyPortableConfigs());
                }

                FoundConfig best = null;
                var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase) { here };
                foreach (string path in candidates)
                {
                    string full;
                    try { full = Path.GetFullPath(path); }
                    catch (Exception) { continue; }
                    if (!seen.Add(full) || !File.Exists(full)) continue;
                    DateTime time = File.GetLastWriteTime(full);
                    if (best != null && time <= best.Time) continue;
                    try
                    {
                        AppConfig c = Import(full);
                        best = new FoundConfig { Path = full, Time = time, Config = c, IsNewLocation = IsNew };
                    }
                    catch (Exception) { }
                }
                if (best == null) return null;
                if (!IsNew)
                {
                    // 이미 설정이 있는 곳: 마지막으로 쓴 다른 위치가 1분 이상 더 최근일 때만 묻는다.
                    if (best.Time <= File.GetLastWriteTime(here).AddMinutes(1)) return null;
                }
                Previous = best;
                return best;
            }
            catch (Exception ex)
            {
                ErrorLog.Write(ex);
                return null;
            }
        }

        /// <summary>exe 근처(같은 상위 폴더)에 풀어 둔 다른 포터블 WinDeck 의 설정 파일.</summary>
        static IEnumerable<string> NearbyPortableConfigs()
        {
            var found = new List<string>();
            string parent = Path.GetDirectoryName(AppDir);
            string grand = parent == null ? null : Path.GetDirectoryName(parent);
            int budget = 400;
            foreach (string root in new[] { parent, grand })
            {
                if (string.IsNullOrEmpty(root)) continue;
                foreach (string d1 in SafeDirs(root))
                {
                    if (--budget < 0) return found;
                    found.Add(Path.Combine(d1, PortableFolderName, "config.json"));
                    // 압축 풀기로 생긴 'WinDeck-Portable-1.2.0\WinDeck\' 같은 한 단계 아래 폴더
                    if (Path.GetFileName(d1).IndexOf("windeck", StringComparison.OrdinalIgnoreCase) < 0) continue;
                    foreach (string d2 in SafeDirs(d1))
                    {
                        if (--budget < 0) return found;
                        found.Add(Path.Combine(d2, PortableFolderName, "config.json"));
                    }
                }
            }
            return found;
        }

        static string[] SafeDirs(string dir)
        {
            try { return Directory.GetDirectories(dir); }
            catch (Exception) { return new string[0]; }
        }

        static void WriteAtomic(string path, byte[] content)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(path));
            string tmp = path + ".tmp";
            // 백신·OneDrive 같은 프로그램이 파일을 잠깐 열고 있으면 실패하므로 조금씩 기다리며 다시 시도한다 (최대 약 1.7초).
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    File.WriteAllBytes(tmp, content);
                    if (!File.Exists(path))
                    {
                        File.Move(tmp, path);
                        return;
                    }
                    try
                    {
                        File.Replace(tmp, path, null);
                    }
                    catch (Exception)
                    {
                        File.Copy(tmp, path, true);
                        File.Delete(tmp);
                    }
                    return;
                }
                catch (IOException)
                {
                    if (attempt >= 8) throw;
                    System.Threading.Thread.Sleep(60 * attempt);
                }
            }
        }

        /// <summary>JavaScriptSerializer 출력은 한 줄이라 사람이 읽을 수 있게 들여쓰기한다.</summary>
        public static string PrettyJson(string json)
        {
            var sb = new StringBuilder(json.Length * 2);
            int indent = 0;
            bool inString = false;
            bool escape = false;
            for (int i = 0; i < json.Length; i++)
            {
                char c = json[i];
                if (inString)
                {
                    sb.Append(c);
                    if (escape) escape = false;
                    else if (c == '\\') escape = true;
                    else if (c == '"') inString = false;
                    continue;
                }
                switch (c)
                {
                    case '"':
                        inString = true;
                        sb.Append(c);
                        break;
                    case '{':
                    case '[':
                        sb.Append(c);
                        int j = i + 1;
                        while (j < json.Length && char.IsWhiteSpace(json[j])) j++;
                        if (j < json.Length && (json[j] == '}' || json[j] == ']'))
                        {
                            sb.Append(json[j]);
                            i = j;
                            break;
                        }
                        indent++;
                        NewLine(sb, indent);
                        break;
                    case '}':
                    case ']':
                        indent--;
                        NewLine(sb, indent);
                        sb.Append(c);
                        break;
                    case ',':
                        sb.Append(c);
                        NewLine(sb, indent);
                        break;
                    case ':':
                        sb.Append(": ");
                        break;
                    default:
                        if (!char.IsWhiteSpace(c)) sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        static void NewLine(StringBuilder sb, int indent)
        {
            sb.Append("\r\n");
            sb.Append(' ', Math.Max(0, indent) * 2);
        }
    }

    public class BackupInfo
    {
        public string Path;
        public DateTime Time;
        public string Reason;

        /// <summary>백업 이유를 사람이 읽는 말로.</summary>
        public string ReasonText
        {
            get
            {
                switch (Reason)
                {
                    case "before_import": return "설정 불러오기 전";
                    case "before_settings": return "설정 바꾸기 전";
                    case "before_restore": return "되돌리기 전";
                    case "before_update": return "업데이트 전";
                    case "before_reload": return "바뀐 설정 다시 읽기 전";
                    case "before_previous": return "이전 설정 가져오기 전";
                    case "external_change": return "다른 곳에서 바뀐 파일";
                    case "daily": return "하루 자동 백업";
                    case "manual": return "직접 백업";
                }
                return string.IsNullOrEmpty(Reason) ? "백업" : Reason;
            }
        }
    }

    /// <summary>다른 위치에서 찾은 이전 설정.</summary>
    public class FoundConfig
    {
        public string Path;
        public DateTime Time;
        public AppConfig Config;
        /// <summary>true: 이 위치에서 처음 실행. false: 이미 설정이 있지만 다른 위치 것이 더 최근.</summary>
        public bool IsNewLocation;
    }
}
