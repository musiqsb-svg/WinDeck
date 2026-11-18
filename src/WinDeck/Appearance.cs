using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;
using System.Linq;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>
    /// 패널 배경(기본·단색·이미지) 그리기. 패널 크기의 비트맵을 한 번 만들어 두고
    /// 패널과 버튼이 그 일부를 잘라 쓰므로, 다시 그릴 때 이미지를 매번 줄이지 않는다.
    /// </summary>
    public static class PanelBackgroundRenderer
    {
        /// <summary>넣는 이미지의 긴 변 최대 픽셀 (200% 배율의 가장 큰 패널도 덮는 크기).</summary>
        public const int MaxImageSide = 1280;

        public static Color BaseColor(PanelBackground bg)
        {
            if (bg != null && bg.Mode != PanelBackground.ModeDefault)
            {
                Color c = Theme.ParseColor(bg.Color, Color.Empty);
                if (!c.IsEmpty) return c;
            }
            return Theme.PanelBack;
        }

        public static bool HasImage(PanelBackground bg)
        {
            return bg != null && bg.Mode == PanelBackground.ModeImage && !string.IsNullOrEmpty(bg.ImageData);
        }

        public static Bitmap Build(PanelBackground bg, Size size, int topBand)
        {
            var bmp = new Bitmap(Math.Max(1, size.Width), Math.Max(1, size.Height), PixelFormat.Format32bppPArgb);
            using (Graphics g = Graphics.FromImage(bmp)) Paint(g, new Rectangle(Point.Empty, bmp.Size), bg, topBand);
            return bmp;
        }

        public static void Paint(Graphics g, Rectangle r, PanelBackground bg, int topBand)
        {
            using (var b = new SolidBrush(BaseColor(bg))) g.FillRectangle(b, r);
            if (!HasImage(bg)) return;
            using (Image img = Decode(bg.ImageData))
            {
                if (img == null) return;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                var src = new RectangleF(0, 0, img.Width, img.Height);
                RectangleF dest = r;
                if (bg.Fit == PanelBackground.FitFill)
                {
                    float s = Math.Max((float)r.Width / img.Width, (float)r.Height / img.Height);
                    float sw = r.Width / s, sh = r.Height / s;
                    src = new RectangleF((img.Width - sw) / 2f, (img.Height - sh) / 2f, sw, sh);
                }
                else if (bg.Fit == PanelBackground.FitFit)
                {
                    float s = Math.Min((float)r.Width / img.Width, (float)r.Height / img.Height);
                    float w = img.Width * s, h = img.Height * s;
                    dest = new RectangleF(r.X + (r.Width - w) / 2f, r.Y + (r.Height - h) / 2f, w, h);
                }
                using (var attr = new ImageAttributes())
                {
                    attr.SetWrapMode(WrapMode.TileFlipXY); // 가장자리 번짐 방지
                    g.DrawImage(img, Rectangle.Round(dest), src.X, src.Y, src.Width, src.Height, GraphicsUnit.Pixel, attr);
                }
            }
            if (bg.Dim > 0)
                using (var dim = new SolidBrush(Color.FromArgb(bg.Dim * 255 / 100, 0, 0, 0))) g.FillRectangle(dim, r);
            if (topBand > 0)
                using (var lg = new LinearGradientBrush(new Rectangle(r.X, r.Y, r.Width, topBand + 1),
                    Color.FromArgb(110, 0, 0, 0), Color.FromArgb(0, 0, 0, 0), LinearGradientMode.Vertical))
                    g.FillRectangle(lg, r.X, r.Y, r.Width, topBand);
        }

        static Image Decode(string data)
        {
            try
            {
                using (var ms = new MemoryStream(Convert.FromBase64String(data)))
                using (Image tmp = Image.FromStream(ms))
                    return new Bitmap(tmp);
            }
            catch (Exception)
            {
                return null;
            }
        }

        /// <summary>배경 위 글자(페이지 이름 등)를 밝은 색/어두운 색 중 무엇으로 쓸지.</summary>
        public static bool IsLight(Bitmap bmp, Rectangle area)
        {
            area.Intersect(new Rectangle(Point.Empty, bmp.Size));
            if (area.Width <= 0 || area.Height <= 0) return false;
            double sum = 0;
            int n = 0;
            for (int yi = 0; yi < 4; yi++)
                for (int xi = 0; xi < 12; xi++)
                {
                    Color c = bmp.GetPixel(area.X + area.Width * (2 * xi + 1) / 24, area.Y + area.Height * (2 * yi + 1) / 8);
                    sum += Theme.Luminance(c);
                    n++;
                }
            return sum / n > 0.6;
        }

        /// <summary>
        /// 사용자가 고른 이미지를 긴 변 1280px 이하 JPEG(base64)로 줄인다.
        /// 4K 사진(약 550KB)이 60KB 안팎이 되어 설정 파일과 메모리를 가볍게 유지한다.
        /// </summary>
        public static string LoadImageData(string file)
        {
            using (FileStream fs = File.OpenRead(file))
            using (Image src = Image.FromStream(fs))
            {
                float s = Math.Min(1f, (float)MaxImageSide / Math.Max(src.Width, src.Height));
                int w = Math.Max(1, (int)Math.Round(src.Width * s));
                int h = Math.Max(1, (int)Math.Round(src.Height * s));
                using (var bmp = new Bitmap(w, h, PixelFormat.Format24bppRgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.Clear(Theme.PanelBack); // 투명 PNG 는 패널 색 위에 합친다
                        g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                        g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                        g.DrawImage(src, new Rectangle(0, 0, w, h));
                    }
                    ImageCodecInfo jpeg = ImageCodecInfo.GetImageEncoders().First(c => c.FormatID == ImageFormat.Jpeg.Guid);
                    using (var ps = new EncoderParameters(1))
                    using (var ms = new MemoryStream())
                    {
                        ps.Param[0] = new EncoderParameter(System.Drawing.Imaging.Encoder.Quality, 85L);
                        bmp.Save(ms, jpeg, ps);
                        return Convert.ToBase64String(ms.ToArray());
                    }
                }
            }
        }

        public static Size ImageSize(string data)
        {
            using (Image img = Decode(data)) return img == null ? Size.Empty : img.Size;
        }
    }

    /// <summary>색 고르기: 사용자 지정 색 16칸과 최근 색을 설정에 기억하고, #RRGGBB 코드를 받는다.</summary>
    public static class ColorChooser
    {
        public const int MaxRecent = 8;

        public static string Pick(IWin32Window owner, AppConfig cfg, string current, Color fallback)
        {
            using (var cd = new ColorDialog { FullOpen = true, AnyColor = true, Color = Theme.ParseColor(current, fallback) })
            {
                int[] custom = new int[16];
                for (int i = 0; i < 16; i++) custom[i] = i < cfg.CustomColors.Count ? cfg.CustomColors[i] : 0xFFFFFF;
                cd.CustomColors = custom;
                DialogResult r = cd.ShowDialog(owner);
                cfg.CustomColors = cd.CustomColors.ToList();
                if (r != DialogResult.OK) return null;
                string hex = Theme.ToHex(cd.Color);
                AddRecent(cfg, hex);
                return hex;
            }
        }

        public static void AddRecent(AppConfig cfg, string hex)
        {
            if (string.IsNullOrEmpty(hex)) return;
            hex = hex.ToUpperInvariant();
            cfg.RecentColors.RemoveAll(c => string.Equals(c, hex, StringComparison.OrdinalIgnoreCase));
            cfg.RecentColors.Insert(0, hex);
            if (cfg.RecentColors.Count > MaxRecent) cfg.RecentColors.RemoveRange(MaxRecent, cfg.RecentColors.Count - MaxRecent);
        }

        /// <summary>"#1a2b3c", "1A2B3C", "#abc" → "#1A2B3C" / "#AABBCC". 잘못된 값이면 null.</summary>
        public static string ParseHex(string text)
        {
            string t = (text ?? "").Trim().TrimStart('#');
            if (t.Length == 3) t = new string(new[] { t[0], t[0], t[1], t[1], t[2], t[2] });
            if (t.Length != 6) return null;
            foreach (char ch in t)
                if (!Uri.IsHexDigit(ch)) return null;
            return "#" + t.ToUpperInvariant();
        }
    }

    /// <summary>버튼 수(8·12·16) 변경 처리.</summary>
    public static class ButtonLayout
    {
        public class HiddenInfo
        {
            public int Buttons;
            public int Hotkeys;
            public List<string> Lines = new List<string>();
        }

        /// <summary>버튼 수를 newCount 로 줄였을 때 보이지 않게 되는 (비어 있지 않은) 버튼.</summary>
        public static HiddenInfo WouldHide(AppConfig cfg, int newCount)
        {
            var info = new HiddenInfo();
            foreach (DeckPage page in cfg.Pages)
            {
                var names = new List<string>();
                for (int i = newCount; i < page.Buttons.Count; i++)
                {
                    DeckButton b = page.Buttons[i];
                    if (b.IsEmpty) continue;
                    info.Buttons++;
                    if (!string.IsNullOrWhiteSpace(b.Hotkey)) info.Hotkeys++;
                    names.Add(string.IsNullOrWhiteSpace(b.Title) ? (i + 1) + "번" : b.Title);
                }
                if (names.Count > 0) info.Lines.Add("'" + page.Name + "' : " + string.Join(", ", names.ToArray()));
            }
            return info;
        }

        /// <summary>
        /// 보이는 칸 밖(newCount 이후)의 버튼을 같은 페이지의 빈 칸으로 당기고,
        /// 그래도 남으면 바로 뒤에 "(이어서)" 페이지를 만들어 옮긴다. 만든 페이지 수를 돌려준다.
        /// </summary>
        public static int Compact(AppConfig cfg, int newCount)
        {
            DeckPage current = cfg.Pages[Math.Max(0, Math.Min(cfg.Pages.Count - 1, cfg.CurrentPage))];
            int created = 0;
            for (int pi = 0; pi < cfg.Pages.Count; pi++)
            {
                DeckPage page = cfg.Pages[pi];
                var overflow = new List<DeckButton>();
                for (int i = newCount; i < page.Buttons.Count; i++)
                {
                    if (page.Buttons[i].IsEmpty) continue;
                    overflow.Add(page.Buttons[i]);
                    page.Buttons[i] = new DeckButton();
                }
                for (int i = 0; i < newCount && overflow.Count > 0; i++)
                {
                    if (!page.Buttons[i].IsEmpty) continue;
                    page.Buttons[i] = overflow[0];
                    overflow.RemoveAt(0);
                }
                int insertAt = pi + 1, n = 0;
                while (overflow.Count > 0)
                {
                    n++;
                    DeckPage extra = DeckPage.CreateEmpty(page.Name + " (이어서" + (n > 1 ? " " + n : "") + ")");
                    for (int i = 0; i < newCount && overflow.Count > 0; i++)
                    {
                        extra.Buttons[i] = overflow[0];
                        overflow.RemoveAt(0);
                    }
                    cfg.Pages.Insert(insertAt++, extra);
                    created++;
                }
                pi = insertAt - 1;
            }
            cfg.CurrentPage = Math.Max(0, cfg.Pages.IndexOf(current));
            return created;
        }
    }
}
