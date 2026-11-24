using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LarkSender.Forms
{
    /// <summary>
    /// 统一的界面视觉风格。
    ///
    /// 设计原则：
    ///   - 大量留白，减少边框线
    ///   - 只用一个主色（蓝），其他都是灰阶
    ///   - 字号有层次：标题 11pt / 正文 9.5pt / 辅助 8.5pt
    ///   - 控件不画 3D 边，需要区隔的地方用极淡的底色块
    /// </summary>
    internal static class Theme
    {
        // ---- 主色 ----
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);
        public static readonly Color PrimaryHover = Color.FromArgb(29, 78, 216);

        // ---- 灰阶 ----
        public static readonly Color Bg = Color.FromArgb(247, 249, 252);
        public static readonly Color Surface = Color.White;
        public static readonly Color Border = Color.FromArgb(214, 222, 234);
        public static readonly Color BorderSoft = Color.FromArgb(241, 245, 249);
        public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
        public static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);
        public static readonly Color TextMuted = Color.FromArgb(148, 163, 184);

        // ---- 语义色 ----
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);

        // ---- 字体 ----
        private const string Family = "Microsoft YaHei UI";
        public static Font FontTitle { get { return new Font(Family, 11.5f, FontStyle.Bold); } }
        public static Font FontBody { get { return new Font(Family, 9.5f); } }
        public static Font FontBodyBold { get { return new Font(Family, 9.5f, FontStyle.Bold); } }
        public static Font FontSmall { get { return new Font(Family, 8.5f); } }
        public static Font FontMono { get { return new Font("Consolas", 9.5f); } }

        /// <summary>应用全局默认样式</summary>
        public static void ApplyForm(Form f)
        {
            f.Font = FontBody;
            f.BackColor = Bg;
            f.ForeColor = TextPrimary;
            f.AutoScaleMode = AutoScaleMode.Dpi;
        }

        /// <summary>次要按钮：白底细边</summary>
        public static void StyleSecondary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderColor = Border;
            b.FlatAppearance.BorderSize = 1;
            b.FlatAppearance.MouseOverBackColor = BorderSoft;
            b.FlatAppearance.MouseDownBackColor = Border;
            b.BackColor = Surface;
            b.ForeColor = TextSecondary;
            b.Cursor = Cursors.Hand;
        }

        /// <summary>主按钮：蓝底白字</summary>
        public static void StylePrimary(Button b)
        {
            b.FlatStyle = FlatStyle.Flat;
            b.FlatAppearance.BorderSize = 0;
            b.FlatAppearance.MouseOverBackColor = PrimaryHover;
            b.FlatAppearance.MouseDownBackColor = PrimaryHover;
            b.BackColor = Primary;
            b.ForeColor = Color.White;
            b.Cursor = Cursors.Hand;
        }

        /// <summary>输入框：无边框 + 淡底</summary>
        public static void StyleInput(Control c)
        {
            c.BackColor = Surface;
            c.ForeColor = TextPrimary;
        }

        /// <summary>画 1px 圆角描边（在 Paint 事件里调用）</summary>
        public static void DrawBorder(Graphics g, Rectangle r, Color color, int radius)        {
            if (r.Width <= 0 || r.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var pen = new Pen(color, 1f))
            using (var path = RoundRect(new Rectangle(r.X, r.Y, r.Width - 1, r.Height - 1), radius))
            {
                g.DrawPath(pen, path);
            }
        }

        /// <summary>填圆角块（分割线、色块等）</summary>
        public static void FillRound(Graphics g, Rectangle r, Color color, int radius)
        {
            if (r.Width <= 0 || r.Height <= 0) return;
            g.SmoothingMode = SmoothingMode.AntiAlias;
            using (var brush = new SolidBrush(color))
            using (var path = RoundRect(new Rectangle(r.X, r.Y, r.Width, r.Height), radius))
            {
                g.FillPath(brush, path);
            }
        }

        public static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }
            if (d > r.Width) d = r.Width;
            if (d > r.Height) d = r.Height;
            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }

    /// <summary>
    /// 带圆角描边的容器面板。
    /// 用来替代 GroupBox —— GroupBox 自带标题边框线，视觉上很重。
    /// </summary>
    internal class CardPanel : Panel
    {
        public int Radius { get; set; }
        public Color BorderColor { get; set; }
        public bool ShowBorder { get; set; }

        public CardPanel()
        {
            Radius = 8;
            BorderColor = Theme.Border;
            ShowBorder = true;
            BackColor = Theme.Surface;
            DoubleBuffered = true;
            ResizeRedraw = true;
        }

        protected override void OnPaint(PaintEventArgs e)
        {
            base.OnPaint(e);
            if (ShowBorder)
                Theme.DrawBorder(e.Graphics, ClientRectangle, BorderColor, Radius);
        }

        // 给边框留出 1px 空间，否则被子控件盖住
        public new Padding Padding
        {
            get { return base.Padding; }
            set { base.Padding = new Padding(value.Left + 1, value.Top + 1, value.Right + 1, value.Bottom + 1); }
        }
    }
}