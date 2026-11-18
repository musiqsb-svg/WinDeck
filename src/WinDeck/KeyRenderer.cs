using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Text;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>패널 버튼과 편집창 미리보기가 함께 쓰는 버튼 그리기.</summary>
    public static class KeyRenderer
    {

        public static void Draw(Graphics g, Rectangle bounds, DeckButton b, bool hover, bool pressed, Color flash, Color parentBack)
        {
            g.SmoothingMode = SmoothingMode.AntiAlias;
            g.InterpolationMode = InterpolationMode.HighQualityBicubic;
            g.PixelOffsetMode = PixelOffsetMode.HighQuality;
            if (!parentBack.IsEmpty) // 비어 있으면 호출한 쪽(패널 배경)이 이미 그렸다
                using (var pb = new SolidBrush(parentBack)) g.FillRectangle(pb, bounds);

            var r = new RectangleF(bounds.X + 0.5f, bounds.Y + 0.5f, bounds.Width - 1f, bounds.Height - 1f);
            if (pressed) r.Inflate(-2f, -2f);
            float size = Math.Min(r.Width, r.Height);
            if (size < 8) return;
            float radius = Math.Max(5f, size * 0.14f);

            if (b == null || b.IsEmpty)
            {
                DrawEmpty(g, r, size, radius, hover, flash, parentBack);
                return;
            }

            Color bg = Theme.ParseColor(b.BackColor, Theme.KeyBack);
            Color fg = Theme.ParseColor(b.ForeColor, Theme.AutoText(bg));
            if (pressed) bg = Theme.Darken(bg, 0.14f);
            else if (hover) bg = Theme.Lighten(bg, 0.10f);

            using (GraphicsPath path = Theme.RoundRect(r, radius))
            {
                RectangleF gr = RectangleF.Inflate(r, 1, 1);
                using (var br = new LinearGradientBrush(gr, Theme.Lighten(bg, 0.07f), Theme.Darken(bg, 0.07f), LinearGradientMode.Vertical))
                    g.FillPath(br, path);
                using (var pen = new Pen(Color.FromArgb(hover ? 80 : 36, 255, 255, 255), 1f))
                    g.DrawPath(pen, path);
                DrawFlash(g, path, flash, size);
            }

            string title = (b.Title ?? "").Trim();
            bool hasTitle = title.Length > 0;
            Image icon = IconCache.ForButton(b);
            string glyph = icon == null ? Glyphs.ForAction(b.Action) : null;

            if (icon == null && glyph == null)
            {
                if (!hasTitle) return;
                Rectangle tr = Rectangle.Round(RectangleF.Inflate(r, -size * 0.08f, -size * 0.08f));
                Font f = Theme.PixelFont(Theme.UiFontName, Math.Max(12f, size * 0.16f), FontStyle.Bold);
                DrawTitle(g, title, f, tr, fg, 3, true);
                return;
            }

            float iconSize = size * (hasTitle ? 0.40f : 0.52f);
            float iconTop = hasTitle ? r.Top + size * 0.14f : r.Top + (r.Height - iconSize) / 2f;
            Rectangle ir = Rectangle.Round(new RectangleF(r.Left + (r.Width - iconSize) / 2f, iconTop, iconSize, iconSize));
            if (icon != null) DrawImageFit(g, icon, ir);
            else DrawGlyph(g, glyph, ir, fg);

            if (hasTitle)
            {
                float fontPx = Math.Max(11f, size * 0.135f);
                var tr = Rectangle.Round(new RectangleF(
                    r.Left + size * 0.05f, ir.Bottom + size * 0.05f,
                    r.Width - size * 0.10f, r.Bottom - ir.Bottom - size * 0.07f));
                DrawTitle(g, title, Theme.PixelFont(Theme.UiFontName, fontPx, FontStyle.Bold), tr, fg, 2, false);
            }
        }

        const TextFormatFlags LineFlags = TextFormatFlags.SingleLine | TextFormatFlags.NoPrefix | TextFormatFlags.NoPadding;

        /// <summary>
        /// 가운데 정렬 제목. GDI 기본 줄바꿈은 한글 단어 중간("보/기")에서 끊기 때문에
        /// 띄어쓰기 기준으로 직접 줄을 나누고, 한 단어가 너무 길 때만 글자 단위로 자른다.
        /// </summary>
        static void DrawTitle(Graphics g, string text, Font font, Rectangle rect, Color color, int maxLines, bool verticalCenter)
        {
            if (rect.Width <= 0 || rect.Height <= 0) return;
            int lineHeight = TextRenderer.MeasureText(g, "가Ag", font, Size.Empty, LineFlags).Height;
            maxLines = Math.Max(1, Math.Min(maxLines, rect.Height / Math.Max(1, lineHeight)));
            List<string> lines = WrapLines(g, text, font, rect.Width, maxLines);
            int total = lines.Count * lineHeight;
            int y = verticalCenter ? rect.Y + (rect.Height - total) / 2 : rect.Y;
            foreach (string line in lines)
            {
                TextRenderer.DrawText(g, line, font, new Rectangle(rect.X, y, rect.Width, lineHeight), color,
                    LineFlags | TextFormatFlags.HorizontalCenter | TextFormatFlags.Top);
                y += lineHeight;
            }
        }

        static int Measure(Graphics g, string s, Font font)
        {
            return TextRenderer.MeasureText(g, s, font, Size.Empty, LineFlags).Width;
        }

        static List<string> WrapLines(Graphics g, string text, Font font, int width, int maxLines)
        {
            var words = new List<string>(text.Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries));
            var lines = new List<string>();
            string current = "";
            int i = 0;
            while (i < words.Count && lines.Count < maxLines)
            {
                string candidate = current.Length == 0 ? words[i] : current + " " + words[i];
                if (Measure(g, candidate, font) <= width)
                {
                    current = candidate;
                    i++;
                    continue;
                }
                if (current.Length > 0)
                {
                    lines.Add(current);
                    current = "";
                    continue;
                }
                // 한 단어가 한 줄보다 길면 들어가는 만큼만 자른다.
                string w = words[i];
                int n = 1;
                while (n < w.Length && Measure(g, w.Substring(0, n + 1), font) <= width) n++;
                lines.Add(w.Substring(0, n));
                words[i] = w.Substring(n);
            }
            if (current.Length > 0 && lines.Count < maxLines)
            {
                lines.Add(current);
                current = "";
            }

            bool overflow = i < words.Count || current.Length > 0;
            if (overflow && lines.Count > 0)
            {
                string rest = lines[lines.Count - 1] + " " + string.Join(" ", words.GetRange(i, words.Count - i).ToArray());
                lines[lines.Count - 1] = Ellipsize(g, rest.Trim(), font, width);
            }
            return lines;
        }

        static string Ellipsize(Graphics g, string s, Font font, int width)
        {
            if (Measure(g, s, font) <= width) return s;
            for (int n = s.Length - 1; n > 0; n--)
            {
                string t = s.Substring(0, n).TrimEnd() + "…";
                if (Measure(g, t, font) <= width) return t;
            }
            return "…";
        }

        static void DrawEmpty(Graphics g, RectangleF r, float size, float radius, bool hover, Color flash, Color parentBack)
        {
            using (GraphicsPath path = Theme.RoundRect(r, radius))
            {
                // 반투명으로 칠해 단색·사진 배경 어디서든 빈 칸이 보이게 한다
                using (var br = new SolidBrush(hover ? Color.FromArgb(28, 255, 255, 255) : Color.FromArgb(46, 0, 0, 0)))
                    g.FillPath(br, path);
                using (var pen = new Pen(hover ? Theme.TextDim : Theme.KeyEmptyBorder, Math.Max(1f, size / 70f)))
                {
                    pen.DashStyle = DashStyle.Dash;
                    g.DrawPath(pen, path);
                }
                DrawFlash(g, path, flash, size);
            }
            float gs = size * 0.26f;
            var gr = Rectangle.Round(new RectangleF(r.Left + (r.Width - gs) / 2f, r.Top + (r.Height - gs) / 2f, gs, gs));
            DrawGlyph(g, Glyphs.Add, gr, hover ? Theme.Text : Theme.KeyEmptyBorder);
        }

        static void DrawFlash(Graphics g, GraphicsPath path, Color flash, float size)
        {
            if (flash.IsEmpty) return;
            using (var pen = new Pen(flash, Math.Max(2f, size / 30f))) g.DrawPath(pen, path);
        }

        public static void DrawGlyph(Graphics g, string glyph, Rectangle rect, Color color)
        {
            if (string.IsNullOrEmpty(glyph) || rect.Height <= 0) return;
            Font f = Theme.PixelFont(Theme.GlyphFontName, rect.Height * 0.86f, FontStyle.Regular);
            TextRenderingHint old = g.TextRenderingHint;
            g.TextRenderingHint = TextRenderingHint.AntiAliasGridFit;
            using (var br = new SolidBrush(color))
            using (var sf = new StringFormat(StringFormat.GenericTypographic))
            {
                sf.Alignment = StringAlignment.Center;
                sf.LineAlignment = StringAlignment.Center;
                g.DrawString(glyph, f, br, rect, sf);
            }
            g.TextRenderingHint = old;
        }

        public static void DrawImageFit(Graphics g, Image img, Rectangle rect)
        {
            if (img.Width <= 0 || img.Height <= 0) return;
            float s = Math.Min((float)rect.Width / img.Width, (float)rect.Height / img.Height);
            float w = img.Width * s, h = img.Height * s;
            g.DrawImage(img, new RectangleF(rect.Left + (rect.Width - w) / 2f, rect.Top + (rect.Height - h) / 2f, w, h));
        }
    }
}
