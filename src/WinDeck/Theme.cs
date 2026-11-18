using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Linq;

namespace WinDeck
{
    public static class Theme
    {
        public static readonly Color PanelBack = Color.FromArgb(28, 28, 30);
        public static readonly Color PanelBorder = Color.FromArgb(64, 64, 68);
        public static readonly Color Text = Color.FromArgb(236, 236, 240);
        public static readonly Color TextDim = Color.FromArgb(150, 150, 156);
        public static readonly Color KeyBack = Color.FromArgb(52, 52, 56);
        public static readonly Color KeyEmptyBorder = Color.FromArgb(80, 80, 84);
        public static readonly Color BarHover = Color.FromArgb(58, 58, 62);
        public static readonly Color Accent = Color.FromArgb(10, 132, 255);
        public static readonly Color Success = Color.FromArgb(48, 209, 88);
        public static readonly Color Error = Color.FromArgb(255, 69, 58);
        public static readonly Color TipBack = Color.FromArgb(40, 40, 44);

        public const string UiFontName = "Malgun Gothic";

        /// <summary>버튼 배경색 견본. 빈 문자열은 기본색.</summary>
        public static readonly string[] Swatches =
        {
            "", "#C42B1C", "#CA5010", "#B7860B", "#107C10", "#038387", "#0063B1", "#5C2D91", "#6B6966"
        };

        static string glyphFont;

        /// <summary>Windows 11 의 Segoe Fluent Icons, 없으면 Windows 10 의 Segoe MDL2 Assets.</summary>
        public static string GlyphFontName
        {
            get
            {
                if (glyphFont == null)
                {
                    glyphFont = "Segoe MDL2 Assets";
                    try
                    {
                        using (var fc = new InstalledFontCollection())
                        {
                            if (fc.Families.Any(f => f.Name == "Segoe Fluent Icons")) glyphFont = "Segoe Fluent Icons";
                        }
                    }
                    catch (Exception) { }
                }
                return glyphFont;
            }
        }

        public static Color ParseColor(string hex, Color fallback)
        {
            if (string.IsNullOrWhiteSpace(hex)) return fallback;
            try { return ColorTranslator.FromHtml(hex.Trim()); }
            catch (Exception) { return fallback; }
        }

        public static string ToHex(Color c)
        {
            return string.Format("#{0:X2}{1:X2}{2:X2}", c.R, c.G, c.B);
        }

        public static Color Blend(Color a, Color b, float t)
        {
            t = Math.Max(0f, Math.Min(1f, t));
            return Color.FromArgb(
                (int)(a.R + (b.R - a.R) * t),
                (int)(a.G + (b.G - a.G) * t),
                (int)(a.B + (b.B - a.B) * t));
        }

        public static Color Lighten(Color c, float t) { return Blend(c, Color.White, t); }

        public static Color Darken(Color c, float t) { return Blend(c, Color.Black, t); }

        public static double Luminance(Color c)
        {
            return (0.299 * c.R + 0.587 * c.G + 0.114 * c.B) / 255.0;
        }

        public static Color AutoText(Color background)
        {
            return Luminance(background) > 0.62 ? Color.FromArgb(28, 28, 30) : Color.White;
        }

        public static GraphicsPath RoundRect(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d <= 0.5f)
            {
                p.AddRectangle(r);
                return p;
            }
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }

        static readonly Dictionary<string, Font> fonts = new Dictionary<string, Font>();

        /// <summary>픽셀 단위 글꼴(캐시). 버튼 크기에 비례해 글자 크기를 맞출 때 쓴다.</summary>
        public static Font PixelFont(string family, float px, FontStyle style)
        {
            px = (float)Math.Round(px * 2) / 2f;
            string key = family + "|" + px + "|" + (int)style;
            Font f;
            if (!fonts.TryGetValue(key, out f))
            {
                f = new Font(family, px, style, GraphicsUnit.Pixel);
                fonts[key] = f;
            }
            return f;
        }
    }

    /// <summary>Segoe Fluent Icons / MDL2 Assets 글리프 코드.</summary>
    public static class Glyphs
    {
        public const string Folder = "";
        public const string Globe = "";
        public const string Edit = "";
        public const string Keyboard = "";
        public const string Play = "";
        public const string Forward = "";
        public const string Add = "";
        public const string Settings = "";
        public const string ChevronLeft = "";
        public const string ChevronRight = "";
        public const string Minimize = "";

        public static string ForAction(string action)
        {
            switch (action)
            {
                case ActionTypes.Folder: return Folder;
                case ActionTypes.Url: return Globe;
                case ActionTypes.Text: return Edit;
                case ActionTypes.Hotkey: return Keyboard;
                case ActionTypes.Run: return Play;
                case ActionTypes.Page: return Forward;
            }
            return null;
        }
    }
}
