using System;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;
using System.Threading;

namespace WinDeck
{
    /// <summary>버튼에 지정된 동작을 실행한다.</summary>
    public static class ActionRunner
    {
        static IntPtr lastExternalWindow;
        static readonly uint ownPid = (uint)Process.GetCurrentProcess().Id;

        /// <summary>
        /// 마지막으로 사용한 다른 프로그램 창을 기억한다.
        /// 편집창을 닫은 직후처럼 WinDeck 이 앞에 있을 때 문장을 그 창으로 돌려보내기 위함.
        /// </summary>
        public static void TrackForeground()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && !IsOwnWindow(fg)) lastExternalWindow = fg;
        }

        public static bool IsOwnWindow(IntPtr hwnd)
        {
            uint pid;
            Native.GetWindowThreadProcessId(hwnd, out pid);
            return pid == ownPid;
        }

        public static void Run(DeckButton b, DeckForm deck)
        {
            switch (b.Action)
            {
                case ActionTypes.Folder:
                    OpenFolder(b.Target);
                    break;
                case ActionTypes.Url:
                    OpenUrl(b.Target);
                    break;
                case ActionTypes.Run:
                    RunFile(b.Target, b.Args);
                    break;
                case ActionTypes.Text:
                    if (string.IsNullOrEmpty(b.Target)) throw new ActionException("입력할 문장이 비어 있습니다.");
                    PrepareInputTarget();
                    if (b.UsePaste) InputSender.PasteText(b.Target, b.PressEnter);
                    else InputSender.TypeText(b.Target, b.PressEnter);
                    break;
                case ActionTypes.Hotkey:
                    var sequence = ParseKeys(b.Target);
                    PrepareInputTarget();
                    InputSender.SendSequence(sequence);
                    break;
                case ActionTypes.Page:
                    if (deck != null && !deck.GoToPage(b.Target))
                        throw new ActionException("이동할 페이지를 찾을 수 없습니다.\n버튼을 우클릭해서 다시 설정하세요.");
                    break;
            }
        }

        static System.Collections.Generic.List<KeyCombo> ParseKeys(string text)
        {
            try
            {
                return KeyCombo.ParseSequence(text);
            }
            catch (FormatException ex)
            {
                throw new ActionException("단축키 형식이 올바르지 않습니다.\n" + ex.Message);
            }
        }

        static void PrepareInputTarget()
        {
            IntPtr fg = Native.GetForegroundWindow();
            if (fg != IntPtr.Zero && !IsOwnWindow(fg)) return;
            if (lastExternalWindow == IntPtr.Zero || !Native.IsWindow(lastExternalWindow)) return;
            if (Native.IsIconic(lastExternalWindow)) Native.ShowWindow(lastExternalWindow, Native.SW_RESTORE);
            Native.SetForegroundWindow(lastExternalWindow);
            Thread.Sleep(150);
        }

        public static string ExpandPath(string target)
        {
            return RemapUserPath(Environment.ExpandEnvironmentVariables((target ?? "").Trim().Trim('"')));
        }

        // 사용자 폴더 안의 알려진 폴더 (OneDrive 로 옮겨진 경우, 한글 이름 포함)
        static readonly Regex DesktopPart = new Regex(@"^(?:OneDrive(?: - [^\\]+)?\\)?(?:Desktop|바탕 화면)(?=\\|$)", RegexOptions.IgnoreCase);
        static readonly Regex DocumentsPart = new Regex(@"^(?:OneDrive(?: - [^\\]+)?\\)?(?:Documents|문서)(?=\\|$)", RegexOptions.IgnoreCase);
        static readonly Regex PicturesPart = new Regex(@"^(?:OneDrive(?: - [^\\]+)?\\)?(?:Pictures|사진)(?=\\|$)", RegexOptions.IgnoreCase);
        static readonly Regex DownloadsPart = new Regex(@"^(?:Downloads|다운로드)(?=\\|$)", RegexOptions.IgnoreCase);
        static readonly Regex ProfilePrefix = new Regex(@"^[A-Za-z]:\\Users\\[^\\]+(?:\\|$)", RegexOptions.IgnoreCase);

        /// <summary>
        /// 다른 PC·다른 사용자에게서 받은 설정의 경로(C:\Users\다른이름\바탕 화면\...)를 이 PC의 같은 폴더로 바꿔 본다.
        /// 원래 경로가 있으면 그대로, 바꾼 경로도 없으면 원래 경로를 돌려준다.
        /// </summary>
        public static string RemapUserPath(string path)
        {
            try
            {
                if (path.Length < 4 || !Path.IsPathRooted(path) || File.Exists(path) || Directory.Exists(path)) return path;
                Match m = ProfilePrefix.Match(path);
                if (!m.Success) return path;
                string rest = path.Substring(m.Length);
                var known = new[]
                {
                    Tuple.Create(DesktopPart, Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory)),
                    Tuple.Create(DownloadsPart, KnownFolders.Downloads),
                    Tuple.Create(DocumentsPart, Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments)),
                    Tuple.Create(PicturesPart, Environment.GetFolderPath(Environment.SpecialFolder.MyPictures))
                };
                foreach (var k in known)
                {
                    Match km = k.Item1.Match(rest);
                    if (!km.Success || string.IsNullOrEmpty(k.Item2)) continue;
                    string candidate = Path.Combine(k.Item2, rest.Substring(km.Length).TrimStart('\\'));
                    if (File.Exists(candidate) || Directory.Exists(candidate)) return candidate;
                }
                string other = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), rest);
                if (rest.Length > 0 && (File.Exists(other) || Directory.Exists(other))) return other;
            }
            catch (Exception) { }
            return path;
        }

        static void OpenFolder(string target)
        {
            string path = ExpandPath(target);
            if (path.Length == 0) throw new ActionException("폴더 경로가 비어 있습니다.");
            if (path.StartsWith("shell:", StringComparison.OrdinalIgnoreCase))
            {
                Process.Start("explorer.exe", path);
                return;
            }
            if (File.Exists(path))
            {
                // 파일 경로면 그 파일이 있는 폴더를 열고 파일을 선택해 준다.
                Process.Start("explorer.exe", "/select,\"" + path + "\"");
                return;
            }
            if (!Directory.Exists(path)) throw new ActionException("폴더를 찾을 수 없습니다.\n" + path);
            if (path.Length > 3) path = path.TrimEnd('\\', '/');
            Process.Start("explorer.exe", path.Length <= 3 ? path : "\"" + path + "\"");
        }

        static readonly Regex SchemePattern = new Regex(@"^[a-zA-Z][a-zA-Z0-9+.\-]*:(?!\d)", RegexOptions.Compiled);

        public static string NormalizeUrl(string target)
        {
            string url = (target ?? "").Trim();
            if (url.Length == 0) return url;
            if (!SchemePattern.IsMatch(url)) url = "https://" + url;
            return url;
        }

        static void OpenUrl(string target)
        {
            string url = NormalizeUrl(target);
            if (url.Length == 0) throw new ActionException("링크 주소가 비어 있습니다.");
            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Win32Exception ex)
            {
                throw new ActionException("링크를 열지 못했습니다.\n" + url + "\n" + ex.Message);
            }
        }

        static void RunFile(string target, string args)
        {
            string path = ExpandPath(target);
            if (path.Length == 0) throw new ActionException("실행할 파일 경로가 비어 있습니다.");
            var psi = new ProcessStartInfo(path) { UseShellExecute = true };
            if (!string.IsNullOrWhiteSpace(args)) psi.Arguments = args.Trim();
            if (Path.IsPathRooted(path))
            {
                if (!File.Exists(path) && !Directory.Exists(path))
                    throw new ActionException("파일을 찾을 수 없습니다.\n" + path);
                string dir = Path.GetDirectoryName(path);
                if (!string.IsNullOrEmpty(dir)) psi.WorkingDirectory = dir;
            }
            try
            {
                Process.Start(psi);
            }
            catch (Win32Exception ex)
            {
                if (ex.NativeErrorCode == 1223) return; // 사용자가 관리자 권한 요청을 취소함
                throw new ActionException("실행하지 못했습니다.\n" + path + "\n" + ex.Message);
            }
        }
    }
}
