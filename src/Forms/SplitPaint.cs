using System;
using System.Drawing;
using System.Drawing.Drawing2D;
using System.Windows.Forms;

namespace LarkSender.Forms
{
    /// <summary>
    /// 美化 SplitContainer 的分割条与面板边框。
    ///
    /// 默认的 SplitContainer 分割条是 3D 凹凸（灰色凹陷），在扁平化界面里显旧。
    /// 这里自己画：
    ///   - 分割条背景用浅灰，中间画一条 1px 细线
    ///   - 鼠标移上去时高亮，让用户知道可以拖
    ///   - 面板边框用 1px 淡圆角框代替系统 3D 边
    /// </summary>
    internal static class SplitPaint
    {
        private static readonly Color TrackBg = Color.FromArgb(250, 251, 252);
        private static readonly Color GripColor = Color.FromArgb(209, 213, 219);
        private static readonly Color GripHover = Color.FromArgb(59, 130, 246);
        private static readonly Color CardBorder = Color.FromArgb(229, 231, 235);

        /// <summary>给 SplitContainer 挂上鼠标状态跟踪（只挂一次）</summary>
        public static void Attach(SplitContainer sc)
        {
            if (sc == null || sc.Tag != null) return;
            sc.Tag = "split-painted";

            sc.MouseEnter += (s, e) => { sc.Invalidate(); };
            sc.MouseLeave += (s, e) => { sc.Invalidate(); };
            sc.MouseDown += (s, e) => { sc.Invalidate(); };
            sc.MouseUp += (s, e) => { sc.Invalidate(); };
        }

        /// <summary>
        /// 在系统绘制后覆盖分割条区域。
        /// 效果：一条与背景同色的间隔 + 中间一根极淡的 1px 竖线，鼠标靠近时变蓝。
        /// </summary>
        public static void PaintOver(SplitContainer sc, PaintEventArgs e)
        {
            if (sc == null || e == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;
            g.PixelOffsetMode = PixelOffsetMode.Half;

            var sr = sc.SplitterRectangle;
            if (sr.Width <= 0 && sr.Height <= 0) return;

            // 用窗体背景色填满分割条（看起来就像两块卡片间的间隙）
            using (var bg = new SolidBrush(Theme.Bg))
            {
                g.FillRectangle(bg, sr);
            }

            bool hover = sc.SplitterRectangle.Contains(sc.PointToClient(Cursor.Position));
            if (hover)
            {
                // 悬停时画一根蓝色细线，提示可拖
                using (var pen = new Pen(Theme.Primary, 1f))
                {
                    if (sc.Orientation == Orientation.Vertical)
                    {
                        int x = sr.Left + sr.Width / 2;
                        g.DrawLine(pen, x, sr.Top + 10, x, sr.Bottom - 10);
                    }
                    else
                    {
                        int y = sr.Top + sr.Height / 2;
                        g.DrawLine(pen, sr.Left + 10, y, sr.Right - 10, y);
                    }
                }
            }
        }

        /// <summary>在 Paint 事件里调用，绘制分割条</summary>
        public static void Paint(SplitContainer sc, PaintEventArgs e)
        {
            if (sc == null || e == null) return;

            var g = e.Graphics;
            g.SmoothingMode = SmoothingMode.None;

            // 计算分割条矩形：SplitterRectangle 是相对 SplitContainer 的
            var sr = sc.SplitterRectangle;
            if (sr.Width <= 0 && sr.Height <= 0) return;

            using (var bg = new SolidBrush(TrackBg))
            {
                g.FillRectangle(bg, sr);
            }

            bool hover = sc.RectangleToScreen(sc.ClientRectangle)
                           .Contains(Cursor.Position);

            using (var pen = new Pen(hover ? GripHover : GripColor, 1f))
            {
                if (sc.Orientation == Orientation.Vertical)
                {
                    // 竖分割条：中间画一根竖线
                    int x = sr.Left + sr.Width / 2;
                    g.DrawLine(pen, x, sr.Top + 6, x, sr.Bottom - 6);
                }
                else
                {
                    int y = sr.Top + sr.Height / 2;
                    g.DrawLine(pen, sr.Left + 6, y, sr.Right - 6, y);
                }
            }
        }

        /// <summary>在 Panel 的 Paint 里调用，画 1px 淡色圆角边框（代替 3D 边）</summary>
        public static void PaintCardBorder(Control c, PaintEventArgs e)
        {
            if (c == null || e == null) return;
            e.Graphics.SmoothingMode = SmoothingMode.AntiAlias;

            var rect = new Rectangle(0, 0, c.Width - 1, c.Height - 1);
            if (rect.Width <= 0 || rect.Height <= 0) return;

            using (var pen = new Pen(CardBorder, 1f))
            using (var path = RoundRect(rect, 6))
            {
                e.Graphics.DrawPath(pen, path);
            }
        }

        private static GraphicsPath RoundRect(Rectangle r, int radius)
        {
            var p = new GraphicsPath();
            int d = radius * 2;
            if (d <= 0) { p.AddRectangle(r); return p; }

            p.AddArc(r.X, r.Y, d, d, 180, 90);
            p.AddArc(r.Right - d, r.Y, d, d, 270, 90);
            p.AddArc(r.Right - d, r.Bottom - d, d, d, 0, 90);
            p.AddArc(r.X, r.Bottom - d, d, d, 90, 90);
            p.CloseFigure();
            return p;
        }
    }
}