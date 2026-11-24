using System;
using System.Collections.Generic;
using System.Drawing;
using System.Text;
using System.Windows.Forms;
using LarkSender.Core;

namespace LarkSender.Forms
{
    /// <summary>
    /// 在 RichTextBox 里模拟飞书卡片的渲染效果。
    ///
    /// 主界面实时预览、预览窗左栏都调这个，保证「预览所见 = 实际发送」。
    /// 渲染能力对齐飞书 lark_md 实际支持的：
    ///   卡片标题栏（彩色）、加粗、斜体、删除线、行内代码、超链接、列表、引用、分割线
    /// </summary>
    internal static class CardRenderer
    {
        // 与飞书卡片配色接近
        private static readonly Color TitleBarBlue = Color.FromArgb(37, 99, 235);
        private static readonly Color TitleBarGreen = Color.FromArgb(22, 163, 74);
        private static readonly Color TitleBarRed = Color.FromArgb(220, 38, 38);
        private static readonly Color TitleBarOrange = Color.FromArgb(234, 88, 12);
        private static readonly Color TitleBarGrey = Color.FromArgb(107, 114, 128);
        private static readonly Color TitleBarPurple = Color.FromArgb(147, 51, 234);
        private static readonly Color TitleBarTurquoise = Color.FromArgb(13, 148, 136);

        private static readonly Color TextPrimary = Color.FromArgb(17, 24, 39);
        private static readonly Color TextSecondary = Color.FromArgb(75, 85, 99);
        private static readonly Color TextMuted = Color.FromArgb(156, 163, 175);
        private static readonly Color LinkColor = Color.FromArgb(37, 99, 235);
        private static readonly Color CodeBg = Color.FromArgb(243, 244, 246);

        /// <summary>在空预览区追加提示文字</summary>
        public static void AppendHint(RichTextBox rtb, string hint)
        {
            if (rtb == null) return;
            Append(rtb, hint, TextMuted, rtb.Font, false);
        }

        /// <summary>把 Markdown 渲染到 RichTextBox（模拟飞书卡片）</summary>
        public static void Render(RichTextBox rtb, string markdown, bool atAll, bool asCard)
        {
            Render(rtb, markdown, atAll, asCard, null, CardBuilder.HeaderColor.Blue, true);
        }

        /// <summary>带标题/颜色参数的渲染（卡片模式）</summary>
        public static void Render(RichTextBox rtb, string markdown, bool atAll, bool asCard,
            string title, CardBuilder.HeaderColor color, bool autoColor)
        {
            if (rtb == null) return;

            rtb.SuspendLayout();
            try
            {
                rtb.Clear();
                rtb.BackColor = Color.White;

                if (string.IsNullOrEmpty(markdown) && string.IsNullOrEmpty(title))
                {
                    Append(rtb, "（在左侧输入内容，这里会显示效果）", TextMuted, rtb.Font, false);
                    return;
                }

                if (!asCard)
                {
                    // 纯文本模式：原样显示
                    if (atAll)
                    {
                        Append(rtb, "@所有人\n", LinkColor, Bold(rtb.Font), false);
                    }
                    Append(rtb, markdown ?? "", TextPrimary, rtb.Font, false);
                    return;
                }

                // ===== 卡片模式 =====
                var parsed = CardBuilder.Parse(markdown, atAll);

                // 界面填的标题优先（用户不需要在正文里写 #）
                if (!string.IsNullOrWhiteSpace(title))
                {
                    parsed.Title = title.Trim();
                    if (autoColor) parsed.Color = GuessColorPublic(parsed.Title);
                    else parsed.Color = color;
                }
                else if (!autoColor)
                {
                    parsed.Color = color;
                }

                RenderCardPreview(rtb, parsed);
            }
            finally
            {
                rtb.ResumeLayout();
            }
        }

        private static CardBuilder.HeaderColor GuessColorPublic(string title)
        {
            // 复用 CardBuilder 的关键词推断：构造一个带 # 的文本让它自己猜
            var r = CardBuilder.Parse("# " + (title ?? ""), false);
            return r.Color;
        }

        /// <summary>按卡片结构渲染（标题栏 + 元素）</summary>
        private static void RenderCardPreview(RichTextBox rtb, CardBuilder.CardResult parsed)
        {
            var baseFont = rtb.Font;

            // ---- 标题栏 ----
            if (!string.IsNullOrEmpty(parsed.Title))
            {
                Color barColor = HeaderColorOf(parsed.Color);
                var titleFont = new Font(baseFont.FontFamily, baseFont.Size + 2.5f, FontStyle.Bold);

                // 模拟彩色标题栏：用背景色块 + 白字
                AppendWithBg(rtb, "  " + parsed.Title + "  ", Color.White, barColor, titleFont);
                Append(rtb, "\n\n", TextPrimary, baseFont, false);
            }

            // ---- 元素 ----
            var elements = parsed.Elements;
            for (int i = 0; i < elements.Count; i++)
            {
                var dict = elements[i] as Dictionary<string, object>;
                if (dict == null) continue;

                string tag = Json.GetString(dict, "tag", "");

                if (tag == "hr")
                {
                    Append(rtb, "\n", TextPrimary, baseFont, false);
                    Append(rtb, new string('─', 48), Color.FromArgb(229, 231, 235), baseFont, false);
                    Append(rtb, "\n\n", TextPrimary, baseFont, false);
                    continue;
                }

                if (tag == "note")
                {
                    var noteElems = Json.GetList(dict, "elements");
                    var sb = new StringBuilder();
                    foreach (var ne in noteElems)
                    {
                        var nd = ne as Dictionary<string, object>;
                        if (nd != null) sb.Append(Json.GetString(nd, "content", ""));
                    }
                    if (i > 0) Append(rtb, "\n", TextPrimary, baseFont, false);
                    Append(rtb, new string('─', 48), Color.FromArgb(229, 231, 235), baseFont, false);
                    Append(rtb, "\n", TextPrimary, baseFont, false);
                    Append(rtb, sb.ToString(), TextMuted, new Font(baseFont.FontFamily, baseFont.Size - 0.5f), false);
                    continue;
                }

                if (tag == "div")
                {
                    var textObj = Json.GetObject(dict, "text");
                    if (textObj == null)
                    {
                        // 可能是 fields（多列）
                        var fields = Json.GetList(dict, "fields");
                        foreach (var f in fields)
                        {
                            var fd = f as Dictionary<string, object>;
                            if (fd == null) continue;
                            var ft = Json.GetObject(fd, "text");
                            if (ft == null) continue;
                            RenderMd(rtb, Json.GetString(ft, "content", ""));
                            Append(rtb, "\n", TextPrimary, baseFont, false);
                        }
                        continue;
                    }

                    RenderMd(rtb, Json.GetString(textObj, "content", ""));
                    if (i < elements.Count - 1) Append(rtb, "\n", TextPrimary, baseFont, false);
                }
            }
        }

        /// <summary>渲染一段 lark_md 文本（支持 **粗** *斜* ~~删~~ `码` [链](url) 列表 引用）</summary>
        public static void RenderMd(RichTextBox rtb, string md)
        {
            if (string.IsNullOrEmpty(md)) return;

            var baseFont = rtb.Font;
            var lines = md.Replace("\r\n", "\n").Split('\n');

            for (int li = 0; li < lines.Length; li++)
            {
                var line = lines[li];
                if (li > 0) Append(rtb, "\n", TextPrimary, baseFont, false);

                // 引用
                if (line.TrimStart().StartsWith("> "))
                {
                    var indent = line.Length - line.TrimStart().Length;
                    Append(rtb, new string(' ', indent) + "▎", Color.FromArgb(209, 213, 219), baseFont, false);
                    RenderInline(rtb, line.TrimStart().Substring(2), TextSecondary);
                    continue;
                }

                // 列表
                var trimmed = line.TrimStart();
                var lead = line.Substring(0, line.Length - trimmed.Length);
                if (trimmed.StartsWith("- ") || trimmed.StartsWith("* "))
                {
                    Append(rtb, lead + "  •  ", TextSecondary, baseFont, false);
                    RenderInline(rtb, trimmed.Substring(2), TextPrimary);
                    continue;
                }

                var mOrdered = System.Text.RegularExpressions.Regex.Match(trimmed, @"^(\d+)\.\s(.*)$");
                if (mOrdered.Success)
                {
                    Append(rtb, lead + "  " + mOrdered.Groups[1].Value + ". ", TextSecondary, baseFont, false);
                    RenderInline(rtb, mOrdered.Groups[2].Value, TextPrimary);
                    continue;
                }

                RenderInline(rtb, line, TextPrimary);
            }
        }

        /// <summary>渲染行内标记</summary>
        private static void RenderInline(RichTextBox rtb, string text, Color defaultColor)
        {
            var baseFont = rtb.Font;
            int i = 0;
            var buf = new StringBuilder();

            Action flushBuf = () =>
            {
                if (buf.Length == 0) return;
                Append(rtb, buf.ToString(), defaultColor, baseFont, false);
                buf.Length = 0;
            };

            while (i < text.Length)
            {
                // **加粗**
                if (i + 1 < text.Length && text[i] == '*' && text[i + 1] == '*')
                {
                    int end = text.IndexOf("**", i + 2, StringComparison.Ordinal);
                    if (end > i)
                    {
                        flushBuf();
                        Append(rtb, text.Substring(i + 2, end - i - 2), defaultColor, Bold(baseFont), false);
                        i = end + 2;
                        continue;
                    }
                }

                // ~~删除线~~
                if (i + 1 < text.Length && text[i] == '~' && text[i + 1] == '~')
                {
                    int end = text.IndexOf("~~", i + 2, StringComparison.Ordinal);
                    if (end > i)
                    {
                        flushBuf();
                        Append(rtb, text.Substring(i + 2, end - i - 2), TextMuted, baseFont, true);
                        i = end + 2;
                        continue;
                    }
                }

                // `行内代码`
                if (text[i] == '`')
                {
                    int end = text.IndexOf('`', i + 1);
                    if (end > i)
                    {
                        flushBuf();
                        var code = text.Substring(i + 1, end - i - 1);
                        AppendWithBg(rtb, " " + code + " ", Color.FromArgb(31, 41, 55), CodeBg,
                            new Font("Consolas", baseFont.Size - 0.5f));
                        i = end + 1;
                        continue;
                    }
                }

                // [文字](链接)
                if (text[i] == '[')
                {
                    int closeBracket = text.IndexOf(']', i + 1);
                    if (closeBracket > i && closeBracket + 1 < text.Length && text[closeBracket + 1] == '(')
                    {
                        int closeParen = text.IndexOf(')', closeBracket + 2);
                        if (closeParen > closeBracket)
                        {
                            flushBuf();
                            var label = text.Substring(i + 1, closeBracket - i - 1);
                            Append(rtb, label, LinkColor, baseFont, true);
                            i = closeParen + 1;
                            continue;
                        }
                    }
                }

                // *斜体*（单个星号，且不是 ** 的一部分）
                if (text[i] == '*' && (i + 1 >= text.Length || text[i + 1] != '*'))
                {
                    int end = text.IndexOf('*', i + 1);
                    if (end > i + 1 && (end + 1 >= text.Length || text[end + 1] != '*'))
                    {
                        flushBuf();
                        Append(rtb, text.Substring(i + 1, end - i - 1), defaultColor, Italic(baseFont), false);
                        i = end + 1;
                        continue;
                    }
                }

                buf.Append(text[i]);
                i++;
            }

            flushBuf();
        }

        // ==================== 基础输出 ====================

        private static void Append(RichTextBox rtb, string text, Color color, Font font, bool strikeout)
        {
            if (string.IsNullOrEmpty(text)) return;
            int start = rtb.TextLength;
            rtb.AppendText(text);
            rtb.Select(start, text.Length);
            rtb.SelectionColor = color;
            rtb.SelectionFont = font;
            if (strikeout) rtb.SelectionFont = new Font(font, font.Style | FontStyle.Strikeout);
            rtb.SelectionLength = 0;
        }

        private static void AppendWithBg(RichTextBox rtb, string text, Color fg, Color bg, Font font)
        {
            if (string.IsNullOrEmpty(text)) return;
            int start = rtb.TextLength;
            rtb.AppendText(text);
            rtb.Select(start, text.Length);
            rtb.SelectionColor = fg;
            rtb.SelectionBackColor = bg;
            rtb.SelectionFont = font;
            rtb.SelectionLength = 0;
        }

        private static Font Bold(Font f) { return new Font(f, f.Style | FontStyle.Bold); }
        private static Font Italic(Font f) { return new Font(f, f.Style | FontStyle.Italic); }

        private static Color HeaderColorOf(CardBuilder.HeaderColor c)
        {
            switch (c)
            {
                case CardBuilder.HeaderColor.Green: return TitleBarGreen;
                case CardBuilder.HeaderColor.Red: return TitleBarRed;
                case CardBuilder.HeaderColor.Orange: return TitleBarOrange;
                case CardBuilder.HeaderColor.Grey: return TitleBarGrey;
                case CardBuilder.HeaderColor.Purple: return TitleBarPurple;
                case CardBuilder.HeaderColor.Turquoise: return TitleBarTurquoise;
                default: return TitleBarBlue;
            }
        }
    }
}