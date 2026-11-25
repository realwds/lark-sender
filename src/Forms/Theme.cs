using System;
using System.Drawing;
using System.Windows.Forms;

namespace LarkSender.Forms
{
    /// <summary>
    /// 统一的界面视觉常量。
    ///
    /// 界面本身使用系统原生控件（默认边框、系统配色），这里只保留：
    ///   - 一个主色（蓝）与语义色（成功/失败/警告），供状态文字使用
    ///   - 字号分层的字体
    ///   - 窗体级默认字体与背景
    /// 不再提供自绘面板/扁平按钮等装饰——保持和系统一致的观感。
    /// </summary>
    internal static class Theme
    {
        // ---- 主色 ----
        public static readonly Color Primary = Color.FromArgb(37, 99, 235);

        // ---- 灰阶 ----
        public static readonly Color Bg = Color.FromArgb(247, 249, 252);
        public static readonly Color TextPrimary = Color.FromArgb(15, 23, 42);
        public static readonly Color TextSecondary = Color.FromArgb(71, 85, 105);

        // ---- 语义色 ----
        public static readonly Color Success = Color.FromArgb(22, 163, 74);
        public static readonly Color Danger = Color.FromArgb(220, 38, 38);
        public static readonly Color Warning = Color.FromArgb(217, 119, 6);

        // ---- 字体 ----
        private const string Family = "Microsoft YaHei UI";
        public static Font FontBody { get { return new Font(Family, 9.5f); } }
        public static Font FontSmall { get { return new Font(Family, 8.5f); } }

        /// <summary>应用全局默认字体与背景</summary>
        public static void ApplyForm(Form f)
        {
            f.Font = FontBody;
            f.BackColor = Bg;
            f.ForeColor = TextPrimary;
            f.AutoScaleMode = AutoScaleMode.Dpi;
        }
    }
}