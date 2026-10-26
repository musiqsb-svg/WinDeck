using System;
using System.Drawing;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>배경 이미지를 가진 부모(패널). 자식 컨트롤은 자기 자리의 배경을 부모에게서 받아 그린다.</summary>
    public interface IBackgroundHost
    {
        void PaintChildBackground(Graphics g, Control child);
        Color BarForeColor { get; }
        Color BarDimColor { get; }
    }

    /// <summary>
    /// 패널의 버튼 한 칸. 포커스를 받지 않는 컨트롤이라 클릭해도 원래 쓰던 창의 커서가 유지된다.
    /// </summary>
    public class DeckKey : Control
    {
        bool hover, pressed;
        Color flash = Color.Empty;
        readonly Timer flashTimer;

        public int Index { get; private set; }
        public DeckButton Data { get; set; }

        public event EventHandler KeyClicked;
        public event Action<int> Wheel;

        public DeckKey(int index)
        {
            Index = index;
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
            AllowDrop = true;
            Cursor = Cursors.Hand;
            flashTimer = new Timer { Interval = 380 };
            flashTimer.Tick += delegate
            {
                flashTimer.Stop();
                flash = Color.Empty;
                Invalidate();
            };
        }

        public void Flash(Color color)
        {
            flash = color;
            flashTimer.Stop();
            flashTimer.Start();
            Invalidate();
        }

        protected override void OnMouseEnter(EventArgs e)
        {
            hover = true;
            Invalidate();
            base.OnMouseEnter(e);
        }

        protected override void OnMouseLeave(EventArgs e)
        {
            hover = false;
            pressed = false;
            Invalidate();
            base.OnMouseLeave(e);
        }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left)
            {
                pressed = true;
                Invalidate();
            }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool fire = pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
            if (fire && KeyClicked != null) KeyClicked(this, EventArgs.Empty);
        }

        protected override void OnMouseWheel(MouseEventArgs e)
        {
            if (Wheel != null) Wheel(e.Delta);
            base.OnMouseWheel(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            var host = Parent as IBackgroundHost;
            if (host != null)
            {
                host.PaintChildBackground(e.Graphics, this);
                KeyRenderer.Draw(e.Graphics, ClientRectangle, Data, hover, pressed, flash, Color.Empty);
            }
            else
            {
                KeyRenderer.Draw(e.Graphics, ClientRectangle, Data, hover, pressed, flash, Parent != null ? Parent.BackColor : Theme.PanelBack);
            }
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing) flashTimer.Dispose();
            base.Dispose(disposing);
        }
    }

    /// <summary>패널 상단 막대의 작은 아이콘 버튼 (포커스 없음).</summary>
    public class GlyphButton : Control
    {
        bool hover, pressed;

        public string Glyph { get; set; }
        public string TipTitle { get; set; }
        public event EventHandler Clicked;

        public GlyphButton(string glyph, string tipTitle)
        {
            Glyph = glyph;
            TipTitle = tipTitle;
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
            TabStop = false;
        }

        protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }

        protected override void OnMouseLeave(EventArgs e) { hover = false; pressed = false; Invalidate(); base.OnMouseLeave(e); }

        protected override void OnMouseDown(MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left) { pressed = true; Invalidate(); }
            base.OnMouseDown(e);
        }

        protected override void OnMouseUp(MouseEventArgs e)
        {
            bool fire = pressed && e.Button == MouseButtons.Left && ClientRectangle.Contains(e.Location);
            pressed = false;
            Invalidate();
            base.OnMouseUp(e);
            if (fire && Clicked != null) Clicked(this, EventArgs.Empty);
        }

        protected override void OnEnabledChanged(EventArgs e)
        {
            Invalidate();
            base.OnEnabledChanged(e);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            Graphics g = e.Graphics;
            var host = Parent as IBackgroundHost;
            if (host != null) host.PaintChildBackground(g, this);
            else g.Clear(Parent != null ? Parent.BackColor : Theme.PanelBack);
            g.SmoothingMode = System.Drawing.Drawing2D.SmoothingMode.AntiAlias;
            if (Enabled && (hover || pressed))
            {
                Color bg = Color.FromArgb(pressed ? 70 : 45, 255, 255, 255); // 어떤 배경 위에서도 보이도록 반투명
                using (var path = Theme.RoundRect(new RectangleF(0.5f, 0.5f, Width - 1f, Height - 1f), Height * 0.22f))
                using (var br = new SolidBrush(bg))
                    g.FillPath(br, path);
            }
            int gs = (int)(Height * 0.5f);
            var gr = new Rectangle((Width - gs) / 2, (Height - gs) / 2, gs, gs);
            Color fore = host != null ? host.BarForeColor : Theme.Text;
            Color dim = host != null ? host.BarDimColor : Theme.TextDim;
            KeyRenderer.DrawGlyph(g, Glyph, gr, Enabled ? (hover ? fore : dim) : Color.FromArgb(110, dim));
        }
    }

    /// <summary>편집창의 버튼 미리보기.</summary>
    public class KeyPreview : Control
    {
        public DeckButton Data { get; set; }

        public KeyPreview()
        {
            SetStyle(ControlStyles.Selectable, false);
            SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            e.Graphics.Clear(Theme.PanelBack);
            int pad = Math.Max(8, Width / 9);
            int s = Math.Min(Width, Height) - pad * 2;
            var r = new Rectangle((Width - s) / 2, (Height - s) / 2, s, s);
            KeyRenderer.Draw(e.Graphics, r, Data, false, false, Color.Empty, Theme.PanelBack);
        }
    }

    /// <summary>
    /// 일반 입력란이지만 BeginCapture() 후에는 다음에 누른 키 조합을 "Ctrl+Shift+N" 형태로 받아 적는다.
    /// </summary>
    public class KeyCaptureBox : TextBox
    {
        bool capturing;
        Color normalBack;

        public bool AppendMode { get; set; }
        public bool Capturing { get { return capturing; } }
        public event EventHandler CaptureChanged;

        public void BeginCapture()
        {
            if (capturing) return;
            capturing = true;
            normalBack = BackColor;
            BackColor = Color.FromArgb(255, 246, 214);
            Focus();
            if (CaptureChanged != null) CaptureChanged(this, EventArgs.Empty);
        }

        public void EndCapture()
        {
            if (!capturing) return;
            capturing = false;
            BackColor = normalBack;
            if (CaptureChanged != null) CaptureChanged(this, EventArgs.Empty);
        }

        protected override bool ProcessCmdKey(ref Message msg, Keys keyData)
        {
            if (!capturing) return base.ProcessCmdKey(ref msg, keyData);
            Keys code = keyData & Keys.KeyCode;
            if (code == Keys.ControlKey || code == Keys.ShiftKey || code == Keys.Menu
                || code == Keys.LWin || code == Keys.RWin || code == Keys.None)
                return true; // 수식키만 눌린 상태: 다음 키를 기다린다

            bool win = (Native.GetAsyncKeyState(0x5B) & 0x8000) != 0 || (Native.GetAsyncKeyState(0x5C) & 0x8000) != 0;
            var combo = new KeyCombo
            {
                Ctrl = (keyData & Keys.Control) == Keys.Control,
                Shift = (keyData & Keys.Shift) == Keys.Shift,
                Alt = (keyData & Keys.Alt) == Keys.Alt,
                Win = win,
                Key = (ushort)code
            };
            string s = combo.ToString();
            string current = Text.Trim().TrimEnd(',').Trim();
            Text = AppendMode && current.Length > 0 ? current + ", " + s : s;
            SelectionStart = Text.Length;
            EndCapture();
            return true;
        }

        protected override void OnLeave(EventArgs e)
        {
            EndCapture();
            base.OnLeave(e);
        }
    }
}
