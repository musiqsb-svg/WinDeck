using System;
using System.Drawing;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;
using System.Windows.Forms;
using Microsoft.Win32;

namespace WinDeck
{
    public static class AppInfo
    {
        public static string Version
        {
            get { return typeof(AppInfo).Assembly.GetName().Version.ToString(3); }
        }
    }

    /// <summary>윈도우 시작 시 자동 실행 (HKCU\...\Run).</summary>
    public static class AutoStart
    {
        const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";
        const string ApprovedKey = @"Software\Microsoft\Windows\CurrentVersion\Explorer\StartupApproved\Run";
        const string ValueName = "WinDeck";

        static string Command
        {
            get { return "\"" + Application.ExecutablePath + "\""; }
        }

        public static bool IsEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, false))
                {
                    if (k == null || k.GetValue(ValueName) == null) return false;
                }
                // 작업 관리자 '시작 앱'에서 꺼 둔 경우 (첫 바이트가 홀수면 사용 안 함)
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, false))
                {
                    byte[] data = a == null ? null : a.GetValue(ValueName) as byte[];
                    if (data != null && data.Length > 0 && (data[0] & 1) == 1) return false;
                }
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        public static void Set(bool enabled)
        {
            using (RegistryKey k = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (enabled) k.SetValue(ValueName, Command);
                else if (k.GetValue(ValueName) != null) k.DeleteValue(ValueName, false);
            }
            try
            {
                using (RegistryKey a = Registry.CurrentUser.OpenSubKey(ApprovedKey, true))
                {
                    if (a != null && a.GetValue(ValueName) != null) a.DeleteValue(ValueName, false);
                }
            }
            catch (Exception) { }
        }

        /// <summary>exe 를 옮긴 경우 자동 실행 경로를 현재 위치로 고친다.</summary>
        public static void RefreshPathIfEnabled()
        {
            try
            {
                using (RegistryKey k = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (k == null) return;
                    string v = k.GetValue(ValueName) as string;
                    if (v != null && !string.Equals(v, Command, StringComparison.OrdinalIgnoreCase))
                        k.SetValue(ValueName, Command);
                }
            }
            catch (Exception) { }
        }
    }

    /// <summary>설정 내보내기/불러오기 대화상자.</summary>
    public static class BackupUi
    {
        const string Filter = "WinDeck 설정 (*.json)|*.json|모든 파일 (*.*)|*.*";

        public static void Export(IWin32Window owner, AppConfig cfg)
        {
            using (var sfd = new SaveFileDialog())
            {
                sfd.Title = "설정 내보내기 (백업)";
                sfd.Filter = Filter;
                sfd.FileName = "WinDeck_백업_" + DateTime.Now.ToString("yyyyMMdd_HHmm") + ".json";
                sfd.InitialDirectory = Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments);
                if (sfd.ShowDialog(owner) != DialogResult.OK) return;
                try
                {
                    ConfigStore.Export(cfg, sfd.FileName);
                    MessageBox.Show(owner, "설정을 저장했습니다.\n" + sfd.FileName, "WinDeck", MessageBoxButtons.OK, MessageBoxIcon.Information);
                }
                catch (Exception ex)
                {
                    MessageBox.Show(owner, "저장하지 못했습니다.\n" + ex.Message, "WinDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                }
            }
        }

        /// <summary>
        /// 불러올 설정 파일을 고른다. 받은 파일은 대개 다운로드·바탕 화면에 있으므로 마지막으로 연 폴더, 없으면 다운로드에서 시작한다.
        /// </summary>
        public static string PickImportFile(IWin32Window owner, string lastFolder)
        {
            using (var ofd = new OpenFileDialog())
            {
                ofd.Title = "설정 불러오기";
                ofd.Filter = Filter;
                string start = !string.IsNullOrEmpty(lastFolder) && Directory.Exists(lastFolder) ? lastFolder : KnownFolders.Downloads;
                if (string.IsNullOrEmpty(start) || !Directory.Exists(start))
                    start = Environment.GetFolderPath(Environment.SpecialFolder.DesktopDirectory);
                ofd.InitialDirectory = start;
                return ofd.ShowDialog(owner) == DialogResult.OK ? ofd.FileName : null;
            }
        }

        /// <summary>설정 파일을 읽고 (confirm 이면) 바꿀지 묻는다. 못 읽거나 취소하면 null. 백업은 부르는 쪽에서 한다.</summary>
        public static AppConfig ReadImport(IWin32Window owner, string path, bool confirm)
        {
            AppConfig imported;
            try
            {
                imported = ConfigStore.Import(path);
            }
            catch (Exception ex)
            {
                MessageBox.Show(owner, "설정 파일을 읽지 못했습니다.\n" + ex.Message, "WinDeck", MessageBoxButtons.OK, MessageBoxIcon.Warning);
                return null;
            }
            if (!confirm) return imported;
            string msg = "현재 버튼 설정을 불러온 파일의 내용으로 바꿉니다.\n"
                + Path.GetFileName(path) + "  (" + ConfigStore.Describe(imported) + ")\n\n"
                + "지금 설정은 자동으로 백업해 둡니다. 불러온 내용은 WinDeck 에 저장되므로 받은 파일은 나중에 지워도 됩니다.\n계속할까요?";
            if (MessageBox.Show(owner, msg, "설정 불러오기", MessageBoxButtons.YesNo, MessageBoxIcon.Question) != DialogResult.Yes)
                return null;
            return imported;
        }
    }

    public static class ErrorLog
    {
        public static string LogPath
        {
            get { return Path.Combine(ConfigStore.Dir, "error.log"); }
        }

        public static void Write(Exception ex)
        {
            try
            {
                Directory.CreateDirectory(ConfigStore.Dir);
                File.AppendAllText(LogPath, DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss") + "  " + ex
                    + Environment.NewLine + Environment.NewLine, Encoding.UTF8);
            }
            catch (Exception) { }
        }

        public static void Report(Exception ex)
        {
            if (ex == null) return;
            Write(ex);
            try
            {
                MessageBox.Show("예기치 않은 오류가 발생했습니다.\n\n" + ex.Message + "\n\n자세한 내용: " + LogPath,
                    "WinDeck", MessageBoxButtons.OK, MessageBoxIcon.Error);
            }
            catch (Exception) { }
        }
    }

    /// <summary>한 줄 입력 대화상자 (페이지 이름 등).</summary>
    public class InputDialog : Form
    {
        readonly TextBox box;

        public string Value
        {
            get { return box.Text.Trim(); }
        }

        public InputDialog(string title, string prompt, string initial)
        {
            SuspendLayout();
            Text = title;
            Font = new Font(Theme.UiFontName, 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(360, 124);

            Controls.Add(new Label { Text = prompt, AutoSize = true, Location = new Point(16, 16) });
            box = new TextBox { Location = new Point(16, 40), Size = new Size(328, 23), Text = initial ?? "", MaxLength = 30 };
            Controls.Add(box);
            var ok = new Button { Text = "확인", Location = new Point(168, 80), Size = new Size(84, 30), DialogResult = DialogResult.OK };
            var cancel = new Button { Text = "취소", Location = new Point(260, 80), Size = new Size(84, 30), DialogResult = DialogResult.Cancel };
            Controls.Add(ok);
            Controls.Add(cancel);
            AcceptButton = ok;
            CancelButton = cancel;
            // DPI 배율은 모든 컨트롤을 추가한 뒤에 지정해야 크기가 두 번 커지지 않는다.
            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        protected override void OnShown(EventArgs e)
        {
            base.OnShown(e);
            box.SelectAll();
            box.Focus();
        }

        public static string Ask(IWin32Window owner, string title, string prompt, string initial)
        {
            using (var d = new InputDialog(title, prompt, initial))
            {
                if (d.ShowDialog(owner) != DialogResult.OK || d.Value.Length == 0) return null;
                return d.Value;
            }
        }
    }

    /// <summary>Windows 탐색기 스타일의 폴더 선택 창 (실패 시 기존 폴더 찾아보기 창).</summary>
    public static class FolderPicker
    {
        const uint FOS_PICKFOLDERS = 0x20;
        const uint FOS_FORCEFILESYSTEM = 0x40;
        const uint FOS_PATHMUSTEXIST = 0x800;
        const uint SIGDN_FILESYSPATH = 0x80058000;
        const int ERROR_CANCELLED_HR = unchecked((int)0x800704C7);

        public static string Pick(IWin32Window owner, string initial, string title)
        {
            try
            {
                return PickModern(owner, initial, title);
            }
            catch (Exception)
            {
                using (var fbd = new FolderBrowserDialog())
                {
                    fbd.Description = title;
                    if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial)) fbd.SelectedPath = initial;
                    return fbd.ShowDialog(owner) == DialogResult.OK ? fbd.SelectedPath : null;
                }
            }
        }

        static string PickModern(IWin32Window owner, string initial, string title)
        {
            var dlg = (IFileOpenDialog)new FileOpenDialogCom();
            try
            {
                uint opts;
                dlg.GetOptions(out opts);
                dlg.SetOptions(opts | FOS_PICKFOLDERS | FOS_FORCEFILESYSTEM | FOS_PATHMUSTEXIST);
                if (!string.IsNullOrEmpty(title)) dlg.SetTitle(title);
                if (!string.IsNullOrEmpty(initial) && Directory.Exists(initial))
                {
                    try
                    {
                        Guid iid = typeof(IShellItem).GUID;
                        IShellItem item;
                        SHCreateItemFromParsingName(initial, IntPtr.Zero, ref iid, out item);
                        if (item != null) dlg.SetFolder(item);
                    }
                    catch (Exception) { }
                }
                int hr = dlg.Show(owner == null ? IntPtr.Zero : owner.Handle);
                if (hr == ERROR_CANCELLED_HR) return null;
                if (hr != 0) Marshal.ThrowExceptionForHR(hr);
                IShellItem result;
                dlg.GetResult(out result);
                string path;
                result.GetDisplayName(SIGDN_FILESYSPATH, out path);
                return path;
            }
            finally
            {
                Marshal.ReleaseComObject(dlg);
            }
        }

        [DllImport("shell32.dll", CharSet = CharSet.Unicode, PreserveSig = false)]
        static extern void SHCreateItemFromParsingName(string pszPath, IntPtr pbc, ref Guid riid, out IShellItem ppv);

        [ComImport, Guid("DC1C5A9C-E88A-4dde-A5A1-60F82A20AEF7")]
        class FileOpenDialogCom { }

        [ComImport, Guid("42f85136-db7e-439c-85f1-e4075d135fc8"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IFileOpenDialog
        {
            [PreserveSig] int Show(IntPtr parent);
            void SetFileTypes(uint cFileTypes, IntPtr rgFilterSpec);
            void SetFileTypeIndex(uint iFileType);
            void GetFileTypeIndex(out uint piFileType);
            void Advise(IntPtr pfde, out uint pdwCookie);
            void Unadvise(uint dwCookie);
            void SetOptions(uint fos);
            void GetOptions(out uint pfos);
            void SetDefaultFolder(IShellItem psi);
            void SetFolder(IShellItem psi);
            void GetFolder(out IShellItem ppsi);
            void GetCurrentSelection(out IShellItem ppsi);
            void SetFileName([MarshalAs(UnmanagedType.LPWStr)] string pszName);
            void GetFileName([MarshalAs(UnmanagedType.LPWStr)] out string pszName);
            void SetTitle([MarshalAs(UnmanagedType.LPWStr)] string pszTitle);
            void SetOkButtonLabel([MarshalAs(UnmanagedType.LPWStr)] string pszText);
            void SetFileNameLabel([MarshalAs(UnmanagedType.LPWStr)] string pszLabel);
            void GetResult(out IShellItem ppsi);
            void AddPlace(IShellItem psi, int fdap);
            void SetDefaultExtension([MarshalAs(UnmanagedType.LPWStr)] string pszDefaultExtension);
            void Close(int hr);
            void SetClientGuid(ref Guid guid);
            void ClearClientData();
            void SetFilter(IntPtr pFilter);
            void GetResults(out IntPtr ppenum);
            void GetSelectedItems(out IntPtr ppsai);
        }

        [ComImport, Guid("43826D1E-E718-42EE-BC55-A1E261C37BFE"), InterfaceType(ComInterfaceType.InterfaceIsIUnknown)]
        interface IShellItem
        {
            void BindToHandler(IntPtr pbc, ref Guid bhid, ref Guid riid, out IntPtr ppv);
            void GetParent(out IShellItem ppsi);
            void GetDisplayName(uint sigdnName, [MarshalAs(UnmanagedType.LPWStr)] out string ppszName);
            void GetAttributes(uint sfgaoMask, out uint psfgaoAttribs);
            void Compare(IShellItem psi, uint hint, out int piOrder);
        }
    }
}
