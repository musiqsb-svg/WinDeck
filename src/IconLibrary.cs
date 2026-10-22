using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using System.Windows.Forms;

namespace WinDeck
{
    /// <summary>exe 에 넣어 둔 기본 아이콘 한 개 (assets\icons, Codex 로 생성).</summary>
    public class IconAsset
    {
        public string Name;
        public string Label;
        public string Category;
        /// <summary>"입체"(3D) 또는 "플랫"(2D).</summary>
        public string Style;
        public byte[] Png;
        Image image;

        public Image Image
        {
            get
            {
                if (image == null)
                    using (var ms = new MemoryStream(Png))
                    using (Image tmp = Image.FromStream(ms))
                        image = new Bitmap(tmp);
                return image;
            }
        }

        /// <summary>버튼 설정(IconData)에 그대로 넣을 수 있는 base64 PNG.</summary>
        public string Base64
        {
            get { return Convert.ToBase64String(Png); }
        }
    }

    /// <summary>
    /// 기본 아이콘 목록. 빌드할 때 assets\icons\*.png 와 manifest.tsv(이름·한글 이름·분류)가
    /// WinDeck.Icons.* 리소스로 들어간다.
    /// </summary>
    public static class IconLibrary
    {
        const string Prefix = "WinDeck.Icons.";
        static List<IconAsset> all;

        public static List<IconAsset> All
        {
            get
            {
                if (all == null) all = Load();
                return all;
            }
        }

        public static IconAsset Find(string name)
        {
            return All.FirstOrDefault(a => a.Name == name);
        }

        public static List<string> Categories
        {
            get { return All.Select(a => a.Category).Distinct().ToList(); }
        }

        public static List<string> Styles
        {
            get { return All.Select(a => a.Style).Distinct().ToList(); }
        }

        static List<IconAsset> Load()
        {
            var list = new List<IconAsset>();
            Assembly asm = typeof(IconLibrary).Assembly;
            using (Stream s = asm.GetManifestResourceStream(Prefix + "manifest.tsv"))
            {
                if (s == null) return list;
                using (var reader = new StreamReader(s, Encoding.UTF8))
                {
                    string line;
                    while ((line = reader.ReadLine()) != null)
                    {
                        if (line.Trim().Length == 0 || line.StartsWith("#")) continue;
                        string[] f = line.Split('\t');
                        if (f.Length < 3) continue;
                        byte[] png = ReadResource(asm, Prefix + f[0].Trim() + ".png");
                        if (png == null) continue;
                        list.Add(new IconAsset
                        {
                            Name = f[0].Trim(),
                            Label = f[1].Trim(),
                            Category = f[2].Trim(),
                            Style = f.Length > 3 && f[3].Trim().Length > 0 ? f[3].Trim() : "입체",
                            Png = png
                        });
                    }
                }
            }
            return list;
        }

        static byte[] ReadResource(Assembly asm, string name)
        {
            using (Stream s = asm.GetManifestResourceStream(name))
            {
                if (s == null) return null;
                using (var ms = new MemoryStream())
                {
                    s.CopyTo(ms);
                    return ms.ToArray();
                }
            }
        }
    }

    /// <summary>기본 아이콘 고르기 창. 분류·검색으로 거르고, 현재 버튼 배경색 위에서 미리 본다.</summary>
    public class IconPickerForm : Form
    {
        readonly Color keyBack;
        readonly ComboBox cboCategory, cboStyle;
        readonly TextBox txtSearch;
        readonly FlowLayoutPanel grid;
        readonly List<IconTile> tiles = new List<IconTile>();
        readonly Button btnOk;
        IconTile selected;

        public IconAsset Selected
        {
            get { return selected == null ? null : selected.Asset; }
        }

        public IconPickerForm(Color keyBack)
        {
            this.keyBack = keyBack;
            SuspendLayout();
            Text = "아이콘 모음";
            Font = new Font(Theme.UiFontName, 9f);
            FormBorderStyle = FormBorderStyle.FixedDialog;
            MaximizeBox = false;
            MinimizeBox = false;
            ShowInTaskbar = false;
            ShowIcon = false;
            StartPosition = FormStartPosition.CenterParent;
            ClientSize = new Size(640, 520);

            Controls.Add(new Label { Text = "스타일", AutoSize = true, Location = new Point(16, 19) });
            cboStyle = new ComboBox { Location = new Point(62, 16), Size = new Size(120, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboStyle.Items.Add("전체");
            foreach (string st in IconLibrary.Styles) cboStyle.Items.Add(st == "입체" ? "입체 (3D)" : st == "플랫" ? "플랫 (2D)" : st);
            cboStyle.SelectedIndex = 0;
            Controls.Add(cboStyle);

            Controls.Add(new Label { Text = "분류", AutoSize = true, Location = new Point(198, 19) });
            cboCategory = new ComboBox { Location = new Point(236, 16), Size = new Size(140, 23), DropDownStyle = ComboBoxStyle.DropDownList };
            cboCategory.Items.Add("전체");
            foreach (string c in IconLibrary.Categories) cboCategory.Items.Add(c);
            cboCategory.SelectedIndex = 0;
            Controls.Add(cboCategory);

            Controls.Add(new Label { Text = "검색", AutoSize = true, Location = new Point(392, 19) });
            txtSearch = new TextBox { Location = new Point(430, 16), Size = new Size(194, 23) };
            Controls.Add(txtSearch);

            grid = new FlowLayoutPanel
            {
                Location = new Point(16, 50),
                Size = new Size(608, 404),
                AutoScroll = true,
                BackColor = Theme.PanelBack,
                Padding = new Padding(6)
            };
            foreach (IconAsset a in IconLibrary.All)
            {
                var t = new IconTile(a, keyBack) { Size = new Size(88, 100), Margin = new Padding(3) };
                t.Click += delegate { SelectTile(t); };
                t.DoubleClick += delegate { SelectTile(t); Accept(); };
                tiles.Add(t);
                grid.Controls.Add(t);
            }
            Controls.Add(grid);
            if (tiles.Count == 0)
                Controls.Add(new Label { Text = "포함된 아이콘이 없습니다.", AutoSize = true, Location = new Point(24, 60), BackColor = Theme.PanelBack, ForeColor = Theme.Text });

            Controls.Add(new Label
            {
                Text = "입체(3D)·플랫(2D) 기본 아이콘 · 더블클릭하면 바로 적용",
                AutoSize = true,
                Location = new Point(16, 468),
                ForeColor = SystemColors.GrayText
            });
            btnOk = new Button { Text = "확인", Location = new Point(436, 476), Size = new Size(92, 30), Enabled = false };
            var cancel = new Button { Text = "취소", Location = new Point(532, 476), Size = new Size(92, 30), DialogResult = DialogResult.Cancel };
            btnOk.Click += delegate { Accept(); };
            Controls.Add(btnOk);
            Controls.Add(cancel);
            CancelButton = cancel;

            cboCategory.SelectedIndexChanged += delegate { ApplyFilter(); };
            cboStyle.SelectedIndexChanged += delegate { ApplyFilter(); };
            txtSearch.TextChanged += delegate { ApplyFilter(); };

            AutoScaleDimensions = new SizeF(96F, 96F);
            AutoScaleMode = AutoScaleMode.Dpi;
            ResumeLayout(false);
            PerformLayout();
        }

        void ApplyFilter()
        {
            string cat = cboCategory.SelectedIndex <= 0 ? null : (string)cboCategory.SelectedItem;
            string style = cboStyle.SelectedIndex <= 0 ? null : IconLibrary.Styles[cboStyle.SelectedIndex - 1];
            string q = txtSearch.Text.Trim();
            grid.SuspendLayout();
            foreach (IconTile t in tiles)
            {
                bool show = (cat == null || t.Asset.Category == cat)
                    && (style == null || t.Asset.Style == style)
                    && (q.Length == 0 || t.Asset.Label.Contains(q) || t.Asset.Name.IndexOf(q, StringComparison.OrdinalIgnoreCase) >= 0);
                t.Visible = show;
            }
            grid.ResumeLayout();
        }

        void SelectTile(IconTile t)
        {
            if (selected != null) selected.Selected = false;
            selected = t;
            t.Selected = true;
            btnOk.Enabled = true;
        }

        void Accept()
        {
            if (selected == null) return;
            DialogResult = DialogResult.OK;
            Close();
        }

        /// <summary>고른 아이콘의 base64 PNG, 취소하면 null.</summary>
        public static string Pick(IWin32Window owner, Color keyBack)
        {
            using (var f = new IconPickerForm(keyBack))
                return f.ShowDialog(owner) == DialogResult.OK && f.Selected != null ? f.Selected.Base64 : null;
        }

        /// <summary>버튼 모양 위에 아이콘과 이름을 그리는 칸.</summary>
        class IconTile : Control
        {
            readonly Color keyBack;
            bool hover, isSelected;

            public IconAsset Asset { get; private set; }

            public bool Selected
            {
                get { return isSelected; }
                set { isSelected = value; Invalidate(); }
            }

            public IconTile(IconAsset asset, Color keyBack)
            {
                Asset = asset;
                this.keyBack = keyBack;
                SetStyle(ControlStyles.UserPaint | ControlStyles.AllPaintingInWmPaint
                    | ControlStyles.OptimizedDoubleBuffer | ControlStyles.ResizeRedraw, true);
                Cursor = Cursors.Hand;
            }

            protected override void OnMouseEnter(EventArgs e) { hover = true; Invalidate(); base.OnMouseEnter(e); }

            protected override void OnMouseLeave(EventArgs e) { hover = false; Invalidate(); base.OnMouseLeave(e); }

            protected override void OnPaint(PaintEventArgs e)
            {
                Graphics g = e.Graphics;
                g.Clear(Theme.PanelBack);
                g.SmoothingMode = SmoothingMode.AntiAlias;
                g.InterpolationMode = InterpolationMode.HighQualityBicubic;
                int key = Math.Min(Width, Height) - (int)(Height * 0.24f);
                var r = new RectangleF((Width - key) / 2f, 2, key - 1, key - 1);
                Color bg = hover ? Theme.Lighten(keyBack, 0.10f) : keyBack;
                using (GraphicsPath path = Theme.RoundRect(r, key * 0.16f))
                {
                    using (var br = new SolidBrush(bg)) g.FillPath(br, path);
                    if (isSelected)
                        using (var pen = new Pen(Theme.Accent, Math.Max(2f, key / 22f))) g.DrawPath(pen, path);
                }
                int icon = (int)(key * 0.66f);
                KeyRenderer.DrawImageFit(g, Asset.Image, new Rectangle((int)(r.X + (key - icon) / 2f), (int)(r.Y + (key - icon) / 2f), icon, icon));
                var tr = new Rectangle(0, (int)r.Bottom + 2, Width, Height - (int)r.Bottom - 2);
                TextRenderer.DrawText(g, Asset.Label, Font, tr, isSelected ? Theme.Accent : Theme.Text,
                    TextFormatFlags.HorizontalCenter | TextFormatFlags.Top | TextFormatFlags.EndEllipsis | TextFormatFlags.NoPrefix);
            }
        }
    }
}
