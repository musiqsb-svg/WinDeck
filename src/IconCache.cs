using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using Microsoft.Win32;

namespace WinDeck
{
    /// <summary>버튼 이미지와 실행 파일 아이콘을 읽고 캐시한다.</summary>
    public static class IconCache
    {
        public const int StoredIconSize = 128;

        static readonly Dictionary<string, Image> dataCache = new Dictionary<string, Image>();
        static readonly Dictionary<string, Image> shellCache = new Dictionary<string, Image>(StringComparer.OrdinalIgnoreCase);

        public static Image ForButton(DeckButton b)
        {
            if (b == null) return null;
            if (!string.IsNullOrEmpty(b.IconData))
            {
                Image img = FromData(b.IconData);
                if (img != null) return img;
            }
            if (b.Action == ActionTypes.Run && !string.IsNullOrWhiteSpace(b.Target)) return ShellIcon(b.Target);
            return null;
        }

        public static Image FromData(string data)
        {
            if (data.StartsWith(IconLibrary.RefPrefix, StringComparison.Ordinal))
            {
                IconAsset asset = IconLibrary.Find(data.Substring(IconLibrary.RefPrefix.Length));
                return asset == null ? null : asset.Image;
            }
            Image img;
            if (dataCache.TryGetValue(data, out img)) return img;
            try
            {
                byte[] bytes = Convert.FromBase64String(data);
                using (var ms = new MemoryStream(bytes))
                using (Image tmp = Image.FromStream(ms))
                    img = new Bitmap(tmp);
            }
            catch (Exception)
            {
                img = null;
            }
            if (dataCache.Count > 300) dataCache.Clear();
            dataCache[data] = img;
            return img;
        }

        public static Image ShellIcon(string target)
        {
            string path = ResolvePath(target);
            if (path == null) return null;
            Image img;
            if (shellCache.TryGetValue(path, out img)) return img;
            img = LoadShellIcon(path);
            if (img != null) shellCache[path] = img;
            return img;
        }

        /// <summary>"notepad.exe" 같은 이름만 있는 경로도 실제 파일 위치를 찾아본다.</summary>
        public static string ResolvePath(string target)
        {
            string p = ActionRunner.ExpandPath(target);
            if (p.Length == 0) return null;
            try
            {
                if (Path.IsPathRooted(p)) return File.Exists(p) || Directory.Exists(p) ? p : null;
            }
            catch (ArgumentException)
            {
                return null;
            }

            var dirs = new List<string> { Environment.SystemDirectory, Environment.GetFolderPath(Environment.SpecialFolder.Windows) };
            dirs.AddRange((Environment.GetEnvironmentVariable("PATH") ?? "").Split(';'));
            foreach (string d in dirs)
            {
                if (string.IsNullOrWhiteSpace(d)) continue;
                try
                {
                    string c = Path.Combine(Environment.ExpandEnvironmentVariables(d.Trim()), p);
                    if (File.Exists(c)) return c;
                    if (!Path.HasExtension(p) && File.Exists(c + ".exe")) return c + ".exe";
                }
                catch (ArgumentException) { }
            }

            // chrome.exe 처럼 App Paths 에 등록된 프로그램
            try
            {
                string exe = Path.HasExtension(p) ? p : p + ".exe";
                foreach (RegistryKey hive in new[] { Registry.CurrentUser, Registry.LocalMachine })
                {
                    using (RegistryKey k = hive.OpenSubKey(@"Software\Microsoft\Windows\CurrentVersion\App Paths\" + exe))
                    {
                        if (k == null) continue;
                        string v = k.GetValue(null) as string;
                        if (string.IsNullOrEmpty(v)) continue;
                        v = Environment.ExpandEnvironmentVariables(v.Trim('"'));
                        if (File.Exists(v)) return v;
                    }
                }
            }
            catch (Exception) { }
            return null;
        }

        public static Image LoadShellIcon(string path)
        {
            try
            {
                var sfi = new Native.SHFILEINFO();
                uint size = (uint)Marshal.SizeOf(sfi);
                if (Native.SHGetFileInfo(path, 0, ref sfi, size, Native.SHGFI_SYSICONINDEX) != IntPtr.Zero)
                {
                    Guid iid = new Guid("46EB5926-582E-4017-9FDF-E8998DAA0950"); // IImageList
                    IntPtr himl;
                    if (Native.SHGetImageList(Native.SHIL_EXTRALARGE, ref iid, out himl) == 0 && himl != IntPtr.Zero)
                    {
                        IntPtr hIcon = Native.ImageList_GetIcon(himl, sfi.iIcon, Native.ILD_TRANSPARENT);
                        if (hIcon != IntPtr.Zero) return IconToBitmap(hIcon);
                    }
                }
                sfi = new Native.SHFILEINFO();
                Native.SHGetFileInfo(path, 0, ref sfi, size, Native.SHGFI_ICON | Native.SHGFI_LARGEICON);
                if (sfi.hIcon != IntPtr.Zero) return IconToBitmap(sfi.hIcon);
            }
            catch (Exception) { }
            return null;
        }

        static Bitmap IconToBitmap(IntPtr hIcon)
        {
            try
            {
                using (Icon ico = Icon.FromHandle(hIcon))
                    return ico.ToBitmap();
            }
            finally
            {
                Native.DestroyIcon(hIcon);
            }
        }

        /// <summary>사용자가 고른 이미지 파일을 128px PNG(base64)로 바꿔 설정에 넣을 수 있게 한다.</summary>
        public static string LoadIconData(string file)
        {
            Bitmap src = null;
            string ext = Path.GetExtension(file).ToLowerInvariant();
            if (ext == ".ico")
            {
                try
                {
                    using (var ico = new Icon(file, 256, 256)) src = ico.ToBitmap();
                }
                catch (Exception) { src = null; }
            }
            else if (ext == ".exe" || ext == ".dll" || ext == ".lnk")
            {
                Image im = LoadShellIcon(file);
                if (im != null) src = new Bitmap(im);
            }
            if (src == null)
            {
                using (FileStream fs = File.OpenRead(file))
                using (Image im = Image.FromStream(fs))
                    src = new Bitmap(im);
            }

            using (src)
            using (Bitmap scaled = Fit(src, StoredIconSize))
            using (var ms = new MemoryStream())
            {
                scaled.Save(ms, ImageFormat.Png);
                return Convert.ToBase64String(ms.ToArray());
            }
        }

        static Bitmap Fit(Image src, int max)
        {
            float s = Math.Min(1f, Math.Min((float)max / src.Width, (float)max / src.Height));
            int w = Math.Max(1, (int)Math.Round(src.Width * s));
            int h = Math.Max(1, (int)Math.Round(src.Height * s));
            var bmp = new Bitmap(w, h, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);
                g.DrawImage(src, new Rectangle(0, 0, w, h));
            }
            return bmp;
        }
    }
}
