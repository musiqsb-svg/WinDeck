using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;
using System.IO;

namespace WinDeck
{
    /// <summary>
    /// 프로그램 아이콘(어두운 본체 + 4×2 컬러 버튼)을 코드로 그린다.
    /// 빌드 때 tools\IconGen 이 같은 코드로 exe 아이콘(.ico)을 만든다.
    /// </summary>
    public static class AppIcon
    {
        static readonly Color[] KeyColors =
        {
            Color.FromArgb(10, 132, 255), Color.FromArgb(48, 209, 88), Color.FromArgb(255, 159, 10), Color.FromArgb(255, 69, 58),
            Color.FromArgb(191, 90, 242), Color.FromArgb(100, 210, 255), Color.FromArgb(255, 214, 10), Color.FromArgb(255, 55, 95)
        };

        public static Bitmap Render(int size)
        {
            var bmp = new Bitmap(size, size, PixelFormat.Format32bppArgb);
            using (Graphics g = Graphics.FromImage(bmp))
            {
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.PixelOffsetMode = PixelOffsetMode.HighQuality;
                g.Clear(Color.Transparent);

                float bw = size * 0.96f, bh = size * 0.68f;
                var body = new RectangleF((size - bw) / 2f, (size - bh) / 2f, bw, bh);
                using (GraphicsPath path = Round(body, size * 0.13f))
                using (var br = new LinearGradientBrush(RectangleF.Inflate(body, 1, 1), Color.FromArgb(70, 70, 76), Color.FromArgb(30, 30, 33), LinearGradientMode.Vertical))
                    g.FillPath(br, path);

                float inner = size * 0.075f, gap = size * 0.05f;
                float kw = (bw - inner * 2 - gap * 3) / 4f;
                float kh = (bh - inner * 2 - gap) / 2f;
                for (int i = 0; i < 8; i++)
                {
                    int col = i % 4, row = i / 4;
                    var k = new RectangleF(body.X + inner + col * (kw + gap), body.Y + inner + row * (kh + gap), kw, kh);
                    using (GraphicsPath kp = Round(k, Math.Min(kw, kh) * 0.28f))
                    using (var kb = new SolidBrush(KeyColors[i]))
                        g.FillPath(kb, kp);
                }
            }
            return bmp;
        }

        public static Icon CreateIcon(int size)
        {
            using (var ms = new MemoryStream(BuildIco(new[] { 16, 20, 24, 32, 40, 48, 64 })))
                return new Icon(ms, size, size);
        }

        /// <summary>여러 크기를 담은 .ico 바이트. 256px 만 PNG, 나머지는 32bit DIB.</summary>
        public static byte[] BuildIco(int[] sizes)
        {
            var frames = new byte[sizes.Length][];
            for (int i = 0; i < sizes.Length; i++)
            {
                using (Bitmap bmp = Render(sizes[i]))
                    frames[i] = sizes[i] >= 256 ? PngFrame(bmp) : DibFrame(bmp);
            }

            using (var ms = new MemoryStream())
            using (var w = new BinaryWriter(ms))
            {
                w.Write((short)0);
                w.Write((short)1);
                w.Write((short)sizes.Length);
                int offset = 6 + 16 * sizes.Length;
                for (int i = 0; i < sizes.Length; i++)
                {
                    int s = sizes[i];
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)(s >= 256 ? 0 : s));
                    w.Write((byte)0);
                    w.Write((byte)0);
                    w.Write((short)1);
                    w.Write((short)32);
                    w.Write(frames[i].Length);
                    w.Write(offset);
                    offset += frames[i].Length;
                }
                foreach (byte[] f in frames) w.Write(f);
                w.Flush();
                return ms.ToArray();
            }
        }

        static byte[] PngFrame(Bitmap bmp)
        {
            using (var ms = new MemoryStream())
            {
                bmp.Save(ms, ImageFormat.Png);
                return ms.ToArray();
            }
        }

        static byte[] DibFrame(Bitmap bmp)
        {
            int w = bmp.Width, h = bmp.Height;
            int maskStride = ((w + 31) / 32) * 4;
            using (var ms = new MemoryStream())
            using (var bw = new BinaryWriter(ms))
            {
                bw.Write(40);
                bw.Write(w);
                bw.Write(h * 2);
                bw.Write((short)1);
                bw.Write((short)32);
                bw.Write(0);
                bw.Write(w * h * 4 + maskStride * h);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);
                bw.Write(0);
                for (int y = h - 1; y >= 0; y--)
                {
                    for (int x = 0; x < w; x++)
                    {
                        Color c = bmp.GetPixel(x, y);
                        bw.Write(c.B);
                        bw.Write(c.G);
                        bw.Write(c.R);
                        bw.Write(c.A);
                    }
                }
                for (int y = h - 1; y >= 0; y--)
                {
                    var row = new byte[maskStride];
                    for (int x = 0; x < w; x++)
                        if (bmp.GetPixel(x, y).A == 0) row[x / 8] |= (byte)(0x80 >> (x % 8));
                    bw.Write(row);
                }
                bw.Flush();
                return ms.ToArray();
            }
        }

        static GraphicsPath Round(RectangleF r, float radius)
        {
            var p = new GraphicsPath();
            float d = Math.Min(radius * 2, Math.Min(r.Width, r.Height));
            if (d < 1f)
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
    }
}
