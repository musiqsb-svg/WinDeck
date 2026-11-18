using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.IO.Compression;
using System.Net;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading;
using System.Web.Script.Serialization;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>GitHub 최신 릴리스 정보.</summary>
    public class UpdateInfo
    {
        public string Version, Tag, Notes, PageUrl;
        public string SetupName, SetupUrl, PortableName, PortableUrl, ChecksumUrl;
    }

    /// <summary>받아서 검사까지 끝난 업데이트.</summary>
    public class PreparedUpdate
    {
        /// <summary>1.4 부터 늘 true (설치 파일 실행).</summary>
        public bool Installer;
        /// <summary>받아서 검사한 설치 파일 경로.</summary>
        public string Path;
        public string Version;
    }

    public class UpdateException : Exception
    {
        public UpdateException(string message) : base(message) { }
    }

    /// <summary>
    /// 새 버전 확인과 설치. 받은 파일은 릴리스에 함께 올린 SHA-256 목록과 파일 버전으로 검사한 뒤에만 쓴다.
    /// 설치 파일을 조용히 실행하고, 설치가 끝나면 설치한 WinDeck 이 다시 켜진다 (설정 %APPDATA%\WinDeck 은 그대로).
    /// 릴리스의 포터블 zip 은 1.3 이하 포터블 판의 자동 업데이트를 위해서만 올린다.
    /// </summary>
    public static class Updater
    {
        public const string Repo = "musiqsb-svg/WinDeck";
        public const string ReleasesPage = "https://github.com/" + Repo + "/releases/latest";
        const string ApiLatest = "https://api.github.com/repos/" + Repo + "/releases/latest";
        const long MaxDownload = 100L * 1024 * 1024;

        static Updater()
        {
            // 이 exe 는 .NET 대상 버전 표시 없이 빌드되어 기본 TLS 가 낮을 수 있다. GitHub 는 TLS 1.2 이상만 받는다.
            try { ServicePointManager.SecurityProtocol |= SecurityProtocolType.Tls12; }
            catch (Exception) { }
        }

        static string UserAgent
        {
            get { return "WinDeck/" + AppInfo.Version; }
        }

        /// <summary>점검용 릴리스 정보 주소 (WINDECK_INSTANCE 와 WINDECK_UPDATE_URL 을 함께 줄 때만).</summary>
        public static string TestUrl
        {
            get
            {
                if (!AppInstance.IsTest) return null;
                string u = Environment.GetEnvironmentVariable("WINDECK_UPDATE_URL");
                return string.IsNullOrWhiteSpace(u) ? null : u.Trim();
            }
        }

        /// <summary>설치 파일로 설치한 WinDeck 인지 (아니면 포터블·단독 exe).</summary>
        public static bool IsInstalled
        {
            get
            {
                // 1.4 설치 프로그램으로 설치한 곳, 또는 1.3 이하 Inno Setup 으로 설치한 곳
                return InstallInfo.IsInstalledAppDir(ConfigStore.AppDir)
                    || File.Exists(System.IO.Path.Combine(ConfigStore.AppDir, "unins000.exe"));
            }
        }

        // ------------------------------------------------------------------ 확인

        /// <summary>최신 릴리스 정보를 받는다 (네트워크를 쓰므로 UI 스레드 밖에서 부를 것).</summary>
        public static UpdateInfo FetchLatest()
        {
            string test = TestUrl;
            if (test != null) return ParseRelease(GetString(test));
            try
            {
                return ParseRelease(GetString(ApiLatest));
            }
            catch (WebException)
            {
                // GitHub API 호출 한도(같은 회사망에서 여럿이 쓸 때 등)에 걸리면 릴리스 페이지 주소로 버전만 알아낸다.
                UpdateInfo info = FromLatestRedirect();
                if (info != null) return info;
                throw;
            }
        }

        public static UpdateInfo ParseRelease(string json)
        {
            var root = new JavaScriptSerializer().DeserializeObject(json) as Dictionary<string, object>;
            if (root == null) throw new UpdateException("릴리스 정보를 읽지 못했습니다.");
            string tag = Str(root, "tag_name");
            var info = new UpdateInfo
            {
                Tag = tag,
                Version = VersionFromTag(tag),
                Notes = Str(root, "body") ?? "",
                PageUrl = Str(root, "html_url") ?? ReleasesPage
            };
            if (info.Version == null) throw new UpdateException("릴리스 버전을 알 수 없습니다: " + tag);
            object assets;
            if (root.TryGetValue("assets", out assets) && assets is object[])
            {
                foreach (object o in (object[])assets)
                {
                    var a = o as Dictionary<string, object>;
                    if (a == null) continue;
                    string name = Str(a, "name"), url = Str(a, "browser_download_url");
                    if (name != null && url != null) AddAsset(info, name, url);
                }
            }
            return info;
        }

        static string Str(Dictionary<string, object> d, string key)
        {
            object v;
            return d.TryGetValue(key, out v) ? v as string : null;
        }

        static void AddAsset(UpdateInfo info, string name, string url)
        {
            string lower = name.ToLowerInvariant();
            if (lower.EndsWith(".exe") && lower.Contains("setup"))
            {
                info.SetupName = name;
                info.SetupUrl = url;
            }
            else if (lower.EndsWith(".zip") && lower.Contains("portable"))
            {
                info.PortableName = name;
                info.PortableUrl = url;
            }
            else if (lower.EndsWith(".txt") && lower.Contains("sha256"))
            {
                info.ChecksumUrl = url;
            }
        }

        /// <summary>github.com/.../releases/latest 가 넘겨주는 주소에서 태그를 읽고, 파일 주소는 정해진 이름으로 만든다.</summary>
        static UpdateInfo FromLatestRedirect()
        {
            try
            {
                var req = (HttpWebRequest)WebRequest.Create(ReleasesPage);
                req.Method = "HEAD";
                req.AllowAutoRedirect = false;
                req.UserAgent = UserAgent;
                req.Timeout = 15000;
                using (var resp = (HttpWebResponse)req.GetResponse())
                {
                    string location = resp.Headers["Location"];
                    if (string.IsNullOrEmpty(location)) return null;
                    string tag = location.Substring(location.LastIndexOf('/') + 1);
                    string v = VersionFromTag(tag);
                    if (v == null) return null;
                    string baseUrl = "https://github.com/" + Repo + "/releases/download/" + tag + "/";
                    var info = new UpdateInfo { Tag = tag, Version = v, Notes = "", PageUrl = "https://github.com/" + Repo + "/releases/tag/" + tag };
                    foreach (string name in new[] { "WinDeck-Setup-" + v + ".exe", "WinDeck-Portable-" + v + ".zip", "WinDeck-SHA256-" + v + ".txt" })
                        AddAsset(info, name, baseUrl + name);
                    return info;
                }
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>"v1.3.0" → "1.3.0". 버전 모양이 아니면 null.</summary>
        public static string VersionFromTag(string tag)
        {
            Version v = ParseVersion(tag);
            return v == null ? null : v.ToString(3);
        }

        static Version ParseVersion(string s)
        {
            if (string.IsNullOrWhiteSpace(s)) return null;
            Version v;
            if (!Version.TryParse(s.Trim().TrimStart('v', 'V'), out v)) return null;
            return new Version(v.Major, v.Minor, Math.Max(0, v.Build));
        }

        public static bool IsNewer(string latest, string current)
        {
            Version a = ParseVersion(latest), b = ParseVersion(current);
            return a != null && b != null && a > b;
        }

        /// <summary>사용자에게 보여줄 오류 설명.</summary>
        public static string Describe(Exception ex)
        {
            if (ex is WebException) return "인터넷 연결을 확인해 주세요. (" + ex.Message + ")";
            return ex.Message;
        }

        // ------------------------------------------------------------------ 받기·검사

        static WebRequest NewRequest(string url)
        {
            WebRequest req = WebRequest.Create(url);
            var http = req as HttpWebRequest;
            if (http != null)
            {
                http.UserAgent = UserAgent;
                http.Timeout = 20000;
                http.ReadWriteTimeout = 30000;
                if (http.RequestUri.Host == "api.github.com") http.Accept = "application/vnd.github+json";
                if (http.Proxy != null) http.Proxy.Credentials = CredentialCache.DefaultCredentials;
            }
            return req;
        }

        public static string GetString(string url)
        {
            using (WebResponse resp = NewRequest(url).GetResponse())
            using (var reader = new StreamReader(resp.GetResponseStream(), Encoding.UTF8))
                return reader.ReadToEnd();
        }

        public static void Download(string url, string dest, Action<long, long> progress, Func<bool> cancelled)
        {
            using (WebResponse resp = NewRequest(url).GetResponse())
            {
                long total = resp.ContentLength;
                if (total > MaxDownload) throw new UpdateException("받을 파일이 너무 큽니다.");
                using (Stream src = resp.GetResponseStream())
                using (FileStream dst = File.Create(dest))
                {
                    var buf = new byte[81920];
                    long done = 0;
                    int n;
                    while ((n = src.Read(buf, 0, buf.Length)) > 0)
                    {
                        if (cancelled != null && cancelled()) throw new OperationCanceledException();
                        dst.Write(buf, 0, n);
                        done += n;
                        if (done > MaxDownload) throw new UpdateException("받을 파일이 너무 큽니다.");
                        if (progress != null) progress(done, total);
                    }
                }
            }
        }

        static readonly Regex ChecksumLine = new Regex(@"^\s*([0-9a-fA-F]{64})\s+\*?(.+?)\s*$", RegexOptions.Multiline);

        /// <summary>sha256sum 형식("해시  파일이름") 목록을 읽는다.</summary>
        public static Dictionary<string, string> ParseChecksums(string text)
        {
            var d = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (Match m in ChecksumLine.Matches(text ?? "")) d[m.Groups[2].Value] = m.Groups[1].Value.ToLowerInvariant();
            return d;
        }

        public static string Sha256(string path)
        {
            using (var sha = SHA256.Create())
            using (FileStream fs = File.OpenRead(path))
            {
                var sb = new StringBuilder(64);
                foreach (byte b in sha.ComputeHash(fs)) sb.Append(b.ToString("x2"));
                return sb.ToString();
            }
        }

        /// <summary>exe 파일 버전이 릴리스 버전과 같은지 확인한다.</summary>
        public static void CheckFileVersion(string path, string version)
        {
            string fv = FileVersionInfo.GetVersionInfo(path).FileVersion;
            if (VersionFromTag(fv) != version)
                throw new UpdateException("받은 파일의 버전(" + (fv ?? "없음") + ")이 릴리스 버전(" + version + ")과 달라 설치하지 않습니다.");
        }

        static string Size(long bytes)
        {
            return bytes >= 1024 * 1024 ? (bytes / 1048576.0).ToString("0.0") + "MB" : (bytes / 1024) + "KB";
        }

        static string WorkDir
        {
            get { return System.IO.Path.Combine(System.IO.Path.GetTempPath(), "WinDeck-update"); }
        }

        /// <summary>
        /// 릴리스의 설치 파일(WinDeck-Setup-x.y.z.exe)을 받아 SHA-256·버전을 검사한다.
        /// 1.4 부터는 설치하지 않고 쓰던 WinDeck 도 이 설치 파일로 설치한다 (포터블 판 없음).
        /// report(상태 문구, 진행률 0~100 또는 -1).
        /// </summary>
        public static PreparedUpdate Prepare(UpdateInfo info, Action<string, int> report, Func<bool> cancelled)
        {
            string url = info.SetupUrl;
            string name = info.SetupName;
            if (url == null) throw new UpdateException("릴리스에서 설치 파일을 찾지 못했습니다.");
            if (info.ChecksumUrl == null) throw new UpdateException("릴리스에 검사용 SHA-256 목록이 없어 자동으로 설치할 수 없습니다.");

            string work = System.IO.Path.Combine(WorkDir, info.Version);
            if (Directory.Exists(work)) Directory.Delete(work, true);
            Directory.CreateDirectory(work);

            report("검사 정보 받는 중…", -1);
            string expected;
            if (!ParseChecksums(GetString(info.ChecksumUrl)).TryGetValue(name, out expected))
                throw new UpdateException("검사 정보에 " + name + " 항목이 없습니다.");

            string file = System.IO.Path.Combine(work, name);
            Download(url, file, (done, total) =>
                report("받는 중…  " + Size(done) + (total > 0 ? " / " + Size(total) : ""), total > 0 ? (int)(done * 100 / total) : -1), cancelled);

            report("받은 파일 검사 중…", 100);
            if (!string.Equals(Sha256(file), expected, StringComparison.OrdinalIgnoreCase))
                throw new UpdateException("받은 파일이 손상되었거나 릴리스와 다릅니다 (SHA-256 불일치). 설치하지 않습니다.");

            CheckFileVersion(file, info.Version);
            return new PreparedUpdate { Installer = true, Path = file, Version = info.Version };
        }

        // ------------------------------------------------------------------ 설치

        /// <summary>
        /// 준비한 설치 파일을 조용히 실행한다 (설치가 끝나면 설치한 WinDeck 이 다시 켜진다).
        /// 이 함수가 끝나면 지금 WinDeck 은 바로 종료해야 한다. 설치 파일은 실행 중인 WinDeck 이 끝나기를 기다린다.
        /// </summary>
        public static void Launch(PreparedUpdate p)
        {
            string args = "/SILENT /SUPPRESSMSGBOXES /NORESTART /RELAUNCH=1";
            if (AppInstance.IsTest) args += " /INSTANCE=" + AppInstance.TestId;
            Process.Start(new ProcessStartInfo(p.Path, args) { UseShellExecute = false });
        }

        /// <summary>업데이트 뒤 남은 WinDeck.exe.old 와 받은 파일을 지운다 (앞 프로세스가 끝날 때까지 몇 번 다시 시도).</summary>
        public static void CleanupAfterUpdate()
        {
            string old = Application.ExecutablePath + ".old";
            string work = WorkDir;
            if (!File.Exists(old) && !Directory.Exists(work)) return;
            var t = new Thread(() =>
            {
                for (int i = 0; i < 30 && File.Exists(old); i++)
                {
                    try { File.Delete(old); }
                    catch (Exception) { Thread.Sleep(500); }
                }
                Thread.Sleep(20000); // 설치 파일이 아직 끝나지 않았을 수 있다
                try { if (Directory.Exists(work)) Directory.Delete(work, true); }
                catch (Exception) { }
            });
            t.IsBackground = true;
            t.Start();
        }
    }
}
