using System;
using System.Collections.Generic;
using System.Text;
using LarkSender.Models;

namespace LarkSender.Core
{
    /// <summary>
    /// 把简化 Markdown 转成飞书 interactive 卡片的 elements。
    ///
    /// 为什么用卡片而不是 post：
    ///   实测群机器人 webhook 的 post 不支持 style（加粗/斜体/下划线），
    ///   只有 interactive 卡片的 lark_md 才真正支持 Markdown 渲染。
    ///
    /// 支持的语法：
    ///   # 一级标题          → 卡片 header（彩色标题栏）
    ///   ## 二级标题         → 加粗大字（div）
    ///   - 列表项 / * 列表项  → 圆点列表
    ///   1. 有序列表         → 数字列表
    ///   **加粗**            → 加粗
    ///   *斜体*              → 斜体
    ///   ~~删除线~~          → 删除线
    ///   [文字](链接)         → 超链接
    ///   `代码`              → 行内代码
    ///   > 引用              → 引用块
    ///   ---                 → 分割线
    ///   @某人 / @所有人      → 提及
    ///   --- 之后的内容       → note 页脚（用 -!- 标记）
    /// </summary>
    public static class CardBuilder
    {
        /// <summary>卡片标题栏配色</summary>
        public enum HeaderColor
        {
            Blue,   // 蓝色，默认
            Green,  // 绿色，成功
            Red,    // 红色，告警
            Orange, // 橙色，警告
            Grey,   // 灰色，普通
            Purple, // 紫色
            Turquoise, // 青色
        }

        /// <summary>解析结果</summary>
        public class CardResult
        {
            public string Title = "";
            public HeaderColor Color = HeaderColor.Blue;
            public List<object> Elements = new List<object>();
        }

        /// <summary>
        /// 解析 Markdown 文本为卡片结构。
        /// </summary>
        public static CardResult Parse(string markdown, bool atAll)
        {
            var result = new CardResult();
            var lines = (markdown ?? "").Replace("\r\n", "\n").Split('\n');

            // ---- 第一遍：抽标题 ----
            int startIndex = 0;
            for (int i = 0; i < lines.Length; i++)
            {
                var t = lines[i].Trim();
                if (t.Length == 0) continue;

                if (t.StartsWith("# "))
                {
                    result.Title = t.Substring(2).Trim();
                    result.Color = GuessColor(result.Title);
                    startIndex = i + 1;
                }
                else if (t.StartsWith("!"))
                {
                    // `!红 标题` 强制指定颜色
                    var m = System.Text.RegularExpressions.Regex.Match(t, @"^!\s*(蓝|绿|红|橙|灰|紫|青)?\s*(.+)$");
                    if (m.Success)
                    {
                        result.Title = m.Groups[2].Value.Trim();
                        result.Color = ParseColorName(m.Groups[1].Value);
                        startIndex = i + 1;
                    }
                }
                break;
            }

            // ---- 第二遍：按块解析 ----
            var buf = new List<string>();       // 累积普通段落
            bool inNote = false;

            Action flush = () =>
            {
                if (buf.Count == 0) return;
                var joined = string.Join("\n", buf.ToArray());
                buf.Clear();
                if (string.IsNullOrWhiteSpace(joined)) return;

                result.Elements.Add(new Dictionary<string, object>
                {
                    { "tag", "div" },
                    { "text", new Dictionary<string, object> {
                        { "tag", "lark_md" },
                        { "content", joined }
                    }}
                });
            };

            for (int i = startIndex; i < lines.Length; i++)
            {
                var raw = lines[i];
                var line = raw.TrimEnd();

                // 分隔标记：--- 之后进 note 区
                if (line.Trim() == "---")
                {
                    flush();
                    if (!inNote) { inNote = true; continue; }
                    // 多个 --- 当作分割线
                    result.Elements.Add(new Dictionary<string, object> { { "tag", "hr" } });
                    continue;
                }

                if (line.Trim().Length == 0)
                {
                    // 空行：段落分界
                    if (buf.Count > 0)
                    {
                        var joined = string.Join("\n", buf.ToArray());
                        buf.Clear();
                        if (!string.IsNullOrWhiteSpace(joined))
                        {
                            result.Elements.Add(new Dictionary<string, object>
                            {
                                { "tag", "div" },
                                { "text", new Dictionary<string, object> {
                                    { "tag", "lark_md" },
                                    { "content", joined }
                                }}
                            });
                        }
                    }
                    continue;
                }

                // ## 二级标题 → 单独一个加粗大字块
                if (line.StartsWith("## "))
                {
                    flush();
                    var h2 = line.Substring(3).Trim();
                    result.Elements.Add(new Dictionary<string, object>
                    {
                        { "tag", "div" },
                        { "text", new Dictionary<string, object> {
                            { "tag", "lark_md" },
                            { "content", "**" + EscapeMd(h2) + "**" }
                        }}
                    });
                    continue;
                }

                if (inNote)
                {
                    // note 区：收集到 note 元素
                    continue;
                }

                buf.Add(ConvertLine(line));
            }

            // 收尾
            if (inNote)
            {
                // 把 --- 之后的原始文本作为 note
                var noteLines = new List<string>();
                for (int i = startIndex; i < lines.Length; i++)
                {
                    if (lines[i].Trim() == "---") { noteLines.Clear(); continue; }
                    if (noteLines.Count >= 0 && IsAfterSeparator(lines, startIndex, i))
                        noteLines.Add(lines[i].TrimEnd());
                }
                if (noteLines.Count > 0 && noteLines.Exists(x => x.Trim().Length > 0))
                {
                    var noteText = string.Join(" ", noteLines.FindAll(x => x.Trim().Length > 0).ToArray());
                    result.Elements.Add(new Dictionary<string, object>
                    {
                        { "tag", "note" },
                        { "elements", new List<object> {
                            new Dictionary<string, object> {
                                { "tag", "plain_text" },
                                { "content", noteText }
                            }
                        }}
                    });
                }
            }
            else
            {
                flush();
            }

            // @所有人：放到最后一条 action 前面，没有就单独加一行
            if (atAll)
            {
                result.Elements.Add(new Dictionary<string, object>
                {
                    { "tag", "div" },
                    { "text", new Dictionary<string, object> {
                        { "tag", "lark_md" },
                        { "content", "<at id=all></at>" }
                    }}
                });
            }

            // 一个块都没有时给个占位，避免空卡片
            if (result.Elements.Count == 0)
            {
                result.Elements.Add(new Dictionary<string, object>
                {
                    { "tag", "div" },
                    { "text", new Dictionary<string, object> {
                        { "tag", "lark_md" },
                        { "content", " " }
                    }}
                });
            }

            return result;
        }

        private static bool IsAfterSeparator(string[] lines, int startIndex, int current)
        {
            bool seen = false;
            for (int i = startIndex; i < current; i++)
            {
                if (lines[i].Trim() == "---") seen = true;
            }
            return seen;
        }

        /// <summary>把一行转换为 lark_md 文本（列表符号转成 Markdown）</summary>
        private static string ConvertLine(string line)
        {
            var t = line.Trim();

            // 引用
            if (t.StartsWith("> "))
            {
                return "> " + EscapeMd(t.Substring(2));
            }

            // 无序列表保持 - 前缀（lark_md 支持）
            if (t.StartsWith("- ") || t.StartsWith("* "))
            {
                return "- " + t.Substring(2);
            }

            // 有序列表保持原样
            if (System.Text.RegularExpressions.Regex.IsMatch(t, @"^\d+\.\s"))
            {
                return t;
            }

            return line;
        }

        /// <summary>转义会被 lark_md 误解析的字符（保留**、*、[]等有意义的标记）</summary>
        private static string EscapeMd(string s)
        {
            return s ?? "";
        }

        /// <summary>根据标题关键词猜颜色</summary>
        private static HeaderColor GuessColor(string title)
        {
            if (string.IsNullOrEmpty(title)) return HeaderColor.Blue;

            if (title.IndexOf("失败", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("错误", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("告警", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("异常", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("故障", StringComparison.Ordinal) >= 0)
                return HeaderColor.Red;

            if (title.IndexOf("成功", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("完成", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("通过", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("上线", StringComparison.Ordinal) >= 0)
                return HeaderColor.Green;

            if (title.IndexOf("警告", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("注意", StringComparison.Ordinal) >= 0 ||
                title.IndexOf("提醒", StringComparison.Ordinal) >= 0)
                return HeaderColor.Orange;

            return HeaderColor.Blue;
        }

        private static HeaderColor ParseColorName(string name)
        {
            switch ((name ?? "").Trim())
            {
                case "绿": return HeaderColor.Green;
                case "红": return HeaderColor.Red;
                case "橙": return HeaderColor.Orange;
                case "灰": return HeaderColor.Grey;
                case "紫": return HeaderColor.Purple;
                case "青": return HeaderColor.Turquoise;
                default: return HeaderColor.Blue;
            }
        }

        public static string ColorToTemplate(HeaderColor c)
        {
            switch (c)
            {
                case HeaderColor.Green: return "green";
                case HeaderColor.Red: return "red";
                case HeaderColor.Orange: return "orange";
                case HeaderColor.Grey: return "grey";
                case HeaderColor.Purple: return "purple";
                case HeaderColor.Turquoise: return "turquoise";
                default: return "blue";
            }
        }

        /// <summary>构建完整的卡片 JSON 对象</summary>
        public static Dictionary<string, object> BuildCard(string markdown, bool atAll)
        {
            var parsed = Parse(markdown, atAll);

            var card = new Dictionary<string, object>();
            card["config"] = new Dictionary<string, object>
            {
                { "wide_screen_mode", true }
            };

            if (!string.IsNullOrEmpty(parsed.Title))
            {
                card["header"] = new Dictionary<string, object>
                {
                    { "title", new Dictionary<string, object> {
                        { "tag", "plain_text" },
                        { "content", parsed.Title }
                    }},
                    { "template", ColorToTemplate(parsed.Color) }
                };
            }

            card["elements"] = parsed.Elements;
            return card;
        }
    }
}