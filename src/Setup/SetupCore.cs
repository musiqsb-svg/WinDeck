using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using Microsoft.Win32;

namespace WinDeck.Setup
{
    public class InstallOptions
    {
        public bool DesktopShortcut = true;
        public bool AutoStart = true;
        public bool Launch = true;
    }

    /// <summary>사용자에게 그대로 보여 줄 설치·제거 오류.</summary>
    public class SetupException : Exception
    {
        public SetupException(string message) : base(message) { }
    }

    /// <summary>
    /// WinDeck 설치·제거. 관리자 권한 없이 현재 사용자 영역만 쓴다.
    ///   설치 폴더 %LOCALAPPDATA%\Programs\WinDeck, 설정 %APPDATA%\WinDeck (InstallInfo 참고)
    /// 제거는 설치가 만든 것만 지운다: 표식 파일과 정해진 설치 경로가 모두 맞을 때만 폴더를 지우고,
    /// 바로가기·자동 실행 값은 설치 폴더를 가리킬 때만 지운다.
    /// </summary>
    public static class SetupCore
    {
        // ------------------------------------------------------------------ 기록

        public static string LogPath
        {
            get
            {
                string root = InstallInfo.TestRoot;
                return root != null ? Path.Combine(root, "setup.log") : Path.Combine(Path.GetTempPath(), "WinDeck-setup.log");
            }
        }

        public static void Log(string line)
        {
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(LogPath));
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + line + "\r\n", Encoding.UTF8);
            }
            catch (Exception) { }
        }

        // ------------------------------------------------------------------ 지금 상태

        public static string FileVersionOf(string exe)
        {
            try
            {
                string v = FileVersionInfo.GetVersionInfo(exe).FileVersion;
                Version parsed;
                return v != null && Version.TryParse(v, out parsed) ? new Version(parsed.Major, parsed.Minor, Math.Max(0, parsed.Build)).ToString(3) : v;
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>설치되어 있는 WinDeck 의 버전 (1.4 설치 또는 1.3 이하 Inno 설치). 없으면 null.</summary>
        public static string InstalledVersion()
        {
            if (File.Exists(InstallInfo.InstalledExe)) return FileVersionOf(InstallInfo.InstalledExe);
            string flat = Path.Combine(InstallInfo.InstallDir, InstallInfo.AppExe);
            if (HasInnoInstall() && File.Exists(flat)) return FileVersionOf(flat);
            return null;
        }

        /// <summary>1.3 이하 Inno Setup 으로 설치한 WinDeck 이 설치 폴더에 있는지.</summary>
        public static bool HasInnoInstall()
        {
            return File.Exists(Path.Combine(InstallInfo.InstallDir, "unins000.exe"));
        }

        /// <summary>지금 설치된 모습에서 읽은 선택 (다시 설치·업데이트 때 그대로 유지). 처음 설치면 기본값.</summary>
        public static InstallOptions CurrentOptions()
        {
            var o = new InstallOptions();
            bool installed = InstallInfo.IsOurInstallDir(InstallInfo.InstallDir) || HasInnoInstall();
            o.AutoStart = installed ? RunValue() != null : true;
            o.DesktopShortcut = installed ? File.Exists(InstallInfo.DesktopShortcut) : true;
            // 처음 설치인데 1.3 이하 포터블 판이 자동 실행을 쓰고 있었다면 그대로 이어 간다.
            if (!installed && RunValue() != null) o.AutoStart = true;
            return o;
        }

        static string RunValue()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(InstallInfo.RunKey, false))
                    return k == null ? null : k.GetValue(InstallInfo.RunValueName) as string;
            }
            catch (Exception)
            {
                return null;
            }
        }

        // ------------------------------------------------------------------ 실행 중인 WinDeck 끝내기

        /// <summary>
        /// 실행 중인 WinDeck 을 끝낸다. 먼저 종료 신호(설정을 저장하고 스스로 끝남, 1.3 이상)를 보내고,
        /// 응답이 없으면 설치 폴더 안의 WinDeck.exe 만 강제로 끝낸다. 그래도 남아 있으면 SetupException.
        /// </summary>
        public static void CloseRunning()
        {
            if (!AppInstance.IsRunning()) return;
            Log("closing running WinDeck");
            AppInstance.Signal(AppInstance.ExitEventName);
            if (AppInstance.WaitForExit(8000))
            {
                Thread.Sleep(300);
                return;
            }
            foreach (Process p in Process.GetProcessesByName("WinDeck"))
            {
                try
                {
                    if (InstallInfo.IsInside(p.MainModule.FileName, InstallInfo.InstallDir))
                    {
                        Log("killing " + p.MainModule.FileName);
                        p.Kill();
                        p.WaitForExit(3000);
                    }
                }
                catch (Exception) { }
            }
            if (AppInstance.WaitForExit(2000)) return;
            throw new SetupException("실행 중인 WinDeck 을 끝내지 못했습니다.\n트레이 아이콘을 우클릭해 [종료]를 누른 뒤 다시 실행하십시오.");
        }

        // ------------------------------------------------------------------ 설치

        /// <summary>
        /// sourceAppDir(꾸러미의 app 폴더) 안의 파일과 제거 프로그램을 설치 폴더에 복사하고,
        /// 표식 파일·바로가기·'설치된 앱' 등록·자동 실행을 설정한다.
        /// </summary>
        public static void Install(string sourceAppDir, string uninstallerSource, InstallOptions o, Action<string> status)
        {
            string sourceExe = Path.Combine(sourceAppDir, InstallInfo.AppExe);
            if (!File.Exists(sourceExe)) throw new SetupException("설치할 파일이 없습니다: " + sourceExe);
            if (!File.Exists(uninstallerSource)) throw new SetupException("제거 프로그램이 없습니다: " + uninstallerSource);
            string version = FileVersionOf(sourceExe) ?? "";
            string dir = InstallInfo.InstallDir;
            string appDir = InstallInfo.InstalledAppDir;
            Log("install " + version + " from " + sourceAppDir + " to " + dir);

            status("실행 중인 WinDeck 을 끝내는 중…");
            CloseRunning();

            status("파일 복사 중…");
            bool ours = InstallInfo.IsOurInstallDir(dir);
            Directory.CreateDirectory(dir);
            WriteMarker(dir, version); // 먼저 표식을 남겨, 도중에 멈춰도 제거 프로그램이 지울 수 있게 한다
            if (ours && Directory.Exists(appDir)) Retry(() => Directory.Delete(appDir, true)); // 지난 버전의 남은 파일 정리
            CopyTree(sourceAppDir, appDir);
            Retry(() => File.Copy(uninstallerSource, Path.Combine(dir, InstallInfo.UninstallerExe), true));
            RemoveInnoLeftovers();

            status("바로가기 만드는 중…");
            string exe = InstallInfo.InstalledExe;
            CreateShortcut(InstallInfo.StartMenuShortcut, exe, "WinDeck - 소프트웨어 스트림덱");
            if (o.DesktopShortcut) CreateShortcut(InstallInfo.DesktopShortcut, exe, "WinDeck - 소프트웨어 스트림덱");
            else RemoveOurShortcut(InstallInfo.DesktopShortcut);

            status("Windows 에 등록하는 중…");
            RegisterUninstall(version);
            SetAutoStart(o.AutoStart ? "\"" + exe + "\"" : null);
            Log("install done");
        }

        static void WriteMarker(string dir, string version)
        {
            string marker = Path.Combine(dir, InstallInfo.MarkerFile);
            if (File.Exists(marker)) File.SetAttributes(marker, FileAttributes.Normal);
            File.WriteAllText(marker,
                InstallInfo.MarkerSignature + "\r\n"
                + "path=" + Path.GetFullPath(dir) + "\r\n"
                + "version=" + version + "\r\n"
                + "installed=" + DateTime.Now.ToString("s") + "\r\n"
                + "이 파일은 WinDeck 설치 표식입니다. 지우면 'WinDeck 제거'가 이 폴더를 지우지 않습니다.\r\n",
                new UTF8Encoding(false));
            File.SetAttributes(marker, FileAttributes.Hidden);
        }

        static void CopyTree(string from, string to)
        {
            Directory.CreateDirectory(to);
            foreach (string d in Directory.GetDirectories(from, "*", SearchOption.AllDirectories))
                Directory.CreateDirectory(Path.Combine(to, d.Substring(from.Length).TrimStart('\\')));
            foreach (string f in Directory.GetFiles(from, "*", SearchOption.AllDirectories))
            {
                string dest = Path.Combine(to, f.Substring(from.Length).TrimStart('\\'));
                Retry(() => File.Copy(f, dest, true));
            }
        }

        /// <summary>백신·탐색기가 파일을 잠깐 잡고 있을 때를 위해 몇 번 다시 시도한다.</summary>
        static void Retry(Action a)
        {
            for (int attempt = 1; ; attempt++)
            {
                try
                {
                    a();
                    return;
                }
                catch (IOException)
                {
                    if (attempt >= 8) throw;
                }
                catch (UnauthorizedAccessException)
                {
                    if (attempt >= 8) throw;
                }
                Thread.Sleep(250);
            }
        }

        /// <summary>1.3 이하 Inno Setup 설치가 남긴 WinDeck 파일·바로가기·등록 정보를 지운다 (이 설치가 대신한다).</summary>
        static void RemoveInnoLeftovers()
        {
            string dir = InstallInfo.InstallDir;
            bool had = false;
            foreach (string name in new[] { "unins000.exe", "unins000.dat", "unins000.msg", InstallInfo.AppExe, InstallInfo.AppExe + ".old",
                "WinDeck 사용 안내.txt", "LICENSE.txt", "WinDeck.defaults.json" })
            {
                string f = Path.Combine(dir, name);
                if (!File.Exists(f)) continue;
                had = true;
                try { File.SetAttributes(f, FileAttributes.Normal); File.Delete(f); }
                catch (Exception ex) { Log("could not delete " + f + ": " + ex.Message); }
            }
            string group = Path.Combine(InstallInfo.StartMenuPrograms, InstallInfo.AppName);
            foreach (string lnk in new[] { "WinDeck.lnk", "WinDeck 사용 안내.lnk" })
                RemoveOurShortcut(Path.Combine(group, lnk), true);
            try
            {
                if (Directory.Exists(group) && !Directory.EnumerateFileSystemEntries(group).Any()) Directory.Delete(group);
            }
            catch (Exception) { }
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(InstallInfo.UninstallParentKey, true))
                    if (k != null && k.OpenSubKey(InstallInfo.InnoUninstallKey) != null)
                    {
                        k.DeleteSubKeyTree(InstallInfo.InnoUninstallKey, false);
                        had = true;
                    }
            }
            catch (Exception ex) { Log("could not delete Inno key: " + ex.Message); }
            if (had) Log("removed 1.3 (Inno Setup) install leftovers");
        }

        static void RegisterUninstall(string version)
        {
            string dir = InstallInfo.InstallDir;
            long size = 0;
            try { size = new DirectoryInfo(dir).GetFiles("*", SearchOption.AllDirectories).Sum(f => f.Length); }
            catch (Exception) { }
            string uninstaller = "\"" + Path.Combine(dir, InstallInfo.UninstallerExe) + "\"";
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(InstallInfo.UninstallKey))
            {
                k.SetValue("DisplayName", InstallInfo.AppName);
                k.SetValue("DisplayVersion", version);
                k.SetValue("Publisher", InstallInfo.Publisher);
                k.SetValue("DisplayIcon", InstallInfo.InstalledExe + ",0");
                k.SetValue("InstallLocation", dir);
                k.SetValue("UninstallString", uninstaller);
                k.SetValue("QuietUninstallString", uninstaller + " /SILENT");
                k.SetValue("URLInfoAbout", InstallInfo.Url);
                k.SetValue("HelpLink", InstallInfo.Url);
                k.SetValue("InstallDate", DateTime.Now.ToString("yyyyMMdd"));
                k.SetValue("EstimatedSize", (int)Math.Min(int.MaxValue, size / 1024), RegistryValueKind.DWord);
                k.SetValue("NoModify", 1, RegistryValueKind.DWord);
                k.SetValue("NoRepair", 1, RegistryValueKind.DWord);
            }
        }

        /// <summary>command 가 null 이면 WinDeck 자동 실행 값을 지운다. 다른 앱의 값은 건드리지 않는다.</summary>
        static void SetAutoStart(string command)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(InstallInfo.RunKey))
            {
                if (command != null) k.SetValue(InstallInfo.RunValueName, command);
                else if (k.GetValue(InstallInfo.RunValueName) != null) k.DeleteValue(InstallInfo.RunValueName, false);
            }
        }

        /// <summary>설치한 WinDeck 을 켠다 (설치 프로그램이 끝나도 계속 실행).</summary>
        public static void LaunchInstalled()
        {
            string exe = InstallInfo.InstalledExe;
            Log("launch " + exe);
            Process.Start(new ProcessStartInfo(exe) { UseShellExecute = false, WorkingDirectory = InstallInfo.InstalledAppDir });
        }

        // ------------------------------------------------------------------ 제거

        /// <summary>
        /// 설치한 WinDeck 을 지운다. 표식 파일과 정해진 설치 경로가 모두 맞지 않으면 아무것도 지우지 않고 SetupException.
        /// deleteSettings 가 true 면 버튼 설정(%APPDATA%\WinDeck)과 WinDeck 레지스트리 키도 지운다.
        /// </summary>
        public static void Uninstall(bool deleteSettings, Action<string> status)
        {
            string dir = InstallInfo.InstallDir;
            if (!InstallInfo.IsOurInstallDir(dir))
            {
                if (HasInnoInstall())
                    throw new SetupException("1.3 이하 설치 프로그램으로 설치한 WinDeck 입니다.\n"
                        + "Windows 설정 > 앱 > 설치된 앱 에서 WinDeck 을 제거하십시오.");
                if (Directory.Exists(dir))
                    throw new SetupException("설치 표식(" + InstallInfo.MarkerFile + ")이 없거나 맞지 않아 아무것도 지우지 않았습니다.\n" + dir);
                throw new SetupException("설치된 WinDeck 이 없습니다.\n" + dir);
            }
            Log("uninstall from " + dir + (deleteSettings ? " (with settings)" : ""));

            status("실행 중인 WinDeck 을 끝내는 중…");
            CloseRunning();

            status("바로가기와 등록 정보 지우는 중…");
            RemoveOurShortcut(InstallInfo.StartMenuShortcut);
            RemoveOurShortcut(InstallInfo.DesktopShortcut);
            string run = RunValue();
            if (run != null && InstallInfo.IsInside(run.Trim('"'), dir)) SetAutoStart(null);
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(InstallInfo.UninstallParentKey, true))
                    if (k != null && k.OpenSubKey(InstallInfo.AppName) != null) k.DeleteSubKeyTree(InstallInfo.AppName, false);
            }
            catch (Exception ex) { Log("could not delete uninstall key: " + ex.Message); }

            status("파일 지우는 중…");
            // 표식과 경로를 지우기 직전에 한 번 더 확인한다.
            if (!InstallInfo.IsOurInstallDir(dir)) throw new SetupException("설치 표식이 바뀌어 폴더를 지우지 않았습니다.\n" + dir);
            Retry(() =>
            {
                foreach (string f in Directory.GetFiles(dir, "*", SearchOption.AllDirectories))
                    File.SetAttributes(f, FileAttributes.Normal);
                Directory.Delete(dir, true);
            });

            if (deleteSettings)
            {
                status("버튼 설정 지우는 중…");
                string settings = InstallInfo.SettingsDir;
                if (Directory.Exists(settings)) Retry(() => Directory.Delete(settings, true));
                try { Registry.CurrentUser.DeleteSubKeyTree(AppInstance.RegistryKey, false); }
                catch (Exception) { }
            }
            Log("uninstall done");
        }

        // ------------------------------------------------------------------ 바로가기 (.lnk)

        static object Shell()
        {
            Type t = Type.GetTypeFromProgID("WScript.Shell");
            if (t == null) throw new SetupException("Windows Script Host 를 찾지 못해 바로가기를 만들 수 없습니다.");
            return Activator.CreateInstance(t);
        }

        public static void CreateShortcut(string lnk, string target, string description)
        {
            Directory.CreateDirectory(Path.GetDirectoryName(lnk));
            object shell = Shell();
            try
            {
                object sc = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                try
                {
                    Type st = sc.GetType();
                    st.InvokeMember("TargetPath", BindingFlags.SetProperty, null, sc, new object[] { target });
                    st.InvokeMember("WorkingDirectory", BindingFlags.SetProperty, null, sc, new object[] { Path.GetDirectoryName(target) });
                    st.InvokeMember("Description", BindingFlags.SetProperty, null, sc, new object[] { description });
                    st.InvokeMember("IconLocation", BindingFlags.SetProperty, null, sc, new object[] { target + ",0" });
                    st.InvokeMember("Save", BindingFlags.InvokeMethod, null, sc, null);
                }
                finally
                {
                    Marshal.FinalReleaseComObject(sc);
                }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        public static string ShortcutTarget(string lnk)
        {
            object shell = Shell();
            try
            {
                object sc = shell.GetType().InvokeMember("CreateShortcut", BindingFlags.InvokeMethod, null, shell, new object[] { lnk });
                try { return sc.GetType().InvokeMember("TargetPath", BindingFlags.GetProperty, null, sc, null) as string; }
                finally { Marshal.FinalReleaseComObject(sc); }
            }
            finally
            {
                Marshal.FinalReleaseComObject(shell);
            }
        }

        static void RemoveOurShortcut(string lnk)
        {
            RemoveOurShortcut(lnk, false);
        }

        /// <summary>바로가기가 설치 폴더 안을 가리킬 때만 지운다 (사용자가 직접 만든 다른 바로가기는 그대로).</summary>
        static void RemoveOurShortcut(string lnk, bool quiet)
        {
            try
            {
                if (!File.Exists(lnk)) return;
                string target = ShortcutTarget(lnk);
                if (!InstallInfo.IsInside(target, InstallInfo.InstallDir))
                {
                    if (!quiet) Log("kept shortcut " + lnk + " (points to " + target + ")");
                    return;
                }
                File.Delete(lnk);
            }
            catch (Exception ex)
            {
                Log("could not remove shortcut " + lnk + ": " + ex.Message);
            }
        }

        // ------------------------------------------------------------------ 명령줄

        /// <summary>"/RELAUNCH=1" 같은 인자 값. 없으면 null, 값 없이 "/SILENT" 만 있으면 "".</summary>
        public static string Arg(string[] args, string name)
        {
            foreach (string a in args)
            {
                string s = a.Trim();
                if (!s.StartsWith("/") && !s.StartsWith("-")) continue;
                s = s.TrimStart('/', '-');
                int eq = s.IndexOf('=');
                string key = eq >= 0 ? s.Substring(0, eq) : s;
                if (string.Equals(key, name, StringComparison.OrdinalIgnoreCase)) return eq >= 0 ? s.Substring(eq + 1).Trim('"') : "";
            }
            return null;
        }

        /// <summary>/INSTANCE=이름: 점검용 실행 (WinDeck 1.3 의 자동 업데이트도 점검 때 이 인자를 넘긴다).</summary>
        public static void ApplyInstanceArg(string[] args)
        {
            string id = Arg(args, "INSTANCE");
            if (!string.IsNullOrEmpty(id)) Environment.SetEnvironmentVariable("WINDECK_INSTANCE", id);
        }
    }
}
